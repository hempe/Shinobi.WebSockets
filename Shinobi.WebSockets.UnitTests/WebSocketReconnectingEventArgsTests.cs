using System;

using Shinobi.WebSockets;

using Xunit;

namespace Shinobi.WebSockets.UnitTests
{
    public class WebSocketReconnectingEventArgsTests
    {
        [Fact]
        public void Constructor_WithValidParameters_ShouldSetAllProperties()
        {
            // Arrange
            var uri = new Uri("wss://example.com/websocket");
            var attemptNumber = 3;
            var delay = TimeSpan.FromSeconds(5);

            // Act
            var eventArgs = new WebSocketReconnectingEventArgs(uri, attemptNumber, delay);

            // Assert
            Assert.Same(uri, eventArgs.CurrentUri);
            Assert.Equal(attemptNumber, eventArgs.AttemptNumber);
            Assert.Equal(delay, eventArgs.Delay);
            Assert.IsAssignableFrom<EventArgs>(eventArgs);
        }




        [Fact]
        public void CurrentUri_SetProperty_ShouldUpdateValue()
        {
            // Arrange
            var originalUri = new Uri("ws://original.com");
            var newUri = new Uri("wss://new.com");
            var eventArgs = new WebSocketReconnectingEventArgs(originalUri, 1, TimeSpan.FromSeconds(1));

            // Act
            eventArgs.CurrentUri = newUri;

            // Assert
            Assert.Same(newUri, eventArgs.CurrentUri);
        }


        [Fact]
        public void EventArgs_ShouldBeUsableInEventHandlerSignature()
        {
            // Arrange
            var uri = new Uri("ws://test.com");
            var eventArgs = new WebSocketReconnectingEventArgs(uri, 1, TimeSpan.FromSeconds(2));

            // Act & Assert - This test ensures the class can be used in event signatures
            WebSocketReconnectingEventHandler handler = (sender, args) =>
            {
                Assert.NotNull(args);
                Assert.IsType<WebSocketReconnectingEventArgs>(args);
            };

            // Simulate event invocation - passing a mock WebSocketClient
            handler.Invoke(null!, eventArgs);
        }

    }
}