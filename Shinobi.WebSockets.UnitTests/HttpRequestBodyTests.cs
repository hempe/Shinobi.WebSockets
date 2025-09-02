using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Shinobi.WebSockets.Http;

using Xunit;

namespace Shinobi.WebSockets.UnitTests
{
    public class HttpRequestBodyTests
    {
        #region ReadAsync with Body Tests

        [Fact]
        public async Task ReadAsync_RequestWithContentLength_ShouldReadBodyAsync()
        {
            // Arrange
            var requestData = "POST /api/data HTTP/1.1\r\n" +
                            "Host: example.com\r\n" +
                            "Content-Type: application/json\r\n" +
                            "Content-Length: 26\r\n" +
                            "\r\n" +
                            "{\"name\":\"test\",\"id\":123}";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(requestData));

            // Act
            using var request = await HttpRequest.ReadAsync(stream, CancellationToken.None);

            // Assert
            Assert.NotNull(request);
            Assert.Equal("POST", request.Method);
            Assert.Equal("/api/data", request.Path);
            Assert.Equal("application/json", request.GetHeaderValue("Content-Type"));
            Assert.Equal("26", request.GetHeaderValue("Content-Length"));
            
            Assert.NotNull(request.Body);
            using var bodyReader = new StreamReader(request.Body);
            var bodyContent = await bodyReader.ReadToEndAsync();
            Assert.Equal("{\"name\":\"test\",\"id\":123}", bodyContent);
        }

        [Fact]
        public async Task ReadAsync_RequestWithoutContentLength_ShouldNotReadBodyAsync()
        {
            // Arrange
            var requestData = "GET /api/data HTTP/1.1\r\n" +
                            "Host: example.com\r\n" +
                            "\r\n";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(requestData));

            // Act
            var request = await HttpRequest.ReadAsync(stream, CancellationToken.None);

