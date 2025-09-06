using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Shinobi.WebSockets.Http;

namespace Shinobi.WebSockets.UnitTests
{
    public class HttpRequestBuilderTests
    {
        [Fact]
        public void AddHeader_WithSingleValue_ShouldAddHeader()
        {
            // Arrange
            var request = HttpRequest.Create("GET", "/");

            // Act
            var result = request.AddHeader("Content-Type", "application/json");

            // Assert
            Assert.Same(request, result); // Should return same instance for chaining
            Assert.True(request.HasHeader("Content-Type"));
            Assert.Contains("application/json", request.GetHeaderValues("Content-Type"));
        }

        [Fact]
        public void AddHeader_WithMultipleValuesForSameKey_ShouldAccumulateValues()
        {
            // Arrange
            var request = HttpRequest.Create("GET", "/");

            // Act
            request.AddHeader("Accept", "text/html")
                   .AddHeader("Accept", "application/json");

            // Assert
            Assert.Contains("text/html", request.GetHeaderValues("Accept"));
            Assert.Contains("application/json", request.GetHeaderValues("Accept"));
            Assert.Equal(2, request.GetHeaderValues("Accept").Count());
        }

        [Fact]
        public void AddHeaders_WithValidDictionary_ShouldAddAllHeaders()
        {
            // Arrange
            var request = HttpRequest.Create("GET", "/");
            var headers = new Dictionary<string, string>
            {
                { "Content-Type", "application/json" },
                { "Authorization", "Bearer token123" },
                { "User-Agent", "TestClient/1.0" }
            };

            // Act
            var result = request.AddHeaders(headers);

            // Assert
            Assert.Same(request, result);
            Assert.Contains("application/json", request.GetHeaderValues("Content-Type"));
            Assert.Contains("Bearer token123", request.GetHeaderValues("Authorization"));
            Assert.Contains("TestClient/1.0", request.GetHeaderValues("User-Agent"));
        }

        [Fact]
        public void AddHeaders_WithNullDictionary_ShouldReturnSameInstance()
        {
            // Arrange
            var request = HttpRequest.Create("GET", "/");

            // Act
            var result = request.AddHeaders((Dictionary<string, string>?)null);

            // Assert
            Assert.Same(request, result);
            Assert.Empty(request.AsKeyValuePairs());
        }

        [Fact]
        public void AddHeaders_WithEmptyDictionary_ShouldReturnSameInstance()
        {
            // Arrange
            var request = HttpRequest.Create("GET", "/");
            var emptyHeaders = new Dictionary<string, string>();

            // Act
            var result = request.AddHeaders(emptyHeaders);

            // Assert
            Assert.Same(request, result);
            Assert.Empty(request.AsKeyValuePairs());
        }

        [Fact]
        public void AddHeader_WithMultipleStringValues_ShouldAddAllValues()
        {
            // Arrange
            var request = HttpRequest.Create("GET", "/");
            var values = new[] { "text/html", "application/json", "application/xml" };

            // Act
            var result = request.AddHeader("Accept", values);

            // Assert
            Assert.Same(request, result);
            foreach (var value in values)
            {
                Assert.Contains(value, request.GetHeaderValues("Accept"));
            }
            Assert.Equal(3, request.GetHeaderValues("Accept").Count());
        }

        [Fact]
        public void AddHeader_WithEmptyEnumerableValues_ShouldCreateEmptyHeaderSet()
        {
            // Arrange
            var request = HttpRequest.Create("GET", "/");
            var emptyValues = new string[0];

            // Act
            var result = request.AddHeader("Custom-Header", emptyValues);

            // Assert
            Assert.Same(request, result);
            Assert.True(request.HasHeader("Custom-Header"));
            Assert.Empty(request.GetHeaderValues("Custom-Header"));
        }

