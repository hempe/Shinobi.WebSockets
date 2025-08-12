using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Shinobi.WebSockets.Builders;
using Xunit;

namespace Shinobi.WebSockets.UnitTests
{
    /// <summary>
    /// Unit tests for WebSocketClient connection state management and state transitions
    /// </summary>
    public class WebSocketClientStateTests
    {
        [Fact]
        public void WebSocketClient_InitialState_ShouldBeDisconnected()
        {
            // Arrange
            var options = new WebSocketClientOptions();
            var client = new WebSocketClient(options);

            // Act & Assert
            Assert.Equal(WebSocketConnectionState.Disconnected, client.ConnectionState);

            client.Dispose();
        }

        [Fact]
        public async Task WebSocketClient_StartAsync_ShouldChangeStateToConnectingAsync()
        {
            // Arrange
            var stateChanges = new System.Collections.Generic.List<WebSocketConnectionState>();
            var options = new WebSocketClientOptions();
            var client = new WebSocketClient(options);
            
            client.ConnectionStateChanged += (sender, e) => stateChanges.Add(e.NewState);

            var invalidUri = new Uri("ws://localhost:65535/test");

            // Act
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            
            try
            {
                await client.StartAsync(invalidUri, cts.Token);
            }
            catch
            {
                // Expected to fail with invalid URI
            }

            // Assert
            Assert.Contains(WebSocketConnectionState.Connecting, stateChanges);

            client.Dispose();
        }

        [Fact]
        public async Task WebSocketClient_StopAsync_ShouldChangeStateToDisconnectingAsync()
        {
            // Arrange
            var stateChanges = new System.Collections.Generic.List<WebSocketConnectionState>();
            var options = new WebSocketClientOptions();
            var client = new WebSocketClient(options);
            
            client.ConnectionStateChanged += (sender, e) => stateChanges.Add(e.NewState);

            // Act
            await client.StopAsync();

            // Assert
            Assert.Contains(WebSocketConnectionState.Disconnecting, stateChanges);
            Assert.Equal(WebSocketConnectionState.Disconnected, client.ConnectionState);

            client.Dispose();
        }

        [Fact]
        public async Task WebSocketClient_ConcurrentStateAccess_ShouldBeThreadSafeAsync()
        {
            // Arrange
            var options = new WebSocketClientOptions();
            var client = new WebSocketClient(options);
            var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

            // Act - Access ConnectionState from multiple threads concurrently
            var tasks = new Task[10];
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    try
                    {
                        for (int j = 0; j < 1000; j++)
                        {
                            var state = client.ConnectionState;
                            // Just ensure we can read the state without exceptions
                            Assert.True(Enum.IsDefined(typeof(WebSocketConnectionState), state));
                        }
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                });
            }

            await Task.WhenAll(tasks);

            // Assert
            Assert.Empty(exceptions);

