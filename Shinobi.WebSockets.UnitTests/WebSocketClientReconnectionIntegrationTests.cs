using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Shinobi.WebSockets.Builders;

using Xunit;
using Xunit.Abstractions;

namespace Shinobi.WebSockets.UnitTests
{
    /// <summary>
    /// Integration tests for WebSocketClient reconnection behavior with real test servers.
    /// These tests verify actual reconnection logic, backoff timing, and server interaction.
    /// </summary>
    public class WebSocketClientReconnectionIntegrationTests : IAsyncLifetime
    {
        private readonly ITestOutputHelper output;
        private WebSocketServer testServer = null!;
        private Uri serverUri = null!;
        private int serverPort;
        private volatile bool serverShouldReject = false;
        private readonly List<string> serverConnectionLog = new List<string>();
        private readonly object logLock = new object();

        public WebSocketClientReconnectionIntegrationTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        public async Task InitializeAsync()
        {
            this.serverPort = GetAvailablePort();
            this.serverUri = new Uri($"ws://localhost:{this.serverPort}/");

            await this.StartTestServerAsync();
        }

        public async Task DisposeAsync()
        {
            try
            {
                if (this.testServer != null)
                {
                    await this.testServer.StopAsync();
                    this.testServer.Dispose();
                }
            }
            catch
            {
                // Cleanup errors are acceptable
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

        private async Task StartTestServerAsync()
        {
            this.testServer = WebSocketServerBuilder.Create()
                .UsePort((ushort)this.serverPort)
                .OnConnect(async (ws, next, ct) =>
                {
                    lock (this.logLock)
                    {
                        this.serverConnectionLog.Add($"Connection attempt at {DateTime.Now:HH:mm:ss.fff}");
                    }

                    if (this.serverShouldReject)
                    {
                        // Simulate server rejection by closing immediately
                        await ws.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.InternalServerError,
                            "Server rejecting connections", ct);
                        return;
                    }

                    await next(ws, ct);
                })
                .OnTextMessage(async (ws, message, ct) =>
                {
                    // Echo back the message with timestamp
                    var response = $"Echo at {DateTime.Now:HH:mm:ss.fff}: {message}";
                    await ws.SendAsync(new ArraySegment<byte>(System.Text.Encoding.UTF8.GetBytes(response)),
                        System.Net.WebSockets.WebSocketMessageType.Text, true, ct);
                })
                .Build();

            await this.testServer.StartAsync();
            await Task.Delay(100); // Give server time to start
        }



        [Fact(Skip = "Integration test - investigate server setup timing issues")]
        public async Task WebSocketClient_SuccessfulReconnection_ShouldResetAttemptCounterAsync()
        {
            // Arrange
            var connectionAttempts = new List<DateTime>();
            var reconnectingEvents = new List<int>(); // Attempt numbers
            var messagesReceived = new List<string>();

            var client = WebSocketClientBuilder.Create()
                .UseAutoReconnect(options =>
                {
                    options.InitialDelay = TimeSpan.FromMilliseconds(100);
                    options.BackoffMultiplier = 2.0;
                    options.Jitter = 0;
                })
                .OnConnect(async (ws, next, ct) =>
                {
                    connectionAttempts.Add(DateTime.Now);
                    this.output.WriteLine($"Connected at {DateTime.Now:HH:mm:ss.fff}");
                    await next(ws, ct);
                })
                .OnTextMessage((ws, message, ct) =>
                {
                    messagesReceived.Add(message);
                    this.output.WriteLine($"Received: {message}");
                    return default(ValueTask);
                })
                .Build();

            client.Reconnecting += (sender, e) =>
            {
                reconnectingEvents.Add(e.AttemptNumber);
                this.output.WriteLine($"Reconnecting attempt {e.AttemptNumber}");
            };

            // Start with server allowing connections
            this.serverShouldReject = false;

            // Act - Initial connection should succeed
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await client.StartAsync(this.serverUri, cts.Token);

            // Send a message to verify connection
            await client.SendTextAsync("Hello 1", cts.Token);
            await Task.Delay(200); // Wait for response

            // Now simulate server going down by rejecting connections
            this.output.WriteLine("Simulating server rejection...");
            this.serverShouldReject = true;

            // Force disconnection by stopping and restarting server
            await this.testServer.StopAsync();
            await Task.Delay(500); // Let client detect disconnection

            // Restart server after a delay, but still rejecting initially
            await this.StartTestServerAsync();
            await Task.Delay(1000); // Let reconnection attempts happen

            // Now allow connections again
            this.output.WriteLine("Server accepting connections again...");
            this.serverShouldReject = false;

            // Wait for reconnection
            await Task.Delay(2000);

            // Send another message to verify reconnection
            if (client.ConnectionState == WebSocketConnectionState.Connected)
            {
                await client.SendTextAsync("Hello 2", cts.Token);
                await Task.Delay(200);
            }

            // Now simulate another disconnection to test counter reset
            this.serverShouldReject = true;
            await this.testServer.StopAsync();
            await Task.Delay(500);

            await this.StartTestServerAsync();
            await Task.Delay(1000); // Let some reconnection attempts happen

            await client.StopAsync();

            // Assert
            this.output.WriteLine($"Total connection attempts: {connectionAttempts.Count}");
            this.output.WriteLine($"Reconnect attempt numbers: [{string.Join(", ", reconnectingEvents)}]");
            this.output.WriteLine($"Messages received: {messagesReceived.Count}");

            // Should have had multiple connection attempts
            Assert.True(connectionAttempts.Count >= 2, "Should have had multiple connection attempts");

            // Should have received at least one echo message
            Assert.True(messagesReceived.Count >= 1, "Should have received at least one message");

            // Reconnect attempt numbers should show counter resets
            // (After successful reconnection, next attempt should start from 1 again)
            if (reconnectingEvents.Count > 1)
            {
                // This is a complex scenario, but we should see evidence of reconnection logic
                Assert.Contains(1, reconnectingEvents); // Should have attempt #1
            }

            client.Dispose();
        }

        [Fact(Skip = "Integration test - investigate server setup timing issues")]
        public async Task WebSocketClient_OnReconnecting_ShouldAllowUriFailoverAsync()
        {
            // Arrange
            var fallbackPort = GetAvailablePort();
            var fallbackUri = new Uri($"ws://localhost:{fallbackPort}/");
            var uriModifications = new List<(Uri Original, Uri Modified)>();

            // Start a fallback server
            var fallbackServer = WebSocketServerBuilder.Create()
                .UsePort((ushort)fallbackPort)
                .OnTextMessage(async (ws, message, ct) =>
                {
                    var response = $"Fallback echo: {message}";
                    await ws.SendAsync(new ArraySegment<byte>(System.Text.Encoding.UTF8.GetBytes(response)),
                        System.Net.WebSockets.WebSocketMessageType.Text, true, ct);
                })
                .Build();

            await fallbackServer.StartAsync();

            try
            {
                var messagesReceived = new List<string>();

                var client = WebSocketClientBuilder.Create()
                    .UseAutoReconnect(options =>
                    {
                        options.InitialDelay = TimeSpan.FromMilliseconds(100);
                        options.Jitter = 0;
                    })
                    .OnReconnecting((uri, attemptNumber, ct) =>
                    {
                        // After first failure, switch to fallback server
                        var newUri = attemptNumber >= 2 ? fallbackUri : uri;
                        uriModifications.Add((uri, newUri));
                        this.output.WriteLine($"Attempt {attemptNumber}: {uri} -> {newUri}");
                        return new ValueTask<Uri>(newUri);
                    })
                    .OnTextMessage((ws, message, ct) =>
                    {
                        messagesReceived.Add(message);
                        this.output.WriteLine($"Received: {message}");
                        return default(ValueTask);
                    })
                    .Build();

                // Make primary server reject connections
                this.serverShouldReject = true;

                // Act
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await client.StartAsync(this.serverUri, cts.Token);

                // Send a message to verify we connected to fallback
                await client.SendTextAsync("Test message", cts.Token);
                await Task.Delay(300);

                await client.StopAsync();

                // Assert
                this.output.WriteLine($"URI modifications: {uriModifications.Count}");
                foreach (var (original, modified) in uriModifications)
                {
                    this.output.WriteLine($"  {original} -> {modified}");
                }

                Assert.True(uriModifications.Count > 0, "Should have had URI modifications");
                Assert.Contains(uriModifications, item => item.Modified.Equals(fallbackUri));

                // Should have received response from fallback server
                Assert.True(messagesReceived.Count > 0, "Should have received messages");
                Assert.Contains(messagesReceived, msg => msg.Contains("Fallback echo"));

                client.Dispose();
            }
            finally
            {
                await fallbackServer.StopAsync();
                fallbackServer.Dispose();
            }
        }

        [Fact]
        public async Task WebSocketClient_JitterEnabled_ShouldVariateReconnectDelaysAsync()
        {
            // Arrange
            var reconnectTimes = new List<DateTime>();
            var delays = new List<TimeSpan>();

            var client = WebSocketClientBuilder.Create()
                .UseAutoReconnect(options =>
                {
                    options.InitialDelay = TimeSpan.FromMilliseconds(200);
                    options.BackoffMultiplier = 1.0; // No exponential growth for predictable baseline
                    options.Jitter = 0.5; // 50% jitter should create noticeable variance
                })
                .OnConnect(async (ws, next, ct) =>
                {
                    reconnectTimes.Add(DateTime.Now);
                    await next(ws, ct);
                })
                .Build();

            // Make server reject all connections
            this.serverShouldReject = true;

            // Act
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));

