using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Shinobi.WebSockets.Http;

namespace Shinobi.WebSockets.UnitTests
{
    public class HttpResponseTests
    {
        [Fact]
        public void Create_ShouldCreateResponseWithCorrectStatusCode()
        {
            // Act
            var response = HttpResponse.Create(200);

            // Assert
            Assert.Equal(200, response.StatusCode);
            Assert.Empty(response.AsKeyValuePairs());
        }

        [Theory]
        [InlineData(100, "Continue")]
        [InlineData(101, "Switching Protocols")]
        [InlineData(102, "Processing")]
        [InlineData(200, "OK")]
        [InlineData(201, "Created")]
        [InlineData(202, "Accepted")]
        [InlineData(203, "Non-Authoritative Information")]
        [InlineData(204, "No Content")]
        [InlineData(205, "Reset Content")]
        [InlineData(206, "Partial Content")]
        [InlineData(207, "Multi-Status")]
        [InlineData(300, "Multiple Choices")]
        [InlineData(301, "Moved Permanently")]
        [InlineData(302, "Found")]
        [InlineData(303, "See Other")]
        [InlineData(304, "Not Modified")]
        [InlineData(305, "Use Proxy")]
        [InlineData(307, "Temporary Redirect")]
        [InlineData(308, "Permanent Redirect")]
        [InlineData(400, "Bad Request")]
        [InlineData(401, "Unauthorized")]
        [InlineData(402, "Payment Required")]
        [InlineData(403, "Forbidden")]
        [InlineData(404, "Not Found")]
        [InlineData(405, "Method Not Allowed")]
        [InlineData(406, "Not Acceptable")]
        [InlineData(407, "Proxy Authentication Required")]
        [InlineData(408, "Request Timeout")]
        [InlineData(409, "Conflict")]
        [InlineData(410, "Gone")]
        [InlineData(411, "Length Required")]
        [InlineData(412, "Precondition Failed")]
        [InlineData(413, "Payload Too Large")]
        [InlineData(414, "URI Too Long")]
        [InlineData(415, "Unsupported Media Type")]
        [InlineData(416, "Range Not Satisfiable")]
        [InlineData(417, "Expectation Failed")]
        [InlineData(426, "Upgrade Required")]
        [InlineData(500, "Internal Server Error")]
        [InlineData(501, "Not Implemented")]
        [InlineData(502, "Bad Gateway")]
        [InlineData(503, "Service Unavailable")]
        [InlineData(504, "Gateway Timeout")]
        [InlineData(505, "HTTP Version Not Supported")]
        [InlineData(999, "Unknown Status")]
        public void Build_ShouldIncludeCorrectReasonPhrase(int statusCode, string expectedReasonPhrase)
        {
            // Arrange
            var response = HttpResponse.Create(statusCode);

            // Act
            var responseText = response.Build();

            // Assert
            Assert.Contains($"HTTP/1.1 {statusCode} {expectedReasonPhrase}", responseText);
        }

        [Fact]
        public void Parse_WithValidResponse_ShouldParseCorrectly()
        {
            // Arrange
            var responseText = "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: 11\r\n\r\n";

            // Act
            var response = HttpResponse.Parse(responseText);

            // Assert
            Assert.NotNull(response);
            Assert.Equal(200, response.StatusCode);
            Assert.Contains("text/plain", response.GetHeaderValues("Content-Type"));
            Assert.Contains("11", response.GetHeaderValues("Content-Length"));
        }

        [Fact]
        public void Parse_WithNullString_ShouldReturnNull()
        {
            // Act
            var response = HttpResponse.Parse((string?)null);

            // Assert
            Assert.Null(response);
        }

        [Fact]
        public void Parse_WithEmptyString_ShouldReturnNull()
        {
            // Act
            var response = HttpResponse.Parse("");

            // Assert
            Assert.Null(response);
        }

        [Fact]
        public void Parse_WithoutNewline_ShouldReturnNull()
        {
            // Arrange
            var malformedResponse = "HTTP/1.1 200 OK";

            // Act
            var response = HttpResponse.Parse(malformedResponse);

            // Assert
            Assert.Null(response);
        }

        [Fact]
        public void Parse_WithMalformedStatusLine_NoSpaces_ShouldReturnNull()
        {
            // Arrange
            var malformedResponse = "HTTP/1.1_200_OK\r\n\r\n";

            // Act
            var response = HttpResponse.Parse(malformedResponse);

            // Assert
            Assert.Null(response);
        }