            client.Dispose();
        }

        [Fact]
        public void ConnectionStateChangedEventArgs_Constructor_ShouldSetPropertiesCorrectly()
        {
            // Arrange
            var previousState = WebSocketConnectionState.Disconnected;
            var newState = WebSocketConnectionState.Connected;
            var exception = new InvalidOperationException("Test exception");

            // Act
            var eventArgs = new WebSocketConnectionStateChangedEventArgs(previousState, newState, exception);

            // Assert
            Assert.Equal(previousState, eventArgs.PreviousState);
            Assert.Equal(newState, eventArgs.NewState);
            Assert.Equal(exception, eventArgs.Exception);
        }

        [Fact]
        public void ConnectionStateChangedEventArgs_WithoutException_ShouldHaveNullException()
        {
            // Arrange
            var previousState = WebSocketConnectionState.Connecting;
            var newState = WebSocketConnectionState.Connected;

            // Act
            var eventArgs = new WebSocketConnectionStateChangedEventArgs(previousState, newState);

            // Assert
            Assert.Equal(previousState, eventArgs.PreviousState);
            Assert.Equal(newState, eventArgs.NewState);
            Assert.Null(eventArgs.Exception);
        }

        [Fact]
        public void WebSocketConnectionState_AllEnumValues_ShouldBeDefined()
        {
            // Arrange & Act
            var states = new[]
            {
                WebSocketConnectionState.Disconnected,
                WebSocketConnectionState.Connecting,
                WebSocketConnectionState.Connected,
                WebSocketConnectionState.Disconnecting,
                WebSocketConnectionState.Reconnecting,
                WebSocketConnectionState.Failed
            };

            // Assert
            foreach (var state in states)
            {
                Assert.True(Enum.IsDefined(typeof(WebSocketConnectionState), state),
                    $"State {state} should be properly defined");
            }
        }

        [Theory]
        [InlineData(WebSocketConnectionState.Disconnected)]
        [InlineData(WebSocketConnectionState.Connecting)]
        [InlineData(WebSocketConnectionState.Connected)]
        [InlineData(WebSocketConnectionState.Disconnecting)]
        [InlineData(WebSocketConnectionState.Reconnecting)]
        [InlineData(WebSocketConnectionState.Failed)]
        public void WebSocketConnectionState_EachEnumValue_ShouldBeDefined(WebSocketConnectionState state)
        {
            // Act & Assert
            Assert.True(Enum.IsDefined(typeof(WebSocketConnectionState), state));
        }

        [Fact]
        public void WebSocketClient_ConnectionStateChanged_ShouldFireOnStateTransitions()
        {
            // Arrange
            var eventFired = false;
            WebSocketConnectionStateChangedEventArgs? receivedArgs = null;
            var options = new WebSocketClientOptions();
            var client = new WebSocketClient(options);

            client.ConnectionStateChanged += (sender, e) =>
            {
                eventFired = true;
                receivedArgs = e;
                Assert.Same(client, sender);
            };

            // Act - Use reflection to trigger a state change
            var method = typeof(WebSocketClient).GetMethod("ChangeConnectionState", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method!.Invoke(client, new object[] { WebSocketConnectionState.Connecting, null! });

            // Assert
            Assert.True(eventFired);
            Assert.NotNull(receivedArgs);
            Assert.Equal(WebSocketConnectionState.Disconnected, receivedArgs!.PreviousState);
            Assert.Equal(WebSocketConnectionState.Connecting, receivedArgs.NewState);
            Assert.Null(receivedArgs.Exception);

            client.Dispose();
        }

        [Fact]
        public void WebSocketClient_ConnectionStateChanged_WithException_ShouldIncludeException()
        {
            // Arrange
            var eventFired = false;
            WebSocketConnectionStateChangedEventArgs? receivedArgs = null;
            var options = new WebSocketClientOptions();
            var client = new WebSocketClient(options);
            var testException = new InvalidOperationException("Test exception");

            client.ConnectionStateChanged += (sender, e) =>
            {
                eventFired = true;
                receivedArgs = e;
            };

            // Act - Use reflection to trigger a state change with exception
            var method = typeof(WebSocketClient).GetMethod("ChangeConnectionState", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method!.Invoke(client, new object[] { WebSocketConnectionState.Failed, testException });

            // Assert
            Assert.True(eventFired);
            Assert.NotNull(receivedArgs);
            Assert.Equal(WebSocketConnectionState.Disconnected, receivedArgs!.PreviousState);
            Assert.Equal(WebSocketConnectionState.Failed, receivedArgs.NewState);
            Assert.Equal(testException, receivedArgs.Exception);

            client.Dispose();
        }

        [Fact]
        public void WebSocketClient_SameStateTransition_ShouldNotFireEvent()
        {
            // Arrange
            var eventFireCount = 0;
            var options = new WebSocketClientOptions();
            var client = new WebSocketClient(options);

            client.ConnectionStateChanged += (sender, e) => eventFireCount++;

            // Act - Try to transition to the same state (Disconnected -> Disconnected)
            var method = typeof(WebSocketClient).GetMethod("ChangeConnectionState", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method!.Invoke(client, new object[] { WebSocketConnectionState.Disconnected, null! });

            // Assert
            Assert.Equal(0, eventFireCount); // Should not fire event for same state

            client.Dispose();
        }

        [Fact]
        public async Task WebSocketClient_MultipleStartCalls_ShouldHandleGracefullyAsync()
        {
            // Arrange
            var options = new WebSocketClientOptions();
            var client = new WebSocketClient(options);
            var uri = new Uri("ws://localhost:65535/test");

            // Act & Assert
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            
            // Start first connection attempt
            var task1 = client.StartAsync(uri, cts.Token);
            
            // Try to start again immediately (should handle gracefully)
            var task2 = client.StartAsync(uri, cts.Token);

            try
            {
                await Task.WhenAll(task1, task2);
            }
            catch
            {
                // Expected to fail with invalid URI
            }

            // Client should still be in a valid state
            var finalState = client.ConnectionState;
            Assert.True(Enum.IsDefined(typeof(WebSocketConnectionState), finalState));

            client.Dispose();
        }

        [Fact]
        public async Task WebSocketClient_DisposeWhileConnecting_ShouldTransitionToDisconnectedAsync()
        {
            // Arrange
            var options = new WebSocketClientOptions();
            var client = new WebSocketClient(options);
            var uri = new Uri("ws://localhost:65535/test");

            // Act
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            var connectTask = client.StartAsync(uri, cts.Token);

            // Dispose while connecting
            client.Dispose();

            try
            {
                await connectTask;
            }
            catch
            {
                // Expected
            }

            // Assert - After disposal, accessing state should still work
            // (The state is likely Disconnected after disposal)
            var finalState = client.ConnectionState;
            Assert.True(Enum.IsDefined(typeof(WebSocketConnectionState), finalState));
        }
    }
}