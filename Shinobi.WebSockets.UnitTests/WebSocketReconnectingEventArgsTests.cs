using System;

using Xunit;

namespace Shinobi.WebSockets.UnitTests
{
    public class WebSocketReconnectingEventArgsTests
    {
        [Fact]
        public void Constructor_WithValidParameters_ShouldInitializeProperties()
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
        }

        [Fact]
        public void CurrentUri_ShouldBeSettableForUriModification()
        {
            // Arrange
            var originalUri = new Uri("ws://original.example.com/websocket");
            var newUri = new Uri("wss://failover.example.com/websocket");
            var eventArgs = new WebSocketReconnectingEventArgs(originalUri, 2, TimeSpan.FromSeconds(1));

            // Act
            eventArgs.CurrentUri = newUri;

            // Assert
            Assert.Same(newUri, eventArgs.CurrentUri);
            Assert.NotSame(originalUri, eventArgs.CurrentUri);
        }

        [Theory]
        [InlineData(1, 1000)]
        [InlineData(5, 5000)]
        [InlineData(10, 30000)]
        public void Constructor_WithDifferentAttemptNumbers_ShouldPreserveAttemptAndDelay(int attemptNumber, int delayMs)
        {
            // Arrange
            var uri = new Uri("ws://test.com");
            var delay = TimeSpan.FromMilliseconds(delayMs);

            // Act
            var eventArgs = new WebSocketReconnectingEventArgs(uri, attemptNumber, delay);

            // Assert
            Assert.Equal(attemptNumber, eventArgs.AttemptNumber);
            Assert.Equal(delay, eventArgs.Delay);
        }

        [Fact]
        public void AttemptNumber_ShouldBeReadOnly()
        {
            // Arrange
            var eventArgs = new WebSocketReconnectingEventArgs(new Uri("ws://test.com"), 1, TimeSpan.FromSeconds(1));

            // Act & Assert - AttemptNumber should not have a setter
            var propertyInfo = typeof(WebSocketReconnectingEventArgs).GetProperty(nameof(eventArgs.AttemptNumber));
            Assert.NotNull(propertyInfo);
            Assert.True(propertyInfo.CanRead);
            Assert.False(propertyInfo.CanWrite);
        }

        [Fact]
        public void Delay_ShouldBeReadOnly()
        {
            // Arrange
            var eventArgs = new WebSocketReconnectingEventArgs(new Uri("ws://test.com"), 1, TimeSpan.FromSeconds(1));

            // Act & Assert - Delay should not have a setter
            var propertyInfo = typeof(WebSocketReconnectingEventArgs).GetProperty(nameof(eventArgs.Delay));
            Assert.NotNull(propertyInfo);
            Assert.True(propertyInfo.CanRead);
            Assert.False(propertyInfo.CanWrite);
        }

        [Fact]
        public void EventArgs_InheritanceTest_ShouldBeEventArgs()
        {
            // Arrange
            var eventArgs = new WebSocketReconnectingEventArgs(new Uri("ws://test.com"), 1, TimeSpan.FromSeconds(1));

            // Act & Assert
            Assert.IsAssignableFrom<EventArgs>(eventArgs);
        }

        [Fact]
        public void UsageInEventHandler_ShouldWorkCorrectly()
        {
            // Arrange
            var originalUri = new Uri("ws://original.com");
            var attemptNumber = 3;
            var delay = TimeSpan.FromSeconds(10);

            WebSocketReconnectingEventArgs? capturedArgs = null;
            var eventRaised = false;

            WebSocketReconnectingEventHandler handler = (sender, args) =>
            {
                eventRaised = true;
                capturedArgs = args;

                // Simulate URI failover logic
                if (args.CurrentUri.Scheme == "ws" && args.AttemptNumber > 2)
                {
                    args.CurrentUri = new Uri(args.CurrentUri.ToString().Replace("ws://", "wss://"));
                }
            };

            // Act
            handler.Invoke(null!, new WebSocketReconnectingEventArgs(originalUri, attemptNumber, delay));

            // Assert
            Assert.True(eventRaised);
            Assert.NotNull(capturedArgs);
            Assert.Equal(attemptNumber, capturedArgs.AttemptNumber);
            Assert.Equal(delay, capturedArgs.Delay);
            Assert.Equal("wss", capturedArgs.CurrentUri.Scheme); // Should be modified by handler
        }

        [Fact]
        public void UriFailoverScenario_ShouldAllowUriModification()
        {
            // Arrange - Simulate a failover scenario
            var primaryUri = new Uri("ws://primary.example.com/websocket");
            var backupUri = new Uri("wss://backup.example.com/websocket");
            var eventArgs = new WebSocketReconnectingEventArgs(primaryUri, 2, TimeSpan.FromSeconds(5));

            // Act - Simulate failover logic
            if (eventArgs.AttemptNumber > 1)
            {
                eventArgs.CurrentUri = backupUri;
            }

            // Assert
            Assert.Equal(backupUri, eventArgs.CurrentUri);
            Assert.Equal("wss", eventArgs.CurrentUri.Scheme);
            Assert.Equal("backup.example.com", eventArgs.CurrentUri.Host);
        }