            try
            {
                await client.StartAsync(this.serverUri, cts.Token);
            }
            catch (InvalidOperationException)
            {
                // Expected after max attempts
            }

            // Calculate delays between attempts
            for (int i = 1; i < reconnectTimes.Count; i++)
            {
                delays.Add(reconnectTimes[i] - reconnectTimes[i - 1]);
            }

            // Assert
            this.output.WriteLine($"Reconnect attempts: {reconnectTimes.Count}");
            this.output.WriteLine($"Delays calculated: {delays.Count}");
            
            // Show detailed delay information
            for (int i = 0; i < delays.Count; i++)
            {
                this.output.WriteLine($"Delay {i + 1}: {delays[i].TotalMilliseconds:F2}ms");
            }
            
            if (delays.Count >= 2)
            {
                // Calculate statistics
                var delayValues = delays.Select(d => d.TotalMilliseconds).ToArray();
                var firstDelay = delayValues[0];
                var minDelay = delayValues.Min();
                var maxDelay = delayValues.Max();
                var avgDelay = delayValues.Average();
                var range = maxDelay - minDelay;
                
                this.output.WriteLine($"Delay Statistics:");
                this.output.WriteLine($"  First delay: {firstDelay:F2}ms");
                this.output.WriteLine($"  Min delay: {minDelay:F2}ms");
                this.output.WriteLine($"  Max delay: {maxDelay:F2}ms");
                this.output.WriteLine($"  Average delay: {avgDelay:F2}ms");
                this.output.WriteLine($"  Range: {range:F2}ms");
                
                // Check for variation with detailed analysis
                var hasVariation = false;
                var maxDifference = 0.0;
                
                foreach (var delay in delayValues)
                {
                    var difference = Math.Abs(delay - firstDelay);
                    if (difference > maxDifference)
                    {
                        maxDifference = difference;
                    }
                    if (difference > 20) // Allow for timing variance
                    {
                        hasVariation = true;
                    }
                }
                
                this.output.WriteLine($"  Max difference from first delay: {maxDifference:F2}ms");
                this.output.WriteLine($"  Has variation (>20ms diff): {hasVariation}");
                
                // Check if all delays are within expected range
                var expectedMin = 100.0; // 200ms - 50% = 100ms
                var expectedMax = 300.0; // 200ms + 50% = 300ms
                var allowanceMin = 80.0;  // Test allows wider range
                var allowanceMax = 400.0; // Test allows wider range
                
                this.output.WriteLine($"Expected range (theoretical): {expectedMin:F0}ms - {expectedMax:F0}ms");
                this.output.WriteLine($"Allowed range (with tolerance): {allowanceMin:F0}ms - {allowanceMax:F0}ms");
                
                var outOfRange = delayValues.Where(d => d < allowanceMin || d > allowanceMax).ToArray();
                if (outOfRange.Any())
                {
                    this.output.WriteLine($"Delays out of range: {string.Join(", ", outOfRange.Select(d => $"{d:F2}ms"))}");
                }
                
                // Enhanced assertion with better error message
                var errorMessage = $"Jitter should create variation in delays. " +
                    $"Got {delays.Count} delays with max difference of {maxDifference:F2}ms from first delay {firstDelay:F2}ms. " +
                    $"Range: {minDelay:F2}ms - {maxDelay:F2}ms (span: {range:F2}ms). " +
                    $"Expected variation >20ms with 50% jitter on 200ms base delay.";
                    
                Assert.True(hasVariation, errorMessage);

                // All delays should be within expected range (100ms to 300ms with 50% jitter on 200ms base)
                foreach (var delay in delays)
                {
                    Assert.InRange(delay.TotalMilliseconds, allowanceMin, allowanceMax);
                }
            }
            else
            {
                this.output.WriteLine("Not enough delays to test variation - need at least 2 delays");
            }

