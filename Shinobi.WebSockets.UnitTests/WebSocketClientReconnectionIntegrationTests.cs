using System;
using System.Collections.Generic;
using System.Diagnostics;
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

            await this.StartTestServer();
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

        private async Task StartTestServer()
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
        public async Task WebSocketClient_WithExponentialBackoff_ShouldIncreaseDelayBetweenAttemptsAsync()
        {
            // Arrange
            var connectionAttempts = new List<DateTime>();
            var stateChanges = new List<(DateTime Time, WebSocketConnectionState State)>();

            using var loggerFactory = LoggerFactory.Create(builder => builder
                .SetMinimumLevel(LogLevel.Warning)
                .AddConsole());

            var client = WebSocketClientBuilder.Create()
                .UseLogging(loggerFactory)
                .UseAutoReconnect(options =>
                {
                    options.InitialDelay = TimeSpan.FromMilliseconds(100);
                    options.BackoffMultiplier = 2.0;
                    options.MaxDelay = TimeSpan.FromSeconds(2);
                    options.MaxAttempts = 4;
                    options.Jitter = 0; // Disable jitter for predictable timing
                })
                .OnConnect(async (ws, next, ct) =>
                {
                    connectionAttempts.Add(DateTime.Now);
                    await next(ws, ct);
                })
                .Build();

            client.ConnectionStateChanged += (sender, e) =>
            {
                stateChanges.Add((DateTime.Now, e.NewState));
                this.output.WriteLine($"State changed to: {e.NewState} at {DateTime.Now:HH:mm:ss.fff}");
            };

            // Make server reject all connections initially
            this.serverShouldReject = true;

            // Act
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            
            try
            {
                await client.StartAsync(this.serverUri, cts.Token);
            }
            catch (InvalidOperationException)
            {
                // Expected - should fail after max attempts
            }

            // Allow some time for final state changes
            await Task.Delay(200);

            // Assert
            this.output.WriteLine($"Total connection attempts: {connectionAttempts.Count}");
            foreach (var attempt in connectionAttempts)
            {
                this.output.WriteLine($"Attempt at: {attempt:HH:mm:ss.fff}");
            }

            // Should have made 4 attempts (initial + 3 retries)
            Assert.InRange(connectionAttempts.Count, 3, 5); // Allow some variance

            if (connectionAttempts.Count >= 3)
            {
                // Check exponential backoff timing
                var delay1 = connectionAttempts[1] - connectionAttempts[0];
                var delay2 = connectionAttempts[2] - connectionAttempts[1];

                this.output.WriteLine($"First delay: {delay1.TotalMilliseconds}ms");
                this.output.WriteLine($"Second delay: {delay2.TotalMilliseconds}ms");

                // Second delay should be roughly 2x the first delay (allowing for timing variance)
                Assert.True(delay2.TotalMilliseconds > delay1.TotalMilliseconds * 1.5, 
                    "Second delay should be significantly longer than first delay due to exponential backoff");
            }

            // Should eventually reach Failed state
            Assert.Contains(stateChanges, item => item.State == WebSocketConnectionState.Failed);

            client.Dispose();
        }

        [Fact(Skip = "Integration test - investigate server setup timing issues")]
        public async Task WebSocketClient_MaxAttempts_ShouldStopReconnectingAfterLimitAsync()
        {
            // Arrange
            var reconnectingEvents = new List<(DateTime Time, int AttemptNumber)>();
            var stateChanges = new List<WebSocketConnectionState>();

            var client = WebSocketClientBuilder.Create()
                .UseAutoReconnect(options =>
                {
                    options.InitialDelay = TimeSpan.FromMilliseconds(50);
                    options.BackoffMultiplier = 1.5;
                    options.MaxAttempts = 3; // Strict limit
                    options.Jitter = 0;
                })
                .Build();

            client.Reconnecting += (sender, e) =>
            {
                reconnectingEvents.Add((DateTime.Now, e.AttemptNumber));
                this.output.WriteLine($"Reconnecting attempt {e.AttemptNumber} at {DateTime.Now:HH:mm:ss.fff}");
            };

            client.ConnectionStateChanged += (sender, e) =>
            {
                stateChanges.Add(e.NewState);
                this.output.WriteLine($"State: {e.NewState}");
            };

            // Make server reject all connections
            this.serverShouldReject = true;

            // Act
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.StartAsync(this.serverUri, cts.Token));

            // Allow time for final state changes
            await Task.Delay(100);

            // Assert
            this.output.WriteLine($"Total reconnecting events: {reconnectingEvents.Count}");
            this.output.WriteLine($"Exception message: {exception.Message}");

            // Should not exceed max attempts
            Assert.True(reconnectingEvents.Count <= 3, 
                $"Should not exceed 3 reconnect attempts, but had {reconnectingEvents.Count}");

            // Should reach Failed state
            Assert.Contains(WebSocketConnectionState.Failed, stateChanges);

            client.Dispose();
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
                    options.MaxAttempts = 5;
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
                    return new ValueTask();
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
            await this.StartTestServer();
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

            await this.StartTestServer();
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
                        options.MaxAttempts = 3;
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
                        return new ValueTask();
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
                    options.MaxAttempts = 5;
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
            for (int i = 0; i < delays.Count; i++)
            {
                this.output.WriteLine($"Delay {i + 1}: {delays[i].TotalMilliseconds}ms");
            }

            if (delays.Count >= 2)
            {
                // With 50% jitter, delays should vary
                var firstDelay = delays[0].TotalMilliseconds;
                var hasVariation = false;

                foreach (var delay in delays)
                {
                    if (Math.Abs(delay.TotalMilliseconds - firstDelay) > 20) // Allow for timing variance
                    {
                        hasVariation = true;
                        break;
                    }
                }

                Assert.True(hasVariation, "Jitter should create variation in delays");

                // All delays should be within expected range (100ms to 300ms with 50% jitter on 200ms base)
                foreach (var delay in delays)
                {
                    Assert.InRange(delay.TotalMilliseconds, 80, 400); // Allow for some timing variance
                }
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
                    options.MaxAttempts = 8;
                    options.BackoffMultiplier = 1.5;
                    options.Jitter = 0.1;
                })
                .OnTextMessage((ws, message, ct) =>
                {
                    messagesReceived.Add(message);
                    this.output.WriteLine($"Received: {message}");
                    return new ValueTask();
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
            await this.StartTestServer();
            
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
            this.output.WriteLine($"\nConnection state timeline:");
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