        [Fact]
        public void BackoffScenario_ShouldPreserveDelayInformation()
        {
            // Arrange - Test different backoff delays
            var shortDelay = TimeSpan.FromMilliseconds(100);
            var mediumDelay = TimeSpan.FromSeconds(5);
            var longDelay = TimeSpan.FromMinutes(1);

            // Act & Assert - Create events for different attempt numbers with increasing delays
            var attempt1 = new WebSocketReconnectingEventArgs(new Uri("ws://test.com"), 1, shortDelay);
            var attempt5 = new WebSocketReconnectingEventArgs(new Uri("ws://test.com"), 5, mediumDelay);
            var attempt10 = new WebSocketReconnectingEventArgs(new Uri("ws://test.com"), 10, longDelay);

            Assert.Equal(shortDelay, attempt1.Delay);
            Assert.Equal(mediumDelay, attempt5.Delay);
            Assert.Equal(longDelay, attempt10.Delay);

            Assert.True(attempt1.Delay < attempt5.Delay);
            Assert.True(attempt5.Delay < attempt10.Delay);
        }

        [Fact]
        public void MultipleUriModifications_ShouldMaintainLastSet()
        {
            // Arrange
            var originalUri = new Uri("ws://original.com");
            var firstFailover = new Uri("wss://failover1.com");
            var secondFailover = new Uri("wss://failover2.com");
            var eventArgs = new WebSocketReconnectingEventArgs(originalUri, 3, TimeSpan.FromSeconds(1));

            // Act - Multiple modifications
            eventArgs.CurrentUri = firstFailover;
            Assert.Equal(firstFailover, eventArgs.CurrentUri);

            eventArgs.CurrentUri = secondFailover;

            // Assert
            Assert.Equal(secondFailover, eventArgs.CurrentUri);
            Assert.NotEqual(originalUri, eventArgs.CurrentUri);
            Assert.NotEqual(firstFailover, eventArgs.CurrentUri);
        }

        [Theory]
        [InlineData("ws://test.com:8080/path", "wss://secure-test.com:8443/path")]
        [InlineData("ws://localhost/websocket", "wss://127.0.0.1/websocket")]
        [InlineData("ws://api.example.com/v1/ws", "wss://api-backup.example.com/v1/ws")]
        public void ProtocolAndHostFailover_ShouldWorkWithDifferentUriFormats(string originalUriString, string failoverUriString)
        {
            // Arrange
            var originalUri = new Uri(originalUriString);
            var failoverUri = new Uri(failoverUriString);
            var eventArgs = new WebSocketReconnectingEventArgs(originalUri, 2, TimeSpan.FromSeconds(2));

            // Act
            eventArgs.CurrentUri = failoverUri;

            // Assert
            Assert.Equal(failoverUri, eventArgs.CurrentUri);
            Assert.Equal(failoverUri.Scheme, eventArgs.CurrentUri.Scheme);
            Assert.Equal(failoverUri.Host, eventArgs.CurrentUri.Host);
            Assert.Equal(failoverUri.Port, eventArgs.CurrentUri.Port);
            Assert.Equal(failoverUri.PathAndQuery, eventArgs.CurrentUri.PathAndQuery);
        }

        [Fact]
        public void Constructor_WithNullUri_ShouldAllowNull()
        {
            // Act & Assert - The class doesn't validate nulls in constructor
            var eventArgs = new WebSocketReconnectingEventArgs(null!, 1, TimeSpan.FromSeconds(1));
            Assert.Null(eventArgs.CurrentUri);
        }

        [Fact]
        public void CurrentUri_SetToNull_ShouldAllowNull()
        {
            // Arrange
            var eventArgs = new WebSocketReconnectingEventArgs(new Uri("ws://test.com"), 1, TimeSpan.FromSeconds(1));

            // Act & Assert - The property doesn't validate nulls
            eventArgs.CurrentUri = null!;
            Assert.Null(eventArgs.CurrentUri);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-10)]
        public void Constructor_WithInvalidAttemptNumber_ShouldStillCreateInstance(int attemptNumber)
        {
            // Arrange & Act - The class doesn't validate attempt numbers, so this should work
            var eventArgs = new WebSocketReconnectingEventArgs(new Uri("ws://test.com"), attemptNumber, TimeSpan.FromSeconds(1));

            // Assert
            Assert.Equal(attemptNumber, eventArgs.AttemptNumber);
        }

        [Fact]
        public void Constructor_WithNegativeDelay_ShouldStillCreateInstance()
        {
            // Arrange & Act - The class doesn't validate delays, so this should work
            var negativeDelay = TimeSpan.FromSeconds(-1);
            var eventArgs = new WebSocketReconnectingEventArgs(new Uri("ws://test.com"), 1, negativeDelay);

            // Assert
            Assert.Equal(negativeDelay, eventArgs.Delay);
        }
    }
}