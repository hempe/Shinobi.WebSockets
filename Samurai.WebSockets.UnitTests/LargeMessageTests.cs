using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Samurai.WebSockets.Internal;
using Xunit;

namespace Samurai.WebSockets.UnitTests
{
    // Thanks Esbjörn for adding this unit test!!
    public class LargeMessageTests
    {
        private class Server : IDisposable
        {
            private HttpListener listener;
            private Task connectionPointTask;
            private WebSocket webSocket;
            public Uri Address { get; private set; }
            public readonly List<byte[]> ReceivedMessages = new List<byte[]>();
            public WebSocketState State => this.webSocket?.State ?? WebSocketState.None;
            private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

            public Server()
            {
                var os = Environment.OSVersion.Version;
                if (os.Major < 6 || os.Major == 6 && os.Minor < 2)
                {
                    throw new InvalidOperationException(
                        "Cannot create server - running on operating system that doesn't support native web sockets...");
                }
            }

            public void StartListener()
            {
                if (this.listener != null)
                {
                    throw new InvalidOperationException("Listener already started.");
                }

                // Create new listener
                var usedPorts = new HashSet<int>(IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Select(a => a.Port));

                for (int i = 49152; i <= 65535; i++)
                {
                    if (usedPorts.Contains(i))
                    {
                        continue;
                    }

                    var listener = new HttpListener();

                    listener.Prefixes.Add($"http://localhost:{i}/");
                    try
                    {
                        listener.Start();
                        this.Address = new Uri($"ws://localhost:{i}/");
                        this.listener = listener;
                        this.connectionPointTask = Task.Run(() => this.ConnectionPointAsync(this.cancellationTokenSource.Token), this.cancellationTokenSource.Token);
                        break;
                    }
                    catch (HttpListenerException)
                    {
                        // right - for some reason we couldn't connect. Try the next port
                    }
                }

                if (this.listener == null)
                {
                    throw new InvalidOperationException("Could not find free port to bind to.");
                }
            }

            private async Task ConnectionPointAsync(CancellationToken cancellationToken)
            {
                try
                {
                    Console.WriteLine("[Server] Waiting for connection...");
                    var context = await this.listener.GetContextAsync();
                    Console.WriteLine("[Server] Connection established.");
                    if (context.Request.IsWebSocketRequest)
                    {
                        Console.WriteLine("[Server] WebSocket request received.");
                        var webSocketContext = await context.AcceptWebSocketAsync(null);
                        Console.WriteLine("[Server] WebSocket accepted.");
                        var webSocket = webSocketContext.WebSocket;
                        this.webSocket = webSocket;
                        var receiveBuffer = new byte[4096];
                        var stream = new MemoryStream();


                        while (webSocket.State == WebSocketState.Open)
                        {
                            var arraySegment = new ArraySegment<byte>(receiveBuffer);
                            Console.WriteLine("[Server] Waiting for message...");
                            var received = await webSocket.ReceiveAsync(arraySegment, cancellationToken);
                            Console.WriteLine($"[Server] Received {received.Count} bytes, EndOfMessage: {received.EndOfMessage}, MessageType: {received.MessageType}");
                            switch (received.MessageType)
                            {
                                case WebSocketMessageType.Close:
                                    {
                                        if (webSocket.State == WebSocketState.CloseReceived)
                                        {
                                            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Ok", cancellationToken);
                                        }

                                        break;
                                    }

                                case WebSocketMessageType.Binary:
                                    {
                                        stream.Write(arraySegment.Array, arraySegment.Offset, received.Count);
                                        if (received.EndOfMessage)
                                        {
                                            this.ReceivedMessages.Add(stream.ToArray());
                                            stream = new MemoryStream();
                                        }

                                        break;
                                    }
                            }
                        }
                    }
                }
                catch (HttpListenerException)
                {
                    // This would happen when the server was stopped for instance.
                }
            }

            public void Dispose()
            {
                this.listener.Stop();
                this.cancellationTokenSource.Cancel();
                this.connectionPointTask.Wait();
                this.cancellationTokenSource.Dispose();
            }
        }

        public LargeMessageTests()
        {
            using var loggerFactory = LoggerFactory.Create(builder => builder
                    .SetMinimumLevel(LogLevel.Trace)
                    .AddConsole());
            Events.Log = new Events(loggerFactory.CreateLogger<Events>());
        }

        private async Task SendBinaryMessageAsync(WebSocket client, byte[] message, int sendBufferLength, CancellationToken cancellationToken)
        {
            if (message.Length > 0)
            {
                // copy data so that masking doesn't affect the original message
                var data = message.ToArray();

                for (int i = 0; i <= (data.Length - 1) / sendBufferLength; i++)
                {
                    int start = i * sendBufferLength;
                    int nextStart = Math.Min(start + sendBufferLength, data.Length);
                    ArraySegment<byte> seg = new ArraySegment<byte>(data, start, nextStart - start);
                    await client.SendAsync(seg, WebSocketMessageType.Binary, nextStart == data.Length, cancellationToken);
                }
            }
        }

        [Theory]
        //[InlineData(false)]
        [InlineData(true)]
        public async Task SendLargeBinaryMessage(bool useSamurai)
        {
            Console.WriteLine("[Client] SendLargeBinaryMessage");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using (var server = new Server())
            {
                server.StartListener();

                // Create client
                WebSocket webSocket;
                if (useSamurai)
                {
                    var factory = new WebSocketClientFactory();
                    Console.WriteLine("[Client] SendLargeBinaryMessage:ConnectAsync");
                    webSocket = await factory.ConnectAsync(server.Address, new WebSocketClientOptions(), cts.Token);
                }
                else
                {
                    var clientWebSocket = new ClientWebSocket();
                    Console.WriteLine("[Client] SendLargeBinaryMessage:ConnectAsync");
                    await clientWebSocket.ConnectAsync(server.Address, cts.Token);
                    webSocket = clientWebSocket;
                }

                Console.WriteLine("[Client] SendLargeBinaryMessage:Random");
                var rand = new Random();
                var message = new byte[10000];
                Random.Shared.NextBytes(message);
                // Send large message
                await this.SendBinaryMessageAsync(webSocket, message, 1024, cts.Token);

                // Close
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Close", cts.Token);

                // Wait for the server to receive our close message
                var stopwatch = Stopwatch.StartNew();
                while (server.State == WebSocketState.Open)
                {
                    await Task.Delay(5);
                    if (stopwatch.Elapsed.TotalSeconds > 10)
                    {
                        throw new TimeoutException("Timeout expired after waiting for close handshake to complete");
                    }
                }

                Assert.Single(server.ReceivedMessages);
                Assert.Equal(message, server.ReceivedMessages[0]);
            }
        }
    }
}