        [Fact]
        public void Parse_WithMalformedStatusLine_NoStatusCode_ShouldReturnNull()
        {
            // Arrange
            var malformedResponse = "HTTP/1.1 \r\n\r\n";

            // Act
            var response = HttpResponse.Parse(malformedResponse);

            // Assert
            Assert.Null(response);
        }

        [Fact]
        public void Parse_WithNonNumericStatusCode_ShouldReturnNull()
        {
            // Arrange
            var malformedResponse = "HTTP/1.1 ABC OK\r\n\r\n";

            // Act
            var response = HttpResponse.Parse(malformedResponse);

            // Assert
            Assert.Null(response);
        }

        [Fact]
        public void Parse_WithStatusCodeOnly_ShouldParseCorrectly()
        {
            // Arrange
            var responseText = "HTTP/1.1 404\r\n\r\n";

            // Act
            var response = HttpResponse.Parse(responseText);

            // Assert
            Assert.NotNull(response);
            Assert.Equal(404, response.StatusCode);
        }

        [Fact]
        public void Parse_WithMultipleHeaders_ShouldParseAll()
        {
            // Arrange
            var responseText = "HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nServer: TestServer/1.0\r\nX-Custom: value1\r\nX-Custom: value2\r\n\r\n";

            // Act
            var response = HttpResponse.Parse(responseText);

            // Assert
            Assert.NotNull(response);
            Assert.Equal(200, response.StatusCode);
            Assert.Contains("text/html", response.GetHeaderValues("Content-Type"));
            Assert.Contains("TestServer/1.0", response.GetHeaderValues("Server"));
            Assert.Contains("value1", response.GetHeaderValues("X-Custom"));
            Assert.Contains("value2", response.GetHeaderValues("X-Custom"));
        }

#if NET8_0_OR_GREATER
        [Fact]
        public void Parse_WithByteSpan_ShouldParseCorrectly()
        {
            // Arrange
            var responseText = "HTTP/1.1 201 Created\r\nLocation: /api/resource/123\r\n\r\n";
            var responseBytes = Encoding.UTF8.GetBytes(responseText);

            // Act
            var response = HttpResponse.Parse(responseBytes.AsSpan());

            // Assert
            Assert.NotNull(response);
            Assert.Equal(201, response.StatusCode);
            Assert.Contains("/api/resource/123", response.GetHeaderValues("Location"));
        }

        [Fact]
        public void Parse_WithEmptyByteSpan_ShouldReturnNull()
        {
            // Act
            var response = HttpResponse.Parse(ReadOnlySpan<byte>.Empty);

            // Assert
            Assert.Null(response);
        }
#endif

        [Fact]
        public async Task ReadAsync_WithValidStream_ShouldParseResponseAsync()
        {
            // Arrange
            var responseText = "HTTP/1.1 500 Internal Server Error\r\nContent-Type: application/json\r\n\r\n";
            var responseBytes = Encoding.UTF8.GetBytes(responseText);
            using var stream = new MemoryStream(responseBytes);

            // Act
            var response = await HttpResponse.ReadAsync(stream, CancellationToken.None);

            // Assert
            Assert.NotNull(response);
            Assert.Equal(500, response.StatusCode);
            Assert.Contains("application/json", response.GetHeaderValues("Content-Type"));
        }

        [Fact]
        public async Task ReadAsync_WithEmptyStream_ShouldReturnNullAsync()
        {
            // Arrange
            using var stream = new MemoryStream();

            // Act
            var response = await HttpResponse.ReadAsync(stream, CancellationToken.None);

            // Assert
            Assert.Null(response);
        }

        [Fact]
        public void Build_WithoutBody_ShouldCreateValidResponse()
        {
            // Arrange
            var response = HttpResponse.Create(404)
                .AddHeader("Content-Type", "text/plain")
                .AddHeader("Server", "TestServer/1.0");

            // Act
            var responseText = response.Build();

            // Assert
            Assert.Contains("HTTP/1.1 404 Not Found", responseText);
            Assert.Contains("Content-Type: text/plain", responseText);
            Assert.Contains("Server: TestServer/1.0", responseText);
            Assert.Contains("Date:", responseText);
            Assert.Contains("\r\n\r\n", responseText);
        }

