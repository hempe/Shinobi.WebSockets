using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Shinobi.WebSockets;

using Xunit;

namespace Shinobi.WebSockets.UnitTests
{
    public class WebSocketClientOptionsTests
    {
        #region WebSocketConnectionStateChangedEventArgs Tests

        [Fact]
        public void WebSocketConnectionStateChangedEventArgs_Constructor_ShouldSetProperties()
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
        public void WebSocketConnectionStateChangedEventArgs_WithoutException_ShouldHaveNullException()
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

        [Theory]
        [InlineData(WebSocketConnectionState.Disconnected, WebSocketConnectionState.Connecting)]
        [InlineData(WebSocketConnectionState.Connecting, WebSocketConnectionState.Connected)]
        [InlineData(WebSocketConnectionState.Connected, WebSocketConnectionState.Disconnecting)]
        [InlineData(WebSocketConnectionState.Disconnecting, WebSocketConnectionState.Disconnected)]
        [InlineData(WebSocketConnectionState.Disconnected, WebSocketConnectionState.Reconnecting)]
        [InlineData(WebSocketConnectionState.Reconnecting, WebSocketConnectionState.Connected)]
        [InlineData(WebSocketConnectionState.Connected, WebSocketConnectionState.Failed)]
        public void WebSocketConnectionStateChangedEventArgs_AllStateTransitions_ShouldWork(WebSocketConnectionState from, WebSocketConnectionState to)
        {
            // Act
            var eventArgs = new WebSocketConnectionStateChangedEventArgs(from, to);

            // Assert
            Assert.Equal(from, eventArgs.PreviousState);
            Assert.Equal(to, eventArgs.NewState);
            Assert.Null(eventArgs.Exception);
        }

        #endregion

        #region WebSocketReconnectingEventArgs Tests

        [Fact]
        public void WebSocketReconnectingEventArgs_Constructor_ShouldSetProperties()
        {
            // Arrange
            var uri = new Uri("ws://localhost:8080");
            var attemptNumber = 3;
            var delay = TimeSpan.FromSeconds(5);

            // Act
            var eventArgs = new WebSocketReconnectingEventArgs(uri, attemptNumber, delay);

            // Assert
            Assert.Equal(uri, eventArgs.CurrentUri);
            Assert.Equal(attemptNumber, eventArgs.AttemptNumber);
            Assert.Equal(delay, eventArgs.Delay);
        }

        [Fact]
        public void WebSocketReconnectingEventArgs_UriProperty_ShouldBeSettable()
        {
            // Arrange
            var originalUri = new Uri("ws://localhost:8080");
            var newUri = new Uri("ws://localhost:8081");
            var eventArgs = new WebSocketReconnectingEventArgs(originalUri, 1, TimeSpan.FromSeconds(1));

            // Act
            eventArgs.CurrentUri = newUri;

            // Assert
            Assert.Equal(newUri, eventArgs.CurrentUri);
        }

        #endregion

        #region WebSocketReconnectOptions Tests

        [Fact]
        public void WebSocketReconnectOptions_DefaultValues_ShouldBeCorrect()
        {
            // Act
            var options = new WebSocketReconnectOptions();

            // Assert
            Assert.False(options.Enabled);
            Assert.Equal(TimeSpan.FromSeconds(1), options.InitialDelay);
            Assert.Equal(TimeSpan.FromSeconds(30), options.MaxDelay);
            Assert.Equal(2.0, options.BackoffMultiplier);
            Assert.Equal(0.1, options.Jitter);
            Assert.NotNull(options.IsStableConnection);
        }

        [Fact]
        public void WebSocketReconnectOptions_IsStableConnection_DefaultImplementation_ShouldWork()
        {
            // Arrange
            var options = new WebSocketReconnectOptions();

            // Act & Assert - Less than 3 seconds should be unstable
            Assert.False(options.IsStableConnection(TimeSpan.FromSeconds(1)));
            Assert.False(options.IsStableConnection(TimeSpan.FromSeconds(2.9)));

            // 3 seconds or more should be stable
            Assert.True(options.IsStableConnection(TimeSpan.FromSeconds(3)));
            Assert.True(options.IsStableConnection(TimeSpan.FromSeconds(10)));
            Assert.True(options.IsStableConnection(TimeSpan.FromMinutes(1)));
        }

        [Fact]
        public void WebSocketReconnectOptions_CustomIsStableConnection_ShouldWork()
        {
            // Arrange
            var options = new WebSocketReconnectOptions
            {
                IsStableConnection = duration => duration >= TimeSpan.FromMinutes(1) // Custom: 1 minute threshold
            };

            // Act & Assert
            Assert.False(options.IsStableConnection(TimeSpan.FromSeconds(59)));
            Assert.True(options.IsStableConnection(TimeSpan.FromMinutes(1)));
            Assert.True(options.IsStableConnection(TimeSpan.FromMinutes(5)));
        }

        [Fact]
        public void WebSocketReconnectOptions_PropertiesCanBeSet()
        {
            // Arrange
            var options = new WebSocketReconnectOptions();

            // Act
            options.Enabled = true;
            options.InitialDelay = TimeSpan.FromSeconds(2);
            options.MaxDelay = TimeSpan.FromMinutes(1);
            options.BackoffMultiplier = 1.5;
            options.Jitter = 0.2;

            // Assert
            Assert.True(options.Enabled);
            Assert.Equal(TimeSpan.FromSeconds(2), options.InitialDelay);
            Assert.Equal(TimeSpan.FromMinutes(1), options.MaxDelay);
            Assert.Equal(1.5, options.BackoffMultiplier);
            Assert.Equal(0.2, options.Jitter);
        }

