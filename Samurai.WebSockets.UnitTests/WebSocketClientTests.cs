using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
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
        public WebSocketClientTests()
        {
            using var loggerFactory = LoggerFactory.Create(builder => builder
                    .SetMinimumLevel(LogLevel.Warning)
                    .AddConsole());
            Events.Log = new Events(loggerFactory.CreateLogger<Events>());
        }

        [Fact]
        public async Task CanCancelReceiveAsync()
        {
            Console.WriteLine("CanCancelReceive");
            using var theInternet = new TheInternet();
            var webSocketClient = new SamuraiWebSocket(Guid.NewGuid(), theInternet.ClientNetworkStream!, TimeSpan.Zero, null, false, true, null);
            var webSocketServer = new SamuraiWebSocket(Guid.NewGuid(), theInternet.ServerNetworkStream!, TimeSpan.Zero, null, false, false, null);
            using var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var buffer = new ArraySegment<byte>(new byte[10]);

            tokenSource.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => webSocketClient.ReceiveAsync(buffer, tokenSource.Token));
        }

        [Fact]
        public async Task CanCancelSendAsync()
        {
            Console.WriteLine("CanCancelSend");
            using var theInternet = new TheInternet();
            var webSocketClient = new SamuraiWebSocket(Guid.NewGuid(), theInternet.ClientNetworkStream!, TimeSpan.Zero, null, false, true, null);
            using var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var buffer = new ArraySegment<byte>(new byte[10]);

            tokenSource.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => webSocketClient.SendAsync(buffer, WebSocketMessageType.Binary, true, tokenSource.Token));
        }

        [Fact]
        public async Task SimpleSendAsync()
        {
            Console.WriteLine("SimpleSend");
            using var theInternet = new TheInternet();
            var webSocketClient = new SamuraiWebSocket(Guid.NewGuid(), theInternet.ClientNetworkStream!, TimeSpan.Zero, null, false, true, null);
            var webSocketServer = new SamuraiWebSocket(Guid.NewGuid(), theInternet.ServerNetworkStream!, TimeSpan.Zero, null, false, false, null);
            using var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var clientReceiveTask = this.ReceiveClientAsync(webSocketClient, tokenSource.Token);
            var serverReceiveTask = this.ReceiveServerAsync(webSocketServer, 256, tokenSource.Token);

            var message1 = this.GetBuffer("Hi");
            var message2 = this.GetBuffer("There");

            await webSocketClient.SendAsync(message1, WebSocketMessageType.Binary, true, tokenSource.Token);
            await webSocketClient.SendAsync(message2, WebSocketMessageType.Binary, true, tokenSource.Token);
            await webSocketClient.CloseAsync(WebSocketCloseStatus.NormalClosure, null, tokenSource.Token);
            string[] replies = await clientReceiveTask;
            foreach (string reply in replies)
                Console.WriteLine(reply);
        }

        [Fact]
        public async Task ReceiveBufferTooSmallToFitWebsocketFrameTestAsync()
        {
            Console.WriteLine("ReceiveBufferTooSmallToFitWebsocketFrameTest");
            string pipeName = Guid.NewGuid().ToString();
            using var clientPipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var serverPipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

            var clientConnectTask = clientPipe.ConnectAsync();
            var serverConnectTask = serverPipe.WaitForConnectionAsync();
            await Task.WhenAll(clientConnectTask, serverConnectTask);

            var webSocketClient = new SamuraiWebSocket(Guid.NewGuid(), clientPipe, TimeSpan.Zero, null, false, true, null);
            var webSocketServer = new SamuraiWebSocket(Guid.NewGuid(), serverPipe, TimeSpan.Zero, null, false, false, null);
            using var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var clientReceiveTask = this.ReceiveClientAsync(webSocketClient, tokenSource.Token);
            // here we use a server with a buffer size of 10 which is smaller than the websocket frame
            var serverReceiveTask = this.ReceiveServerAsync(webSocketServer, 10, tokenSource.Token);
            var message1 = this.GetBuffer("This is a test message");

            await webSocketClient.SendAsync(message1, WebSocketMessageType.Binary, true, tokenSource.Token);
            await webSocketClient.CloseAsync(WebSocketCloseStatus.NormalClosure, null, tokenSource.Token);

            await serverReceiveTask;
            var replies = await clientReceiveTask;
            foreach (string reply in replies)
                Console.WriteLine(reply);

            Assert.Equal(3, replies.Length);
            Assert.Equal("Server: This is ", replies[0]);
            Assert.Equal("Server: a test m", replies[1]);
            Assert.Equal("Server: essage", replies[2]);
        }

        [Fact]
        public async Task SimpleNamedPipesAsync()
        {
            Console.WriteLine("SimpleNamedPipes");
            string pipeName = Guid.NewGuid().ToString();
            using var clientPipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var serverPipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

            var clientConnectTask = clientPipe.ConnectAsync();
            var serverConnectTask = serverPipe.WaitForConnectionAsync();
            await Task.WhenAll(clientConnectTask, serverConnectTask);

            var webSocketClient = new SamuraiWebSocket(Guid.NewGuid(), clientPipe, TimeSpan.Zero, null, false, true, null);
            var webSocketServer = new SamuraiWebSocket(Guid.NewGuid(), serverPipe, TimeSpan.Zero, null, false, false, null);
            using var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var clientReceiveTask = this.ReceiveClientAsync(webSocketClient, tokenSource.Token);
            var serverReceiveTask = this.ReceiveServerAsync(webSocketServer, 256, tokenSource.Token);

            var message1 = this.GetBuffer("Hi");
            var message2 = this.GetBuffer("There");

            await webSocketClient.SendAsync(message1, WebSocketMessageType.Binary, true, tokenSource.Token);
            await webSocketClient.SendAsync(message2, WebSocketMessageType.Binary, true, tokenSource.Token);
            await webSocketClient.CloseAsync(WebSocketCloseStatus.NormalClosure, null, tokenSource.Token);

            foreach (string reply in await clientReceiveTask)
                Console.WriteLine(reply);
        }

        private ArraySegment<byte> GetBuffer(string text)
        {
            var buffer = Encoding.UTF8.GetBytes(text);
            return new ArraySegment<byte>(buffer, 0, buffer.Length);
        }

        public Task<string[]> ReceiveClientAsync(WebSocket webSocket, CancellationToken cancellationToken)
        {
            return Task.Run(async () =>
            {
                var values = new List<string>();
                var array = new byte[256];
                var buffer = new ArraySegment<byte>(array);

                while (!cancellationToken.IsCancellationRequested)
                {
                    var result = await webSocket.ReceiveAsync(buffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                        break;

                    var value = Encoding.UTF8.GetString(buffer.Array!, 0, result.Count);
                    values.Add(value);
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

                while (!cancellationToken.IsCancellationRequested)
                {
                    var result = await webSocket.ReceiveAsync(buffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                        break;

                    var value = Encoding.UTF8.GetString(buffer.Array!, 0, result.Count);
                    var reply = "Server: " + value;
                    var toSend = Encoding.UTF8.GetBytes(reply);
                    await webSocket.SendAsync(new ArraySegment<byte>(toSend, 0, toSend.Length), WebSocketMessageType.Binary, true, cancellationToken);
                }
            });
        }
    }
}