        [Fact]
        public void AddHeaderIf_WithTrueCondition_ShouldAddHeader()
        {
            // Arrange
            var request = HttpRequest.Create("GET", "/");
            var condition = true;

            // Act
            var result = request.AddHeaderIf(condition, "X-Debug", "enabled");

            // Assert
            Assert.Same(request, result);
            Assert.Contains("enabled", request.GetHeaderValues("X-Debug"));
        }

        [Fact]
        public void AddHeaderIf_WithFalseCondition_ShouldNotAddHeader()
        {
            // Arrange
            var request = HttpRequest.Create("GET", "/");
            var condition = false;

            // Act
            var result = request.AddHeaderIf(condition, "X-Debug", "enabled");

            // Assert
            Assert.Same(request, result);
            Assert.False(request.HasHeader("X-Debug"));
        }

        [Fact]
        public void AddHeaderIf_WithValueFactory_AndTrueCondition_ShouldCallFactoryAndAddHeader()
        {
            // Arrange
            var request = HttpRequest.Create("GET", "/");
            var condition = true;
            var factoryCalled = false;
            
            // Act
            var result = request.AddHeaderIf(condition, "X-Timestamp", () =>
            {
                factoryCalled = true;
                return DateTimeOffset.UtcNow.ToString("O");
            });

            // Assert
            Assert.Same(request, result);
            Assert.True(factoryCalled);
            Assert.True(request.HasHeader("X-Timestamp"));
        }

        [Fact]
        public void AddHeaderIf_WithValueFactory_AndFalseCondition_ShouldNotCallFactory()
        {
            // Arrange
            var request = HttpRequest.Create("GET", "/");
            var condition = false;
            var factoryCalled = false;

            // Act
            var result = request.AddHeaderIf(condition, "X-Timestamp", () =>
            {
                factoryCalled = true;
                return DateTimeOffset.UtcNow.ToString("O");
            });

            // Assert
            Assert.Same(request, result);
            Assert.False(factoryCalled);
            Assert.False(request.HasHeader("X-Timestamp"));
        }

        [Fact]
        public void AddRawHeaders_WithValidHeaderString_ShouldParseAndAddHeaders()
        {
            // Arrange
            var request = HttpRequest.Create("GET", "/");
            var rawHeaders = "Content-Type: application/json\r\nAuthorization: Bearer token123\r\nUser-Agent: TestClient/1.0";

            // Act
            var result = request.AddRawHeaders(rawHeaders);

            // Assert
            Assert.Same(request, result);
            Assert.Contains("application/json", request.GetHeaderValues("Content-Type"));
            Assert.Contains("Bearer token123", request.GetHeaderValues("Authorization"));
            Assert.Contains("TestClient/1.0", request.GetHeaderValues("User-Agent"));
        }

        [Fact]
        public void AddRawHeaders_WithNewlineDelimiters_ShouldParseCorrectly()
        {
            // Arrange
            var request = HttpRequest.Create("GET", "/");
            var rawHeaders = "Content-Type: application/json\nAuthorization: Bearer token123\nUser-Agent: TestClient/1.0";

            // Act
            request.AddRawHeaders(rawHeaders);

            // Assert
            Assert.Contains("application/json", request.GetHeaderValues("Content-Type"));
            Assert.Contains("Bearer token123", request.GetHeaderValues("Authorization"));
            Assert.Contains("TestClient/1.0", request.GetHeaderValues("User-Agent"));
        }

        [Fact]
        public void AddRawHeaders_WithMalformedHeaders_ShouldIgnoreInvalidLines()
        {
            // Arrange
            var request = HttpRequest.Create("GET", "/");
            var rawHeaders = "Content-Type: application/json\r\nInvalidHeader\r\nAuthorization: Bearer token123\r\n:NoName\r\nValid: header";

            // Act
            request.AddRawHeaders(rawHeaders);

            // Assert
            Assert.Contains("application/json", request.GetHeaderValues("Content-Type"));
            Assert.Contains("Bearer token123", request.GetHeaderValues("Authorization"));
            Assert.Contains("header", request.GetHeaderValues("Valid"));
            Assert.Equal(3, request.AsKeyValuePairs().Count()); // Only valid headers should be added
        }

