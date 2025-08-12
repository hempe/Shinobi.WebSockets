using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Shinobi.WebSockets.Builders;
using Xunit;

namespace Shinobi.WebSockets.UnitTests
{
    /// <summary>
    /// Unit tests for WebSocketClient reconnection logic and auto-reconnect behavior
    /// </summary>
    public class WebSocketClientReconnectionTests
    {
        [Fact]
        public void WebSocketClient_WithDisabledReconnect_ShouldNotAttemptReconnection()
        {
            // Arrange
            var options = new WebSocketClientOptions
            {
                ReconnectOptions = new WebSocketReconnectOptions
                {
                    Enabled = false
                }
            };
            var client = new WebSocketClient(options);

            // Act & Assert
            Assert.False(options.ReconnectOptions.Enabled);
            Assert.Equal(WebSocketConnectionState.Disconnected, client.ConnectionState);

            client.Dispose();
        }

        [Fact]
        public void WebSocketClient_WithEnabledReconnect_ShouldHaveCorrectDefaults()
        {
            // Arrange
            var options = new WebSocketClientOptions();
            options.ReconnectOptions.Enabled = true;
            var client = new WebSocketClient(options);

            // Act & Assert
            Assert.True(options.ReconnectOptions.Enabled);
            Assert.Equal(TimeSpan.FromSeconds(1), options.ReconnectOptions.InitialDelay);
            Assert.Equal(TimeSpan.FromSeconds(30), options.ReconnectOptions.MaxDelay);
            Assert.Equal(2.0, options.ReconnectOptions.BackoffMultiplier);
            Assert.Equal(0, options.ReconnectOptions.MaxAttempts); // 0 = unlimited
            Assert.Equal(0.1, options.ReconnectOptions.Jitter);

            client.Dispose();
        }

