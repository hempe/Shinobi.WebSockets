using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;

using Shinobi.WebSockets.Exceptions;
using Shinobi.WebSockets.Extensions;
using Shinobi.WebSockets.Http;

using Xunit;

namespace Shinobi.WebSockets.UnitTests
{
    public class WebSocketHttpContextExtensionsTests
    {
        private WebSocketHttpContext CreateTestContext(Dictionary<string, string> headers)
        {
            var request = "GET /test HTTP/1.1\r\n" +
                         "Host: example.com:8080\r\n";

            foreach (var header in headers)
            {
                request += $"{header.Key}: {header.Value}\r\n";
            }

            request += "\r\n";

            var httpRequest = HttpRequest.Parse(request);

            return new WebSocketHttpContext(null, httpRequest!, Stream.Null, Guid.NewGuid());
        }

        private WebSocketServerOptions CreateDefaultOptions()
        {
            return new WebSocketServerOptions();
        }

        [Fact]
        public void HandshakeResponse_WithValidWebSocketRequest_ShouldReturnSuccessfulHandshake()
        {
            // Arrange
            var headers = new Dictionary<string, string>
            {
                ["Sec-WebSocket-Key"] = "dGhlIHNhbXBsZSBub25jZQ==",
                ["Sec-WebSocket-Version"] = "13"
            };
            var context = this.CreateTestContext(headers);
            var options = this.CreateDefaultOptions();

            // Act
            var response = context.HandshakeResponse(options);

            // Assert
            Assert.Equal(101, response.StatusCode);
            Assert.Equal("Upgrade", response.GetHeaderValue("Connection"));
            Assert.Equal("websocket", response.GetHeaderValue("Upgrade"));
            Assert.Equal("s3pPLMBiTxaQ9kYGzzhZRbK+xOo=", response.GetHeaderValue("Sec-WebSocket-Accept"));
        }

        [Fact]
        public void HandshakeResponse_WithNullHttpRequest_ShouldThrowArgumentNullException()
        {
            // Arrange - Create context with valid headers but set HttpRequest to null via reflection
            var headers = new Dictionary<string, string>
            {
                ["Sec-WebSocket-Key"] = "dGhlIHNhbXBsZSBub25jZQ==",
                ["Sec-WebSocket-Version"] = "13"
            };
            var context = this.CreateTestContext(headers);
            var options = this.CreateDefaultOptions();

            // Use reflection to set HttpRequest to null to test the null check
            var httpRequestField = typeof(WebSocketHttpContext).GetField("HttpRequest",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            httpRequestField?.SetValue(context, null);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => context.HandshakeResponse(options));
        }

        [Fact]
        public void HandshakeResponse_WithMissingSecWebSocketKey_ShouldThrowSecWebSocketKeyMissingException()
        {
            // Arrange
            var headers = new Dictionary<string, string>
            {
                ["Sec-WebSocket-Version"] = "13"
            };
            var context = this.CreateTestContext(headers);
            var options = this.CreateDefaultOptions();

            // Act & Assert
            var exception = Assert.Throws<SecWebSocketKeyMissingException>(() => context.HandshakeResponse(options));
            Assert.Contains("Sec-WebSocket-Key", exception.Message);
        }

        [Fact]
        public void HandshakeResponse_WithEmptySecWebSocketKey_ShouldThrowSecWebSocketKeyMissingException()
        {
            // Arrange
            var headers = new Dictionary<string, string>
            {
                ["Sec-WebSocket-Key"] = "",
                ["Sec-WebSocket-Version"] = "13"
            };
            var context = this.CreateTestContext(headers);
            var options = this.CreateDefaultOptions();

            // Act & Assert
            Assert.Throws<SecWebSocketKeyMissingException>(() => context.HandshakeResponse(options));
        }

        [Fact]
        public void HandshakeResponse_WithMissingWebSocketVersion_ShouldThrowWebSocketVersionNotSupportedException()
        {
            // Arrange
            var headers = new Dictionary<string, string>
            {
                ["Sec-WebSocket-Key"] = "dGhlIHNhbXBsZSBub25jZQ=="
            };
            var context = this.CreateTestContext(headers);
            var options = this.CreateDefaultOptions();

            // Act & Assert
            var exception = Assert.Throws<WebSocketVersionNotSupportedException>(() => context.HandshakeResponse(options));
            Assert.Contains("Sec-WebSocket-Version", exception.Message);
        }

        [Theory]
        [InlineData("12")]
        [InlineData("10")]
        [InlineData("1")]
        public void HandshakeResponse_WithUnsupportedWebSocketVersion_ShouldThrowWebSocketVersionNotSupportedException(string version)
        {
            // Arrange
            var headers = new Dictionary<string, string>
            {
                ["Sec-WebSocket-Key"] = "dGhlIHNhbXBsZSBub25jZQ==",
                ["Sec-WebSocket-Version"] = version
            };
            var context = this.CreateTestContext(headers);
            var options = this.CreateDefaultOptions();

            // Act & Assert
            var exception = Assert.Throws<WebSocketVersionNotSupportedException>(() => context.HandshakeResponse(options));
            Assert.Contains($"WebSocket Version {version} not suported", exception.Message);
        }

