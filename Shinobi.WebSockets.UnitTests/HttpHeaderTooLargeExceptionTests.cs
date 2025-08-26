using System;

using Shinobi.WebSockets.Exceptions;

using Xunit;

namespace Shinobi.WebSockets.UnitTests
{
    public class HttpHeaderTooLargeExceptionTests
    {
        [Fact]
        public void WithActualAndMaxSize_ShouldSetPropertiesAndGenerateMessage()
        {
            // Arrange
            var actualSize = 32768; // 32KB
            var maxSize = 16384; // 16KB

            // Act
            var exception = new HttpHeaderTooLargeException(actualSize, maxSize);

            // Assert
            Assert.Equal(actualSize, exception.ActualSize);
            Assert.Equal(maxSize, exception.MaxSize);
            Assert.Contains(actualSize.ToString(), exception.Message);
            Assert.Contains(maxSize.ToString(), exception.Message);
            Assert.Contains("exceeds maximum allowed size", exception.Message);
        }

        [Fact]
        public void WithStringMessage_ShouldNotSetSizeProperties()
        {
            // Arrange
            var message = "Custom header size error message";

            // Act
            var exception = new HttpHeaderTooLargeException(message);

            // Assert
            Assert.Equal(message, exception.Message);
            Assert.Null(exception.ActualSize);
            Assert.Null(exception.MaxSize);
        }

        [Fact]
        public void DefaultConstructor_ShouldNotSetSizeProperties()
        {
            // Act
            var exception = new HttpHeaderTooLargeException();

            // Assert
            Assert.Null(exception.ActualSize);
            Assert.Null(exception.MaxSize);
        }

        [Fact]
        public void RealisticScenario_LargeRequestHeaders()
        {
            // Arrange - Simulate realistic HTTP header size limits
            var typicalMaxSize = 8192; // 8KB typical server limit
            var oversizedHeader = 12288; // 12KB request from malicious client

            // Act
            var exception = new HttpHeaderTooLargeException(oversizedHeader, typicalMaxSize);

            // Assert
            Assert.Equal(oversizedHeader, exception.ActualSize);
            Assert.Equal(typicalMaxSize, exception.MaxSize);
            Assert.Contains("12288", exception.Message);
            Assert.Contains("8192", exception.Message);
        }
    }
}