using System;
using Shinobi.WebSockets.Exceptions;
using Xunit;

namespace Shinobi.WebSockets.UnitTests
{
    /// <summary>
    /// Unit tests for WebSocket exception classes
    /// </summary>
    public class WebSocketExceptionTests
    {
        [Fact]
        public void WebSocketVersionNotSupportedException_DefaultConstructor_ShouldCreateException()
        {
            // Act
            var exception = new WebSocketVersionNotSupportedException();

            // Assert
            Assert.NotNull(exception);
            Assert.IsType<WebSocketVersionNotSupportedException>(exception);
        }

        [Fact]
        public void WebSocketVersionNotSupportedException_WithMessage_ShouldSetMessage()
        {
            // Arrange
            var message = "WebSocket version 12 is not supported";

            // Act
            var exception = new WebSocketVersionNotSupportedException(message);

            // Assert
            Assert.Equal(message, exception.Message);
        }

        [Fact]
        public void WebSocketVersionNotSupportedException_WithMessageAndInnerException_ShouldSetBoth()
        {
            // Arrange
            var message = "WebSocket version error";
            var innerException = new InvalidOperationException("Inner error");

            // Act
            var exception = new WebSocketVersionNotSupportedException(message, innerException);

            // Assert
            Assert.Equal(message, exception.Message);
            Assert.Same(innerException, exception.InnerException);
        }

        [Fact]
        public void WebSocketHandshakeFailedException_DefaultConstructor_ShouldCreateException()
        {
            // Act
            var exception = new WebSocketHandshakeFailedException();

            // Assert
            Assert.NotNull(exception);
            Assert.IsType<WebSocketHandshakeFailedException>(exception);
        }

        [Fact]
        public void WebSocketHandshakeFailedException_WithMessage_ShouldSetMessage()
        {
            // Arrange
            var message = "Handshake failed due to invalid Sec-WebSocket-Accept";

            // Act
            var exception = new WebSocketHandshakeFailedException(message);

            // Assert
            Assert.Equal(message, exception.Message);
        }

        [Fact]
        public void WebSocketHandshakeFailedException_WithMessageAndInnerException_ShouldSetBoth()
        {
            // Arrange
            var message = "Handshake error";
            var innerException = new ArgumentException("Invalid argument");

            // Act
            var exception = new WebSocketHandshakeFailedException(message, innerException);

            // Assert
            Assert.Equal(message, exception.Message);
            Assert.Same(innerException, exception.InnerException);
        }

        [Fact]
        public void WebSocketBufferOverflowException_DefaultConstructor_ShouldCreateException()
        {
            // Act
            var exception = new WebSocketBufferOverflowException();

            // Assert
            Assert.NotNull(exception);
            Assert.IsType<WebSocketBufferOverflowException>(exception);
        }

        [Fact]
        public void WebSocketBufferOverflowException_WithMessage_ShouldSetMessage()
        {
            // Arrange
            var message = "Buffer overflow detected in WebSocket frame";

            // Act
            var exception = new WebSocketBufferOverflowException(message);

            // Assert
            Assert.Equal(message, exception.Message);
        }

        [Fact]
        public void WebSocketBufferOverflowException_WithMessageAndInnerException_ShouldSetBoth()
        {
            // Arrange
            var message = "Buffer overflow";
            var innerException = new OutOfMemoryException("Memory error");

            // Act
            var exception = new WebSocketBufferOverflowException(message, innerException);

            // Assert
            Assert.Equal(message, exception.Message);
            Assert.Same(innerException, exception.InnerException);
        }

        [Fact]
        public void ServerListenerSocketException_DefaultConstructor_ShouldCreateException()
        {
            // Act
            var exception = new ServerListenerSocketException();

            // Assert
            Assert.NotNull(exception);
            Assert.IsType<ServerListenerSocketException>(exception);
        }

        [Fact]
        public void ServerListenerSocketException_WithMessage_ShouldSetMessage()
        {
            // Arrange
            var message = "Server socket listener failed to start on port 8080";

            // Act
            var exception = new ServerListenerSocketException(message);

            // Assert
            Assert.Equal(message, exception.Message);
        }

        [Fact]
        public void ServerListenerSocketException_WithMessageAndInnerException_ShouldSetBoth()
        {
            // Arrange
            var message = "Socket listener error";
            var innerException = new System.Net.Sockets.SocketException();

            // Act
            var exception = new ServerListenerSocketException(message, innerException);

            // Assert
            Assert.Equal(message, exception.Message);
            Assert.Same(innerException, exception.InnerException);
        }
    }
}