        [Theory]
        [InlineData("13")]
        [InlineData("14")]
        [InlineData("15")]
        public void HandshakeResponse_WithSupportedWebSocketVersion_ShouldSucceed(string version)
        {
            // Arrange
            var headers = new Dictionary<string, string>
            {
                ["Sec-WebSocket-Key"] = "dGhlIHNhbXBsZSBub25jZQ==",
                ["Sec-WebSocket-Version"] = version
            };
            var context = this.CreateTestContext(headers);
            var options = this.CreateDefaultOptions();

            // Act
            var response = context.HandshakeResponse(options);

            // Assert
            Assert.Equal(101, response.StatusCode);
        }

        [Fact]
        public void HandshakeResponse_WithInvalidWebSocketVersionFormat_ShouldThrowFormatException()
        {
            // Arrange
            var headers = new Dictionary<string, string>
            {
                ["Sec-WebSocket-Key"] = "dGhlIHNhbXBsZSBub25jZQ==",
                ["Sec-WebSocket-Version"] = "invalid"
            };
            var context = this.CreateTestContext(headers);
            var options = this.CreateDefaultOptions();

            // Act & Assert
            Assert.ThrowsAny<Exception>(() => context.HandshakeResponse(options));
        }

        [Fact]
        public void HandshakeResponse_WithDifferentSecWebSocketKey_ShouldGenerateCorrectAcceptValue()
        {
            // Arrange
            var headers = new Dictionary<string, string>
            {
                ["Sec-WebSocket-Key"] = "x3JJHMbDL1EzLkh9GBhXDw==",
                ["Sec-WebSocket-Version"] = "13"
            };
            var context = this.CreateTestContext(headers);
            var options = this.CreateDefaultOptions();

            // Act
            var response = context.HandshakeResponse(options);

            // Assert
            // This should generate a different accept value for a different key
            Assert.Equal("HSmrc0sMlYUkAGmm5OPpG2HaGWk=", response.GetHeaderValue("Sec-WebSocket-Accept"));
        }

        [Fact]
        public void HandshakeResponse_ShouldIncludeRequiredHeaders()
        {
            // Arrange
            var headers = new Dictionary<string, string>
            {
                ["Sec-WebSocket-Key"] = "dGhlIHNhbXBsZSBub25jZQ==",
                ["Sec-WebSocket-Version"] = "13"
            };
            var context = this.CreateTestContext(headers);
            var options = this.CreateDefaultOptions();

            // Act
            var response = context.HandshakeResponse(options);

            // Assert
            Assert.NotNull(response.GetHeaderValue("Connection"));
            Assert.NotNull(response.GetHeaderValue("Upgrade"));
            Assert.NotNull(response.GetHeaderValue("Sec-WebSocket-Accept"));
            Assert.Equal("Upgrade", response.GetHeaderValue("Connection"));
            Assert.Equal("websocket", response.GetHeaderValue("Upgrade"));
        }

#if NET8_0_OR_GREATER
        [Fact]
        public void HandshakeResponse_WithPerMessageDeflateDisabled_ShouldNotIncludeExtensionsHeader()
        {
            // Arrange
            var headers = new Dictionary<string, string>
            {
                ["Sec-WebSocket-Key"] = "dGhlIHNhbXBsZSBub25jZQ==",
                ["Sec-WebSocket-Version"] = "13",
                ["Sec-WebSocket-Extensions"] = "permessage-deflate"
            };
            var context = this.CreateTestContext(headers);
            var options = this.CreateDefaultOptions();
            options.PerMessageDeflate.Enabled = false;

            // Act
            var response = context.HandshakeResponse(options);

            // Assert
            Assert.Null(response.GetHeaderValue("Sec-WebSocket-Extensions"));
        }

        [Fact]
        public void HandshakeResponse_WithPerMessageDeflateEnabled_ShouldIncludeExtensionsHeader()
        {
            // Arrange
            var headers = new Dictionary<string, string>
            {
                ["Sec-WebSocket-Key"] = "dGhlIHNhbXBsZSBub25jZQ==",
                ["Sec-WebSocket-Version"] = "13",
                ["Sec-WebSocket-Extensions"] = "permessage-deflate"
            };
            var context = this.CreateTestContext(headers);
            var options = this.CreateDefaultOptions();
            options.PerMessageDeflate.Enabled = true;

            // Act
            var response = context.HandshakeResponse(options);

            // Assert
            var extensionsHeader = response.GetHeaderValue("Sec-WebSocket-Extensions");
            Assert.NotNull(extensionsHeader);
            Assert.Contains("permessage-deflate", extensionsHeader);
        }