        #endregion

        #region WebSocketClientOptions Tests

        [Fact]
        public void WebSocketClientOptions_DefaultValues_ShouldBeCorrect()
        {
            // Act
            var options = new WebSocketClientOptions();

            // Assert
            Assert.Equal(TimeSpan.FromSeconds(30), options.KeepAliveInterval);
            Assert.True(options.NoDelay);
            Assert.NotNull(options.AdditionalHttpHeaders);
            Assert.Empty(options.AdditionalHttpHeaders);
            Assert.False(options.IncludeExceptionInCloseResponse);
            Assert.Null(options.SecWebSocketExtensions);
            Assert.Null(options.SecWebSocketProtocol);
            Assert.NotNull(options.ReconnectOptions);
            Assert.False(options.ReconnectOptions.Enabled); // Default is disabled
        }

        [Fact]
        public void WebSocketClientOptions_PropertiesCanBeSet()
        {
            // Arrange
            var options = new WebSocketClientOptions();
            var reconnectOptions = new WebSocketReconnectOptions { Enabled = true };

            // Act
            options.KeepAliveInterval = TimeSpan.FromSeconds(60);
            options.NoDelay = false;
            options.AdditionalHttpHeaders.Add("Custom-Header", "value");
            options.IncludeExceptionInCloseResponse = true;
            options.SecWebSocketExtensions = "permessage-deflate";
            options.SecWebSocketProtocol = "chat";
            options.ReconnectOptions = reconnectOptions;

            // Assert
            Assert.Equal(TimeSpan.FromSeconds(60), options.KeepAliveInterval);
            Assert.False(options.NoDelay);
            Assert.Contains("Custom-Header", options.AdditionalHttpHeaders.Keys);
            Assert.Equal("value", options.AdditionalHttpHeaders["Custom-Header"]);
            Assert.True(options.IncludeExceptionInCloseResponse);
            Assert.Equal("permessage-deflate", options.SecWebSocketExtensions);
            Assert.Equal("chat", options.SecWebSocketProtocol);
            Assert.Same(reconnectOptions, options.ReconnectOptions);
            Assert.True(options.ReconnectOptions.Enabled);
        }

        [Fact]
        public void WebSocketClientOptions_InterceptorLists_ShouldBeNullByDefault()
        {
            // Act
            var options = new WebSocketClientOptions();

            // Assert
            Assert.Null(options.OnConnect);
            Assert.Null(options.OnClose);
            Assert.Null(options.OnError);
            Assert.Null(options.OnMessage);
            Assert.Null(options.OnReconnecting);
        }

        [Fact]
        public async Task WebSocketReconnectingHandler_ShouldWorkAsync()
        {
            // Arrange
            var originalUri = new Uri("ws://localhost:8080");
            var newUri = new Uri("ws://localhost:8081");

            WebSocketReconnectingHandler handler = async (currentUri, attemptNumber, cancellationToken) =>
            {
                await Task.Delay(1, cancellationToken); // Simulate async work
                return attemptNumber > 1 ? newUri : currentUri;
            };

            // Act & Assert
            var result1 = await handler(originalUri, 1, CancellationToken.None);
            var result2 = await handler(originalUri, 2, CancellationToken.None);

            Assert.Equal(originalUri, result1);
            Assert.Equal(newUri, result2);
        }

        #endregion

        #region Enum Coverage Tests

        [Fact]
        public void WebSocketConnectionState_AllValues_ShouldBeDefined()
        {
            // Act & Assert - Ensure all enum values are properly defined
            var states = Enum.GetValues(typeof(WebSocketConnectionState)).Cast<WebSocketConnectionState>().ToArray();


            Assert.Contains(WebSocketConnectionState.Disconnected, states);
            Assert.Contains(WebSocketConnectionState.Connecting, states);
            Assert.Contains(WebSocketConnectionState.Connected, states);
            Assert.Contains(WebSocketConnectionState.Reconnecting, states);
            Assert.Contains(WebSocketConnectionState.Disconnecting, states);
            Assert.Contains(WebSocketConnectionState.Failed, states);

            // Verify we have all expected states
            Assert.Equal(6, states.Length);
        }

        [Fact]
        public void WebSocketConnectionState_EnumValues_ShouldHaveCorrectNumberValues()
        {
            // Act & Assert - Ensure enum values have expected integer values
            Assert.Equal(0, (int)WebSocketConnectionState.Disconnected);
            Assert.Equal(1, (int)WebSocketConnectionState.Connecting);
            Assert.Equal(2, (int)WebSocketConnectionState.Connected);
            Assert.Equal(3, (int)WebSocketConnectionState.Reconnecting);
            Assert.Equal(4, (int)WebSocketConnectionState.Disconnecting);
            Assert.Equal(5, (int)WebSocketConnectionState.Failed);
        }

        [Theory]
        [InlineData(WebSocketConnectionState.Disconnected)]
        [InlineData(WebSocketConnectionState.Connecting)]
        [InlineData(WebSocketConnectionState.Connected)]
        [InlineData(WebSocketConnectionState.Reconnecting)]
        [InlineData(WebSocketConnectionState.Disconnecting)]
        [InlineData(WebSocketConnectionState.Failed)]
        public void WebSocketConnectionState_ToString_ShouldWork(WebSocketConnectionState state)
        {
            // Act
            var stringValue = state.ToString();

            // Assert
            Assert.NotNull(stringValue);
            Assert.NotEmpty(stringValue);
        }

        #endregion
    }
}