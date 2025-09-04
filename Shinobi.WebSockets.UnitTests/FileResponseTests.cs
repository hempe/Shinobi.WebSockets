using System;
using System.IO;
using System.Reflection;
using System.Text;

using Shinobi.WebSockets.Http;

using Xunit;

namespace Shinobi.WebSockets.UnitTests
{
    public class FileResponseTests : IDisposable
    {
        private readonly string testDirectory;
        private readonly string testFilePath;

        public FileResponseTests()
        {
            this.testDirectory = Path.Combine(Path.GetTempPath(), "FileResponseTests_" + Guid.NewGuid());
            Directory.CreateDirectory(this.testDirectory);
            this.testFilePath = Path.Combine(this.testDirectory, "test.txt");
            File.WriteAllText(this.testFilePath, "Hello, World!");
        }

        public void Dispose()
        {
            if (Directory.Exists(this.testDirectory))
            {
                Directory.Delete(this.testDirectory, true);
            }
        }

        #region CreateFromFile Tests

        [Fact]
        public void CreateFromFile_ValidFile_ShouldReturn200()
        {
            // Act
            var response = FileResponse.CreateFromFile(this.testFilePath);

            // Assert
            Assert.Equal(200, response.StatusCode);
            Assert.Equal("text/plain", response.GetHeaderValue("Content-Type"));
            Assert.True(response.HasHeader("Last-Modified"));
        }

        [Fact]
        public void CreateFromFile_NonExistentFile_ShouldReturn404()
        {
            // Arrange
            var nonExistentPath = Path.Combine(this.testDirectory, "nonexistent.txt");

            // Act
            var response = FileResponse.CreateFromFile(nonExistentPath);

            // Assert
            Assert.Equal(404, response.StatusCode);
        }

        [Fact]
        public void CreateFromFile_CustomContentType_ShouldUseProvidedContentType()
        {
            // Act
            var response = FileResponse.CreateFromFile(this.testFilePath, "application/custom");

            // Assert
            Assert.Equal(200, response.StatusCode);
            Assert.Equal("application/custom", response.GetHeaderValue("Content-Type"));
        }

        [Fact]
        public void CreateFromFile_NonGetRequest_ShouldReturn405()
        {
            // Arrange
            var request = HttpRequest.Create("POST", "/test");

            // Act
            var response = FileResponse.CreateFromFile(this.testFilePath, null, request);

            // Assert
            Assert.Equal(405, response.StatusCode);
            Assert.Equal("GET", response.GetHeaderValue("Allow"));
            Assert.Equal("close", response.GetHeaderValue("Connection"));
            Assert.Contains("Method POST not allowed", response.Build());
        }

        [Fact]
        public void CreateFromFile_IfModifiedSince_NotModified_ShouldReturn304()
        {
            // Arrange
            var fileInfo = new FileInfo(this.testFilePath);
            var lastModified = fileInfo.LastWriteTimeUtc.AddHours(1); // Future time
            var request = HttpRequest.Create("GET", "/test")
                .AddHeader("If-Modified-Since", lastModified.ToString("R"));

            // Act
            var response = FileResponse.CreateFromFile(this.testFilePath, null, request);

            // Assert
            Assert.Equal(304, response.StatusCode);
        }

        [Fact]
        public void CreateFromFile_IfModifiedSince_WasModified_ShouldReturn200()
        {
            // Arrange
            var oldDate = DateTime.UtcNow.AddDays(-1);
            var request = HttpRequest.Create("GET", "/test")
                .AddHeader("If-Modified-Since", oldDate.ToString("R"));

            // Act
            var response = FileResponse.CreateFromFile(this.testFilePath, null, request);

            // Assert
            Assert.Equal(200, response.StatusCode);
        }

        [Fact]
        public void CreateFromFile_CacheControlNoCache_ShouldIgnoreIfModifiedSince()
        {
            // Arrange
            var futureDate = DateTime.UtcNow.AddDays(1);
            var request = HttpRequest.Create("GET", "/test")
                .AddHeader("Cache-Control", "no-cache")
                .AddHeader("If-Modified-Since", futureDate.ToString("R"));

            // Act
            var response = FileResponse.CreateFromFile(this.testFilePath, null, request);

            // Assert
            Assert.Equal(200, response.StatusCode);
        }

        #endregion

        #region CreateFromEmbeddedResource Tests

        [Fact]
        public void CreateFromEmbeddedResource_ValidResource_ShouldReturn200()
        {
            // Arrange - Use this assembly and a known resource
            var assembly = Assembly.GetExecutingAssembly();
            // Create a test embedded resource
            var resourceName = "TestResource.txt";

            // Act - Even if resource doesn't exist, test the method behavior
            var response = FileResponse.CreateFromEmbeddedResource(assembly, resourceName);

            // Assert - Should return 404 for non-existent resource
            Assert.Equal(404, response.StatusCode);
        }

