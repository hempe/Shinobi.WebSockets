using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Shinobi.WebSockets.Http;

using Xunit;

namespace Shinobi.WebSockets.UnitTests
{
    public class HttpResponseDateHeaderTests
    {
        [Fact]
        public async Task WriteToStreamAsync_ShouldAddDateHeader_WhenNotPresentAsync()
        {
            // Arrange
            var response = HttpResponse.Create(200)
                .AddHeader("Content-Type", "text/plain")
                .WithBody("Hello World");

            using var stream = new MemoryStream();

            // Act
            await response.WriteToStreamAsync(stream, CancellationToken.None);

            // Assert
            stream.Position = 0;
            using var reader = new StreamReader(stream);
            var responseText = await reader.ReadToEndAsync();

            Assert.Contains("Date:", responseText);
            Assert.Contains("200 OK", responseText);
        }

        [Fact]
        public async Task WriteToStreamAsync_ShouldNotOverrideExistingDateHeaderAsync()
        {
            // Arrange
            var customDate = "Wed, 21 Oct 2015 07:28:00 GMT";
            var response = HttpResponse.Create(200)
                .AddHeader("Date", customDate)
                .AddHeader("Content-Type", "text/plain")
                .WithBody("Hello World");

            using var stream = new MemoryStream();

            // Act
            await response.WriteToStreamAsync(stream, CancellationToken.None);

            // Assert
            stream.Position = 0;
            using var reader = new StreamReader(stream);
            var responseText = await reader.ReadToEndAsync();

            Assert.Contains("Date: " + customDate, responseText);
            // Should not contain current UTC date
            Assert.DoesNotContain(DateTime.UtcNow.ToString("R"), responseText);
        }

        [Fact]
        public void Build_ShouldAddDateHeader_WhenNotPresent()
        {
            // Arrange
            var response = HttpResponse.Create(404)
                .AddHeader("Content-Type", "text/plain");

            // Act
            var responseText = response.Build();

            // Assert
            Assert.Contains("Date:", responseText);
            Assert.Contains("404 Not Found", responseText);
        }

        [Fact]
        public void Build_ShouldNotOverrideExistingDateHeaderAsync()
        {
            // Arrange
            var customDate = "Wed, 21 Oct 2015 07:28:00 GMT";
            var response = HttpResponse.Create(404)
                .AddHeader("Date", customDate)
                .AddHeader("Content-Type", "text/plain");

            // Act
            var responseText = response.Build();

            // Assert
            Assert.Contains("Date: " + customDate, responseText);
            // Should not contain current UTC date
            Assert.DoesNotContain(DateTime.UtcNow.ToString("R"), responseText);
        }
    }
}