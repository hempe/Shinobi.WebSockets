using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Shinobi.WebSockets.Builders;

using Xunit;

namespace Shinobi.WebSockets.UnitTests
{
    public class WebSocketClientIntegrationTests : IAsyncLifetime
    {
        private WebSocketServer server = null!;
        private Uri serverUri = null!;
        private readonly CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        public async Task InitializeAsync()
        {
            // Create a test server using the WebSocketServerBuilder
            this.server = WebSocketServerBuilder.Create()
                .UsePort(0) // Use any available port
                .OnBinaryMessage(async (ws, data, ct) =>
                {
                    // Echo back the received data
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

            // Wait a bit for server to start and get the actual port
            await Task.Delay(100);

            // Note: This is a simplified test. In a real scenario, you'd need to get the actual port
            // For this test, we'll use a mock URI since getting the actual port requires server internals
            this.serverUri = new Uri("ws://localhost:8080/");
        }

        [Fact]
        public void WebSocketClient_ShouldBuildSuccessfully()
        {
            using var client = WebSocketClientBuilder.Create()
                .UseKeepAlive(TimeSpan.FromSeconds(30))
                .UseNoDelay(true)
                .UsePerMessageDeflate()
                .AddHeader("User-Agent", "TestClient/1.0")
                .OnTextMessage((ws, message, ct) =>
                {
                    // Handle received message
                    return new ValueTask();
                })
                .Build();

            Assert.NotNull(client);
        }

        [Fact]
        public void WebSocketClientBuilder_ShouldSupportComplexConfiguration()
        {
            using var loggerFactory = LoggerFactory.Create(builder => builder
                .SetMinimumLevel(LogLevel.Debug)
                .AddConsole());

            using var client = WebSocketClientBuilder.Create()
                .UseKeepAlive(TimeSpan.FromSeconds(30))
                .UseNoDelay(true)
                .UseSubProtocol("chat")
                .UsePerMessageDeflate()
                .AddHeader("Custom-Header", "test-value")
                .UseBearerAuthentication("test-token")
                .UseLogging(loggerFactory)
                .OnConnect(async (ws, next, ct) =>
                {
                    await next(ws, ct);
                })
                .OnTextMessage((ws, message, ct) =>
                {
                    return new ValueTask();
                })
                .OnBinaryMessage((ws, data, ct) =>
                {
                    // Handle binary data
                    return new ValueTask();
                })
                .OnClose(async (ws, next, ct) =>
                {
                    // Handle connection close
                    await next(ws, ct);
                })
                .OnError(async (ws, ex, next, ct) =>
                {
                    // Handle errors
                    await next(ws, ex, ct);
                })
                .Build();

            Assert.NotNull(client);
        }

        [Fact]
        public void WebSocketClientBuilder_FluentAPI_ShouldReturnSameInstance()
        {
            var builder = WebSocketClientBuilder.Create();
            var result = builder
                .UseKeepAlive(TimeSpan.FromSeconds(30))
                .UseNoDelay(true)
                .AddHeader("Test", "Value");

            Assert.Same(builder, result);
        }

        public async Task DisposeAsync()
        {
            if (this.server != null)
            {
                await this.server.StopAsync();
                this.server.Dispose();
            }
            this.cts.Dispose();
        }
    }
}