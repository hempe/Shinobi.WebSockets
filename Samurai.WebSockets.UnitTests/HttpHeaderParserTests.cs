using System;
using System.Linq;
using Xunit;
using Samurai.WebSockets.Extensions;

namespace Samurai.WebSockets.UnitTests
{

    public class HttpHeaderParserTests
    {
        [Fact]
        public void ParseWebSocketHandshakeRequest_ShouldParseCorrectly()
        {
            // Arrange
            var request = "GET /chat HTTP/1.1\r\n" +
                         "Host: example.com:8080\r\n" +
                         "Upgrade: websocket\r\n" +
                         "Connection: Upgrade\r\n" +
                         "Sec-WebSocket-Key: x3JJHMbDL1EzLkh9GBhXDw==\r\n" +
                         "Sec-WebSocket-Protocol: chat, superchat\r\n" +
                         "Sec-WebSocket-Version: 13\r\n" +
                         "Sec-WebSocket-Extensions: permessage-deflate; client_max_window_bits\r\n" +
                         "Origin: https://example.com\r\n" +
                         "User-Agent: TestClient/1.0\r\n" +
                         "\r\n";

            // Act
            var result = HttpRequest.Parse(request);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("x3JJHMbDL1EzLkh9GBhXDw==", result.GetHeaderValue("Sec-WebSocket-Key"));
            Assert.Equal("chat, superchat", result.GetHeaderValue("Sec-WebSocket-Protocol"));
            Assert.Equal("13", result.GetHeaderValue("Sec-WebSocket-Version"));
            Assert.Equal("permessage-deflate; client_max_window_bits", result.GetHeaderValue("Sec-WebSocket-Extensions"));

            // Test protocol parsing
            var protocols = result.GetHeaderValue("Sec-WebSocket-Protocol").ParseCommaSeparated();
            Assert.Equal(2, protocols.Length);
            Assert.Equal("chat", protocols[0]);
            Assert.Equal("superchat", protocols[1]);
        }

        [Fact]
        public void ParseWebSocketHandshakeResponse_ShouldParseCorrectly()
        {
            // Arrange
            var response = "HTTP/1.1 101 Switching Protocols\r\n" +
                          "Upgrade: websocket\r\n" +
                          "Connection: Upgrade\r\n" +
                          "Sec-WebSocket-Accept: HSmrc0sMlYUkAGmm5OPpG2HaGWk=\r\n" +
                          "Sec-WebSocket-Protocol: chat\r\n" +
                          "Sec-WebSocket-Extensions: permessage-deflate; server_max_window_bits=10\r\n" +
                          "Server: TestServer/1.0\r\n" +
                          "\r\n";

            // Act
            var result = HttpResponse.Parse(response);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(101, result.StatusCode);
            Assert.Equal("HSmrc0sMlYUkAGmm5OPpG2HaGWk=", result.GetHeaderValue("Sec-WebSocket-Accept"));
            Assert.Equal("chat", result.GetHeaderValue("Sec-WebSocket-Protocol"));
            Assert.Equal("permessage-deflate; server_max_window_bits=10", result.GetHeaderValue("Sec-WebSocket-Extensions"));

            // Test extension parsing
            var extensions = result.GetHeaderValue("Sec-WebSocket-Extensions").ParseExtension();
            Assert.NotNull(extensions);
            Assert.Equal("permessage-deflate", extensions.Name);
            Assert.Equal("10", extensions.Parameters["server_max_window_bits"].ToString());
        }

        [Fact]
        public void ValidateWebSocketHandshake_ValidResponse()
        {
            // Arrange
            var validResponse = "HTTP/1.1 101 Switching Protocols\r\n" +
                               "Sec-WebSocket-Accept: HSmrc0sMlYUkAGmm5OPpG2HaGWk=\r\n" +
                               "\r\n";

            var result = HttpResponse.Parse(validResponse);

            Assert.NotNull(result);
            Assert.Equal(101, result.StatusCode);
            Assert.True(result.HasHeader("Sec-WebSocket-Accept"));
        }

        [Fact]
        public void ValidateWebSocketHandshake_InvalidStatusCode_ShouldThrow()
        {
            // Arrange
            var invalidResponse = "HTTP/1.1 400 Bad Request\r\n" +
                                 "Content-Type: text/plain\r\n" +
                                 "\r\n";

            var result = HttpResponse.Parse(invalidResponse);

            // Act & Assert
            Assert.NotNull(result);
            Assert.NotEqual(101, result.StatusCode);
        }

        [Fact]
        public void ValidateWebSocketHandshake_MissingAcceptHeader()
        {
            // Arrange
            var missingAcceptResponse = "HTTP/1.1 101 Switching Protocols\r\n" +
                                       "Upgrade: websocket\r\n" +
                                       "\r\n";

            var result = HttpResponse.Parse(missingAcceptResponse);

            // Act & Assert
            Assert.NotNull(result);
            Assert.False(result.HasHeader("Sec-WebSocket-Accept"));
        }

