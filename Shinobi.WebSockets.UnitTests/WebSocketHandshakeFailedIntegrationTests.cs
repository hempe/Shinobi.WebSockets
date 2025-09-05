using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;
using Shinobi.WebSockets.Builders;
using Shinobi.WebSockets.Exceptions;
using Shinobi.WebSockets.Http;

namespace Shinobi.WebSockets.UnitTests
{
    public class WebSocketHandshakeFailedIntegrationTests : IDisposable
    {
        private readonly ILoggerFactory loggerFactory;
        private readonly ushort testPort;

        public WebSocketHandshakeFailedIntegrationTests()
        {
            this.loggerFactory = LoggerFactory.Create(builder => builder
                .SetMinimumLevel(LogLevel.Warning)
                .AddConsole());

            this.testPort = GetAvailablePort();
        }

        public void Dispose()
        {
            this.loggerFactory?.Dispose();
        }

        private static ushort GetAvailablePort()
        {
            using var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork,
                                                            System.Net.Sockets.SocketType.Stream,
                                                            System.Net.Sockets.ProtocolType.Tcp);
            socket.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
            return (ushort)((System.Net.IPEndPoint)socket.LocalEndPoint!).Port;
        }

        [Fact]
        public async Task WebSocketClient_WithInvalidSecWebSocketAccept_ThrowsWebSocketHandshakeFailedExceptionAsync()
        {
            // Arrange - Create a server that returns wrong Sec-WebSocket-Accept header
            using var testServer = WebSocketServerBuilder.Create()
                .UsePort(this.testPort)
                .OnHandshake((context, next, cancellationToken) =>
                {
                    // Get the normal handshake response first
                    var response = next(context, cancellationToken);

                    // Corrupt the Sec-WebSocket-Accept header on purpose
                    var corruptResponse = response.Result
                        .RemoveHeader("Sec-WebSocket-Accept")
                        .AddHeader("Sec-WebSocket-Accept", "INVALID_ACCEPT_STRING_FOR_TESTING");

                    return new ValueTask<HttpResponse>(corruptResponse);
                })
                .Build();

            await testServer.StartAsync();

            using var client = WebSocketClientBuilder.Create()
                .Build();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<WebSocketHandshakeFailedException>(() =>
                client.StartAsync(new Uri($"ws://localhost:{this.testPort}")));

            // Verify exception details
            Assert.Contains("Handshake failed", exception.Message);
            Assert.Contains("accept string", exception.Message);
            Assert.Contains("INVALID_ACCEPT_STRING_FOR_TESTING", exception.Message);
        }

        [Fact]
        public async Task WebSocketClient_WithMissingSecWebSocketAccept_ThrowsWebSocketHandshakeFailedExceptionAsync()
        {
            // Arrange - Create a server that omits Sec-WebSocket-Accept header completely
            using var testServer = WebSocketServerBuilder.Create()
                .UsePort(this.testPort)
                .OnHandshake((context, next, cancellationToken) =>
                {
                    // Get the normal handshake response first
                    var response = next(context, cancellationToken);

                    // Remove the Sec-WebSocket-Accept header entirely
                    var responseWithoutAccept = response.Result
                        .RemoveHeader("Sec-WebSocket-Accept");

                    return ValueTask.FromResult(responseWithoutAccept);
                })
                .Build();

            await testServer.StartAsync();

            using var client = WebSocketClientBuilder.Create()
                .Build();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<WebSocketHandshakeFailedException>(() =>
                client.StartAsync(new Uri($"ws://localhost:{this.testPort}")));

            // Verify exception details
            Assert.Contains("Handshake failed", exception.Message);
        }

        [Fact]
        public async Task WebSocketClient_WithServerException_ThrowsWebSocketHandshakeFailedExceptionAsync()
        {
            // Arrange - Create a server that throws an exception during handshake
            using var testServer = WebSocketServerBuilder.Create()
                .UsePort(this.testPort)
                .OnHandshake((context, next, cancellationToken) =>
                {
                    // Simulate a server-side exception during handshake
                    throw new InvalidOperationException("Simulated server error during handshake");
                })
                .Build();

            await testServer.StartAsync();

            using var client = WebSocketClientBuilder.Create()
                .Build();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidHttpResponseCodeException>(() =>
                client.StartAsync(new Uri($"ws://localhost:{this.testPort}")));

            Assert.Contains("Invalid status code: 50", exception.Message);
        }

        [Fact]
        public async Task WebSocketClient_WithValidHandshake_DoesNotThrowExceptionAsync()
        {
            // Arrange - Create a server with normal, valid handshake (control test)
            using var testServer = WebSocketServerBuilder.Create()
                .UsePort(this.testPort)
                .OnConnect((webSocket, next, cancellationToken) =>
                {
                    // Just close the connection immediately after successful handshake
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(100);
                        webSocket.Dispose();
                    });
                    return next(webSocket, cancellationToken);
                })
                .Build();

            await testServer.StartAsync();

            using var client = WebSocketClientBuilder.Create()
                .Build();

            // Act - Should not throw during handshake
            await client.StartAsync(new Uri($"ws://localhost:{this.testPort}"));

            // Assert - Successfully created WebSocket connection
            Assert.Equal(WebSocketConnectionState.Connected, client.ConnectionState);
        }
    }
}