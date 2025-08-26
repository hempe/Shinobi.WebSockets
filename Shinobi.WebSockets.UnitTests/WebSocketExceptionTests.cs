using System;
using System.Runtime.Serialization;

using Shinobi.WebSockets.Exceptions;

using Xunit;

namespace Shinobi.WebSockets.UnitTests
{
    public class WebSocketExceptionTests
    {
        [Fact]
        public void AllExceptions_ShouldBeSerializable()
        {
            // Test that all custom exceptions can be used in exception handling scenarios
            // where serialization might be required (e.g., remoting, logging frameworks)

            var exceptions = new Exception[]
            {
                new HttpHeaderTooLargeException(16384, 8192),
                new InvalidHttpResponseCodeException(500),
                new SecWebSocketKeyMissingException("Test message"),
                new WebSocketVersionNotSupportedException("Test message")
            };

            foreach (var exception in exceptions)
            {
                // Assert - Each exception should have the Serializable attribute
                var type = exception.GetType();
                var serializableAttribute = Attribute.GetCustomAttribute(type, typeof(SerializableAttribute));
                Assert.NotNull(serializableAttribute);
            }
        }

        [Fact]
        public void AllCustomExceptions_ShouldInheritFromException()
        {
            // Arrange
            var exceptionTypes = new[]
            {
                typeof(HttpHeaderTooLargeException),
                typeof(InvalidHttpResponseCodeException),
                typeof(SecWebSocketKeyMissingException),
                typeof(WebSocketVersionNotSupportedException)
            };

            foreach (var exceptionType in exceptionTypes)
            {
                // Act & Assert
                Assert.True(typeof(Exception).IsAssignableFrom(exceptionType), 
                    $"{exceptionType.Name} should inherit from Exception");
            }
        }
    }
}