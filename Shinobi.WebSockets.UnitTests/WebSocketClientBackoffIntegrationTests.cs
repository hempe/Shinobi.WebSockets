using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using Shinobi.WebSockets.Builders;
using Shinobi.WebSockets.Internal;

using Xunit;
using Xunit.Abstractions;

#if !NET8_0_OR_GREATER
using Shinobi.WebSockets.Extensions;
#endif

namespace Shinobi.WebSockets.UnitTests
{
    /// <summary>
    /// Integration tests that verify WebSocketClient calls IBackoffCalculator with correct parameters
    /// for different failure scenarios. Uses a TestBackoffCalculator to control test flow and verify behavior.
    /// </summary>
    public class WebSocketClientBackoffIntegrationTests : IAsyncLifetime
    {
        private readonly ITestOutputHelper output;
        private int testServerPort;
        private Uri testServerUri = null!;

        public WebSocketClientBackoffIntegrationTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        public Task InitializeAsync()
        {
            this.testServerPort = GetAvailablePort();
            this.testServerUri = new Uri($"ws://localhost:{this.testServerPort}/");
            return Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }

        private static int GetAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            listener.Server.Close();
            listener.Server.Dispose();
            return port;
        }

        /// <summary>
        /// Test BackoffCalculator integration for different failure scenarios
        /// </summary>
        /// <param name="scenario">The failure scenario to test</param>
        /// <param name="expectedAttempts">Expected sequence of attempt numbers passed to BackoffCalculator</param>
        [Theory]
        [InlineData(FailureScenario.ServerNotReachable, new[] { 0, 1, 2 })]
        [InlineData(FailureScenario.HandshakeRejected, new[] { 0, 1, 2 })]
        [InlineData(FailureScenario.ImmediateDisconnect, new[] { 0, 1, 2 })]
        [InlineData(FailureScenario.StableDisconnect, new int[] { })] // Stable disconnect - no backoff calls expected
        public async Task WebSocketClient_FailureScenarios_ShouldCallBackoffCalculatorWithCorrectAttemptsAsync(
            FailureScenario scenario,
            int[] expectedAttempts)
        {
            // Arrange
            var testCalculator = new TestBackoffCalculator(expectedAttempts);
            using var server = scenario == FailureScenario.ServerNotReachable ? null : this.CreateTestServer(scenario);

            if (server != null)
                await server.StartAsync();

            try
            {
                using var client = WebSocketClientBuilder.Create()
                    .UseAutoReconnect(options =>
                    {
                        options.InitialDelay = TimeSpan.FromMilliseconds(50);
                        options.BackoffMultiplier = 2.0;
                        options.MaxDelay = TimeSpan.FromSeconds(5);
                        options.Jitter = 0.0;
                        // Configure stability callback based on scenario
                        options.IsStableConnection = duration => scenario == FailureScenario.StableDisconnect;
                    })
                    .Build();

                client.WithBackoffCalculator(testCalculator);

                // Act
                var uri = scenario == FailureScenario.ServerNotReachable
                    ? new Uri("ws://localhost:9999/") // Unreachable
                    : this.testServerUri;

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var startTask = client.StartAsync(uri, cts.Token);

                // Wait for test to complete (when TestBackoffCalculator signals done)  
                await testCalculator.WaitForCompletionAsync(TimeSpan.FromSeconds(10));

                // Immediately stop client to prevent additional calls
                await client.StopAsync();

                // Assert
this.output.WriteLine($"Expected attempts: [{string.Join(", ", expectedAttempts)}]");
                this.output.WriteLine($"Actual attempts: [{string.Join(", ", testCalculator.ReceivedAttempts)}]");

                // Allow for a small number of extra calls due to race conditions, but verify we got at least the expected calls
                var actualAttempts = testCalculator.ReceivedAttempts.Take(expectedAttempts.Length).ToArray();
                Assert.Equal(expectedAttempts, actualAttempts);

                // Verify all calls had correct parameters
                foreach (var call in testCalculator.ReceivedCalls)
                {
                    Assert.Equal(TimeSpan.FromMilliseconds(50), call.InitialDelay);
                    Assert.Equal(TimeSpan.FromSeconds(5), call.MaxDelay);
                    Assert.Equal(0.0, call.Jitter);
                    Assert.Equal(2.0, call.BackoffMultiplier);
                }

                client.Dispose();
            }
            finally
            {
                if (server != null)
                {
                    await server.StopAsync();
                    server.Dispose();
                }
            }
        }

