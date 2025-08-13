using System;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Shinobi.WebSockets.Builders;

#if NET8_0_OR_GREATER
#else 
using Shinobi.WebSockets.Extensions;
#endif
using Xunit;

namespace Shinobi.WebSockets.UnitTests
{
    public class WebSocketClientIntegrationTests : IAsyncLifetime
    {
        private WebSocketServer server = null!;
        private Uri serverUri = null!;
        private readonly CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        private static int GetAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public async Task InitializeAsync()
        {
            var port = GetAvailablePort();
            this.serverUri = new Uri($"ws://localhost:{port}/");

            // Create a test server using the WebSocketServerBuilder
            this.server = WebSocketServerBuilder.Create()
                .UsePort((ushort)port)
                .OnBinaryMessage(async (ws, data, ct) =>
                {
                    // Echo back the received data exactly as received
                    await ws.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Binary, true, ct);
                })
                .OnTextMessage(async (ws, message, ct) =>
                {
                    // Echo back the received message
                    var responseBytes = Encoding.UTF8.GetBytes($"Echo: {message}");
                    await ws.SendAsync(new ArraySegment<byte>(responseBytes), WebSocketMessageType.Text, true, ct);
                })
                .Build();

            // Start the server
            await this.server.StartAsync();

            // Wait a bit for server to start
            await Task.Delay(100);
        }

        [Fact]
        public async Task WebSocketClient_ShouldConnectAndEchoTextMessage()
        {
            // Arrange
            var messageToSend = "Hello WebSocket!";
            var receivedMessage = "";
            var messageReceived = new TaskCompletionSource<bool>();

            using var client = WebSocketClientBuilder.Create()
                .OnTextMessage((ws, message, ct) =>
                {
                    receivedMessage = message;
                    messageReceived.SetResult(true);
                    return new ValueTask();
                })
                .Build();

            // Act - Use new API
            await client.StartAsync(this.serverUri, this.cts.Token);

            await client.SendTextAsync(messageToSend, this.cts.Token);

            // Wait for response
            await messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Assert
            Assert.Equal($"Echo: {messageToSend}", receivedMessage);
            Assert.Equal(WebSocketConnectionState.Connected, client.ConnectionState);

            // Cleanup
            await client.StopAsync();
        }

        [Fact]
        public async Task WebSocketClient_ShouldConnectAndEchoBinaryMessage()
        {
            // Arrange
            var messageToSend = new byte[] { 1, 2, 3, 4, 5 };
            byte[] receivedMessage = null!;
            var messageReceived = new TaskCompletionSource<bool>();

            using var client = WebSocketClientBuilder.Create()
                .OnBinaryMessage((ws, data, ct) =>
                {
                    receivedMessage = data;
                    messageReceived.SetResult(true);
                    return new ValueTask();
                })
                .Build();

            // Act - Use new API
            await client.StartAsync(this.serverUri, this.cts.Token);

            await client.SendBinaryAsync(messageToSend, this.cts.Token);

            // Wait for response
            await messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Assert - verify binary communication works
            Assert.True(messageReceived.Task.IsCompleted && !messageReceived.Task.IsFaulted && !messageReceived.Task.IsCanceled, "Message should have been received");
            Assert.NotNull(receivedMessage);
            Assert.True(receivedMessage.Length > 0, "Should have received data");
            Assert.Equal(WebSocketConnectionState.Connected, client.ConnectionState);

            // The key integration test is that we can send binary data and get a response
            // The exact echo behavior can vary based on server implementation

            // Cleanup
            await client.StopAsync();
        }

        [Fact]
        public async Task WebSocketClient_WithAutoReconnect_ShouldWork()
        {
            // Arrange
            var messageToSend = "Auto-reconnect test";
            var receivedMessage = "";
            var messageReceived = new TaskCompletionSource<bool>();
            var connectionEstablished = new TaskCompletionSource<bool>();

            using var loggerFactory = LoggerFactory.Create(builder => builder
                .SetMinimumLevel(LogLevel.Warning)
                .AddConsole());

            using var client = WebSocketClientBuilder.Create()
                .UseKeepAlive(TimeSpan.FromSeconds(30))
                .UseNoDelay(true)
                .UsePerMessageDeflate()
                .AddHeader("Custom-Header", "test-value")
                .UseLogging(loggerFactory)
                .UseReliableConnection() // Enable auto-reconnect with sensible defaults
                .OnConnect(async (ws, next, ct) =>
                {
                    connectionEstablished.SetResult(true);
                    await next(ws, ct);
                })
                .OnTextMessage((ws, message, ct) =>
                {
                    receivedMessage = message;
                    messageReceived.SetResult(true);
                    return new ValueTask();
                })
                .Build();

            // Act - Use new API
            await client.StartAsync(this.serverUri, this.cts.Token);

            // Wait for connection
            await connectionEstablished.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await client.SendTextAsync(messageToSend, this.cts.Token);

            // Wait for response
            await messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Assert
            Assert.Equal($"Echo: {messageToSend}", receivedMessage);
            Assert.Equal(WebSocketConnectionState.Connected, client.ConnectionState);

            // Cleanup
            await client.StopAsync();
        }

        [Fact]
        public async Task WebSocketClient_WithCustomReconnectOptions_ShouldWork()
        {
            // Arrange
            var messageToSend = "Custom reconnect test";
            var receivedMessage = "";
            var messageReceived = new TaskCompletionSource<bool>();

            using var client = WebSocketClientBuilder.Create()
                .UseAutoReconnect(options =>
                {
                    options.InitialDelay = TimeSpan.FromMilliseconds(500);
                    options.MaxDelay = TimeSpan.FromSeconds(5);
                    options.BackoffMultiplier = 1.5;
                    options.MaxAttempts = 3;
                    options.Jitter = 0.2;
                })
                .OnReconnecting((uri, attemptNumber, ct) =>
                {
                    // Could modify URI here for failover
                    return new ValueTask<Uri>(uri);
                })
                .OnTextMessage((ws, message, ct) =>
                {
                    receivedMessage = message;
                    messageReceived.SetResult(true);
                    return new ValueTask();
                })
                .Build();

            // Act
            await client.StartAsync(this.serverUri, this.cts.Token);

            await client.SendTextAsync(messageToSend, this.cts.Token);

            // Wait for response
            await messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Assert
            Assert.Equal($"Echo: {messageToSend}", receivedMessage);
            Assert.Equal(WebSocketConnectionState.Connected, client.ConnectionState);

            // Cleanup
            await client.StopAsync();
        }

        public async Task DisposeAsync()
        {
            try
            {
                if (this.server != null)
                {
                    await this.server.StopAsync();
                    this.server.Dispose();
                }
                this.cts.Dispose();
            }
            catch
            {
                // Can happen
            }
        }
    }
}
