using System;

using Shinobi.WebSockets.Exceptions;

using Xunit;

namespace Shinobi.WebSockets.UnitTests
{
    public class InvalidHttpResponseCodeExceptionTests
    {
        [Theory]
        [InlineData(400, "400")]
        [InlineData(403, "403")]
        [InlineData(404, "404")]
        [InlineData(500, "500")]
        [InlineData(503, "503")]
        public void WithSpecificResponseCode_ShouldIncludeCodeInMessage(int responseCode, string expectedInMessage)
        {
            // Act
            var exception = new InvalidHttpResponseCodeException(responseCode);

            // Assert
            Assert.Contains(expectedInMessage, exception.Message);
            Assert.Equal(responseCode, exception.ResponseCode);
        }

        [Fact]
        public void WithNullResponseCode_ShouldHandleNullCorrectly()
        {
            // Act
            var exception = new InvalidHttpResponseCodeException((int?)null);

            // Assert
            Assert.Contains("Invalid status code:", exception.Message);
            Assert.Null(exception.ResponseCode);
        }

    }
}