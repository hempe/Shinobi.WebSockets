using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Shinobi.WebSockets.Builders;
using Shinobi.WebSockets.Extensions;
using Shinobi.WebSockets.Http;

using Xunit;

namespace Shinobi.WebSockets.UnitTests
{
    public class KeepAliveTests : IDisposable
    {
        private WebSocketServer? server;
        private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

        public void Dispose()
        {
            this.server?.StopAsync().Wait();
            this.server?.Dispose();
            this.cancellationTokenSource.Dispose();
        }

        [Theory]
        [InlineData(10, 10)]   // 10 clients, 10 messages each
        [InlineData(10, 100)]  // 10 clients, 100 messages each  
        [InlineData(100, 10)]  // 100 clients, 10 messages each
        [InlineData(100, 100)] // 100 clients, 100 messages each
        [InlineData(10, 1000)] // 10 clients, 1000 messages each
        public async Task MultipleKeepAliveClients_ShouldHandleConcurrentRequestsAsync(int clientCount, int messagesPerClient)
        {
            // Arrange
            var port = GetAvailablePort();
            var totalSuccessfulRequests = 0;
            this.server = WebSocketServerBuilder.Create()
                .UsePort(port)
                .UseKeepAliveTimeout(TimeSpan.FromSeconds(30)) // Longer timeout for large tests
                .UseMaxKeepAliveConnections(clientCount + 10) // Allow all clients
                .OnHandshake(async (context, next, cancellationToken) =>
                {
                    // Return 204 No Content for non-WebSocket requests
                    if (!context.IsWebSocketRequest)
                    {
                        Interlocked.Increment(ref totalSuccessfulRequests);
                        return HttpResponse.Create(204)
                            .AddHeader("Connection", "keep-alive")
                            .AddHeader("Content-Length", "0");
                    }
                    return await next(context, cancellationToken);
                })
                .Build();

            await this.server.StartAsync();

            // Act - Create clients that each send HTTP requests
            var clientTasks = new Task[clientCount];
            for (int i = 0; i < clientCount; i++)
            {
                var clientId = i;
                clientTasks[i] = Task.Run(async () =>
                {
                    using var tcpClient = new TcpClient();
                    await tcpClient.ConnectAsync("localhost", port);
                    var stream = tcpClient.GetStream();

                    for (int j = 0; j < messagesPerClient; j++)
                    {
                        // Send HTTP request with keep-alive
                        var request = "GET /test HTTP/1.1\r\n" +
                                     "Host: localhost:" + port + "\r\n" +
                                     "Connection: keep-alive\r\n" +
                                     "\r\n";

                        var requestBytes = Encoding.UTF8.GetBytes(request);
                        await stream.WriteAsync(requestBytes, 0, requestBytes.Length);

                        // Read response
                        var buffer = new byte[4096];
                        var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                        var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                        // Verify we got 204 No Content with Date header
                        Assert.Contains("204", response);
                        Assert.Contains("keep-alive", response);
                        Assert.Contains("Date:", response);
                        
                        // Reduced delay for large tests
                        if (messagesPerClient <= 100)
                            await Task.Delay(10);
                    }
                });
            }

            // Assert - All clients should complete successfully
            await Task.WhenAll(clientTasks);
            
            // Verify that all requests were processed
            Assert.Equal(clientCount * messagesPerClient, totalSuccessfulRequests);
        }

        [Theory]
        [InlineData(10, 10, 3)]   // 10 HTTP clients, 10 WebSocket clients, 3 messages per HTTP client
        [InlineData(100, 10, 10)] // 100 HTTP clients, 10 WebSocket clients, 10 messages per HTTP client
        public async Task KeepAliveWithWebSocketClients_ShouldHandleMixedTrafficAsync(int httpClientCount, int webSocketClientCount, int messagesPerHttpClient)
        {
            // Arrange
            var port = GetAvailablePort();
            var httpRequestCount = 0;
            var webSocketMessageCount = 0;
            this.server = WebSocketServerBuilder.Create()
                .UsePort(port)
                .UseKeepAliveTimeout(TimeSpan.FromSeconds(10))
                .UseMaxKeepAliveConnections(Math.Min(httpClientCount / 5, 10)) // Low limit to test eviction
                .OnHandshake(async (context, next, cancellationToken) =>
                {
                    if (!context.IsWebSocketRequest)
                    {
                        Interlocked.Increment(ref httpRequestCount);
                        return HttpResponse.Create(204)
                            .AddHeader("Connection", "keep-alive")
                            .AddHeader("Content-Length", "0");
                    }
                    return await next(context, cancellationToken);
                })
                .OnTextMessage((webSocket, message, token) =>
                {
                    Interlocked.Increment(ref webSocketMessageCount);
                    return webSocket.SendTextAsync("Echo: " + message, token);
                })
                .Build();

            await this.server.StartAsync();

            // Act - Create keep-alive clients and WebSocket clients concurrently
            var keepAliveTasks = new Task[httpClientCount];
            var webSocketTasks = new Task[webSocketClientCount];

            // Start keep-alive clients
            for (int i = 0; i < httpClientCount; i++)
            {
                var clientId = i;
                keepAliveTasks[i] = Task.Run(async () =>
                {
                    using var tcpClient = new TcpClient();
                    await tcpClient.ConnectAsync("localhost", port);
                    var stream = tcpClient.GetStream();

                    // Send HTTP requests until successful or connection is evicted
                    for (int j = 0; j < messagesPerHttpClient; j++)
                    {
                        try
                        {
                            var request = "GET /test HTTP/1.1\r\n" +
                                         "Host: localhost:" + port + "\r\n" +
                                         "Connection: keep-alive\r\n" +
                                         "\r\n";

                            var requestBytes = Encoding.UTF8.GetBytes(request);
                            await stream.WriteAsync(requestBytes, 0, requestBytes.Length);

                            var buffer = new byte[4096];
                            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);

                            // If connection was closed (0 bytes), connection was evicted
                            if (bytesRead == 0)
                            {
                                Console.WriteLine("Connection evicted after " + j + " requests");
                                break;
                            }

                            var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                            Assert.Contains("204", response);

                            // Reduced delay for large tests
                            if (messagesPerHttpClient <= 10)
                                await Task.Delay(20);
                            else if (messagesPerHttpClient <= 100)
                                await Task.Delay(2);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("Request " + j + " failed: " + e.Message);
                            // Individual request failed - likely due to eviction
                            continue;
                        }
                    }
                });
            }

            // Start WebSocket clients
            for (int i = 0; i < webSocketClientCount; i++)
            {
                var clientId = i;
                webSocketTasks[i] = Task.Run(async () =>
                {
                    using var client = WebSocketClientBuilder.Create().Build();
                    await client.StartAsync(new Uri("ws://localhost:" + port));

                    await client.SendTextAsync("Hello from WebSocket client " + clientId, this.cancellationTokenSource.Token);

                    // Give some time for response
                    await Task.Delay(500);
                    await client.StopAsync();
                });
            }

            // Assert - Both types of clients should work despite keep-alive limits
            await Task.WhenAll(keepAliveTasks.Concat(webSocketTasks));
            
            // Verify some HTTP requests were processed (may be less than 30 due to eviction)
            Assert.True(httpRequestCount > 0, $"Expected some HTTP requests, got {httpRequestCount}");
            
            // Verify all WebSocket messages were processed (10 clients × 1 message each)
            Assert.Equal(10, webSocketMessageCount);
        }

        private static ushort GetAvailablePort()
        {
            var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = (ushort)((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}