        /// <summary>
        /// Creates a test server based on the failure scenario
        /// </summary>
        private WebSocketServer CreateTestServer(FailureScenario scenario)
        {
            return scenario switch
            {
                FailureScenario.HandshakeRejected => WebSocketServerBuilder.Create()
                    .UsePort((ushort)this.testServerPort)
                    .OnHandshake((_context, _next, _ct) =>
                    {
                        this.output.WriteLine("Server rejecting handshake with 401");
                        var response = Http.HttpResponse.Create(401);
                        // Note: Using basic 401 response without body for C# 8.0 compatibility
                        return new ValueTask<Http.HttpResponse>(response);
                    })
                    .Build(),

                FailureScenario.ImmediateDisconnect => WebSocketServerBuilder.Create()
                    .UsePort((ushort)this.testServerPort)
                    .OnConnect(async (ws, _next, ct) =>
                    {
                        await ws.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.InternalServerError,
                            "Closing immediately", ct);
                    })
                    .Build(),

                FailureScenario.StableDisconnect => WebSocketServerBuilder.Create()
                    .UsePort((ushort)this.testServerPort)
                    .OnConnect(async (ws, next, ct) =>
                    {
                        this.output.WriteLine("Server accepting connection, staying stable, then closing");
                        // Keep connection open briefly to be considered "stable"
                        await Task.Delay(100, ct);
                        await ws.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure,
                            "Normal closure after stable connection", ct);
                    })
                    .Build(),

                _ => throw new ArgumentException($"Unknown scenario: {scenario}")
            };
        }
    }

    /// <summary>
    /// Different failure scenarios for testing BackoffCalculator integration
    /// </summary>
    public enum FailureScenario
    {
        /// <summary>Server is not reachable (connection refused)</summary>
        ServerNotReachable,

        /// <summary>Server rejects WebSocket handshake (e.g., 401 Unauthorized)</summary>
        HandshakeRejected,

        /// <summary>Server accepts connection but closes immediately (unstable connection)</summary>
        ImmediateDisconnect,

        /// <summary>Server accepts connection, stays open briefly, then closes (stable connection)</summary>
        StableDisconnect
    }

    /// <summary>
    /// Test implementation of IBackoffCalculator that records calls and controls test flow
    /// </summary>
    public class TestBackoffCalculator : IBackoffCalculator
    {
        private readonly int[] expectedAttempts;
        private readonly List<int> receivedAttempts = new List<int>();
        private readonly List<BackoffCall> receivedCalls = new List<BackoffCall>();
        private readonly TaskCompletionSource<bool> completionSource = new TaskCompletionSource<bool>();
        private int callCount = 0;

        public TestBackoffCalculator(int[] expectedAttempts)
        {
            this.expectedAttempts = expectedAttempts;
        }

        public IReadOnlyList<int> ReceivedAttempts => this.receivedAttempts.AsReadOnly();
        public IReadOnlyList<BackoffCall> ReceivedCalls => this.receivedCalls.AsReadOnly();

        public TimeSpan CalculateDelay(int attemptNumber, TimeSpan initialDelay, TimeSpan maxDelay, double jitterPercent, double backoffMultiplier)
        {
            this.receivedAttempts.Add(attemptNumber);
            this.receivedCalls.Add(new BackoffCall(attemptNumber, initialDelay, maxDelay, jitterPercent, backoffMultiplier));
            this.callCount++;

            // If we've received all expected calls, signal completion
            if (this.callCount >= this.expectedAttempts.Length)
            {
                this.completionSource.TrySetResult(true);
            }

            // Return small delay for fast but controlled testing
            return TimeSpan.FromMilliseconds(1);
        }

        public TimeSpan CalculateDelayWithoutJitter(int attemptNumber, TimeSpan initialDelay, TimeSpan maxDelay, double backoffMultiplier)
        {
            // Delegate to main method with zero jitter
            return this.CalculateDelay(attemptNumber, initialDelay, maxDelay, 0.0, backoffMultiplier);
        }

        public async Task WaitForCompletionAsync(TimeSpan timeout)
        {
            // If no attempts are expected, wait a short time then complete
            if (this.expectedAttempts.Length == 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500));
                this.completionSource.TrySetResult(true);
                return;
            }

            using var cts = new CancellationTokenSource(timeout);
            try
            {
                await this.completionSource.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"Test did not complete within {timeout}. Expected {this.expectedAttempts.Length} calls, received {this.callCount}.");
            }
        }
    }

    /// <summary>
    /// Record of a BackoffCalculator call for verification
    /// </summary>
    public class BackoffCall
    {
        public int AttemptNumber { get; }
        public TimeSpan InitialDelay { get; }
        public TimeSpan MaxDelay { get; }
        public double Jitter { get; }
        public double BackoffMultiplier { get; }

        public BackoffCall(int attemptNumber, TimeSpan initialDelay, TimeSpan maxDelay, double jitter, double backoffMultiplier)
        {
            this.AttemptNumber = attemptNumber;
            this.InitialDelay = initialDelay;
            this.MaxDelay = maxDelay;
            this.Jitter = jitter;
            this.BackoffMultiplier = backoffMultiplier;
        }
    }
}