        [Fact]
        public void Build_WithStringBody_ShouldIncludeBodyInResponse()
        {
            // Arrange
            var bodyContent = "Error: Resource not found";
            var response = HttpResponse.Create(404)
                .AddHeader("Content-Type", "text/plain")
                .WithBody(bodyContent);

            // Act
            var responseText = response.Build();

            // Assert
            Assert.Contains("HTTP/1.1 404 Not Found", responseText);
            Assert.Contains($"Content-Length: {bodyContent.Length}", responseText);
            Assert.EndsWith(bodyContent, responseText);
        }

        [Fact]
        public void Build_WithBinaryStreamBody_ShouldIncludeBinaryDataAsText()
        {
            // Arrange
            var binaryData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0xFF };
            var response = HttpResponse.Create(200)
                .AddHeader("Content-Type", "application/octet-stream")
                .WithBody(binaryData);

            // Act
            var responseText = response.Build();

            // Assert
            Assert.Contains("HTTP/1.1 200 OK", responseText);
            Assert.Contains($"Content-Length: {binaryData.Length}", responseText);
            // The binary data will be included as UTF-8 interpreted text
            Assert.True(responseText.Length > 100); // Should contain the headers plus binary data
        }

        [Fact]
        public void Build_WithNonReadableStreamBody_ShouldHandleException()
        {
            // Arrange
            var nonReadableStream = new NonReadableStream();
            var response = HttpResponse.Create(200);
            response.body = nonReadableStream;

            // Act
            var responseText = response.Build();

            // Assert
            Assert.Contains("HTTP/1.1 200 OK", responseText);
            Assert.Contains($"Content-Length: {nonReadableStream.Length}", responseText);
            Assert.Contains("[BINARY BODY", responseText);
        }

        [Fact]
        public async Task WriteToStreamAsync_WithBody_ShouldWriteCompleteResponseAsync()
        {
            // Arrange
            var bodyContent = "Hello, World!";
            var response = HttpResponse.Create(200)
                .AddHeader("Content-Type", "text/plain")
                .WithBody(bodyContent);

            using var outputStream = new MemoryStream();

            // Act
            await response.WriteToStreamAsync(outputStream, CancellationToken.None);

            // Assert
            outputStream.Position = 0;
            using var reader = new StreamReader(outputStream);
            var responseText = await reader.ReadToEndAsync();

            Assert.Contains("HTTP/1.1 200 OK", responseText);
            Assert.Contains("Content-Type: text/plain", responseText);
            Assert.Contains($"Content-Length: {bodyContent.Length}", responseText);
            Assert.EndsWith(bodyContent, responseText);
        }

        [Fact]
        public async Task WriteToStreamAsync_WithoutBody_ShouldWriteHeadersOnlyAsync()
        {
            // Arrange
            var response = HttpResponse.Create(204)
                .AddHeader("Cache-Control", "no-cache");

            using var outputStream = new MemoryStream();

            // Act
            await response.WriteToStreamAsync(outputStream, CancellationToken.None);

            // Assert
            outputStream.Position = 0;
            using var reader = new StreamReader(outputStream);
            var responseText = await reader.ReadToEndAsync();

            Assert.Contains("HTTP/1.1 204 No Content", responseText);
            Assert.Contains("Cache-Control: no-cache", responseText);
            Assert.DoesNotContain("Content-Length:", responseText);
            Assert.EndsWith("\r\n\r\n", responseText);
        }

        [Fact]
        public void ValidateWebSocketHandshakeResponse_WithValidHandshake_ShouldNotThrow()
        {
            // Arrange
            var response = HttpResponse.Create(101)
                .AddHeader("Upgrade", "websocket")
                .AddHeader("Connection", "Upgrade")
                .AddHeader("Sec-WebSocket-Accept", "s3pPLMBiTxaQ9kYGzzhZRbK+xOo=");

            // Act & Assert
            response.ValidateWebSocketHandshakeResponse(); // Should not throw
        }

