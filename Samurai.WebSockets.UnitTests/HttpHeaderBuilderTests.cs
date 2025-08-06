using System;
using System.Linq;
using Xunit;
using Samurai.WebSockets.Extensions;

namespace Samurai.WebSockets.UnitTests
{
    public class HttpHeaderBuilderTests
    {
        [Fact]
        public void CreateResponse_ShouldBuildCorrectHttpResponse()
        {
            // Arrange & Act
            var response = HttpResponse.Create(200)
                .AddHeader("Content-Type", "application/json")
                .AddHeader("Cache-Control", "no-cache")
                .AddHeader("Server", "TestServer/1.0")
                .ToHttpResponse("OK");

            // Assert
            var expected = "HTTP/1.1 200 OK\r\n" +
                          "Content-Type: application/json\r\n" +
                          "Cache-Control: no-cache\r\n" +
                          "Server: TestServer/1.0\r\n" +
                          "\r\n";

            Assert.Equal(expected, response);
        }

        [Fact]
        public void CreateRequest_ShouldBuildCorrectHttpRequest()
        {
            // Arrange & Act
            var request = HttpRequest.Create("GET", "/api/data")
                .AddHeader("Host", "example.com")
                .AddHeader("Authorization", "Bearer token123")
                .AddHeader("Accept", "application/json")
                .ToHttpRequest();

            // Assert
            var expected = "GET /api/data HTTP/1.1\r\n" +
                          "Host: example.com\r\n" +
                          "Authorization: Bearer token123\r\n" +
                          "Accept: application/json\r\n" +
                          "\r\n";

            Assert.Equal(expected, request);
        }

        [Fact]
        public void CreateWebSocketHandshakeRequest_ShouldBuildCorrectly()
        {
            // Arrange
            var uri = new Uri("ws://example.com:8080/chat");
            var secWebSocketKey = "x3JJHMbDL1EzLkh9GBhXDw==";
            var subProtocol = "chat";
            var extensions = "permessage-deflate; client_max_window_bits";

            // Act
            var request = HttpRequest.Create("GET", uri.PathAndQuery)
                .AddHeader("Host", $"{uri.Host}:{uri.Port}")
                .AddHeader("Upgrade", "websocket")
                .AddHeader("Connection", "Upgrade")
                .AddHeader("Sec-WebSocket-Key", secWebSocketKey)
                .AddHeader("Origin", $"http://{uri.Host}:{uri.Port}")
                .AddHeaderIf(!string.IsNullOrEmpty(subProtocol), "Sec-WebSocket-Protocol", subProtocol)
                .AddHeaderIf(!string.IsNullOrEmpty(extensions), "Sec-WebSocket-Extensions", extensions)
                .AddHeader("Sec-WebSocket-Version", "13")
                .ToHttpRequest();

            // Assert
            var expected = "GET /chat HTTP/1.1\r\n" +
                          "Host: example.com:8080\r\n" +
                          "Upgrade: websocket\r\n" +
                          "Connection: Upgrade\r\n" +
                          "Sec-WebSocket-Key: x3JJHMbDL1EzLkh9GBhXDw==\r\n" +
                          "Origin: http://example.com:8080\r\n" +
                          "Sec-WebSocket-Protocol: chat\r\n" +
                          "Sec-WebSocket-Extensions: permessage-deflate; client_max_window_bits\r\n" +
                          "Sec-WebSocket-Version: 13\r\n" +
                          "\r\n";

            Assert.Equal(expected, request);
        }

        [Fact]
        public void CreateWebSocketHandshakeResponse_ShouldBuildCorrectly()
        {
            // Arrange
            var webSocketAccept = "HSmrc0sMlYUkAGmm5OPpG2HaGWk=";
            var subProtocol = "chat";
            var extensions = "permessage-deflate; server_max_window_bits=10";

            // Act
            var response = HttpResponse.Create(101)
                .AddHeader("Upgrade", "websocket")
                .AddHeader("Connection", "Upgrade")
                .AddHeader("Sec-WebSocket-Accept", webSocketAccept)
                .AddHeaderIf(!string.IsNullOrEmpty(subProtocol), "Sec-WebSocket-Protocol", subProtocol)
                .AddHeaderIf(!string.IsNullOrEmpty(extensions), "Sec-WebSocket-Extensions", extensions)
                .AddHeader("Server", "TestServer/1.0")
                .ToHttpResponse("Switching Protocols");

            // Assert
            var expected = "HTTP/1.1 101 Switching Protocols\r\n" +
                          "Upgrade: websocket\r\n" +
                          "Connection: Upgrade\r\n" +
                          "Sec-WebSocket-Accept: HSmrc0sMlYUkAGmm5OPpG2HaGWk=\r\n" +
                          "Sec-WebSocket-Protocol: chat\r\n" +
                          "Sec-WebSocket-Extensions: permessage-deflate; server_max_window_bits=10\r\n" +
                          "Server: TestServer/1.0\r\n" +
                          "\r\n";

            Assert.Equal(expected, response);
        }