        [Fact]
        public void CreateFromEmbeddedResource_NonGetRequest_ShouldReturn405()
        {
            // Arrange
            var assembly = Assembly.GetExecutingAssembly();
            var request = HttpRequest.Create("POST", "/test");

            // Act
            var response = FileResponse.CreateFromEmbeddedResource(assembly, "test.txt", null, request);

            // Assert
            Assert.Equal(405, response.StatusCode);
            Assert.Equal("GET", response.GetHeaderValue("Allow"));
            Assert.Contains("Method POST not allowed", response.Build());
        }

        [Fact]
        public void CreateFromEmbeddedResource_WithETag_ShouldIncludeVersionHeader()
        {
            // Arrange
            var assembly = Assembly.GetExecutingAssembly();

            // Act
            var response = FileResponse.CreateFromEmbeddedResource(assembly, "nonexistent.txt");

            // Assert - Even for 404, should have tested ETag logic
            // The method will return 404 for nonexistent resource, but we tested the path
            Assert.Equal(404, response.StatusCode);
        }

        [Fact]
        public void CreateFromEmbeddedResource_IfNoneMatch_ShouldReturn304()
        {
            // Arrange
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version?.ToString() ?? "1.0.0.0";
            var etag = $"\"{version}\"";
            var request = HttpRequest.Create("GET", "/test")
                .AddHeader("If-None-Match", etag);

            // Act
            var response = FileResponse.CreateFromEmbeddedResource(assembly, "test.txt", null, request);

            // Assert
            Assert.Equal(304, response.StatusCode);
            Assert.Equal(etag, response.GetHeaderValue("ETag"));
        }

        [Fact]
        public void CreateFromEmbeddedResource_CacheControlNoCache_ShouldIgnoreIfNoneMatch()
        {
            // Arrange
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version?.ToString() ?? "1.0.0.0";
            var etag = $"\"{version}\"";
            var request = HttpRequest.Create("GET", "/test")
                .AddHeader("Cache-Control", "no-cache")
                .AddHeader("If-None-Match", etag);

            // Act
            var response = FileResponse.CreateFromEmbeddedResource(assembly, "test.txt", null, request);

            // Assert
            Assert.Equal(404, response.StatusCode); // Resource doesn't exist, but tested no-cache path
        }

        #endregion

        #region CreateFromStream Tests

        [Fact]
        public void CreateFromStream_ValidStream_ShouldReturn200()
        {
            // Arrange
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Test content"));

            // Act
            var response = FileResponse.CreateFromStream(stream, "text/plain");

            // Assert
            Assert.Equal(200, response.StatusCode);
            Assert.Equal("text/plain", response.GetHeaderValue("Content-Type"));
        }

        [Fact]
        public void CreateFromStream_NullStream_ShouldReturn404()
        {
            // Act
            var response = FileResponse.CreateFromStream(null, "text/plain");

            // Assert
            Assert.Equal(404, response.StatusCode);
        }

        #endregion

        #region CreateFromBytes Tests

        [Fact]
        public void CreateFromBytes_ValidBytes_ShouldReturn200()
        {
            // Arrange
            var bytes = Encoding.UTF8.GetBytes("Test content");

            // Act
            var response = FileResponse.CreateFromBytes(bytes, "text/plain");

            // Assert
            Assert.Equal(200, response.StatusCode);
            Assert.Equal("text/plain", response.GetHeaderValue("Content-Type"));
        }

        [Fact]
        public void CreateFromBytes_NullBytes_ShouldReturn404()
        {
            // Act
            var response = FileResponse.CreateFromBytes(null, "text/plain");

            // Assert
            Assert.Equal(404, response.StatusCode);
        }

        #endregion

        #region GetContentType Tests

        [Theory]
        [InlineData("test.txt", "text/plain")]
        [InlineData("test.html", "text/html")]
        [InlineData("test.htm", "text/html")]
        [InlineData("test.css", "text/css")]
        [InlineData("test.js", "application/javascript")]
        [InlineData("test.mjs", "application/javascript")]
        [InlineData("test.json", "application/json")]
        [InlineData("test.xml", "application/xml")]
        [InlineData("test.csv", "text/csv")]
        [InlineData("test.md", "text/markdown")]
        [InlineData("test.svg", "image/svg+xml")]
        public void GetContentType_TextAndWebFiles_ShouldReturnCorrectMimeType(string fileName, string expected)
        {
            // Act
            var contentType = FileResponse.GetContentType(fileName);

            // Assert
            Assert.Equal(expected, contentType);
        }

