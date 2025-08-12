using System;
using Xunit;

namespace Shinobi.WebSockets.UnitTests
{
    /// <summary>
    /// Unit tests for WebSocket client options and configuration classes
    /// </summary>
    public class WebSocketClientOptionsTests
    {
        [Fact]
        public void WebSocketReconnectOptions_DefaultValues_ShouldBeCorrect()
        {
            // Arrange & Act
            var options = new WebSocketReconnectOptions();

            // Assert
            Assert.False(options.Enabled);
            Assert.Equal(TimeSpan.FromSeconds(1), options.InitialDelay);
            Assert.Equal(TimeSpan.FromSeconds(30), options.MaxDelay);
            Assert.Equal(2.0, options.BackoffMultiplier);
            Assert.Equal(0, options.MaxAttempts);
            Assert.Equal(0.1, options.Jitter);
        }

        [Fact]
        public void WebSocketReconnectOptions_SetValues_ShouldPersist()
        {
            // Arrange
            var options = new WebSocketReconnectOptions();
            var initialDelay = TimeSpan.FromSeconds(5);
            var maxDelay = TimeSpan.FromMinutes(2);
            var backoffMultiplier = 1.5;
            var maxAttempts = 10;
            var jitter = 0.2;

            // Act
            options.Enabled = true;
            options.InitialDelay = initialDelay;
            options.MaxDelay = maxDelay;
            options.BackoffMultiplier = backoffMultiplier;
            options.MaxAttempts = maxAttempts;
            options.Jitter = jitter;

            // Assert
            Assert.True(options.Enabled);
            Assert.Equal(initialDelay, options.InitialDelay);
            Assert.Equal(maxDelay, options.MaxDelay);
            Assert.Equal(backoffMultiplier, options.BackoffMultiplier);
            Assert.Equal(maxAttempts, options.MaxAttempts);
            Assert.Equal(jitter, options.Jitter);
        }

        [Fact]
        public void WebSocketClientOptions_DefaultValues_ShouldBeCorrect()
        {
            // Arrange & Act
            var options = new WebSocketClientOptions();

            // Assert
            Assert.Equal(TimeSpan.FromSeconds(20), options.KeepAliveInterval);
            Assert.True(options.NoDelay);
            Assert.NotNull(options.AdditionalHttpHeaders);
            Assert.Empty(options.AdditionalHttpHeaders);
            Assert.False(options.IncludeExceptionInCloseResponse);
            Assert.Null(options.SecWebSocketProtocol);
            Assert.NotNull(options.ReconnectOptions);
            Assert.False(options.ReconnectOptions.Enabled);
        }

        [Fact]
        public void WebSocketConnectionStateChangedEventArgs_Constructor_ShouldSetProperties()
        {
            // Arrange
            var previousState = WebSocketConnectionState.Connecting;
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
            var previousState = WebSocketConnectionState.Connected;
            var newState = WebSocketConnectionState.Disconnected;

            // Act
            var eventArgs = new WebSocketConnectionStateChangedEventArgs(previousState, newState);

            // Assert
            Assert.Equal(previousState, eventArgs.PreviousState);
            Assert.Equal(newState, eventArgs.NewState);
            Assert.Null(eventArgs.Exception);
        }

        [Fact]
        public void WebSocketReconnectingEventArgs_Constructor_ShouldSetProperties()
        {
            // Arrange
            var uri = new Uri("ws://example.com");
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
        public void WebSocketReconnectingEventArgs_CurrentUri_ShouldBeSettable()
        {
            // Arrange
            var originalUri = new Uri("ws://primary.com");
            var newUri = new Uri("ws://backup.com");
            var eventArgs = new WebSocketReconnectingEventArgs(originalUri, 1, TimeSpan.FromSeconds(1));

            // Act
            eventArgs.CurrentUri = newUri;

            // Assert
            Assert.Equal(newUri, eventArgs.CurrentUri);
        }

        [Theory]
        [InlineData(WebSocketConnectionState.Disconnected)]
        [InlineData(WebSocketConnectionState.Connecting)]
        [InlineData(WebSocketConnectionState.Connected)]
        [InlineData(WebSocketConnectionState.Reconnecting)]
        [InlineData(WebSocketConnectionState.Disconnecting)]
        [InlineData(WebSocketConnectionState.Failed)]
        public void WebSocketConnectionState_AllEnumValues_ShouldBeDefined(WebSocketConnectionState state)
        {
            // Act & Assert - Just verifying all enum values are defined and can be used
            var stateName = state.ToString();
            Assert.NotNull(stateName);
            Assert.NotEmpty(stateName);
        }

        [Fact]
        public void MessageType_EnumValues_ShouldHaveCorrectValues()
        {
            // Act & Assert
            Assert.Equal(0, (int)MessageType.Text);
            Assert.Equal(1, (int)MessageType.Binary);
        }

        [Fact]
        public void WebSocketClientOptions_ReconnectOptions_ShouldNotBeNull()
        {
            // Arrange & Act
            var options = new WebSocketClientOptions();

            // Assert
            Assert.NotNull(options.ReconnectOptions);
            Assert.IsType<WebSocketReconnectOptions>(options.ReconnectOptions);
        }

        [Fact]
        public void WebSocketClientOptions_AdditionalHttpHeaders_ShouldBeModifiable()
        {
            // Arrange
            var options = new WebSocketClientOptions();
            var headerKey = "Authorization";
            var headerValue = "Bearer token123";

            // Act
            options.AdditionalHttpHeaders[headerKey] = headerValue;

            // Assert
            Assert.True(options.AdditionalHttpHeaders.ContainsKey(headerKey));
            Assert.Equal(headerValue, options.AdditionalHttpHeaders[headerKey]);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(100)]
        public void WebSocketReconnectOptions_MaxAttempts_ShouldAllowValidValues(int maxAttempts)
        {
            // Arrange
            var options = new WebSocketReconnectOptions();

            // Act
            options.MaxAttempts = maxAttempts;

            // Assert
            Assert.Equal(maxAttempts, options.MaxAttempts);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(0.1)]
        [InlineData(0.5)]
        [InlineData(1.0)]
        public void WebSocketReconnectOptions_Jitter_ShouldAllowValidValues(double jitter)
        {
            // Arrange
            var options = new WebSocketReconnectOptions();

            // Act
            options.Jitter = jitter;

            // Assert
            Assert.Equal(jitter, options.Jitter);
        }
    }
}