        [Fact]
        public void AddHeaderIf_TrueCondition_ShouldAddHeader()
        {
            // Arrange & Act
            var header = HttpResponse.Create(200)
                .AddHeaderIf(true, "X-Test", "value")
                .Build();

            // Assert
            Assert.True(header.HasHeader("X-Test"));
            Assert.Equal("value", header.GetHeaderValue("X-Test"));
        }

        [Fact]
        public void AddHeaderIf_FalseCondition_ShouldNotAddHeader()
        {
            // Arrange & Act
            var header = HttpResponse.Create(200)
                .AddHeaderIf(false, "X-Test", "value")
                .Build();

            // Assert
            Assert.False(header.HasHeader("X-Test"));
            Assert.Null(header.GetHeaderValue("X-Test"));
        }

        [Fact]
        public void AddHeaderIf_WithValueFactory_TrueCondition_ShouldAddHeader()
        {
            // Arrange & Act
            var header = HttpResponse.Create(200)
                .AddHeaderIf(true, "X-Timestamp", () => DateTimeOffset.UtcNow.ToString("R"))
                .Build();

            // Assert
            Assert.True(header.HasHeader("X-Timestamp"));
            Assert.NotNull(header.GetHeaderValue("X-Timestamp"));
        }

        [Fact]
        public void AddHeaderIf_WithValueFactory_FalseCondition_ShouldNotCallFactory()
        {
            // Arrange
            var factoryCalled = false;

            // Act
            var header = HttpResponse.Create(200)
                .AddHeaderIf(false, "X-Test", () =>
                {
                    factoryCalled = true;
                    return "value";
                })
                .Build();

            // Assert
            Assert.False(header.HasHeader("X-Test"));
            Assert.False(factoryCalled);
        }

        [Fact]
        public void AddHeader_MultipleValues_ShouldHandleCorrectly()
        {
            // Arrange & Act
            var header = HttpResponse.Create(200)
                .AddHeader("Accept", new[] { "text/html", "application/json", "application/xml" })
                .Build();

            // Assert
            var values = header.GetHeaderValues("Accept").ToArray();
            Assert.Equal(3, values.Length);
            Assert.Contains("text/html", values);
            Assert.Contains("application/json", values);
            Assert.Contains("application/xml", values);

            Assert.Equal("text/html, application/json, application/xml", header.GetHeaderValuesCombined("Accept"));
        }

        [Fact]
        public void AddHeader_DuplicateHeaderNames_ShouldAccumulate()
        {
            // Arrange & Act
            var header = HttpResponse.Create(200)
                .AddHeader("Set-Cookie", "session=abc123")
                .AddHeader("Set-Cookie", "theme=dark")
                .AddHeader("Set-Cookie", "lang=en")
                .Build();

            // Assert
            var cookies = header.GetHeaderValues("Set-Cookie").ToArray();
            Assert.Equal(3, cookies.Length);
            Assert.Contains("session=abc123", cookies);
            Assert.Contains("theme=dark", cookies);
            Assert.Contains("lang=en", cookies);
        }

        [Fact]
        public void AddRawHeaders_ValidInput_ShouldParseCorrectly()
        {
            // Arrange
            var rawHeaders = "Content-Type: application/json\r\n" +
                           "Cache-Control: no-cache\r\n" +
                           "X-Custom: custom-value\r\n";

            // Act
            var header = HttpResponse.Create(200)
                .AddRawHeaders(rawHeaders)
                .Build();

            // Assert
            Assert.Equal("application/json", header.GetHeaderValue("Content-Type"));
            Assert.Equal("no-cache", header.GetHeaderValue("Cache-Control"));
            Assert.Equal("custom-value", header.GetHeaderValue("X-Custom"));
        }

        [Fact]
        public void AddRawHeaders_EmptyInput_ShouldNotThrow()
        {
            // Arrange & Act
            var header = HttpResponse.Create(200)
                .AddRawHeaders("")
                .AddRawHeaders(null)
                .Build();

            // Assert
            Assert.Equal(200, header.StatusCode);
            Assert.Empty(header.AsKeyValuePairs());
        }

        [Fact]
        public void AddRawHeaders_MalformedHeaders_ShouldSkipInvalidLines()
        {
            // Arrange
            var rawHeaders = "Valid-Header: valid-value\r\n" +
                           "Invalid Header Without Colon\r\n" +
                           "Another-Valid: another-value\r\n" +
                           ": empty-name\r\n";

            // Act
            var header = HttpResponse.Create(200)
                .AddRawHeaders(rawHeaders)
                .Build();

            // Assert
            Assert.Equal("valid-value", header.GetHeaderValue("Valid-Header"));
            Assert.Equal("another-value", header.GetHeaderValue("Another-Valid"));
            Assert.False(header.HasHeader("Invalid Header Without Colon"));
        }

