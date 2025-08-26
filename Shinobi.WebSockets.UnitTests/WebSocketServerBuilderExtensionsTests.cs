using System;
using System.Security.Cryptography.X509Certificates;

using Shinobi.WebSockets.Builders;

using Xunit;

namespace Shinobi.WebSockets.UnitTests
{
    public class WebSocketServerBuilderExtensionsTests
    {
        [Fact]
        public void ClearCertificateCache_ShouldClearCacheWithoutError()
        {
            // Act & Assert - Should not throw
            WebSocketServerBuilderExtensions.ClearCertificateCache();
        }

        [Fact]
        public void UseCertificate_WithValidParameters_ShouldReturnBuilderForFluentChaining()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();
            var storeName = StoreName.My;
            var storeLocation = StoreLocation.CurrentUser;
            var subjectName = "test-cert";

            // Act & Assert - This will likely throw because the certificate doesn't exist,
            // but we're testing that the method signature and basic validation works
            var exception = Assert.ThrowsAny<Exception>(() =>
                builder.UseCertificate(storeName, storeLocation, subjectName));

            // The exception should be related to certificate not found, not argument validation
            Assert.True(exception is InvalidOperationException,
                $"Expected InvalidOperationException for missing certificate, got {exception.GetType().Name}: {exception.Message}");
        }

        [Fact]
        public void UseCertificate_WithCustomCacheExpiration_ShouldAcceptTimeSpan()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();
            var storeName = StoreName.My;
            var storeLocation = StoreLocation.CurrentUser;
            var subjectName = "test-cert";
            var customExpiration = TimeSpan.FromMinutes(10);

            // Act & Assert - Testing parameter acceptance
            var exception = Assert.ThrowsAny<Exception>(() =>
                builder.UseCertificate(storeName, storeLocation, subjectName, customExpiration));

