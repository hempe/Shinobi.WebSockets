using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Shinobi.WebSockets.Exceptions;
using Shinobi.WebSockets.Http;

namespace Shinobi.WebSockets.UnitTests
{
    public class HttpHeaderTooLargeIntegrationTests
    {
        [Fact]
        public async Task ReadHttpHeaderDataAsync_WithHeadersExceeding16KB_ThrowsHttpHeaderTooLargeExceptionAsync()
        {
            // Arrange - Create HTTP headers larger than 16KB (16384 bytes)
            const int maxHeaderSize = 16 * 1024; // 16KB

            // Create a header value that will make the entire request exceed 16KB
            var largeHeaderValue = new string('A', maxHeaderSize); // 16KB of 'A's
            var httpRequest = "GET /test HTTP/1.1\r\n" +
                             "Host: example.com\r\n" +
                             $"X-Large-Header: {largeHeaderValue}\r\n" +
                             "Connection: Upgrade\r\n" +
                             "Upgrade: websocket\r\n" +
                             "\r\n"; // End headers

            var requestBytes = Encoding.UTF8.GetBytes(httpRequest);

            // Verify our test data is actually larger than 16KB
            Assert.True(requestBytes.Length > maxHeaderSize,
                $"Test setup error: Request size {requestBytes.Length} should exceed {maxHeaderSize}");

            using var stream = new MemoryStream(requestBytes);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<HttpHeaderTooLargeException>(
                () => HttpHeader.ReadHttpHeaderDataAsync(stream, CancellationToken.None));

            Assert.True(exception.ActualSize.HasValue);
            Assert.Equal(maxHeaderSize, exception.MaxSize);
            Assert.Contains("exceeds maximum allowed size", exception.Message);
            Assert.Contains(exception.ActualSize.Value.ToString(), exception.Message);
            Assert.Contains(maxHeaderSize.ToString(), exception.Message);
        }

        [Fact]
        public async Task ReadHttpHeaderDataAsync_WithHeadersJustUnder16KB_DoesNotThrowAsync()
        {
            // Arrange - Create HTTP headers just under 16KB to verify boundary
            const int maxHeaderSize = 16 * 1024; // 16KB
            const int safeHeaderSize = maxHeaderSize - 200; // Leave room for other headers

            var largeButSafeHeaderValue = new string('B', safeHeaderSize - 100); // Account for other header text
            var httpRequest = "GET /test HTTP/1.1\r\n" +
                             "Host: example.com\r\n" +
                             $"X-Safe-Header: {largeButSafeHeaderValue}\r\n" +
                             "\r\n"; // End headers

            var requestBytes = Encoding.UTF8.GetBytes(httpRequest);

            // Verify our test data is under the limit
            Assert.True(requestBytes.Length < maxHeaderSize,
                $"Test setup error: Request size {requestBytes.Length} should be under {maxHeaderSize}");

            using var stream = new MemoryStream(requestBytes);

            // Act - Should not throw
            var result = await HttpHeader.ReadHttpHeaderDataAsync(stream, CancellationToken.None);

            // Assert
            Assert.True(result.Count > 0);
            Assert.True(result.Count < maxHeaderSize);
        }

        [Fact]
        public async Task WebSocketHandshake_WithOversizedHeaders_ThrowsHttpHeaderTooLargeExceptionAsync()
        {
            // Arrange - Integration test through WebSocket server that would encounter large headers
            const int maxHeaderSize = 16 * 1024;

            // Create a very large custom header that will exceed the limit
            var largeHeaderValue = new string('X', maxHeaderSize); // Definitely exceeds 16KB
            var oversizedRequest = "GET /websocket HTTP/1.1\r\n" +
                                  "Host: localhost:8080\r\n" +
                                  "Connection: Upgrade\r\n" +
                                  "Upgrade: websocket\r\n" +
                                  "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n" +
                                  "Sec-WebSocket-Version: 13\r\n" +
                                  $"X-Oversized-Header: {largeHeaderValue}\r\n" +
                                  "\r\n";

            var requestBytes = Encoding.UTF8.GetBytes(oversizedRequest);
            Assert.True(requestBytes.Length > maxHeaderSize);

            using var stream = new MemoryStream(requestBytes);

            // Act & Assert - Should throw when trying to read the oversized headers
            var exception = await Assert.ThrowsAsync<HttpHeaderTooLargeException>(
                () => HttpHeader.ReadHttpHeaderDataAsync(stream, CancellationToken.None));

            Assert.NotNull(exception);
            Assert.True(exception.ActualSize >= maxHeaderSize);
            Assert.Equal(maxHeaderSize, exception.MaxSize);
        }

