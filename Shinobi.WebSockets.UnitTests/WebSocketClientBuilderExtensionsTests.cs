using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Shinobi.WebSockets.Builders;
using Xunit;

namespace Shinobi.WebSockets.UnitTests
{
    /// <summary>
    /// Unit tests for WebSocketClientBuilderExtensions methods
    /// </summary>
    public class WebSocketClientBuilderExtensionsTests
    {
        [Fact]
        public void EnableAutoReconnect_ShouldEnableReconnection()
        {
            // Arrange
            var builder = WebSocketClientBuilder.Create();

            // Act
            var result = builder.EnableAutoReconnect();

            // Assert
            Assert.Same(builder, result); // Should return the same builder for fluent interface
            
            // Build and check that reconnection is enabled
            var client = result.Build();
            Assert.NotNull(client);
            // Note: We can't directly access the options, but we can verify the client was built
        }

        [Fact]
        public void UseReliableConnection_ShouldReturnBuilder()
        {
            // Arrange
            var builder = WebSocketClientBuilder.Create();

            // Act
            var result = builder.UseReliableConnection();

            // Assert
            Assert.Same(builder, result); // Should return the same builder for fluent interface
        }

        [Fact]
        public void UseAutoReconnect_WithAction_ShouldReturnBuilder()
        {
            // Arrange
            var builder = WebSocketClientBuilder.Create();
            var configurationCalled = false;

            // Act
            var result = builder.UseAutoReconnect(options =>
            {
                configurationCalled = true;
                options.Enabled = true;
                options.MaxAttempts = 5;
                options.InitialDelay = TimeSpan.FromSeconds(2);
            });

            // Assert
            Assert.Same(builder, result);
            // We can't directly verify the configuration was applied, but we can verify the callback was called
            // by building the client (which should trigger the configuration)
            var client = result.Build();
            Assert.NotNull(client);
            Assert.True(configurationCalled);
        }

        [Fact]
        public void UseAutoReconnect_WithNullAction_ShouldThrowArgumentNullException()
        {
            // Arrange
            var builder = WebSocketClientBuilder.Create();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => builder.UseAutoReconnect(null));
        }

        [Fact]
        public void UseExponentialBackoff_ShouldReturnBuilder()
        {
            // Arrange
            var builder = WebSocketClientBuilder.Create();
            var initialDelay = TimeSpan.FromSeconds(1);
            var maxDelay = TimeSpan.FromSeconds(60);
            var multiplier = 2.0;
            var jitter = 0.1;

            // Act
            var result = builder.UseExponentialBackoff(initialDelay, maxDelay, multiplier, jitter);

            // Assert
            Assert.Same(builder, result);
            
            var client = result.Build();
            Assert.NotNull(client);
        }

        [Fact]
        public void UseMaxReconnectAttempts_ShouldReturnBuilder()
        {
            // Arrange
            var builder = WebSocketClientBuilder.Create();
            var maxAttempts = 10;

            // Act
            var result = builder.UseMaxReconnectAttempts(maxAttempts);

            // Assert
            Assert.Same(builder, result);
            
            var client = result.Build();
            Assert.NotNull(client);
        }

        [Fact]
        public void UseFallbackUrls_WithSingleUrl_ShouldReturnBuilder()
        {
            // Arrange
            var builder = WebSocketClientBuilder.Create();
            var fallbackUrl = new Uri("ws://backup.example.com");

            // Act
            var result = builder.UseFallbackUrls(fallbackUrl);

            // Assert
            Assert.Same(builder, result);
            
            var client = result.Build();
            Assert.NotNull(client);
        }

        [Fact]
        public void UseFallbackUrls_WithMultipleUrls_ShouldReturnBuilder()
        {
            // Arrange
            var builder = WebSocketClientBuilder.Create();
            var fallbackUrls = new[]
            {
                new Uri("ws://backup1.example.com"),
                new Uri("ws://backup2.example.com"),
                new Uri("ws://backup3.example.com")
            };

            // Act
            var result = builder.UseFallbackUrls(fallbackUrls);

            // Assert
            Assert.Same(builder, result);
            
            var client = result.Build();
            Assert.NotNull(client);
        }

        [Fact]
        public void UseFallbackUrls_WithEmptyArray_ShouldThrowArgumentException()
        {
            // Arrange
            var builder = WebSocketClientBuilder.Create();
            var fallbackUrls = new Uri[0];

            // Act & Assert
            Assert.Throws<ArgumentException>(() => builder.UseFallbackUrls(fallbackUrls));
        }

        [Fact]
        public void OnTextMessage_WithSimpleHandler_ShouldReturnBuilder()
        {
            // Arrange
            var builder = WebSocketClientBuilder.Create();
            var handlerCalled = false;

            // Act
            var result = builder.OnTextMessage((message, ct) =>
            {
                handlerCalled = true;
                return new ValueTask();
            });

            // Assert
            Assert.Same(builder, result);
            
            var client = result.Build();
            Assert.NotNull(client);
            // Note: We can't easily test if the handler is called without a full integration test
        }

        [Fact]
        public void OnBinaryMessage_WithSimpleHandler_ShouldReturnBuilder()
        {
            // Arrange
            var builder = WebSocketClientBuilder.Create();
            var handlerCalled = false;

            // Act
            var result = builder.OnBinaryMessage((data, ct) =>
            {
                handlerCalled = true;
                return new ValueTask();
            });

            // Assert
            Assert.Same(builder, result);
            
            var client = result.Build();
            Assert.NotNull(client);
        }

        [Fact]
        public void ChainedExtensionMethods_ShouldAllReturnSameBuilder()
        {
            // Arrange
            var builder = WebSocketClientBuilder.Create();

            // Act
            var result = builder
                .EnableAutoReconnect()
                .UseMaxReconnectAttempts(5)
                .UseExponentialBackoff(
                    initialDelay: TimeSpan.FromSeconds(1),
                    maxDelay: TimeSpan.FromSeconds(30),
                    multiplier: 2.0,
                    jitter: 0.1)
                .OnTextMessage((message, ct) => new ValueTask())
                .OnBinaryMessage((data, ct) => new ValueTask());

            // Assert
            Assert.Same(builder, result);
            
            var client = result.Build();
            Assert.NotNull(client);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(100)]
        public void UseMaxReconnectAttempts_WithDifferentValues_ShouldReturnBuilder(int maxAttempts)
        {
            // Arrange
            var builder = WebSocketClientBuilder.Create();

            // Act
            var result = builder.UseMaxReconnectAttempts(maxAttempts);

            // Assert
            Assert.Same(builder, result);
            
            var client = result.Build();
            Assert.NotNull(client);
        }

        [Fact]
        public void UseExponentialBackoff_WithZeroValues_ShouldReturnBuilder()
        {
            // Arrange
            var builder = WebSocketClientBuilder.Create();

            // Act
            var result = builder.UseExponentialBackoff(
                initialDelay: TimeSpan.Zero,
                maxDelay: TimeSpan.Zero,
                multiplier: 0.0,
                jitter: 0.0);

            // Assert
            Assert.Same(builder, result);
            
            var client = result.Build();
            Assert.NotNull(client);
        }
    }
}