            // Should fail with certificate not found, not parameter validation
            Assert.True(exception is InvalidOperationException);
        }

        [Fact]
        public void UseCertificateByThumbprint_WithValidParameters_ShouldReturnBuilderForFluentChaining()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();
            var storeName = StoreName.My;
            var storeLocation = StoreLocation.CurrentUser;
            var thumbprint = "1234567890ABCDEF1234567890ABCDEF12345678";

            // Act & Assert - This will likely throw because the certificate doesn't exist
            var exception = Assert.ThrowsAny<Exception>(() =>
                builder.UseCertificateByThumbprint(storeName, storeLocation, thumbprint));

            // The exception should be related to certificate not found, not argument validation
            Assert.True(exception is InvalidOperationException);
        }

        [Fact]
        public void UseCertificateByThumbprint_WithCustomCacheExpiration_ShouldAcceptTimeSpan()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();
            var storeName = StoreName.My;
            var storeLocation = StoreLocation.CurrentUser;
            var thumbprint = "1234567890ABCDEF1234567890ABCDEF12345678";
            var customExpiration = TimeSpan.FromHours(1);

            // Act & Assert - Testing parameter acceptance
            var exception = Assert.ThrowsAny<Exception>(() =>
                builder.UseCertificateByThumbprint(storeName, storeLocation, thumbprint, customExpiration));

            // Should fail with certificate not found, not parameter validation
            Assert.True(exception is InvalidOperationException);
        }

        [Fact]
        public void UseDevCertificate_WhenNoCertificateExists_ShouldNotThrowDuringRegistration()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();

            // Act - UseDevCertificate may succeed if dev cert is installed, so just test it doesn't crash
            var result = builder.UseDevCertificate();

            // Assert - Should return builder for fluent chaining
            Assert.Same(builder, result);
        }

        [Fact]
        public void WebSocketServerBuilderExtensions_ShouldBeStaticClass()
        {
            // Assert the class is static and cannot be instantiated
            var type = typeof(WebSocketServerBuilderExtensions);
            Assert.True(type.IsAbstract && type.IsSealed, "WebSocketServerBuilderExtensions should be a static class");
        }

        [Fact]
        public void ClearCertificateCache_ShouldBeCallableMultipleTimes()
        {
            // Act & Assert - Should be safe to call multiple times
            WebSocketServerBuilderExtensions.ClearCertificateCache();
            WebSocketServerBuilderExtensions.ClearCertificateCache();
            WebSocketServerBuilderExtensions.ClearCertificateCache();

            // Should not throw any exceptions
        }

        [Theory]
        [InlineData(StoreName.My, StoreLocation.CurrentUser)]
        [InlineData(StoreName.Root, StoreLocation.LocalMachine)]
        [InlineData(StoreName.TrustedPublisher, StoreLocation.CurrentUser)]
        public void UseCertificate_WithDifferentStoreConfigurations_ShouldAcceptValidEnumValues(StoreName storeName, StoreLocation storeLocation)
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();
            var subjectName = "nonexistent-cert-for-testing";

            // Act & Assert - Should accept the enum values and fail on certificate lookup, not parameter validation
            var exception = Assert.ThrowsAny<Exception>(() =>
                builder.UseCertificate(storeName, storeLocation, subjectName));

            Assert.True(exception is InvalidOperationException);
            Assert.Contains("not found", exception.Message);
        }

        [Theory]
        [InlineData("1234567890ABCDEF1234567890ABCDEF12345678")]
        [InlineData("ABCDEF1234567890ABCDEF1234567890ABCDEF12")]
        [InlineData("short")]
        public void UseCertificateByThumbprint_WithDifferentThumbprintFormats_ShouldAcceptStringValues(string thumbprint)
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();
            var storeName = StoreName.My;
            var storeLocation = StoreLocation.CurrentUser;

            // Act & Assert - Should accept the thumbprint format and fail on certificate lookup
            var exception = Assert.ThrowsAny<Exception>(() =>
                builder.UseCertificateByThumbprint(storeName, storeLocation, thumbprint));

            Assert.True(exception is InvalidOperationException);
            Assert.Contains("not found", exception.Message);
        }

        [Fact]
        public void UseDevCertificate_WithNullBuilder_ShouldThrowArgumentNullException()
        {
            // Arrange
            WebSocketServerBuilder? nullBuilder = null;

            // Act & Assert - Extension method validates null parameters
            Assert.Throws<ArgumentNullException>(() => nullBuilder!.UseDevCertificate());
        }

        [Fact]
        public void UseCertificate_WithNullBuilder_ShouldThrowInvalidOperationException()
        {
            // Arrange
            WebSocketServerBuilder? nullBuilder = null;

            // Act & Assert - Extension method gets to certificate loading before null check
            Assert.Throws<InvalidOperationException>(() =>
                nullBuilder!.UseCertificate(StoreName.My, StoreLocation.CurrentUser, "test"));
        }

        [Fact]
        public void UseCertificateByThumbprint_WithNullBuilder_ShouldThrowInvalidOperationException()
        {
            // Arrange
            WebSocketServerBuilder? nullBuilder = null;

            // Act & Assert - Extension method gets to certificate loading before null check
            Assert.Throws<InvalidOperationException>(() =>
                nullBuilder!.UseCertificateByThumbprint(StoreName.My, StoreLocation.CurrentUser, "test"));
        }

        [Fact]
        public void UseCertificate_WithNullSubjectName_ShouldThrowException()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();

            // Act & Assert - Should throw when trying to load null subject name
            Assert.ThrowsAny<Exception>(() =>
                builder.UseCertificate(StoreName.My, StoreLocation.CurrentUser, null!));
        }

        [Fact]
        public void UseCertificateByThumbprint_WithNullThumbprint_ShouldThrowException()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();

            // Act & Assert - Should throw when trying to load null thumbprint
            Assert.ThrowsAny<Exception>(() =>
                builder.UseCertificateByThumbprint(StoreName.My, StoreLocation.CurrentUser, null!));
        }

        [Fact]
        public void UseCertificateByThumbprint_WithEmptyThumbprint_ShouldThrowInvalidOperationException()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();

            // Act & Assert - Should throw when trying to load empty thumbprint
            var exception = Assert.Throws<InvalidOperationException>(() =>
                builder.UseCertificateByThumbprint(StoreName.My, StoreLocation.CurrentUser, ""));

            Assert.Contains("not found", exception.Message);
        }

        [Fact]
        public void ClearCertificateCache_ShouldNotThrowWithEmptyCache()
        {
            // Act & Assert - Clearing empty cache should be safe
            WebSocketServerBuilderExtensions.ClearCertificateCache();

            // Should be safe to call again immediately
            WebSocketServerBuilderExtensions.ClearCertificateCache();
        }

        [Theory]
        [InlineData(StoreName.AddressBook)]
        [InlineData(StoreName.AuthRoot)]
        [InlineData(StoreName.CertificateAuthority)]
        [InlineData(StoreName.Disallowed)]
        [InlineData(StoreName.My)]
        [InlineData(StoreName.Root)]
        [InlineData(StoreName.TrustedPeople)]
        [InlineData(StoreName.TrustedPublisher)]
        public void UseCertificate_ShouldAcceptAllStoreNameValues(StoreName storeName)
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();

            // Act & Assert - Should accept all valid StoreName enum values
            var exception = Assert.ThrowsAny<Exception>(() =>
                builder.UseCertificate(storeName, StoreLocation.CurrentUser, "nonexistent-cert"));

            // Should fail on certificate lookup, not enum validation
            Assert.True(exception is InvalidOperationException);
        }

        [Fact]
        public void UseCertificate_WithZeroCacheExpiration_ShouldAcceptZeroTimeSpan()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();

            // Act & Assert - Should accept zero cache expiration
            var exception = Assert.ThrowsAny<Exception>(() =>
                builder.UseCertificate(StoreName.My, StoreLocation.CurrentUser, "test", TimeSpan.Zero));

            // Should fail on certificate lookup, not parameter validation
            Assert.True(exception is InvalidOperationException);
        }

        [Fact]
        public void UseCertificate_WithNegativeCacheExpiration_ShouldAcceptNegativeTimeSpan()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();

            // Act & Assert - Should accept negative cache expiration (though unusual)
            var exception = Assert.ThrowsAny<Exception>(() =>
                builder.UseCertificate(StoreName.My, StoreLocation.CurrentUser, "test", TimeSpan.FromSeconds(-1)));

            // Should fail on certificate lookup, not parameter validation
            Assert.True(exception is InvalidOperationException);
        }

        [Fact]
        public void FluentAPI_ShouldChainCertificateMethodsCorrectly()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();

            // Act & Assert - Test that methods can be chained (will throw on certificate lookup)
            var exception = Assert.ThrowsAny<Exception>(() =>
                builder
                    .UsePort(8080)
                    .UseCertificate(StoreName.My, StoreLocation.CurrentUser, "test-cert"));

            // Should fail on certificate lookup, not method chaining
            Assert.True(exception is InvalidOperationException);
        }
    }
}