        [Fact]
        public void Parse_MultipleHeaderValues_ShouldHandleCorrectly()
        {
            // Arrange
            var request = "GET /test HTTP/1.1\r\n" +
                         "Host: example.com\r\n" +
                         "Sec-WebSocket-Protocol: chat\r\n" +
                         "Sec-WebSocket-Protocol: superchat\r\n" +
                         "Sec-WebSocket-Protocol: megachat\r\n" +
                         "Accept: text/html\r\n" +
                         "Accept: application/json\r\n" +
                         "\r\n";

            // Act
            var result = HttpRequest.Parse(request);
            Assert.NotNull(result);

            // Assert
            var protocols = result.GetHeaderValues("Sec-WebSocket-Protocol").ToArray();
            Assert.Equal(3, protocols.Length);
            Assert.Equal("chat", protocols[0]);
            Assert.Equal("superchat", protocols[1]);
            Assert.Equal("megachat", protocols[2]);

            Assert.Equal("chat, superchat, megachat", result.GetHeaderValuesCombined("Sec-WebSocket-Protocol"));

            var accepts = result.GetHeaderValues("Accept").ToArray();
            Assert.Equal(2, accepts.Length);
            Assert.Equal("text/html", accepts[0]);
            Assert.Equal("application/json", accepts[1]);
        }

        [Fact]
        public void ParseCommaSeparated_ShouldHandleComplexValues()
        {
            // Arrange
            var response = "HTTP/1.1 200 OK\r\n" +
                          "Accept-Encoding: gzip, deflate, br\r\n" +
                          "Cache-Control: no-cache, no-store, must-revalidate\r\n" +
                          "Vary: Origin, Accept-Encoding\r\n" +
                          "\r\n";

            // Act
            var result = HttpResponse.Parse(response);
            Assert.NotNull(result);

            // Assert
            Assert.Equal(200, result.StatusCode);

            var encoding = result.GetHeaderValue("Accept-Encoding").ParseCommaSeparated();
            Assert.Equal(3, encoding.Length);
            Assert.Equal("gzip", encoding[0]);
            Assert.Equal("deflate", encoding[1]);
            Assert.Equal("br", encoding[2]);

            var cacheControl = result.GetHeaderValue("Cache-Control").ParseCommaSeparated();
            Assert.Equal(3, cacheControl.Length);
            Assert.Equal("no-cache", cacheControl[0]);
            Assert.Equal("no-store", cacheControl[1]);
            Assert.Equal("must-revalidate", cacheControl[2]);
        }

        [Fact]
        public void Parse_FoldedHeaders_ShouldCombineCorrectly()
        {
            // Arrange
            var request = "GET /test HTTP/1.1\r\n" +
                         "Host: example.com\r\n" +
                         "User-Agent: Mozilla/5.0\r\n" +
                         "  (Windows NT 10.0; Win64; x64)\r\n" +
                         "  AppleWebKit/537.36\r\n" +
                         "Accept: text/html,\r\n" +
                         " application/json,\r\n" +
                         "\tapplication/xml\r\n" +
                         "\r\n";

            // Act
            var result = HttpRequest.Parse(request);
            Assert.NotNull(result);
            // Assert
            var userAgent = result.GetHeaderValue("User-Agent");
            Assert.Contains("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36", userAgent);

            var accept = result.GetHeaderValue("Accept");
            Assert.Contains("text/html, application/json, application/xml", accept);
        }

        [Fact]
        public void Parse_EdgeCases_ShouldHandleGracefully()
        {
            // Test empty input
            var emptyResult = HttpResponse.Parse("");
            Assert.Null(emptyResult);

            // Test status-only response
            var statusOnlyResult = HttpResponse.Parse("HTTP/1.1 404 Not Found\r\n\r\n");
            Assert.NotNull(statusOnlyResult);
            Assert.Equal(404, statusOnlyResult.StatusCode);
            Assert.Empty(statusOnlyResult.AsKeyValuePairs());

            // Test malformed header (missing colon)
            var malformedResult = HttpResponse.Parse("HTTP/1.1 200 OK\r\nHost example.com\r\nContent-Type: text/plain\r\n\r\n");
            Assert.NotNull(malformedResult);
            Assert.Equal("text/plain", malformedResult.GetHeaderValue("Content-Type"));
            Assert.False(malformedResult.HasHeader("Host example.com"));

            // Test header with empty value
            var emptyValueResult = HttpResponse.Parse("HTTP/1.1 200 OK\r\nX-Empty:\r\nContent-Length: 0\r\n\r\n");
            Assert.NotNull(emptyValueResult);
            Assert.Equal("", emptyValueResult.GetHeaderValue("X-Empty"));
            Assert.Equal("0", emptyValueResult.GetHeaderValue("Content-Length"));
        }

        [Theory]
        [InlineData("HTTP/1.1 200 OK", 200)]
        [InlineData("HTTP/1.1 404 Not Found", 404)]
        [InlineData("HTTP/1.1 101 Switching Protocols", 101)]
        [InlineData("HTTP/2 200", 200)]
        [InlineData("GET /test HTTP/1.1", null)]
        [InlineData("", null)]
        [InlineData("HTTP/1.1", null)]
        public void ExtractStatusCode_ShouldParseCorrectly(string firstLine, int? expectedStatusCode)
        {
            // Arrange
            var input = string.IsNullOrEmpty(firstLine) ? "" : firstLine + "\r\n\r\n";

            // Act
            var result = HttpResponse.Parse(input);

            // Assert
            Assert.Equal(expectedStatusCode, result?.StatusCode);
        }
    }
}