        [Fact]
        public void ValidateWebSocketHandshakeResponse_WithWrongStatusCode_ShouldThrow()
        {
            // Arrange
            var response = HttpResponse.Create(200)
                .AddHeader("Upgrade", "websocket")
                .AddHeader("Connection", "Upgrade")
                .AddHeader("Sec-WebSocket-Accept", "s3pPLMBiTxaQ9kYGzzhZRbK+xOo=");

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() => response.ValidateWebSocketHandshakeResponse());
            Assert.Contains("Expected status 101, got 200", exception.Message);
        }

        [Fact]
        public void ValidateWebSocketHandshakeResponse_WithoutSecWebSocketAccept_ShouldThrow()
        {
            // Arrange
            var response = HttpResponse.Create(101)
                .AddHeader("Upgrade", "websocket")
                .AddHeader("Connection", "Upgrade");

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() => response.ValidateWebSocketHandshakeResponse());
            Assert.Contains("Missing Sec-WebSocket-Accept header", exception.Message);
        }

        [Fact]
        public void ValidateWebSocketHandshakeResponse_WithoutUpgradeHeader_ShouldThrow()
        {
            // Arrange
            var response = HttpResponse.Create(101)
                .AddHeader("Connection", "Upgrade")
                .AddHeader("Sec-WebSocket-Accept", "s3pPLMBiTxaQ9kYGzzhZRbK+xOo=");

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() => response.ValidateWebSocketHandshakeResponse());
            Assert.Contains("Missing or invalid Upgrade header", exception.Message);
        }

        [Fact]
        public void ValidateWebSocketHandshakeResponse_WithWrongUpgradeHeader_ShouldThrow()
        {
            // Arrange
            var response = HttpResponse.Create(101)
                .AddHeader("Upgrade", "http2")
                .AddHeader("Connection", "Upgrade")
                .AddHeader("Sec-WebSocket-Accept", "s3pPLMBiTxaQ9kYGzzhZRbK+xOo=");

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() => response.ValidateWebSocketHandshakeResponse());
            Assert.Contains("Missing or invalid Upgrade header", exception.Message);
        }

        [Fact]
        public void ValidateWebSocketHandshakeResponse_WithoutConnectionHeader_ShouldThrow()
        {
            // Arrange
            var response = HttpResponse.Create(101)
                .AddHeader("Upgrade", "websocket")
                .AddHeader("Sec-WebSocket-Accept", "s3pPLMBiTxaQ9kYGzzhZRbK+xOo=");

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() => response.ValidateWebSocketHandshakeResponse());
            Assert.Contains("Missing or invalid Connection header", exception.Message);
        }

        [Fact]
        public void ValidateWebSocketHandshakeResponse_WithWrongConnectionHeader_ShouldThrow()
        {
            // Arrange
            var response = HttpResponse.Create(101)
                .AddHeader("Upgrade", "websocket")
                .AddHeader("Connection", "close")
                .AddHeader("Sec-WebSocket-Accept", "s3pPLMBiTxaQ9kYGzzhZRbK+xOo=");

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() => response.ValidateWebSocketHandshakeResponse());
            Assert.Contains("Missing or invalid Connection header", exception.Message);
        }

        [Fact]
        public void ValidateWebSocketHandshakeResponse_WithCaseInsensitiveHeaders_ShouldNotThrow()
        {
            // Arrange
            var response = HttpResponse.Create(101)
                .AddHeader("Upgrade", "WebSocket")
                .AddHeader("Connection", "upgrade")
                .AddHeader("Sec-WebSocket-Accept", "s3pPLMBiTxaQ9kYGzzhZRbK+xOo=");

            // Act & Assert
            response.ValidateWebSocketHandshakeResponse(); // Should not throw
        }

        [Fact]
        public void ValidateWebSocketHandshakeResponse_WithConnectionUpgradeInList_ShouldNotThrow()
        {
            // Arrange
            var response = HttpResponse.Create(101)
                .AddHeader("Upgrade", "websocket")
                .AddHeader("Connection", "keep-alive, Upgrade")
                .AddHeader("Sec-WebSocket-Accept", "s3pPLMBiTxaQ9kYGzzhZRbK+xOo=");

            // Act & Assert
            response.ValidateWebSocketHandshakeResponse(); // Should not throw
        }

#if !NET8_0_OR_GREATER
        [Fact]
        public void Parse_WithNonHttpPrefix_ShouldReturnNull()
        {
            // Arrange
            var malformedResponse = "FTP/1.1 200 OK\r\n\r\n";

            // Act
            var response = HttpResponse.Parse(malformedResponse);

            // Assert
            Assert.Null(response);
        }
#endif

        // Helper class for testing streams that throw when read
        private class NonReadableStream : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => 10;
            public override long Position { get => 0; set => throw new NotSupportedException(); }

            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Stream is not readable");
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}