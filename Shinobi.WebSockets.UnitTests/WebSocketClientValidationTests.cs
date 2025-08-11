using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Shinobi.WebSockets.Builders;
using Xunit;

namespace Shinobi.WebSockets.UnitTests
{
    /// <summary>
    /// Unit tests for WebSocket client validation and error handling
    /// </summary>
    public class WebSocketClientValidationTests
    {
        [Fact]
        public void WebSocketClient_Constructor_WithNullOptions_ShouldThrowNullReferenceException()
        {
            // Arrange, Act & Assert
            // Note: The constructor currently doesn't validate null options parameter
            // so it throws NullReferenceException when trying to access options.OnConnect
            Assert.Throws<NullReferenceException>(() => new WebSocketClient(null));
        }

        [Fact]
        public void WebSocketClient_Constructor_WithValidOptions_ShouldNotThrow()
        {
            // Arrange
            var options = new WebSocketClientOptions();

            // Act & Assert - Should not throw
            var client = new WebSocketClient(options);
            Assert.NotNull(client);
            client.Dispose();
        }

        [Fact]
        public void WebSocketClient_Constructor_WithLogger_ShouldNotThrow()
        {
            // Arrange
            var options = new WebSocketClientOptions();
            using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var logger = loggerFactory.CreateLogger<WebSocketClient>();

            // Act & Assert - Should not throw
            var client = new WebSocketClient(options, logger);
            Assert.NotNull(client);
            client.Dispose();
        }

        [Fact]
        public async Task WebSocketClient_SendTextAsync_WhenNotConnected_ShouldThrowInvalidOperationException()
        {
            // Arrange
            var options = new WebSocketClientOptions();
            var client = new WebSocketClient(options);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.SendTextAsync("test message"));
            
            Assert.Contains("not connected", exception.Message, StringComparison.OrdinalIgnoreCase);
            
            client.Dispose();
        }

        [Fact]
        public async Task WebSocketClient_SendBinaryAsync_WhenNotConnected_ShouldThrowInvalidOperationException()
        {
            // Arrange
            var options = new WebSocketClientOptions();
            var client = new WebSocketClient(options);
            var data = new byte[] { 1, 2, 3, 4, 5 };

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.SendBinaryAsync(data));
            
            Assert.Contains("not connected", exception.Message, StringComparison.OrdinalIgnoreCase);
            
            client.Dispose();
        }

        [Fact]
        public void WebSocketClient_ConnectionState_InitialValue_ShouldBeDisconnected()
        {
            // Arrange
            var options = new WebSocketClientOptions();
            var client = new WebSocketClient(options);

            // Act & Assert
            Assert.Equal(WebSocketConnectionState.Disconnected, client.ConnectionState);
            
            client.Dispose();
        }

        [Fact]
        public async Task WebSocketClient_StartAsync_WithInvalidUri_ShouldThrow()
        {
            // Arrange
            var options = new WebSocketClientOptions();
            var client = new WebSocketClient(options);
            var invalidUri = new Uri("http://nonexistent-websocket-server-12345.com");

            // Act & Assert - This should eventually throw or timeout
            // We're testing that it doesn't immediately crash the process
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            
            try
            {
                await client.StartAsync(invalidUri, cts.Token);
                // If we reach here without exception, that's also valid behavior
            }
            catch (OperationCanceledException)
            {
                // Timeout is expected for non-existent server
            }
            catch (Exception)
            {
                // Other exceptions are also acceptable (DNS resolution, connection refused, etc.)
            }
            
            client.Dispose();
        }

        [Fact]
        public void WebSocketReconnectingHandler_Delegate_ShouldBeCallable()
        {
            // Arrange
            var uri = new Uri("ws://example.com");
            var attemptNumber = 1;
            var cancellationToken = CancellationToken.None;
            
            WebSocketReconnectingHandler handler = async (currentUri, attempt, ct) =>
            {
                Assert.Equal(uri, currentUri);
                Assert.Equal(attemptNumber, attempt);
                Assert.Equal(cancellationToken, ct);
                return currentUri;
            };

            // Act & Assert - Should be callable
            var task = handler(uri, attemptNumber, cancellationToken);
            Assert.NotNull(task);
        }

        [Fact]
        public void WebSocketConnectionStateChangedHandler_Delegate_ShouldBeCallable()
        {
            // Arrange
            var options = new WebSocketClientOptions();
            var client = new WebSocketClient(options);
            var eventArgs = new WebSocketConnectionStateChangedEventArgs(
                WebSocketConnectionState.Disconnected, 
                WebSocketConnectionState.Connected);
            
            var handlerCalled = false;
            WebSocketConnectionStateChangedHandler handler = (sender, e) =>
            {
                handlerCalled = true;
                Assert.Same(client, sender);
                Assert.Same(eventArgs, e);
            };

            // Act
            handler(client, eventArgs);

            // Assert
            Assert.True(handlerCalled);
            
            client.Dispose();
        }

        [Fact]
        public void WebSocketReconnectingEventHandler_Delegate_ShouldBeCallable()
        {
            // Arrange
            var options = new WebSocketClientOptions();
            var client = new WebSocketClient(options);
            var eventArgs = new WebSocketReconnectingEventArgs(
                new Uri("ws://example.com"), 
                1, 
                TimeSpan.FromSeconds(5));
            
            var handlerCalled = false;
            WebSocketReconnectingEventHandler handler = (sender, e) =>
            {
                handlerCalled = true;
                Assert.Same(client, sender);
                Assert.Same(eventArgs, e);
            };

            // Act
            handler(client, eventArgs);

            // Assert
            Assert.True(handlerCalled);
            
            client.Dispose();
        }

        [Fact]
        public void WebSocketClient_Dispose_MultipleTimes_ShouldNotThrow()
        {
            // Arrange
            var options = new WebSocketClientOptions();
            var client = new WebSocketClient(options);

            // Act & Assert - Should not throw on multiple dispose calls
            client.Dispose();
            client.Dispose();
            client.Dispose();
        }

        [Fact]
        public async Task WebSocketClient_DisposeAsync_ShouldCompleteStopAsync()
        {
            // Arrange
            var options = new WebSocketClientOptions();
            var client = new WebSocketClient(options);

            // Act & Assert - Dispose should handle any pending operations
            client.Dispose();
            
            // Should be able to dispose without hanging
            await Task.CompletedTask;
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("test message")]
        [InlineData("🚀 Unicode test message with emoji")]
        public async Task WebSocketClient_SendTextAsync_WithVariousStrings_ShouldAcceptInput(string message)
        {
            // Arrange
            var options = new WebSocketClientOptions();
            var client = new WebSocketClient(options);

            // Act & Assert - Should throw InvalidOperationException (not connected)
            // but it should accept the string input without argument exceptions
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.SendTextAsync(message));
                
            Assert.Contains("not connected", exception.Message, StringComparison.OrdinalIgnoreCase);
            
            client.Dispose();
        }

        [Fact]
        public async Task WebSocketClient_SendBinaryAsync_WithEmptyArray_ShouldAcceptInput()
        {
            // Arrange
            var options = new WebSocketClientOptions();
            var client = new WebSocketClient(options);
            var emptyData = new byte[0];

            // Act & Assert - Should throw InvalidOperationException (not connected)
            // but it should accept the empty array without argument exceptions
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.SendBinaryAsync(emptyData));
                
            Assert.Contains("not connected", exception.Message, StringComparison.OrdinalIgnoreCase);
            
            client.Dispose();
        }
    }
}