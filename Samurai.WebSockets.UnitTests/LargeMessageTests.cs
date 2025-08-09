using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Samurai.WebSockets.Extensions;
using Samurai.WebSockets.Internal;

using Xunit;

namespace Samurai.WebSockets.UnitTests
{
    // Thanks Esbjörn for adding this unit test!!

    public enum Implementation : ushort
    {
        Native,
        Ninja,
        Samurai
    }

    public class LargeMessageTests
    {
        private ILogger<WebSocketClientTests> logger;

        private interface IServer : IDisposable
        {
            public Uri? Address { get; }
            public WebSocketState State { get; }
            public List<byte[]> ReceivedMessages { get; }

            public void StartListener();

            public Task WaitAsync();
        }

        private class EsbjörnServer : IServer
        {
            private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

            private HttpListener? listener;
            private Task? connectionPointTask;
            public Uri? Address { get; private set; }
            public List<byte[]> ReceivedMessages { get; } = new List<byte[]>();
            private WebSocket? webSocket;
            private ILogger logger;

            public WebSocketState State => this.webSocket?.State ?? WebSocketState.None;

            public EsbjörnServer(ILogger logger)
            {
                this.logger = logger;
                var os = Environment.OSVersion.Version;
                if (os.Major < 6 || os.Major == 6 && os.Minor < 2)
                {
                    throw new InvalidOperationException(
                        "Cannot create server - running on operating system that doesn't support native web sockets...");
                }
            }
            public Task WaitAsync() => this.connectionPointTask!;
            public void StartListener()
            {
                if (this.listener != null)
                    throw new InvalidOperationException("Listener already started.");

                var port = GetAvailablePort();
                this.Address = new Uri($"ws://localhost:{port}/");
                this.listener = new HttpListener();
                this.listener.Prefixes.Add($"http://localhost:{port}/");
                this.listener.Start();
                this.connectionPointTask = Task.Run(() => this.ConnectionPointAsync(this.listener, this.cancellationTokenSource.Token), this.cancellationTokenSource.Token);
            }