            // Assert
            Assert.NotNull(request);
            Assert.Equal("GET", request.Method);
            Assert.Equal("/api/data", request.Path);
            Assert.Null(request.Body);
        }

        [Fact]
        public async Task ReadAsync_RequestWithZeroContentLength_ShouldNotReadBodyAsync()
        {
            // Arrange
            var requestData = "POST /api/data HTTP/1.1\r\n" +
                            "Host: example.com\r\n" +
                            "Content-Length: 0\r\n" +
                            "\r\n";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(requestData));

            // Act
            var request = await HttpRequest.ReadAsync(stream, CancellationToken.None);

            // Assert
            Assert.NotNull(request);
            Assert.Equal("POST", request.Method);
            Assert.Null(request.Body);
        }

        [Fact]
        public async Task ReadAsync_RequestWithInvalidContentLength_ShouldNotReadBodyAsync()
        {
            // Arrange
            var requestData = "POST /api/data HTTP/1.1\r\n" +
                            "Host: example.com\r\n" +
                            "Content-Length: invalid\r\n" +
                            "\r\n";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(requestData));

            // Act
            var request = await HttpRequest.ReadAsync(stream, CancellationToken.None);

            // Assert
            Assert.NotNull(request);
            Assert.Equal("POST", request.Method);
            Assert.Null(request.Body);
        }

        [Fact]
        public async Task ReadAsync_RequestWithPartialBody_ShouldReadAvailableDataAsync()
        {
            // Arrange - Content-Length says 50 but we only provide 20 characters
            var requestData = "POST /api/data HTTP/1.1\r\n" +
                            "Host: example.com\r\n" +
                            "Content-Length: 50\r\n" +
                            "\r\n" +
                            "partial body content"; // Only 20 chars

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(requestData));

            // Act
            var request = await HttpRequest.ReadAsync(stream, CancellationToken.None);

            // Assert
            Assert.NotNull(request);
            Assert.NotNull(request.Body);
            
            using var bodyReader = new StreamReader(request.Body);
            var bodyContent = await bodyReader.ReadToEndAsync();
            Assert.Equal("partial body content", bodyContent); // Should only get what was available
        }

        #endregion

        #region WithBody Extension Method Tests

        [Fact]
        public void WithBody_StringContent_ShouldSetBodyAndHeaders()
        {
            // Arrange
            var request = HttpRequest.Create("POST", "/api/data");
            const string bodyContent = "Hello, World!";

            // Act
            request.WithBody(bodyContent);

            // Assert
            Assert.NotNull(request.Body);
            Assert.Equal("text/plain; charset=utf-8", request.GetHeaderValue("Content-Type"));
            Assert.Equal("13", request.GetHeaderValue("Content-Length")); // "Hello, World!" is 13 bytes in UTF-8

            using var reader = new StreamReader(request.Body);
            Assert.Equal(bodyContent, reader.ReadToEnd());
        }

        [Fact]
        public void WithBody_StringContentWithCustomContentType_ShouldSetCustomContentType()
        {
            // Arrange
            var request = HttpRequest.Create("POST", "/api/data");
            const string bodyContent = "Hello, World!";

            // Act
            request.WithBody(bodyContent, "text/html; charset=utf-8");

            // Assert
            Assert.Equal("text/html; charset=utf-8", request.GetHeaderValue("Content-Type"));
        }

        [Fact]
        public void WithBody_ByteArrayContent_ShouldSetBodyAndHeaders()
        {
            // Arrange
            var request = HttpRequest.Create("POST", "/api/data");
            var bodyContent = Encoding.UTF8.GetBytes("Hello, World!");

            // Act
            request.WithBody(bodyContent);

            // Assert
            Assert.NotNull(request.Body);
            Assert.Equal("application/octet-stream", request.GetHeaderValue("Content-Type"));
            Assert.Equal("13", request.GetHeaderValue("Content-Length"));

            using var reader = new StreamReader(request.Body);
            Assert.Equal("Hello, World!", reader.ReadToEnd());
        }

        [Fact]
        public void WithBody_StreamContent_ShouldSetBodyAndHeaders()
        {
            // Arrange
            var request = HttpRequest.Create("POST", "/api/data");
            var bodyContent = new MemoryStream(Encoding.UTF8.GetBytes("Hello, World!"));

            // Act
            request.WithBody(bodyContent);

            // Assert
            Assert.Same(bodyContent, request.Body);
            Assert.Equal("application/octet-stream", request.GetHeaderValue("Content-Type"));
            Assert.Equal("13", request.GetHeaderValue("Content-Length"));
        }

        [Fact]
        public void WithJsonBody_ShouldSetCorrectContentType()
        {
            // Arrange
            var request = HttpRequest.Create("POST", "/api/users");
            const string jsonContent = "{\"name\":\"John\",\"age\":30}";

            // Act
            request.WithJsonBody(jsonContent);

            // Assert
            Assert.NotNull(request.Body);
            Assert.Equal("application/json; charset=utf-8", request.GetHeaderValue("Content-Type"));
            Assert.Equal("24", request.GetHeaderValue("Content-Length"));

            using var reader = new StreamReader(request.Body);
            Assert.Equal(jsonContent, reader.ReadToEnd());
        }

        [Fact]
        public void WithFormBody_ShouldSetCorrectContentType()
        {
            // Arrange
            var request = HttpRequest.Create("POST", "/api/login");
            const string formContent = "username=john&password=secret";

            // Act
            request.WithFormBody(formContent);

            // Assert
            Assert.NotNull(request.Body);
            Assert.Equal("application/x-www-form-urlencoded; charset=utf-8", request.GetHeaderValue("Content-Type"));
            Assert.Equal("29", request.GetHeaderValue("Content-Length"));

            using var reader = new StreamReader(request.Body);
            Assert.Equal(formContent, reader.ReadToEnd());
        }

        [Fact]
        public void WithBody_NullString_ShouldNotSetBody()
        {
            // Arrange
            var request = HttpRequest.Create("POST", "/api/data");

            // Act
            request.WithBody((string?)null);

            // Assert
            Assert.Null(request.Body);
            Assert.False(request.HasHeader("Content-Type"));
            Assert.False(request.HasHeader("Content-Length"));
        }

        [Fact]
        public void WithBody_NullByteArray_ShouldNotSetBody()
        {
            // Arrange
            var request = HttpRequest.Create("POST", "/api/data");

            // Act
            request.WithBody((byte[]?)null);

            // Assert
            Assert.Null(request.Body);
            Assert.False(request.HasHeader("Content-Type"));
            Assert.False(request.HasHeader("Content-Length"));
        }

        [Fact]
        public void WithBody_EmptyByteArray_ShouldNotSetBody()
        {
            // Arrange
            var request = HttpRequest.Create("POST", "/api/data");

            // Act
            request.WithBody(new byte[0]);

            // Assert
            Assert.Null(request.Body);
            Assert.False(request.HasHeader("Content-Type"));
            Assert.False(request.HasHeader("Content-Length"));
        }

        #endregion

        #region ToHttpRequest with Body Tests

        [Fact]
        public void ToHttpRequest_WithStringBody_ShouldIncludeBodyInOutput()
        {
            // Arrange
            var request = HttpRequest.Create("POST", "/api/users")
                .AddHeader("Host", "example.com")
                .WithJsonBody("{\"name\":\"John\"}");

            // Act
            var httpRequestString = request.ToHttpRequest();

            // Assert
            var expectedStart = "POST /api/users HTTP/1.1\r\n";
            Assert.StartsWith(expectedStart, httpRequestString);
            Assert.Contains("Host: example.com", httpRequestString);
            Assert.Contains("Content-Type: application/json; charset=utf-8", httpRequestString);
            Assert.Contains("Content-Length: 15", httpRequestString);
            Assert.EndsWith("\r\n\r\n{\"name\":\"John\"}", httpRequestString);
        }

        [Fact]
        public void ToHttpRequest_WithoutBody_ShouldNotIncludeBody()
        {
            // Arrange
            var request = HttpRequest.Create("GET", "/api/users")
                .AddHeader("Host", "example.com");

            // Act
            var httpRequestString = request.ToHttpRequest();

            // Assert
            var expected = "GET /api/users HTTP/1.1\r\n" +
                          "Host: example.com\r\n" +
                          "\r\n";
            Assert.Equal(expected, httpRequestString);
        }

        [Fact]
        public void ToHttpRequest_WithBodyMultipleCalls_ShouldResetStreamPosition()
        {
            // Arrange
            var request = HttpRequest.Create("POST", "/api/data")
                .WithBody("test content");

            // Act
            var httpRequest1 = request.ToHttpRequest();
            var httpRequest2 = request.ToHttpRequest();

            // Assert - Both calls should produce the same result
            Assert.Equal(httpRequest1, httpRequest2);
            Assert.Contains("test content", httpRequest1);
            Assert.Contains("test content", httpRequest2);
        }

        #endregion

        #region Integration Tests

        [Fact]
        public async Task CompletePostRequest_WithJsonBody_ShouldWorkEndToEndAsync()
        {
            // Arrange - Build a POST request with JSON body
            var originalRequest = HttpRequest.Create("POST", "/api/users")
                .AddHeader("Host", "api.example.com")
                .AddHeader("Authorization", "Bearer token123")
                .WithJsonBody("{\"name\":\"Alice\",\"email\":\"alice@example.com\"}");

            // Convert to HTTP string
            var httpString = originalRequest.ToHttpRequest();

            // Act - Parse it back from the HTTP string
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(httpString));
            using var parsedRequest = await HttpRequest.ReadAsync(stream, CancellationToken.None);

            // Assert - Verify all data is preserved
            Assert.NotNull(parsedRequest);
            Assert.Equal("POST", parsedRequest.Method);
            Assert.Equal("/api/users", parsedRequest.Path);
            Assert.Equal("api.example.com", parsedRequest.GetHeaderValue("Host"));
            Assert.Equal("Bearer token123", parsedRequest.GetHeaderValue("Authorization"));
            Assert.Equal("application/json; charset=utf-8", parsedRequest.GetHeaderValue("Content-Type"));
            Assert.Equal("44", parsedRequest.GetHeaderValue("Content-Length"));

            Assert.NotNull(parsedRequest.Body);
            using var reader = new StreamReader(parsedRequest.Body);
            var bodyContent = await reader.ReadToEndAsync();
            Assert.Equal("{\"name\":\"Alice\",\"email\":\"alice@example.com\"}", bodyContent);
        }

        [Fact]
        public async Task CompletePutRequest_WithFormBody_ShouldWorkEndToEndAsync()
        {
            // Arrange
            var originalRequest = HttpRequest.Create("PUT", "/api/profile")
                .AddHeader("Host", "api.example.com")
                .WithFormBody("name=Bob&age=25&city=Seattle");

            var httpString = originalRequest.ToHttpRequest();

            // Act
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(httpString));
            using var parsedRequest = await HttpRequest.ReadAsync(stream, CancellationToken.None);

            // Assert
            Assert.NotNull(parsedRequest);
            Assert.Equal("PUT", parsedRequest.Method);
            Assert.Equal("/api/profile", parsedRequest.Path);
            Assert.Equal("application/x-www-form-urlencoded; charset=utf-8", parsedRequest.GetHeaderValue("Content-Type"));

            Assert.NotNull(parsedRequest.Body);
            using var reader = new StreamReader(parsedRequest.Body);
            var bodyContent = await reader.ReadToEndAsync();
            Assert.Equal("name=Bob&age=25&city=Seattle", bodyContent);
        }

        #endregion

        #region Edge Case Tests

        [Fact]
        public void WithBody_ChainedCalls_ShouldOverridePreviousBody()
        {
            // Arrange
            var request = HttpRequest.Create("POST", "/api/data");

            // Act - Set body multiple times
            request.WithBody("first body")
                   .WithJsonBody("{\"second\":\"body\"}")
                   .WithFormBody("third=body");

            // Assert - Should have the last body set
            Assert.NotNull(request.Body);
            Assert.Equal("application/x-www-form-urlencoded; charset=utf-8", request.GetHeaderValue("Content-Type"));
            
            using var reader = new StreamReader(request.Body);
            Assert.Equal("third=body", reader.ReadToEnd());
        }

        [Fact]
        public void WithBody_NonSeekableStream_ShouldNotSetContentLength()
        {
            // Arrange
            var request = HttpRequest.Create("POST", "/api/data");
            var nonSeekableStream = new NonSeekableStream(Encoding.UTF8.GetBytes("test"));

            // Act
            request.WithBody(nonSeekableStream);

            // Assert
            Assert.Same(nonSeekableStream, request.Body);
            Assert.Equal("application/octet-stream", request.GetHeaderValue("Content-Type"));
            Assert.False(request.HasHeader("Content-Length")); // Should not be set for non-seekable streams
        }

        #endregion

        // Helper class for testing non-seekable streams
        private class NonSeekableStream : Stream
        {
            private readonly MemoryStream innerStream;

            public NonSeekableStream(byte[] data)
            {
                this.innerStream = new MemoryStream(data);
            }

            public override bool CanRead => this.innerStream.CanRead;
            public override bool CanSeek => false; // This is the key difference
            public override bool CanWrite => this.innerStream.CanWrite;
            public override long Length => this.innerStream.Length;
            public override long Position 
            { 
                get => this.innerStream.Position; 
                set => throw new NotSupportedException("Stream is not seekable"); 
            }

            public override void Flush() => this.innerStream.Flush();
            public override int Read(byte[] buffer, int offset, int count) => this.innerStream.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException("Stream is not seekable");
            public override void SetLength(long value) => throw new NotSupportedException("Stream is not seekable");
            public override void Write(byte[] buffer, int offset, int count) => this.innerStream.Write(buffer, offset, count);

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    this.innerStream?.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}