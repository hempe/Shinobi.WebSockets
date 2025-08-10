using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.Pipes;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Samurai.WebSockets.Internal;

using Xunit;

namespace Samurai.WebSockets.UnitTests
{
    public class WebSocketClientTests
    {
        private readonly ILogger logger;
        public WebSocketClientTests()
        {
            using var loggerFactory = LoggerFactory.Create(builder => builder
                    .SetMinimumLevel(LogLevel.Trace)
                    .AddConsole());
            Events.Log = new Events(loggerFactory.CreateLogger<Events>());
            this.logger = loggerFactory.CreateLogger<WebSocketClientTests>();
        }


        [Fact]
        public async Task CanCancelReceiveAsync()
        {
            this.logger.LogDebug("CanCancelReceive");
            using var theInternet = new TheInternet();
            using var webSocketClient = this.GetWebSocket(Guid.NewGuid(), theInternet.ClientNetworkStream!, TimeSpan.Zero, false, false, true, null);
            var webSocketServer = this.GetWebSocket(Guid.NewGuid(), theInternet.ServerNetworkStream!, TimeSpan.Zero, false, false, false, null);
            using var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var buffer = new ArraySegment<byte>(new byte[10]);

            tokenSource.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => webSocketClient.ReceiveAsync(buffer, tokenSource.Token));
        }