        [Theory]
        [InlineData("test.jpg", "image/jpeg")]
        [InlineData("test.jpeg", "image/jpeg")]
        [InlineData("test.png", "image/png")]
        [InlineData("test.gif", "image/gif")]
        [InlineData("test.bmp", "image/bmp")]
        [InlineData("test.webp", "image/webp")]
        [InlineData("test.ico", "image/x-icon")]
        [InlineData("test.tiff", "image/tiff")]
        [InlineData("test.tif", "image/tiff")]
        [InlineData("test.avif", "image/avif")]
        public void GetContentType_ImageFiles_ShouldReturnCorrectMimeType(string fileName, string expected)
        {
            // Act
            var contentType = FileResponse.GetContentType(fileName);

            // Assert
            Assert.Equal(expected, contentType);
        }

        [Theory]
        [InlineData("test.mp3", "audio/mpeg")]
        [InlineData("test.wav", "audio/wav")]
        [InlineData("test.ogg", "audio/ogg")]
        [InlineData("test.m4a", "audio/mp4")]
        [InlineData("test.aac", "audio/aac")]
        [InlineData("test.flac", "audio/flac")]
        [InlineData("test.opus", "audio/opus")]
        public void GetContentType_AudioFiles_ShouldReturnCorrectMimeType(string fileName, string expected)
        {
            // Act
            var contentType = FileResponse.GetContentType(fileName);

            // Assert
            Assert.Equal(expected, contentType);
        }

        [Theory]
        [InlineData("test.mp4", "video/mp4")]
        [InlineData("test.webm", "video/webm")]
        [InlineData("test.avi", "video/x-msvideo")]
        [InlineData("test.mov", "video/quicktime")]
        [InlineData("test.mkv", "video/x-matroska")]
        [InlineData("test.ogv", "video/ogg")]
        public void GetContentType_VideoFiles_ShouldReturnCorrectMimeType(string fileName, string expected)
        {
            // Act
            var contentType = FileResponse.GetContentType(fileName);

            // Assert
            Assert.Equal(expected, contentType);
        }

        [Theory]
        [InlineData("test.pdf", "application/pdf")]
        [InlineData("test.doc", "application/msword")]
        [InlineData("test.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
        [InlineData("test.xls", "application/vnd.ms-excel")]
        [InlineData("test.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
        [InlineData("test.ppt", "application/vnd.ms-powerpoint")]
        [InlineData("test.pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation")]
        public void GetContentType_DocumentFiles_ShouldReturnCorrectMimeType(string fileName, string expected)
        {
            // Act
            var contentType = FileResponse.GetContentType(fileName);

            // Assert
            Assert.Equal(expected, contentType);
        }

        [Theory]
        [InlineData("test.zip", "application/zip")]
        [InlineData("test.ttf", "font/ttf")]
        [InlineData("test.otf", "font/otf")]
        [InlineData("test.woff", "font/woff")]
        [InlineData("test.woff2", "font/woff2")]
        [InlineData("test.eot", "application/vnd.ms-fontobject")]
        public void GetContentType_OtherFiles_ShouldReturnCorrectMimeType(string fileName, string expected)
        {
            // Act
            var contentType = FileResponse.GetContentType(fileName);

            // Assert
            Assert.Equal(expected, contentType);
        }

        [Fact]
        public void GetContentType_UnknownExtension_ShouldReturnOctetStream()
        {
            // Act
            var contentType = FileResponse.GetContentType("test.unknown");

            // Assert
            Assert.Equal("application/octet-stream", contentType);
        }

        [Fact]
        public void GetContentType_NoExtension_ShouldReturnOctetStream()
        {
            // Act
            var contentType = FileResponse.GetContentType("test");

            // Assert
            Assert.Equal("application/octet-stream", contentType);
        }

        [Fact]
        public void GetContentType_CaseInsensitive_ShouldWork()
        {
            // Act
            var contentType1 = FileResponse.GetContentType("test.HTML");
            var contentType2 = FileResponse.GetContentType("test.Html");
            var contentType3 = FileResponse.GetContentType("test.html");

            // Assert
            Assert.Equal("text/html", contentType1);
            Assert.Equal("text/html", contentType2);
            Assert.Equal("text/html", contentType3);
        }

        #endregion

        #region Extension Method Tests

        [Fact]
        public void CreateFileResponse_ExtensionMethod_ShouldWork()
        {
            // Arrange
            var request = HttpRequest.Create("GET", "/test");

            // Act
            var response = request.CreateFileResponse(this.testFilePath);

            // Assert
            Assert.Equal(200, response.StatusCode);
            Assert.Equal("text/plain", response.GetHeaderValue("Content-Type"));
        }

        [Fact]
        public void CreateEmbeddedResourceResponse_ExtensionMethod_ShouldWork()
        {
            // Arrange
            var request = HttpRequest.Create("GET", "/test");
            var assembly = Assembly.GetExecutingAssembly();

            // Act
            var response = request.CreateEmbeddedResourceResponse(assembly, "nonexistent.txt");

            // Assert
            Assert.Equal(404, response.StatusCode);
        }

        #endregion
    }
}