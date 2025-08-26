using System;
using System.Threading;
using System.Threading.Tasks;

using Shinobi.WebSockets.Builders;

using Xunit;

namespace Shinobi.WebSockets.UnitTests
{
    public class WebSocketClientBuilderExtensionsTests
    {
        [Fact]
        public void EnableAutoReconnect_ShouldEnableReconnectOptions()
        {
            // Arrange
            var builder = WebSocketClientBuilder.Create();

            // Act
            var result = builder.EnableAutoReconnect();

            // Assert
            Assert.Same(builder, result); // Should return same builder for fluent API
            Assert.True(builder.configuration.ReconnectOptions.Enabled);
        }

        [Fact]
        public void UseAutoReconnect_WithValidConfiguration_ShouldApplySettings()
        {
            // Arrange
            var builder = WebSocketClientBuilder.Create();
            var initialDelay = TimeSpan.FromSeconds(2);
            var maxDelay = TimeSpan.FromMinutes(2);

            // Act
            var result = builder.UseAutoReconnect(options =>
            {
                options.InitialDelay = initialDelay;
                options.MaxDelay = maxDelay;
            });

            // Assert
            Assert.Same(builder, result);
            Assert.True(builder.configuration.ReconnectOptions.Enabled);
            Assert.Equal(initialDelay, builder.configuration.ReconnectOptions.InitialDelay);
            Assert.Equal(maxDelay, builder.configuration.ReconnectOptions.MaxDelay);
        }

        [Fact]
        public void UseAutoReconnect_WithNullConfiguration_ShouldThrowArgumentNullException()
        {
            // Arrange
            var builder = WebSocketClientBuilder.Create();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => builder.UseAutoReconnect(null!));
        }

        [Fact]
        public async Task OnReconnecting_WithValidHandler_ShouldSetHandlerAsync()
        {
            // Arrange
            var builder = WebSocketClientBuilder.Create();
            var handlerCalled = false;

            Shinobi.WebSockets.WebSocketReconnectingHandler handler = (currentUri, attemptNumber, cancellationToken) =>
            {
                handlerCalled = true;
                return new ValueTask<Uri>(currentUri);
            };

            // Act
            var result = builder.OnReconnecting(handler);

            // Assert
            Assert.Same(builder, result);
            Assert.NotNull(builder.configuration.OnReconnecting);

            // Test that the handler is actually called
            var testUri = new Uri("ws://test.com");
            var returnedUri = await builder.configuration.OnReconnecting!(testUri, 1, CancellationToken.None);
            Assert.True(handlerCalled);
            Assert.Equal(testUri, returnedUri);
        }