        [Fact]
        public void HandshakeResponse_WithServerContextTakeoverForceDisabled_ShouldIncludeServerNoContextTakeover()
        {
            // Arrange
            var headers = new Dictionary<string, string>
            {
                ["Sec-WebSocket-Key"] = "dGhlIHNhbXBsZSBub25jZQ==",
                ["Sec-WebSocket-Version"] = "13",
                ["Sec-WebSocket-Extensions"] = "permessage-deflate"
            };
            var context = this.CreateTestContext(headers);
            var options = this.CreateDefaultOptions();
            options.PerMessageDeflate.Enabled = true;
            options.PerMessageDeflate.ServerContextTakeover = ContextTakeoverMode.ForceDisabled;

            // Act
            var response = context.HandshakeResponse(options);

            // Assert
            var extensionsHeader = response.GetHeaderValue("Sec-WebSocket-Extensions");
            Assert.Contains("server_no_context_takeover", extensionsHeader);
        }

        [Fact]
        public void HandshakeResponse_WithClientContextTakeoverForceDisabled_ShouldIncludeClientNoContextTakeover()
        {
            // Arrange
            var headers = new Dictionary<string, string>
            {
                ["Sec-WebSocket-Key"] = "dGhlIHNhbXBsZSBub25jZQ==",
                ["Sec-WebSocket-Version"] = "13",
                ["Sec-WebSocket-Extensions"] = "permessage-deflate"
            };
            var context = this.CreateTestContext(headers);
            var options = this.CreateDefaultOptions();
            options.PerMessageDeflate.Enabled = true;
            options.PerMessageDeflate.ClientContextTakeover = ContextTakeoverMode.ForceDisabled;

            // Act
            var response = context.HandshakeResponse(options);

            // Assert
            var extensionsHeader = response.GetHeaderValue("Sec-WebSocket-Extensions");
            Assert.Contains("client_no_context_takeover", extensionsHeader);
        }

        [Fact]
        public void HandshakeResponse_WithClientRequestingServerNoContext_ShouldThrowWhenDontAllow()
        {
            // Arrange
            var headers = new Dictionary<string, string>
            {
                ["Sec-WebSocket-Key"] = "dGhlIHNhbXBsZSBub25jZQ==",
                ["Sec-WebSocket-Version"] = "13",
                ["Sec-WebSocket-Extensions"] = "permessage-deflate; server_no_context_takeover"
            };
            var context = this.CreateTestContext(headers);
            var options = this.CreateDefaultOptions();
            options.PerMessageDeflate.Enabled = true;
            options.PerMessageDeflate.ServerContextTakeover = ContextTakeoverMode.DontAllow;

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() => context.HandshakeResponse(options));
            Assert.Contains("context takeover mode that is not allowed", exception.Message);
        }

        [Fact]
        public void HandshakeResponse_WithClientRequestingClientNoContext_ShouldThrowWhenDontAllow()
        {
            // Arrange
            var headers = new Dictionary<string, string>
            {
                ["Sec-WebSocket-Key"] = "dGhlIHNhbXBsZSBub25jZQ==",
                ["Sec-WebSocket-Version"] = "13",
                ["Sec-WebSocket-Extensions"] = "permessage-deflate; client_no_context_takeover"
            };
            var context = this.CreateTestContext(headers);
            var options = this.CreateDefaultOptions();
            options.PerMessageDeflate.Enabled = true;
            options.PerMessageDeflate.ClientContextTakeover = ContextTakeoverMode.DontAllow;

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() => context.HandshakeResponse(options));
            Assert.Contains("context takeover mode that is not allowed", exception.Message);
        }

        [Fact]
        public void HandshakeResponse_WithNoClientDeflateRequest_ShouldNotIncludeExtensionsHeader()
        {
            // Arrange
            var headers = new Dictionary<string, string>
            {
                ["Sec-WebSocket-Key"] = "dGhlIHNhbXBsZSBub25jZQ==",
                ["Sec-WebSocket-Version"] = "13"
                // No Sec-WebSocket-Extensions header
            };
            var context = this.CreateTestContext(headers);
            var options = this.CreateDefaultOptions();
            options.PerMessageDeflate.Enabled = true;

            // Act
            var response = context.HandshakeResponse(options);

            // Assert
            Assert.Null(response.GetHeaderValue("Sec-WebSocket-Extensions"));
        }
#endif

        [Fact]
        public void HandshakeResponse_ShouldBeIdempotent()
        {
            // Arrange
            var headers = new Dictionary<string, string>
            {
                ["Sec-WebSocket-Key"] = "dGhlIHNhbXBsZSBub25jZQ==",
                ["Sec-WebSocket-Version"] = "13"
            };
            var context = this.CreateTestContext(headers);
            var options = this.CreateDefaultOptions();

            // Act
            var response1 = context.HandshakeResponse(options);
            var response2 = context.HandshakeResponse(options);

            // Assert
            Assert.Equal(response1.StatusCode, response2.StatusCode);
            Assert.Equal(response1.GetHeaderValue("Sec-WebSocket-Accept"), response2.GetHeaderValue("Sec-WebSocket-Accept"));
            Assert.Equal(response1.GetHeaderValue("Connection"), response2.GetHeaderValue("Connection"));
            Assert.Equal(response1.GetHeaderValue("Upgrade"), response2.GetHeaderValue("Upgrade"));
        }
    }
}