        [Fact]
        public void AddRawHeaders_WithNullString_ShouldReturnSameInstance()
        {
            // Arrange
            var request = HttpRequest.Create("GET", "/");

            // Act
            var result = request.AddRawHeaders(null);

            // Assert
            Assert.Same(request, result);
            Assert.Empty(request.AsKeyValuePairs());
        }

        [Fact]
        public void AddRawHeaders_WithEmptyString_ShouldReturnSameInstance()
        {
            // Arrange
            var request = HttpRequest.Create("GET", "/");

            // Act
            var result = request.AddRawHeaders("");

            // Assert
            Assert.Same(request, result);
            Assert.Empty(request.AsKeyValuePairs());
        }

        [Fact]
        public void AddRawHeaders_WithWhitespaceInHeaderValues_ShouldTrimCorrectly()
        {
            // Arrange
            var request = HttpRequest.Create("GET", "/");
            var rawHeaders = "Content-Type:   application/json   \r\nAuthorization:   Bearer token123   ";

            // Act
            request.AddRawHeaders(rawHeaders);

            // Assert
            Assert.Contains("application/json", request.GetHeaderValues("Content-Type"));
            Assert.Contains("Bearer token123", request.GetHeaderValues("Authorization"));
        }

        [Fact]
        public void AddHeaders_FromHttpHeader_ShouldCopyAllHeaders()
        {
            // Arrange
            var sourceRequest = HttpRequest.Create("GET", "/");
            sourceRequest.AddHeader("Content-Type", "application/json")
                         .AddHeader("Authorization", "Bearer token123");

            var targetRequest = HttpRequest.Create("POST", "/api");

            // Act
            var result = targetRequest.AddHeaders(sourceRequest);

            // Assert
            Assert.Same(targetRequest, result);
            Assert.Contains("application/json", targetRequest.GetHeaderValues("Content-Type"));
            Assert.Contains("Bearer token123", targetRequest.GetHeaderValues("Authorization"));
        }

        [Fact]
        public void WithBody_StringContent_ShouldSetBodyAndHeaders()
        {
            // Arrange
            var request = HttpRequest.Create("POST", "/api");
            var bodyContent = "Hello World";

            // Act
            var result = request.WithBody(bodyContent);

            // Assert
            Assert.Same(request, result);
            Assert.NotNull(request.Body);
            Assert.Contains("text/plain; charset=utf-8", request.GetHeaderValues("Content-Type"));
            Assert.Contains("11", request.GetHeaderValues("Content-Length")); // "Hello World".Length = 11
        }

        [Fact]
        public void WithBody_NullString_ShouldReturnSameInstanceUnchanged()
        {
            // Arrange
            var request = HttpRequest.Create("POST", "/api");

            // Act
            var result = request.WithBody((string?)null);

            // Assert
            Assert.Same(request, result);
            Assert.Null(request.Body);
        }

        [Fact]
        public void WithBody_StringWithCustomContentType_ShouldSetCustomContentType()
        {
            // Arrange
            var request = HttpRequest.Create("POST", "/api");
            var bodyContent = "Hello World";
            var customContentType = "text/html; charset=utf-8";

            // Act
            request.WithBody(bodyContent, customContentType);

            // Assert
            Assert.Contains(customContentType, request.GetHeaderValues("Content-Type"));
        }

        [Fact]
        public void WithBody_ByteArrayContent_ShouldSetBodyAndHeaders()
        {
            // Arrange
            var request = HttpRequest.Create("POST", "/api");
            var bodyBytes = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };

            // Act
            var result = request.WithBody(bodyBytes);

            // Assert
            Assert.Same(request, result);
            Assert.NotNull(request.Body);
            Assert.Contains("application/octet-stream", request.GetHeaderValues("Content-Type"));
            Assert.Contains("5", request.GetHeaderValues("Content-Length"));
        }

