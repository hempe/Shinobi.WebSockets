using System;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Xunit;
using Microsoft.Extensions.Logging;
using Shinobi.WebSockets.Builders;
using Shinobi.WebSockets.Http;
using Shinobi.WebSockets.Exceptions;

namespace Shinobi.WebSockets.UnitTests
{
    public class AuthenticationMethodsTests : IDisposable
    {
        private readonly WebSocketServer testServer;
        private readonly ILoggerFactory loggerFactory;
        private const string TestToken = "demo-token-12345";
        private const int TestPort = 8083;

        public AuthenticationMethodsTests()
        {
            this.loggerFactory = LoggerFactory.Create(builder => builder
                .SetMinimumLevel(LogLevel.Warning) // Reduce noise in tests
                .AddConsole());

            // Create test server with all authentication methods enabled
            this.testServer = WebSocketServerBuilder.Create()
                .UsePort(TestPort)
                .UseLogging(loggerFactory)
                .AllowSubprotocolHeaders("Authorization", "X-API-Key")
                .AllowQueryParamHeaders("Authorization", "X-API-Key") 
                .UseSupportedSubProtocols("|h|", "chat", "v1")
                .OnHandshake(async (context, next, cancellationToken) =>
                {
                    // Test authentication logic
                    var authHeader = context.HttpRequest?.GetHeaderValue("Authorization");
                    if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                    {
                        return HttpResponse.Create(401)
                            .AddHeader("Connection", "close")
                            .WithBody("Authentication required");
                    }

                    var token = authHeader.Substring("Bearer ".Length);
                    if (token != TestToken)
                    {
                        return HttpResponse.Create(401)
                            .AddHeader("Connection", "close")
                            .WithBody("Invalid authentication token");
                    }

                    return await next(context, cancellationToken);
                })
                .OnTextMessage(async (webSocket, message, cancellationToken) =>
                {
                    var responseBytes = Encoding.UTF8.GetBytes($"ECHO: {message}");
                    await webSocket.SendAsync(new ArraySegment<byte>(responseBytes), System.Net.WebSockets.WebSocketMessageType.Text, true, cancellationToken);
                })
                .Build();
        }

        [Fact]
        public async Task Method1_HttpHeaders_ShouldAuthenticateSuccessfully()
        {
            // Arrange
            await this.testServer.StartAsync();

            // Act & Assert - Test C# client with direct headers (Method 1 - Highest Priority)
            using var client = WebSocketClientBuilder.Create()
                .AddHeader("Authorization", $"Bearer {TestToken}")
                .OnTextMessage((ws, message, ct) =>
                {
                    Assert.Equal("ECHO: test-message", message);
                    return default;
                })
                .Build();

            await client.StartAsync(new Uri($"ws://localhost:{TestPort}"));
            
            // Test message exchange
            await client.SendTextAsync("test-message", CancellationToken.None);
            
            // Give time for message processing
            await Task.Delay(100);
            
            Assert.Equal(WebSocketConnectionState.Connected, client.ConnectionState);
            
            await client.StopAsync();
        }

        [Fact] 
        public async Task Method2_SubprotocolHeaders_ShouldAuthenticateSuccessfully()
        {
            // Arrange
            await this.testServer.StartAsync();

            // Act & Assert - Test with subprotocol headers (Method 2 - Medium Priority)
            // Use the convenient UseSubprotocolHeader method
            using var client = WebSocketClientBuilder.Create()
                .UseSubprotocolHeader("Authorization", $"Bearer {TestToken}")
                .OnTextMessage(async (ws, message, ct) =>
                {
                    Assert.Equal("ECHO: subprotocol-test", message);
                })
                .Build();

            await client.StartAsync(new Uri($"ws://localhost:{TestPort}"));
            
            // Send message using SendTextAsync
            await client.SendTextAsync("subprotocol-test", CancellationToken.None);
            await Task.Delay(500);
            
            Assert.Equal(WebSocketConnectionState.Connected, client.ConnectionState);
            await client.StopAsync();
        }

        [Fact]
        public async Task Method3_QueryParameterHeaders_ShouldAuthenticateSuccessfully()
        {
            // Arrange  
            await this.testServer.StartAsync();

            // Act & Assert - Test with query parameters (Method 3 - Lowest Priority)
            var authToken = Uri.EscapeDataString($"Bearer {TestToken}");
            var uri = new Uri($"ws://localhost:{TestPort}?Authorization={authToken}");
            
            using var client = WebSocketClientBuilder.Create()
                .OnTextMessage((ws, message, ct) =>
                {
                    Assert.Equal("ECHO: query-test", message);
                    return default;
                })
                .Build();

            await client.StartAsync(uri);
            await client.SendTextAsync("query-test", CancellationToken.None);
            await Task.Delay(100);
            
            Assert.Equal(WebSocketConnectionState.Connected, client.ConnectionState);
            await client.StopAsync();
        }

        [Fact]
        public async Task AuthenticationPriority_HeadersTakePrecedenceOverQueryParams()
        {
            // Arrange
            await this.testServer.StartAsync();
            
            // Act & Assert - Headers should take precedence over query parameters
            var wrongToken = Uri.EscapeDataString("Bearer wrong-token");
            var uri = new Uri($"ws://localhost:{TestPort}?Authorization={wrongToken}");
            
            using var client = WebSocketClientBuilder.Create()
                .AddHeader("Authorization", $"Bearer {TestToken}") // Correct token in header
                .Build();

            // Should succeed because header (correct token) takes precedence over query param (wrong token)
            await client.StartAsync(uri);
            Assert.Equal(WebSocketConnectionState.Connected, client.ConnectionState);
            await client.StopAsync();
        }

        [Fact]
        public async Task InvalidAuthentication_ShouldRejectConnection()
        {
            // Arrange
            await this.testServer.StartAsync();

            // Act & Assert
            using var client = WebSocketClientBuilder.Create()
                .AddHeader("Authorization", "Bearer invalid-token")
                .Build();

            var exception = await Assert.ThrowsAsync<InvalidHttpResponseCodeException>(() => 
                client.StartAsync(new Uri($"ws://localhost:{TestPort}")));
            
            Assert.Contains("401", exception.Message);
        }

        [Fact]
        public async Task NoAuthentication_ShouldRejectConnection()
        {
            // Arrange
            await this.testServer.StartAsync();

            // Act & Assert
            using var client = WebSocketClientBuilder.Create().Build();

            var exception = await Assert.ThrowsAsync<InvalidHttpResponseCodeException>(() => 
                client.StartAsync(new Uri($"ws://localhost:{TestPort}")));
            
            Assert.Contains("401", exception.Message);
        }

        private static string EncodeBase58(string input)
        {
            const string alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
            
            var bytes = System.Text.Encoding.UTF8.GetBytes(input);
            var num = System.Numerics.BigInteger.Zero;
            
            // Convert bytes to big integer
            foreach (var b in bytes)
            {
                num = num * 256 + b;
            }
            
            // Convert to base58
            var result = string.Empty;
            while (num > 0)
            {
                var remainder = (int)(num % 58);
                result = alphabet[remainder] + result;
                num /= 58;
            }
            
            // Add leading zeros
            foreach (var b in bytes)
            {
                if (b == 0)
                    result = '1' + result;
                else
                    break;
            }
            
            return string.IsNullOrEmpty(result) ? "1" : result;
        }

        public void Dispose()
        {
            this.testServer?.StopAsync().GetAwaiter().GetResult();
            this.testServer?.Dispose();
            this.loggerFactory?.Dispose();
        }
    }
}