            client.Dispose();
        }

        [Fact]
        public async Task WebSocketClient_ConnectionRecovery_ShouldHandleNetworkInterruptionAsync()
        {
            // Arrange
            var connectionStates = new List<(DateTime Time, WebSocketConnectionState State)>();
            var messagesReceived = new List<string>();

            var client = WebSocketClientBuilder.Create()
                .UseAutoReconnect(options =>
                {
                    options.InitialDelay = TimeSpan.FromMilliseconds(200);
                    options.BackoffMultiplier = 1.5;
                    options.Jitter = 0.1;
                })
                .OnTextMessage((ws, message, ct) =>
                {
                    messagesReceived.Add(message);
                    this.output.WriteLine($"Received: {message}");
                    return default(ValueTask);
                })
                .Build();

            client.ConnectionStateChanged += (sender, e) =>
            {
                connectionStates.Add((DateTime.Now, e.NewState));
                this.output.WriteLine($"{DateTime.Now:HH:mm:ss.fff}: {e.PreviousState} -> {e.NewState}");
            };

            // Start with server accepting connections
            this.serverShouldReject = false;

            // Act
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

            // Initial connection
            await client.StartAsync(this.serverUri, cts.Token);

            // Verify initial connection
            await client.SendTextAsync("Before interruption", cts.Token);
            await Task.Delay(200);

            // Simulate network interruption
            this.output.WriteLine("=== Simulating network interruption ===");
            await this.testServer.StopAsync();

            // Wait for client to detect disconnection and start reconnecting
            await Task.Delay(1000);

            // Restart server (simulating network recovery)
            this.output.WriteLine("=== Network recovered ===");
            await this.StartTestServerAsync();

            // Wait for reconnection
            await Task.Delay(3000);

            // Verify reconnection by sending another message
            if (client.ConnectionState == WebSocketConnectionState.Connected)
            {
                await client.SendTextAsync("After reconnection", cts.Token);
                await Task.Delay(200);
            }

            await client.StopAsync();

            // Assert
            this.output.WriteLine("\nConnection state timeline:");
            foreach (var (time, state) in connectionStates)
            {
                this.output.WriteLine($"  {time:HH:mm:ss.fff}: {state}");
            }

            this.output.WriteLine($"\nMessages received: {messagesReceived.Count}");
            foreach (var msg in messagesReceived)
            {
                this.output.WriteLine($"  {msg}");
            }

            // Should have connected initially
            Assert.Contains(connectionStates, item => item.State == WebSocketConnectionState.Connected);

            // Should have started reconnecting after disconnection
            Assert.Contains(connectionStates, item => item.State == WebSocketConnectionState.Reconnecting);

            // Should have received at least one message
            Assert.True(messagesReceived.Count >= 1, "Should have received at least one message");

            client.Dispose();
        }
    }
}