        [Fact]
        public async Task ReadHttpHeaderDataAsync_WithMultipleLargeHeaders_ThrowsHttpHeaderTooLargeExceptionAsync()
        {
            // Arrange - Test cumulative header size exceeding limit
            const int maxHeaderSize = 16 * 1024;

            // Create multiple headers that together exceed 16KB
            var mediumHeaderValue = new string('M', 3000); // 3KB each
            var httpRequest = "GET /test HTTP/1.1\r\n" +
                             "Host: example.com\r\n" +
                             $"X-Header-1: {mediumHeaderValue}\r\n" +
                             $"X-Header-2: {mediumHeaderValue}\r\n" +
                             $"X-Header-3: {mediumHeaderValue}\r\n" +
                             $"X-Header-4: {mediumHeaderValue}\r\n" +
                             $"X-Header-5: {mediumHeaderValue}\r\n" +
                             $"X-Header-6: {mediumHeaderValue}\r\n" + // 6 * 3KB = 18KB+ with other headers
                             "\r\n";

            var requestBytes = Encoding.UTF8.GetBytes(httpRequest);
            Assert.True(requestBytes.Length > maxHeaderSize);

            using var stream = new MemoryStream(requestBytes);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<HttpHeaderTooLargeException>(
                () => HttpHeader.ReadHttpHeaderDataAsync(stream, CancellationToken.None));

            Assert.True(exception.ActualSize >= maxHeaderSize);
            Assert.Equal(maxHeaderSize, exception.MaxSize);
        }

        [Fact]
        public async Task HttpRequest_ReadAsync_WithOversizedHeaders_ThrowsHttpHeaderTooLargeExceptionAsync()
        {
            // Arrange - Create a stream with more than 16KB of data (16384 + 1)
            const int maxHeaderSize = 16 * 1024; // 16KB
            var oversizedData = new string('A', maxHeaderSize + 1);
            var dataBytes = Encoding.UTF8.GetBytes(oversizedData);
            
            using var stream = new MemoryStream(dataBytes);
            
            // Act & Assert
            var exception = await Assert.ThrowsAsync<HttpHeaderTooLargeException>(
                async () => await HttpRequest.ReadAsync(stream, CancellationToken.None));
            
            Assert.True(exception.ActualSize >= maxHeaderSize);
            Assert.Equal(maxHeaderSize, exception.MaxSize);
            Assert.Contains("exceeds maximum allowed size", exception.Message);
        }

        [Fact]
        public async Task HttpResponse_ReadAsync_WithOversizedHeaders_ThrowsHttpHeaderTooLargeExceptionAsync()
        {
            // Arrange - Create a stream with more than 16KB of data (16384 + 1)
            const int maxHeaderSize = 16 * 1024; // 16KB
            var oversizedData = new string('B', maxHeaderSize + 1);
            var dataBytes = Encoding.UTF8.GetBytes(oversizedData);
            
            using var stream = new MemoryStream(dataBytes);
            
            // Act & Assert
            var exception = await Assert.ThrowsAsync<HttpHeaderTooLargeException>(
                async () => await HttpResponse.ReadAsync(stream, CancellationToken.None));
            
            Assert.True(exception.ActualSize >= maxHeaderSize);
            Assert.Equal(maxHeaderSize, exception.MaxSize);
            Assert.Contains("exceeds maximum allowed size", exception.Message);
        }

        [Fact]
        public async Task HttpRequest_ReadAsync_WithExactly16KB_DoesNotThrowAsync()
        {
            // Arrange - Create valid HTTP request data that's just under the limit
            const int maxHeaderSize = 16 * 1024; // 16KB
            const int safeSize = maxHeaderSize - 100; // Leave room for HTTP structure
            
            var validHttpRequest = "GET / HTTP/1.1\r\n" +
                                  "Host: example.com\r\n" +
                                  $"X-Large-Header: {new string('C', safeSize - 50)}\r\n" +
                                  "\r\n";
            
            var requestBytes = Encoding.UTF8.GetBytes(validHttpRequest);
            Assert.True(requestBytes.Length < maxHeaderSize); // Verify test setup
            
            using var stream = new MemoryStream(requestBytes);
            
            // Act - Should not throw
            var result = await HttpRequest.ReadAsync(stream, CancellationToken.None);
            
            // Assert
            Assert.NotNull(result);
        }

        [Fact]
        public async Task HttpResponse_ReadAsync_WithExactly16KB_DoesNotThrowAsync()
        {
            // Arrange - Create valid HTTP response data that's just under the limit
            const int maxHeaderSize = 16 * 1024; // 16KB
            const int safeSize = maxHeaderSize - 100; // Leave room for HTTP structure
            
            var validHttpResponse = "HTTP/1.1 200 OK\r\n" +
                                   "Content-Type: text/html\r\n" +
                                   $"X-Large-Header: {new string('D', safeSize - 50)}\r\n" +
                                   "\r\n";
            
            var responseBytes = Encoding.UTF8.GetBytes(validHttpResponse);
            Assert.True(responseBytes.Length < maxHeaderSize); // Verify test setup
            
            using var stream = new MemoryStream(responseBytes);
            
            // Act - Should not throw
            var result = await HttpResponse.ReadAsync(stream, CancellationToken.None);
            
            // Assert
            Assert.NotNull(result);
        }
    }
}