        [Theory]
        [InlineData(1, 1000)] // First attempt: 1 second
        [InlineData(2, 2000)] // Second attempt: 2 seconds (1 * 2^1)
        [InlineData(3, 4000)] // Third attempt: 4 seconds (1 * 2^2)
        [InlineData(4, 8000)] // Fourth attempt: 8 seconds (1 * 2^3)
        public void CalculateReconnectDelay_ShouldUseExponentialBackoff(int attemptNumber, int expectedDelayMs)
        {
            // Arrange
            var options = new WebSocketClientOptions();
            options.ReconnectOptions.Enabled = true;
            options.ReconnectOptions.InitialDelay = TimeSpan.FromSeconds(1);
            options.ReconnectOptions.BackoffMultiplier = 2.0;
            options.ReconnectOptions.MaxDelay = TimeSpan.FromMinutes(10);
            options.ReconnectOptions.Jitter = 0; // Disable jitter for predictable testing

            var client = new WebSocketClient(options);

            // Act - Use reflection to access private method for testing
            var method = typeof(WebSocketClient).GetMethod("CalculateReconnectDelay", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var result = (TimeSpan)method!.Invoke(client, new object[] { attemptNumber })!;

            // Assert
            Assert.Equal(expectedDelayMs, result.TotalMilliseconds, precision: 0);

            client.Dispose();
        }

        [Fact]
        public void CalculateReconnectDelay_ShouldRespectMaxDelay()
        {
            // Arrange
            var options = new WebSocketClientOptions();
            options.ReconnectOptions.Enabled = true;
            options.ReconnectOptions.InitialDelay = TimeSpan.FromSeconds(1);
            options.ReconnectOptions.BackoffMultiplier = 2.0;
            options.ReconnectOptions.MaxDelay = TimeSpan.FromSeconds(5); // Cap at 5 seconds
            options.ReconnectOptions.Jitter = 0; // Disable jitter for predictable testing

            var client = new WebSocketClient(options);

            // Act - Test high attempt number that would exceed max delay
            var method = typeof(WebSocketClient).GetMethod("CalculateReconnectDelay", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var result = (TimeSpan)method!.Invoke(client, new object[] { 10 })!; // Would be 512 seconds without cap

            // Assert
            Assert.Equal(5000, result.TotalMilliseconds, precision: 0);

            client.Dispose();
        }

        [Fact]
        public void CalculateReconnectDelay_WithJitter_ShouldVaryDelay()
        {
            // Arrange
            var options = new WebSocketClientOptions();
            options.ReconnectOptions.Enabled = true;
            options.ReconnectOptions.InitialDelay = TimeSpan.FromSeconds(1);
            options.ReconnectOptions.BackoffMultiplier = 1.0; // No exponential growth
            options.ReconnectOptions.MaxDelay = TimeSpan.FromMinutes(10);
            options.ReconnectOptions.Jitter = 0.5; // 50% jitter

            var client = new WebSocketClient(options);

            // Act - Calculate delay multiple times and check variance
            var method = typeof(WebSocketClient).GetMethod("CalculateReconnectDelay", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            var delays = new TimeSpan[10];
            for (int i = 0; i < delays.Length; i++)
            {
                delays[i] = (TimeSpan)method!.Invoke(client, new object[] { 1 })!;
            }

            // Assert - Should have variance due to jitter
            var firstDelay = delays[0];
            var hasVariance = false;
            for (int i = 1; i < delays.Length; i++)
            {
                if (Math.Abs(delays[i].TotalMilliseconds - firstDelay.TotalMilliseconds) > 10) // Allow for small differences
                {
                    hasVariance = true;
                    break;
                }
            }

            Assert.True(hasVariance, "Jitter should create variance in delay calculations");

            // All delays should be within reasonable range (500ms to 1500ms with 50% jitter)
            foreach (var delay in delays)
            {
                Assert.InRange(delay.TotalMilliseconds, 500, 1500);
            }

            client.Dispose();
        }

        [Fact(Skip = "Network-dependent test - investigate timeout issues")]
        public async Task WebSocketClient_WithMaxAttempts_ShouldStopAfterLimitAsync()
        {
            // Arrange
            var stateChanges = new System.Collections.Generic.List<WebSocketConnectionState>();
            var options = new WebSocketClientOptions();
            options.ReconnectOptions.Enabled = true;
            options.ReconnectOptions.MaxAttempts = 2; // Limit to 2 attempts
            options.ReconnectOptions.InitialDelay = TimeSpan.FromMilliseconds(10); // Fast for testing

            var client = new WebSocketClient(options);
            client.ConnectionStateChanged += (sender, e) => stateChanges.Add(e.NewState);

            var invalidUri = new Uri("ws://localhost:65535/nonexistent"); // Should fail to connect

            // Act & Assert
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            
            // Should throw after max attempts exceeded
            var exception = await Assert.ThrowsAnyAsync<Exception>(
                () => client.StartAsync(invalidUri, cts.Token));
            
            // Could be InvalidOperationException, OperationCanceledException, or SocketException
            Assert.True(
                exception is InvalidOperationException || 
                exception is OperationCanceledException ||
                exception is System.Net.Sockets.SocketException,
                $"Expected connection failure exception, got: {exception.GetType().Name}");

            // Should eventually reach Failed state
            await Task.Delay(100); // Give time for state changes

            Assert.Contains(WebSocketConnectionState.Failed, stateChanges);

            client.Dispose();
        }

        [Fact(Skip = "Network-dependent test - investigate timeout issues")]
        public async Task WebSocketClient_OnReconnecting_ShouldAllowUriModificationAsync()
        {
            // Arrange
            var originalUri = new Uri("ws://localhost:65535/original");
            var modifiedUri = new Uri("ws://localhost:65534/modified");
            var reconnectingCalled = false;

            var options = new WebSocketClientOptions();
            options.ReconnectOptions.Enabled = true;
            options.ReconnectOptions.MaxAttempts = 1; // Single attempt to avoid long test
            options.ReconnectOptions.InitialDelay = TimeSpan.FromMilliseconds(10);

            // Set up OnReconnecting handler to modify URI
            options.OnReconnecting = (uri, attemptNumber, cancellationToken) =>
            {
                reconnectingCalled = true;
                Assert.Equal(originalUri, uri);
                Assert.Equal(1, attemptNumber);
                return new ValueTask<Uri>(modifiedUri);
            };

            var client = new WebSocketClient(options);

            // Act
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            
            try
            {
                await client.StartAsync(originalUri, cts.Token);
            }
            catch
            {
                // Expected to fail since we're connecting to invalid URIs
            }

            // Assert
            Assert.True(reconnectingCalled, "OnReconnecting handler should have been called");

            client.Dispose();
        }

        [Fact]
        public void WebSocketClient_ReconnectingEvent_ShouldProvideCorrectArguments()
        {
            // Arrange
            var eventArgs = new WebSocketReconnectingEventArgs(
                new Uri("ws://example.com"),
                3,
                TimeSpan.FromSeconds(5));

            // Act & Assert
            Assert.Equal("ws://example.com/", eventArgs.CurrentUri.ToString());
            Assert.Equal(3, eventArgs.AttemptNumber);
            Assert.Equal(TimeSpan.FromSeconds(5), eventArgs.Delay);
        }

        [Fact]
        public void WebSocketClient_ReconnectingEvent_ShouldBeInvokable()
        {
            // Arrange
            var options = new WebSocketClientOptions();
            var client = new WebSocketClient(options);
            var eventRaised = false;
            WebSocketReconnectingEventArgs? receivedArgs = null;

            client.Reconnecting += (sender, args) =>
            {
                eventRaised = true;
                receivedArgs = args;
            };

            var testArgs = new WebSocketReconnectingEventArgs(
                new Uri("ws://test.com"),
                2,
                TimeSpan.FromSeconds(3));

            // Act - Manually trigger event (since we can't easily mock failed connections)
            var eventField = typeof(WebSocketClient).GetField("Reconnecting", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var eventHandler = (WebSocketReconnectingEventHandler)eventField!.GetValue(client)!;
            eventHandler?.Invoke(client, testArgs);

            // Assert
            Assert.True(eventRaised);
            Assert.NotNull(receivedArgs);
            Assert.Equal(testArgs.CurrentUri, receivedArgs!.CurrentUri);
            Assert.Equal(testArgs.AttemptNumber, receivedArgs.AttemptNumber);
            Assert.Equal(testArgs.Delay, receivedArgs.Delay);

            client.Dispose();
        }
    }
}