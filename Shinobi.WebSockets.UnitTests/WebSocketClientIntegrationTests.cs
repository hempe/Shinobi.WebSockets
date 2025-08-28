using System;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Shinobi.WebSockets.Builders;

#if NET8_0_OR_GREATER
#else 
using Shinobi.WebSockets.Extensions;
#endif
using Xunit;

namespace Shinobi.WebSockets.UnitTests
{
    public class WebSocketClientIntegrationTests : IAsyncDisposable
    {
        private readonly CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        /// <summary>
        /// Gets an available TCP port
        /// </summary>
        private static int GetAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                listener.Start();
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();

                return port;
            }
            finally
            {
#if !NETFRAMEWORK

                listener.Dispose();
#endif
            }
        }

        [Fact]
        public async Task WebSocketClient_ShouldConnectToEchoServerAsync()
        {
            // Arrange
            var testPort = GetAvailablePort();
            var testServer = WebSocketServerBuilder.Create()
                .UsePort((ushort)testPort)
                .OnTextMessage(async (ws, message, ct) =>
                {
                    var responseBytes = Encoding.UTF8.GetBytes($"Echo: {message}");
                    await ws.SendAsync(new ArraySegment<byte>(responseBytes), WebSocketMessageType.Text, true, ct);
                })
                .Build();

            await testServer.StartAsync();
            var testServerUri = new Uri($"ws://localhost:{testPort}/");

            string? receivedMessage = null;
            var messageReceived = new TaskCompletionSource<bool>();

            using var client = WebSocketClientBuilder.Create()
                .OnTextMessage((ws, message, ct) =>
                {
                    receivedMessage = message;
                    messageReceived.TrySetResult(true);
                    return default(ValueTask);
                })
                .Build();

            try
            {
                // Act
                await client.StartAsync(testServerUri, this.cts.Token);

                var messageToSend = "Hello Server!";
                await client.SendTextAsync(messageToSend, this.cts.Token);

                // Wait for response
                await messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

                // Assert
                Assert.Equal($"Echo: {messageToSend}", receivedMessage);
                Assert.Equal(WebSocketConnectionState.Connected, client.ConnectionState);

                // Cleanup
                await client.StopAsync();
            }
            finally
            {
                await testServer.StopAsync();
                testServer.Dispose();
            }
        }

        [Fact]
        public async Task WebSocketClient_WithAutoReconnect_ShouldWorkAsync()
        {
            // Arrange
            var testPort = GetAvailablePort();
            var connected = new TaskCompletionSource<bool>();

            var testServer = WebSocketServerBuilder.Create()
                 .UsePort((ushort)testPort)
                 .OnTextMessage(async (ws, message, ct) =>
                 {
                     var responseBytes = Encoding.UTF8.GetBytes($"Echo: {message}");
                     await ws.SendAsync(new ArraySegment<byte>(responseBytes), WebSocketMessageType.Text, true, ct);
                 })
                 .OnConnected((webSocket, next, cancellationToken) =>
                 {
                     connected.TrySetResult(true);
                     return next(webSocket, cancellationToken);
                 })
                 .Build();


            await testServer.StartAsync();
            var testServerUri = new Uri($"ws://localhost:{testPort}/");

            string? receivedMessage = null;
            var messageReceived = new TaskCompletionSource<bool>();

            using var client = WebSocketClientBuilder.Create()
                .UseReliableConnection()
                .OnTextMessage((ws, message, ct) =>
                {
                    receivedMessage = message;
                    messageReceived.TrySetResult(true);
                    return default(ValueTask);
                })
                .Build();

            try
            {

                // Act
                await client.StartAsync(testServerUri, this.cts.Token);
                await connected.Task;

                var messageToSend = "Hello Server!";
                await client.SendTextAsync(messageToSend, this.cts.Token);

                // Wait for response
                await messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

                // Assert
                Assert.Equal($"Echo: {messageToSend}", receivedMessage);
                Assert.Equal(WebSocketConnectionState.Connected, client.ConnectionState);

                // Cleanup
                await client.StopAsync();
            }
            finally
            {
                await testServer.StopAsync();
                testServer.Dispose();
            }
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                this.cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // ignore
            }

            this.cts.Dispose();
            return default(ValueTask);
        }
    }
}