        [Fact]
        public void WithBody_NullByteArray_ShouldReturnSameInstanceUnchanged()
        {
            // Arrange
            var request = HttpRequest.Create("POST", "/api");

            // Act
            var result = request.WithBody((byte[]?)null);

            // Assert
            Assert.Same(request, result);
            Assert.Null(request.Body);
        }

        [Fact]
        public void WithBody_EmptyByteArray_ShouldReturnSameInstanceUnchanged()
        {
            // Arrange
            var request = HttpRequest.Create("POST", "/api");
            var emptyBytes = new byte[0];

            // Act
            var result = request.WithBody(emptyBytes);

            // Assert
            Assert.Same(request, result);
            Assert.Null(request.Body);
        }

        [Fact]
        public void WithBody_StreamWithKnownLength_ShouldSetContentLength()
        {
            // Arrange
            var request = HttpRequest.Create("POST", "/api");
            var bodyBytes = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
            var bodyStream = new MemoryStream(bodyBytes);

            // Act
            var result = request.WithBody(bodyStream);

            // Assert
            Assert.Same(request, result);
            Assert.Same(bodyStream, request.Body);
            Assert.Contains("application/octet-stream", request.GetHeaderValues("Content-Type"));
            Assert.Contains("5", request.GetHeaderValues("Content-Length"));
        }

        [Fact]
        public void WithBody_StreamWithoutSeekCapability_ShouldNotSetContentLength()
        {
            // Arrange
            var request = HttpRequest.Create("POST", "/api");
            var nonSeekableStream = new NonSeekableStream();

            // Act
            var result = request.WithBody(nonSeekableStream);

            // Assert
            Assert.Same(request, result);
            Assert.Same(nonSeekableStream, request.Body);
            Assert.Contains("application/octet-stream", request.GetHeaderValues("Content-Type"));
            Assert.False(request.HasHeader("Content-Length"));
        }

        [Fact]
        public void WithBody_NullStream_ShouldReturnSameInstanceUnchanged()
        {
            // Arrange
            var request = HttpRequest.Create("POST", "/api");

            // Act
            var result = request.WithBody((Stream?)null);

            // Assert
            Assert.Same(request, result);
            Assert.Null(request.Body);
        }

        [Fact]
        public void WithJsonBody_ShouldSetJsonContentTypeAndBody()
        {
            // Arrange
            var request = HttpRequest.Create("POST", "/api");
            var jsonContent = "{\"name\": \"test\", \"value\": 123}";

            // Act
            var result = request.WithJsonBody(jsonContent);

            // Assert
            Assert.Same(request, result);
            Assert.NotNull(request.Body);
            Assert.Contains("application/json; charset=utf-8", request.GetHeaderValues("Content-Type"));
        }

        [Fact]
        public void WithFormBody_ShouldSetFormContentTypeAndBody()
        {
            // Arrange
            var request = HttpRequest.Create("POST", "/api");
            var formContent = "name=test&value=123&enabled=true";

            // Act
            var result = request.WithFormBody(formContent);

            // Assert
            Assert.Same(request, result);
            Assert.NotNull(request.Body);
            Assert.Contains("application/x-www-form-urlencoded; charset=utf-8", request.GetHeaderValues("Content-Type"));
        }

        [Fact]
        public void WithBody_ShouldRemoveExistingContentHeaders()
        {
            // Arrange
            var request = HttpRequest.Create("POST", "/api");
            request.AddHeader("Content-Type", "text/plain")
                   .AddHeader("Content-Length", "999");

            var bodyContent = "New content";

            // Act
            request.WithBody(bodyContent, "application/json");

            // Assert
            Assert.Contains("application/json", request.GetHeaderValues("Content-Type"));
            Assert.DoesNotContain("text/plain", request.GetHeaderValues("Content-Type"));
            Assert.Contains("11", request.GetHeaderValues("Content-Length"));
            Assert.DoesNotContain("999", request.GetHeaderValues("Content-Length"));
        }

        // Helper class for testing non-seekable streams
        private class NonSeekableStream : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => 0; set => throw new NotSupportedException(); }

            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => 0;
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}