        [Fact]
        public void AddHeaders_FromAnotherHttpHeader_ShouldCopyAllHeaders()
        {
            // Arrange
            var sourceHeader = HttpResponse.Create(404)
                .AddHeader("Content-Type", "text/plain")
                .AddHeader("X-Error-Code", "USER_NOT_FOUND")
                .AddHeader("Set-Cookie", new[] { "error=true", "timestamp=123456" })
                .Build();

            // Act
            var targetHeader = HttpResponse.Create(200)
                .AddHeader("Server", "TestServer/1.0")
                .AddHeaders(sourceHeader)
                .Build();

            // Assert
            Assert.Equal(200, targetHeader.StatusCode); // Should keep target's status
            Assert.Equal("TestServer/1.0", targetHeader.GetHeaderValue("Server"));
            Assert.Equal("text/plain", targetHeader.GetHeaderValue("Content-Type"));
            Assert.Equal("USER_NOT_FOUND", targetHeader.GetHeaderValue("X-Error-Code"));

            var cookies = targetHeader.GetHeaderValues("Set-Cookie").ToArray();
            Assert.Equal(2, cookies.Length);
            Assert.Contains("error=true", cookies);
            Assert.Contains("timestamp=123456", cookies);
        }

        [Fact]
        public void ImplicitConversion_ShouldWorkCorrectly()
        {
            // Arrange
            HttpResponse header = HttpResponse.Create(201)
                .AddHeader("Location", "/api/users/123")
                .AddHeader("Content-Type", "application/json");

            // Assert
            Assert.Equal(201, header.StatusCode);
            Assert.Equal("/api/users/123", header.GetHeaderValue("Location"));
            Assert.Equal("application/json", header.GetHeaderValue("Content-Type"));
        }

        [Fact]
        public void ToHttpRequest_CustomHttpVersion_ShouldUseCorrectVersion()
        {
            // Arrange & Act
            var request = HttpRequest.Create("POST", "/api/data")
                .AddHeader("Content-Type", "application/json")
                .ToHttpRequest("HTTP/2");

            // Assert
            Assert.StartsWith("POST /api/data HTTP/2\r\n", request);
        }

        [Fact]
        public void FluentChaining_ShouldWorkSeamlessly()
        {
            // Arrange & Act
            var response = HttpResponse.Create(200)
                .AddHeader("Content-Type", "application/json")
                .AddHeaderIf(true, "X-Request-ID", "req-123")
                .AddHeaderIf(false, "X-Debug", "enabled")
                .AddHeader("Cache-Control", new[] { "no-cache", "no-store" })
                .AddRawHeaders("X-Custom: raw-value\r\nX-Another: another-value")
                .ToHttpResponse();

            // Assert
            Assert.Contains("HTTP/1.1 200 OK", response);
            Assert.Contains("Content-Type: application/json", response);
            Assert.Contains("X-Request-ID: req-123", response);
            Assert.DoesNotContain("X-Debug", response);
            Assert.Contains("Cache-Control: no-cache", response);
            Assert.Contains("Cache-Control: no-store", response);
            Assert.Contains("X-Custom: raw-value", response);
            Assert.Contains("X-Another: another-value", response);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("   ")]
        public void AddHeaderIf_WithEmptyOrNullValues_ShouldHandleGracefully(string? value)
        {
            // Arrange & Act
            var header = HttpResponse.Create(200)
                .AddHeaderIf(!string.IsNullOrWhiteSpace(value), "X-Optional", value!)
                .Build();

            // Assert
            Assert.False(header.HasHeader("X-Optional"));
        }

        [Fact]
        public void RoundTrip_ParseAndBuild_ShouldMaintainEquivalence()
        {
            // Arrange
            var originalRequest = "GET /api/test HTTP/1.1\r\n" +
                                "Host: example.com\r\n" +
                                "Authorization: Bearer token123\r\n" +
                                "Accept: application/json\r\n" +
                                "User-Agent: TestClient/1.0\r\n" +
                                "\r\n";

            // Act - Parse then rebuild
            var parsed = HttpRequest.Parse(originalRequest);
            Assert.NotNull(parsed);

            var rebuilt = HttpRequest.Create(parsed.Method, parsed.Path)
                .AddHeaders(parsed)
                .ToHttpRequest();

            // Assert - Should contain same information (order might differ)
            Assert.Contains("GET /api/test HTTP/1.1", rebuilt);
            Assert.Contains("Host: example.com", rebuilt);
            Assert.Contains("Authorization: Bearer token123", rebuilt);
            Assert.Contains("Accept: application/json", rebuilt);
            Assert.Contains("User-Agent: TestClient/1.0", rebuilt);
        }
    }
}