        [Fact]
        public async Task CanCancelSendAsync()
        {
            this.logger.LogDebug("CanCancelSend");
            using var theInternet = new TheInternet();
            using var webSocketClient = this.GetWebSocket(Guid.NewGuid(), theInternet.ClientNetworkStream!, TimeSpan.Zero, false, false, true, null);
            using var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var buffer = new ArraySegment<byte>(new byte[10]);

            tokenSource.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => webSocketClient.SendAsync(buffer, WebSocketMessageType.Binary, true, tokenSource.Token));
        }

        [Fact]
        public async Task SimpleSendAsync()
        {
            this.logger.LogDebug("SimpleSend");
            using var theInternet = new TheInternet();
            using var webSocketClient = this.GetWebSocket(Guid.NewGuid(), theInternet.ClientNetworkStream!, TimeSpan.Zero, false, false, true, null);
            var webSocketServer = this.GetWebSocket(Guid.NewGuid(), theInternet.ServerNetworkStream!, TimeSpan.Zero, false, false, false, null);
            using var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var clientReceiveTask = this.ReceiveClientAsync(webSocketClient, tokenSource.Token);
            var serverReceiveTask = this.ReceiveServerAsync(webSocketServer, 256, tokenSource.Token);

            var message1 = this.GetBuffer("Hi");
            var message2 = this.GetBuffer("There");

            await webSocketClient.SendAsync(message1, WebSocketMessageType.Binary, true, tokenSource.Token);
            await webSocketClient.SendAsync(message2, WebSocketMessageType.Binary, true, tokenSource.Token);

            await webSocketClient.CloseAsync(WebSocketCloseStatus.NormalClosure, null, tokenSource.Token);
            var replies = await clientReceiveTask;
            Assert.Equal(2, replies.Length);
            Assert.Equal("Server [1]: Hi", replies[0].Text);
            Assert.Equal("Server [1]: There", replies[1].Text);
        }


        [Fact]
        public async Task SimpleSendHugeMessageAsync()
        {
            this.logger.LogDebug("SimpleSendHugeMessage");
            using var theInternet = new TheInternet();
            using var webSocketClient = this.GetWebSocket(Guid.NewGuid(), theInternet.ClientNetworkStream!, TimeSpan.Zero, false, false, true, null);
            var webSocketServer = this.GetWebSocket(Guid.NewGuid(), theInternet.ServerNetworkStream!, TimeSpan.Zero, false, false, false, null);
            using var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var clientReceiveTask = this.ReceiveClientAsync(webSocketClient, tokenSource.Token);
            var serverReceiveTask = this.ReceiveServerAsync(webSocketServer, 256, tokenSource.Token);

            var message = string.Join(string.Empty, Enumerable.Range(0, 32 * 1024).Select(_ => 'A'));
            var message1 = this.GetBuffer(message);

            await webSocketClient.SendAsync(message1, WebSocketMessageType.Binary, true, tokenSource.Token);
            await webSocketClient.CloseAsync(WebSocketCloseStatus.NormalClosure, null, tokenSource.Token);
            var replies = await clientReceiveTask;
            Assert.Single(replies);
            Assert.Equal($"Server [128]: {message}".Length, replies[0].Text.Length);
            Assert.Equal($"Server [128]: {message}", replies[0].Text);
        }


        [Fact]
        public async Task PermessageDeflateAsync()
        {
            this.logger.LogDebug("PermessageDeflateSendAsync");
            using var theInternet = new TheInternet();
            using var webSocketClient = this.GetWebSocket(Guid.NewGuid(), theInternet.ClientNetworkStream!, TimeSpan.Zero, true, false, true, null);
            var webSocketServer = this.GetWebSocket(Guid.NewGuid(), theInternet.ServerNetworkStream!, TimeSpan.Zero, true, false, false, null);
            using var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var clientReceiveTask = this.ReceiveClientAsync(webSocketClient, tokenSource.Token);
            var serverReceiveTask = this.ReceiveServerAsync(webSocketServer, 256, tokenSource.Token);

            var message1 = this.GetBuffer("Hi");
            var message2 = this.GetBuffer("There");

            await webSocketClient.SendAsync(message1, WebSocketMessageType.Binary, true, tokenSource.Token);
            await webSocketClient.SendAsync(message2, WebSocketMessageType.Binary, true, tokenSource.Token);
            await webSocketClient.CloseAsync(WebSocketCloseStatus.NormalClosure, null, tokenSource.Token);
            var replies = await clientReceiveTask;
            Assert.Equal(2, replies.Length);
            Assert.Equal("Server [1]: Hi", replies[0].Text);
            Assert.Equal("Server [1]: There", replies[1].Text);
        }

        [Fact]
        public async Task PermessageDeflateGiantMessageAsync()
        {
            this.logger.LogDebug("PermessageDeflateGiantMessage");
            using var theInternet = new TheInternet();
            using var webSocketClient = this.GetWebSocket(Guid.NewGuid(), theInternet.ClientNetworkStream!, TimeSpan.Zero, true, false, true, null);
            using var webSocketServer = this.GetWebSocket(Guid.NewGuid(), theInternet.ServerNetworkStream!, TimeSpan.Zero, true, false, false, null);
            using var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(100));

            var clientReceiveTask = this.ReceiveClientAsync(webSocketClient, tokenSource.Token);
            var serverReceiveTask = this.ReceiveServerAsync(webSocketServer, 32 * 1024, tokenSource.Token);

            var message = string.Join(string.Empty, Enumerable.Range(0, 4 * 1024).Select(_ => 'A'));
            var message1 = this.GetBuffer(message);

            await webSocketClient.SendAsync(message1, WebSocketMessageType.Binary, true, tokenSource.Token);
            await webSocketClient.CloseAsync(WebSocketCloseStatus.NormalClosure, null, tokenSource.Token);
            var replies = await clientReceiveTask;
            Assert.Single(replies);
            Assert.Equal($"Server [1]: {message}".Length, replies[0].Text.Length);
            Assert.Equal($"Server [1]: {message}", replies[0].Text);
        }


        [Fact]
        public async Task ReceiveBufferTooSmallToFitWebsocketFrameTestAsync()
        {
            this.logger.LogDebug("ReceiveBufferTooSmallToFitWebsocketFrameTest");
            string pipeName = Guid.NewGuid().ToString();
            using var clientPipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var serverPipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

            var clientConnectTask = clientPipe.ConnectAsync();
            var serverConnectTask = serverPipe.WaitForConnectionAsync();
            await Task.WhenAll(clientConnectTask, serverConnectTask);

            using var webSocketClient = this.GetWebSocket(Guid.NewGuid(), clientPipe, TimeSpan.Zero, false, false, true, null);
            var webSocketServer = this.GetWebSocket(Guid.NewGuid(), serverPipe, TimeSpan.Zero, false, false, false, null);
            using var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var clientReceiveTask = this.ReceiveClientAsync(webSocketClient, tokenSource.Token);
            // here we use a server with a buffer size of 10 which is smaller than the websocket frame
            var serverReceiveTask = this.ReceiveServerAsync(webSocketServer, 10, tokenSource.Token);
            var message1 = this.GetBuffer("This is a test message");

            await webSocketClient.SendAsync(message1, WebSocketMessageType.Binary, true, tokenSource.Token);
            await webSocketClient.CloseAsync(WebSocketCloseStatus.NormalClosure, null, tokenSource.Token);

            await serverReceiveTask;
            var replies = await clientReceiveTask;
            Assert.Single(replies);
            Assert.Equal(1, replies[0].Count);
            Assert.Equal("Server [3]: This is a test message", replies[0].Text);
        }

        [Fact]
        public async Task SimpleNamedPipesAsync()
        {
            this.logger.LogDebug("SimpleNamedPipes");
            string pipeName = Guid.NewGuid().ToString();
            using var clientPipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var serverPipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

            var clientConnectTask = clientPipe.ConnectAsync();
            var serverConnectTask = serverPipe.WaitForConnectionAsync();
            await Task.WhenAll(clientConnectTask, serverConnectTask);

            using var webSocketClient = this.GetWebSocket(Guid.NewGuid(), clientPipe, TimeSpan.Zero, false, false, true, null);
            var webSocketServer = this.GetWebSocket(Guid.NewGuid(), serverPipe, TimeSpan.Zero, false, false, false, null);
            using var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var clientReceiveTask = this.ReceiveClientAsync(webSocketClient, tokenSource.Token);
            var serverReceiveTask = this.ReceiveServerAsync(webSocketServer, 256, tokenSource.Token);

            var message1 = this.GetBuffer("Hi");
            var message2 = this.GetBuffer("There");

            await webSocketClient.SendAsync(message1, WebSocketMessageType.Binary, true, tokenSource.Token);
            await webSocketClient.SendAsync(message2, WebSocketMessageType.Binary, true, tokenSource.Token);
            await webSocketClient.CloseAsync(WebSocketCloseStatus.NormalClosure, null, tokenSource.Token);

            var replies = await clientReceiveTask;

            Assert.Equal(2, replies.Length);
            Assert.Equal("Server [1]: Hi", replies[0].Text);
            Assert.Equal("Server [1]: There", replies[1].Text);
        }

        private SamuraiWebSocket GetWebSocket(
            Guid guid,
            Stream mockNetworkStream,
            TimeSpan keepAliveInterval,
            bool permessageDeflate,
            bool includeExceptionInCloseResponse,
            bool isClient,
            string? subProtocol)
        {

            return new SamuraiWebSocket(
                new WebSocketHttpContext(HttpResponse.Create(101), mockNetworkStream, guid),
                permessageDeflate ? new WebSocketExtension() : null,
                keepAliveInterval,
                includeExceptionInCloseResponse,
                isClient,
                subProtocol
            );
        }

        private ArraySegment<byte> GetBuffer(string text)
        {
            var buffer = Encoding.UTF8.GetBytes(text);
            return new ArraySegment<byte>(buffer, 0, buffer.Length);
        }

        private struct ReadResult
        {
            public string Text { get; set; }
            public int Count { get; set; }
        }

        private Task<ReadResult[]> ReceiveClientAsync(WebSocket webSocket, CancellationToken cancellationToken)
        {
            return Task.Run(async () =>
            {
                var values = new List<ReadResult>();
                var array = new byte[256];
                var buffer = new ArraySegment<byte>(array);
                using var ms = new ArrayPoolStream();
                ms.SetLength(0);
                var count = 0;
                var size = 0;
                while (!cancellationToken.IsCancellationRequested)
                {
                    var result = await webSocket.ReceiveAsync(buffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                        break;

                    count++;
                    size += result.Count;
                    ms.Write(buffer.Array!, 0, result.Count);

                    if (result.EndOfMessage)
                    {

                        ms.Position = 0;
                        using var reader = new StreamReader(ms, Encoding.UTF8, bufferSize: 1024, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
                        values.Add(new ReadResult { Text = reader.ReadToEnd(), Count = count });

                        count = 0;
                        ms.SetLength(0);
                    }
                }

                return values.ToArray();
            });
        }

        private Task ReceiveServerAsync(WebSocket webSocket, int bufferSize, CancellationToken cancellationToken)
        {
            return Task.Run(async () =>
            {
                var array = new byte[bufferSize];
                var buffer = new ArraySegment<byte>(array);
                using var ms = new ArrayPoolStream();
                var count = 0;

                while (!cancellationToken.IsCancellationRequested)
                {
                    var result = await webSocket.ReceiveAsync(buffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                        break;

                    count++;
                    ms.Write(buffer.Array!, 0, result.Count);
                    if (result.EndOfMessage)
                    {
                        string value;
                        ms.Position = 0;

                        using var reader = new StreamReader(ms, Encoding.UTF8, bufferSize: buffer.Count, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
                        value = reader.ReadToEnd();

                        ms.SetLength(0);

                        var reply = $"Server [{count}]: {value}";
                        count = 0;
                        const int chunkSize = 4096;
                        var bytes = Encoding.UTF8.GetBytes(reply);
                        var chunks = bytes
                            .Select((b, i) => new { Byte = b, Index = i })
                            .GroupBy(x => x.Index / chunkSize)
                            .Select(g => new ArraySegment<byte>(g.Select(x => x.Byte).ToArray()))
                            .ToArray();

                        for (var i = 0; i < chunks.Length; i++)
                        {
                            await webSocket.SendAsync(chunks[i], WebSocketMessageType.Binary, i == chunks.Length - 1, cancellationToken);
                        }

                    }
                }
            });
        }
    }
}