            private async Task ConnectionPointAsync(HttpListener listener, CancellationToken cancellationToken)
            {
                try
                {
                    this.logger.LogDebug("[Server] Waiting for connection...");
                    var context = await listener.GetContextAsync();
                    this.logger.LogDebug("[Server] Connection established.");

                    if (context.Request.IsWebSocketRequest)
                    {

                        this.logger.LogDebug("[Server] WebSocket request received.");

                        HttpListenerWebSocketContext webSocketContext = await context.AcceptWebSocketAsync(null);
                        this.logger.LogDebug("[Server] WebSocket accepted.");
                        var webSocket = webSocketContext.WebSocket;
                        this.webSocket = webSocket;
                        var receiveBuffer = new byte[4096];
                        using var stream = new MemoryStream();

                        while (webSocket.State == WebSocketState.Open)
                        {
                            var arraySegment = new ArraySegment<byte>(receiveBuffer);
                            this.logger.LogDebug("[Server] Waiting for message...");

                            var received = await webSocket.ReceiveAsync(arraySegment, CancellationToken.None);
                            this.logger.LogDebug($"[Server] Received {received.Count} bytes, EndOfMessage: {received.EndOfMessage}, MessageType: {received.MessageType}");

                            switch (received.MessageType)
                            {
                                case WebSocketMessageType.Close:
                                    {
                                        if (webSocket.State == WebSocketState.CloseReceived)
                                            await webSocket.CloseAsync(
                                                WebSocketCloseStatus.NormalClosure,
                                                "Ok",
                                                CancellationToken.None);

                                        break;
                                    }

                                case WebSocketMessageType.Binary:
                                    {
                                        stream.Write(arraySegment.Array!, arraySegment.Offset, received.Count);
                                        if (received.EndOfMessage)
                                        {
                                            this.ReceivedMessages.Add(stream.ToArray());
                                            stream.SetLength(0);
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
                catch when (cancellationToken.IsCancellationRequested)
                {
                    // Server was stopped
                }
            }

            public void Dispose()
            {
                this.listener?.Stop();
                this.cancellationTokenSource.Cancel();
                this.connectionPointTask?.Wait();
                this.cancellationTokenSource.Dispose();
            }
        }

        private class SamuraiServer : IServer
        {
            private TcpListener? listener;
            private Task? connectionPointTask;
            private WebSocket? webSocket;
            private ILogger logger;

            public Uri? Address { get; private set; }
            public List<byte[]> ReceivedMessages { get; } = new List<byte[]>();
            public WebSocketState State => this.webSocket?.State ?? WebSocketState.None;
            private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

            public SamuraiServer(ILogger logger)
            {
                this.logger = logger;
                var os = Environment.OSVersion.Version;
                if (os.Major < 6 || os.Major == 6 && os.Minor < 2)
                {
                    throw new InvalidOperationException(
                        "Cannot create server - running on operating system that doesn't support native web sockets...");
                }
            }

            public Task WaitAsync() => this.connectionPointTask!;
            public void StartListener()
            {
                if (this.listener != null)
                    throw new InvalidOperationException("Listener already started.");

                var port = GetAvailablePort();
                this.Address = new Uri($"ws://localhost:{port}/");
                this.listener = new TcpListener(IPAddress.Loopback, port);
                this.listener.Start();
                this.connectionPointTask = Task.Run(() => this.ConnectionPointAsync(this.listener, this.cancellationTokenSource.Token), this.cancellationTokenSource.Token);
            }

            private async Task ConnectionPointAsync(
                TcpListener listener,
                CancellationToken cancellationToken)
            {
                try
                {
                    this.logger.LogDebug("[Server] Waiting for connection...");
                    using var tcpClient = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    using var str = tcpClient.GetStream();
                    var context = await str.ReadHttpHeaderFromStreamAsync(cancellationToken);

                    this.logger.LogDebug("[Server] Connection established.");
                    if (context.IsWebSocketRequest)
                    {
                        this.logger.LogDebug("[Server] WebSocket request received.");
                        this.webSocket = await context.AcceptWebSocketAsync(cancellationToken);
                        this.logger.LogDebug("[Server] WebSocket accepted.");
                        var receiveBuffer = new byte[4096];
                        var stream = new MemoryStream();

                        while (this.webSocket.State == WebSocketState.Open)
                        {
                            var arraySegment = new ArraySegment<byte>(receiveBuffer);
                            this.logger.LogDebug("[Server] Waiting for message...");
                            var received = await this.webSocket.ReceiveAsync(arraySegment, cancellationToken);
                            this.logger.LogDebug($"[Server] Received {received.Count} bytes, EndOfMessage: {received.EndOfMessage}, MessageType: {received.MessageType}");
                            switch (received.MessageType)
                            {
                                case WebSocketMessageType.Close:
                                    {
                                        if (this.webSocket.State == WebSocketState.CloseReceived)
                                            await this.webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Ok", cancellationToken);

                                        break;
                                    }

                                case WebSocketMessageType.Binary:
                                    {
                                        stream.Write(arraySegment.Array!, arraySegment.Offset, received.Count);
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
                catch when (cancellationToken.IsCancellationRequested)
                {
                    // Server was stopped
                }
            }

            public void Dispose()
            {
                this.listener?.Stop();
                this.cancellationTokenSource.Cancel();
                this.connectionPointTask?.Wait();
                this.cancellationTokenSource.Dispose();
            }
        }

        private class NinjaServer : IServer
        {
            private TcpListener? listener;
            private Task? connectionPointTask;
            private WebSocket? webSocket;
            private ILogger logger;

            public Uri? Address { get; private set; }
            public List<byte[]> ReceivedMessages { get; } = new List<byte[]>();
            public WebSocketState State => this.webSocket?.State ?? WebSocketState.None;
            private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

            public NinjaServer(ILogger logger)
            {
                this.logger = logger;
                var os = Environment.OSVersion.Version;
                if (os.Major < 6 || os.Major == 6 && os.Minor < 2)
                {
                    throw new InvalidOperationException(
                        "Cannot create server - running on operating system that doesn't support native web sockets...");
                }
            }

            public Task WaitAsync() => this.connectionPointTask!;
            public void StartListener()
            {
                if (this.listener != null)
                    throw new InvalidOperationException("Listener already started.");

                var port = GetAvailablePort();
                this.Address = new Uri($"ws://localhost:{port}/");
                this.listener = new TcpListener(IPAddress.Loopback, port);
                this.listener.Start();
                this.connectionPointTask = Task.Run(() => this.ConnectionPointAsync(this.listener, this.cancellationTokenSource.Token), this.cancellationTokenSource.Token);
            }

            private async Task ConnectionPointAsync(
                TcpListener listener,
                CancellationToken cancellationToken)
            {
                try
                {
                    this.logger.LogDebug("[Server] Waiting for connection...");
                    using var tcpClient = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    using var str = tcpClient.GetStream();

                    var context = await str.ReadHttpHeaderFromStreamAsync(cancellationToken);

                    this.logger.LogDebug("[Server] Connection established.");
                    if (context.IsWebSocketRequest)
                    {
                        this.logger.LogDebug("[Server] WebSocket request received.");
                        this.webSocket = await context.AcceptWebSocketAsync(cancellationToken);
                        this.logger.LogDebug("[Server] WebSocket accepted.");
                        var receiveBuffer = new byte[4096];
                        var stream = new MemoryStream();

                        while (this.webSocket.State == WebSocketState.Open)
                        {
                            var arraySegment = new ArraySegment<byte>(receiveBuffer);
                            this.logger.LogDebug("[Server] Waiting for message...");
                            var received = await this.webSocket.ReceiveAsync(arraySegment, cancellationToken);
                            this.logger.LogDebug($"[Server] Received {received.Count} bytes, EndOfMessage: {received.EndOfMessage}, MessageType: {received.MessageType}");
                            switch (received.MessageType)
                            {
                                case WebSocketMessageType.Close:
                                    {
                                        if (this.webSocket.State == WebSocketState.CloseReceived)
                                            await this.webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Ok", cancellationToken);

                                        break;
                                    }

                                case WebSocketMessageType.Binary:
                                    {
                                        stream.Write(arraySegment.Array!, arraySegment.Offset, received.Count);
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
                catch when (cancellationToken.IsCancellationRequested)
                {
                    // Server was stopped
                }
            }

            public void Dispose()
            {
                this.listener?.Stop();
                this.cancellationTokenSource.Cancel();
                this.connectionPointTask?.Wait();
                this.cancellationTokenSource.Dispose();
            }
        }

        private static int GetAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public LargeMessageTests()
        {
            using var loggerFactory = LoggerFactory.Create(builder => builder
                    .SetMinimumLevel(LogLevel.Error)
                    .AddConsole());
            Events.Log = new Events(loggerFactory.CreateLogger<Events>());
            this.logger = loggerFactory.CreateLogger<WebSocketClientTests>();
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
                    var endOfMessage = nextStart == data.Length;
                    await client.SendAsync(seg, WebSocketMessageType.Binary, endOfMessage, cancellationToken);
                }
            }
        }

        [Theory]
#if !NETFRAMEWORK
        [InlineData(Implementation.Native, Implementation.Native)]
        [InlineData(Implementation.Native, Implementation.Ninja)]
        [InlineData(Implementation.Native, Implementation.Samurai)]
#endif
        [InlineData(Implementation.Ninja, Implementation.Native)]
        [InlineData(Implementation.Ninja, Implementation.Ninja)]
        [InlineData(Implementation.Ninja, Implementation.Samurai)]
        [InlineData(Implementation.Samurai, Implementation.Native)]
        [InlineData(Implementation.Samurai, Implementation.Ninja)]
        [InlineData(Implementation.Samurai, Implementation.Samurai)]
        public async Task SendLargeBinaryMessageAsync(Implementation serverImp, Implementation clientImpl)
        {
            this.logger.LogDebug("[Client] SendLargeBinaryMessage");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using (IServer server = serverImp switch
            {
                Implementation.Ninja => new NinjaServer(this.logger),
                Implementation.Samurai => new SamuraiServer(this.logger),
                _ => new EsbjörnServer(this.logger)
            })
            {
                server.StartListener();

                // Create client
                WebSocket webSocket;
                if (clientImpl == Implementation.Samurai)
                {
                    var factory = new WebSocketClientFactory();
                    this.logger.LogDebug("[Client] SendLargeBinaryMessage:ConnectAsync:Samurai");
                    webSocket = await factory.ConnectAsync(server.Address!, new WebSocketClientOptions(), cts.Token);
                }
                else if (clientImpl == Implementation.Ninja)
                {
                    var factory = new Ninja.WebSockets.WebSocketClientFactory();
                    this.logger.LogDebug("[Client] SendLargeBinaryMessage:ConnectAsync:Ninja");
                    webSocket = await factory.ConnectAsync(server.Address!, new Ninja.WebSockets.WebSocketClientOptions(), cts.Token);
                }
                else
                {
                    var client = new ClientWebSocket();
                    this.logger.LogDebug("[Client] SendLargeBinaryMessage:ConnectAsync:");
                    await client.ConnectAsync(server.Address!, cts.Token);
                    webSocket = client;
                }

                this.logger.LogDebug("[Client] SendLargeBinaryMessage:Random");
                var rand = new Random();
                var message = new byte[32 * 1023];
                Shared.NextBytes(message);
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

                await server.WaitAsync();
                Assert.Single(server.ReceivedMessages);
                Assert.Equal(message, server.ReceivedMessages[0]);
            }
        }
    }
}