        [Fact]
        public void OnReconnecting_WithNullHandler_ShouldThrowArgumentNullException()
        {
            // Arrange
            var builder = WebSocketClientBuilder.Create();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => builder.OnReconnecting(null!));
        }

        [Fact]
        public async Task UseUrlSelector_WithValidSelector_ShouldSetupReconnectingHandlerAsync()
        {
            // Arrange
            var builder = WebSocketClientBuilder.Create();
            var originalUri = new Uri("ws://original.com");
            var newUri = new Uri("ws://new.com");

            Func<Uri, int, Uri> urlSelector = (currentUri, attemptNumber) =>
            {
                return attemptNumber > 1 ? newUri : currentUri;
            };

            // Act
            var result = builder.UseUrlSelector(urlSelector);

            // Assert
            Assert.Same(builder, result);
            Assert.NotNull(builder.configuration.OnReconnecting);

            // Test the URL selector logic
            var firstAttempt = await builder.configuration.OnReconnecting!(originalUri, 1, CancellationToken.None);
            var secondAttempt = await builder.configuration.OnReconnecting!(originalUri, 2, CancellationToken.None);

            Assert.Equal(originalUri, firstAttempt);
            Assert.Equal(newUri, secondAttempt);
        }

        [Fact]
        public void UseUrlSelector_WithNullSelector_ShouldThrowArgumentNullException()
        {
            // Arrange
            var builder = WebSocketClientBuilder.Create();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => builder.UseUrlSelector(null!));
        }

        [Fact]
        public async Task UseFallbackUrls_WithValidUrls_ShouldSetupUrlFallbackAsync()
        {
            // Arrange
            var builder = WebSocketClientBuilder.Create();
            var originalUri = new Uri("ws://primary.com");
            var fallback1 = new Uri("ws://fallback1.com");
            var fallback2 = new Uri("ws://fallback2.com");

            // Act
            var result = builder.UseFallbackUrls(fallback1, fallback2);

            // Assert
            Assert.Same(builder, result);
            Assert.NotNull(builder.configuration.OnReconnecting);

            // Test fallback URL cycling
            var attempt1 = await builder.configuration.OnReconnecting!(originalUri, 1, CancellationToken.None);
            var attempt2 = await builder.configuration.OnReconnecting!(originalUri, 2, CancellationToken.None);
            var attempt3 = await builder.configuration.OnReconnecting!(originalUri, 3, CancellationToken.None);
            var attempt4 = await builder.configuration.OnReconnecting!(originalUri, 4, CancellationToken.None);

            Assert.Equal(originalUri, attempt1);     // First attempt uses original
            Assert.Equal(fallback1, attempt2);      // Second attempt uses first fallback
            Assert.Equal(fallback2, attempt3);      // Third attempt uses second fallback  
            Assert.Equal(fallback1, attempt4);      // Fourth attempt cycles back to first fallback
        }

        [Fact]
        public void UseFallbackUrls_WithNullUrls_ShouldThrowArgumentException()
        {
            // Arrange
            var builder = WebSocketClientBuilder.Create();

            // Act & Assert
            Assert.Throws<ArgumentException>(() => builder.UseFallbackUrls(null!));
        }

        [Fact]
        public void UseFallbackUrls_WithEmptyUrls_ShouldThrowArgumentException()
        {
            // Arrange
            var builder = WebSocketClientBuilder.Create();

            // Act & Assert
            Assert.Throws<ArgumentException>(() => builder.UseFallbackUrls());
        }

        [Fact]
        public void UseExponentialBackoff_WithCustomSettings_ShouldConfigureBackoffOptions()
        {
            // Arrange
            var builder = WebSocketClientBuilder.Create();
            var initialDelay = TimeSpan.FromSeconds(2);
            var maxDelay = TimeSpan.FromMinutes(5);
            var multiplier = 1.5;
            var jitter = 0.2;

            // Act
            var result = builder.UseExponentialBackoff(initialDelay, maxDelay, multiplier, jitter);

            // Assert
            Assert.Same(builder, result);
            Assert.True(builder.configuration.ReconnectOptions.Enabled);
            Assert.Equal(initialDelay, builder.configuration.ReconnectOptions.InitialDelay);
            Assert.Equal(maxDelay, builder.configuration.ReconnectOptions.MaxDelay);
            Assert.Equal(multiplier, builder.configuration.ReconnectOptions.BackoffMultiplier);
            Assert.Equal(jitter, builder.configuration.ReconnectOptions.Jitter);
        }

        [Fact]
        public void UseExponentialBackoff_WithDefaultMultiplierAndJitter_ShouldUseDefaults()
        {
            // Arrange
            var builder = WebSocketClientBuilder.Create();
            var initialDelay = TimeSpan.FromSeconds(1);
            var maxDelay = TimeSpan.FromSeconds(30);

            // Act
            var result = builder.UseExponentialBackoff(initialDelay, maxDelay);

            // Assert
            Assert.Same(builder, result);
            Assert.True(builder.configuration.ReconnectOptions.Enabled);
            Assert.Equal(2.0, builder.configuration.ReconnectOptions.BackoffMultiplier);
            Assert.Equal(0.1, builder.configuration.ReconnectOptions.Jitter);
        }

        [Fact]
        public void OnConnectionStateChanged_WithValidHandler_ShouldStoreHandlerReference()
        {
            // Arrange
            var builder = WebSocketClientBuilder.Create();

            WebSocketConnectionStateChangedHandler handler = (client, args) =>
            {
                // Handler implementation for testing
            };

            // Act
            var result = builder.OnConnectionStateChanged(handler);

            // Assert
            Assert.Same(builder, result);
            // Note: This is a workaround implementation that stores the handler name in headers
            Assert.True(builder.configuration.AdditionalHttpHeaders.ContainsKey("__ConnectionStateHandler"));
        }

        [Fact]
        public void OnConnectionStateChanged_WithNullHandler_ShouldThrowArgumentNullException()
        {
            // Arrange
            var builder = WebSocketClientBuilder.Create();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => builder.OnConnectionStateChanged(null!));
        }

        [Fact]
        public void UseReliableConnection_ShouldConfigureReconnectWithDefaults()
        {
            // Arrange
            var builder = WebSocketClientBuilder.Create();

            // Act
            var result = builder.UseReliableConnection();

            // Assert
            Assert.Same(builder, result);
            Assert.True(builder.configuration.ReconnectOptions.Enabled);
            Assert.Equal(TimeSpan.FromSeconds(1), builder.configuration.ReconnectOptions.InitialDelay);
            Assert.Equal(TimeSpan.FromSeconds(30), builder.configuration.ReconnectOptions.MaxDelay);
            Assert.Equal(2.0, builder.configuration.ReconnectOptions.BackoffMultiplier);
            Assert.Equal(0.1, builder.configuration.ReconnectOptions.Jitter);
        }

        [Fact]
        public void OnTextMessage_WithSimpleHandler_ShouldWrapHandler()
        {
            // Arrange
            var builder = WebSocketClientBuilder.Create();
            // Variables to verify handler registration

            Func<string, CancellationToken, ValueTask> handler = (message, ct) => default;

            // Act
            var result = builder.OnTextMessage(handler);

            // Assert
            Assert.Same(builder, result);
            Assert.NotEmpty(builder.onMessage);

            // The handler should be wrapped, so we can't test it directly,
            // but we can verify the builder state changed
            Assert.Single(builder.onMessage);
        }

        [Fact]
        public void OnTextMessage_WithNullHandler_ShouldThrowArgumentNullException()
        {
            // Arrange
            var builder = WebSocketClientBuilder.Create();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => builder.OnTextMessage((Func<string, CancellationToken, ValueTask>)null!));
        }

        [Fact]
        public void OnBinaryMessage_WithSimpleHandler_ShouldWrapHandler()
        {
            // Arrange
            var builder = WebSocketClientBuilder.Create();

            Func<byte[], CancellationToken, ValueTask> handler = (data, ct) => default;

            // Act
            var result = builder.OnBinaryMessage(handler);

            // Assert
            Assert.Same(builder, result);
            Assert.NotEmpty(builder.onMessage);
            Assert.Single(builder.onMessage);
        }

        [Fact]
        public void OnBinaryMessage_WithNullHandler_ShouldThrowArgumentNullException()
        {
            // Arrange
            var builder = WebSocketClientBuilder.Create();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => builder.OnBinaryMessage((Func<byte[], CancellationToken, ValueTask>)null!));
        }

        [Fact]
        public void FluentAPI_ShouldChainMethodsCorrectly()
        {
            // Arrange
            var builder = WebSocketClientBuilder.Create();
            var fallbackUrl = new Uri("ws://fallback.com");

            // Act - Chain multiple extension methods
            var result = builder
                .EnableAutoReconnect()
                .UseExponentialBackoff(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(60))
                .UseFallbackUrls(fallbackUrl)
                .OnTextMessage((message, ct) => default)
                .OnBinaryMessage((data, ct) => default);

            // Assert
            Assert.Same(builder, result);
            Assert.True(builder.configuration.ReconnectOptions.Enabled);
            Assert.NotNull(builder.configuration.OnReconnecting);
            Assert.Equal(2, builder.onMessage.Count); // One text + one binary handler
        }
    }
}