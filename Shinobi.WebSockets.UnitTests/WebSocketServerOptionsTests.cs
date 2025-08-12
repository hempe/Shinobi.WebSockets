using System;
using System.Collections.Generic;
using Xunit;

namespace Shinobi.WebSockets.UnitTests
{
    /// <summary>
    /// Unit tests for WebSocketServerOptions configuration class
    /// </summary>
    public class WebSocketServerOptionsTests
    {
        [Fact]
        public void WebSocketServerOptions_DefaultConstructor_ShouldSetCorrectDefaults()
        {
            // Act
            var options = new WebSocketServerOptions();

            // Assert
            Assert.Equal((ushort)8080, options.Port);
            Assert.Equal(TimeSpan.FromSeconds(60), options.KeepAliveInterval);
            Assert.False(options.IncludeExceptionInCloseResponse);
            Assert.Null(options.SupportedSubProtocols);
            Assert.NotNull(options.PerMessageDeflate);
        }

        [Fact]
        public void WebSocketServerOptions_Port_ShouldGetAndSet()
        {
            // Arrange
            var options = new WebSocketServerOptions();
            ushort expectedPort = 9999;

            // Act
            options.Port = expectedPort;

            // Assert
            Assert.Equal(expectedPort, options.Port);
        }

        [Fact]
        public void WebSocketServerOptions_KeepAliveInterval_ShouldGetAndSet()
        {
            // Arrange
            var options = new WebSocketServerOptions();
            var expectedInterval = TimeSpan.FromMinutes(5);

            // Act
            options.KeepAliveInterval = expectedInterval;

            // Assert
            Assert.Equal(expectedInterval, options.KeepAliveInterval);
        }

        [Fact]
        public void WebSocketServerOptions_IncludeExceptionInCloseResponse_ShouldGetAndSet()
        {
            // Arrange
            var options = new WebSocketServerOptions();

            // Act
            options.IncludeExceptionInCloseResponse = true;

            // Assert
            Assert.True(options.IncludeExceptionInCloseResponse);
        }

        [Fact]
        public void WebSocketServerOptions_SupportedSubProtocols_ShouldGetAndSet()
        {
            // Arrange
            var options = new WebSocketServerOptions();
            var expectedProtocols = new HashSet<string> { "chat", "echo" };

            // Act
            options.SupportedSubProtocols = expectedProtocols;

            // Assert
            Assert.Same(expectedProtocols, options.SupportedSubProtocols);
        }

        [Fact]
        public void PerMessageDeflateOptions_DefaultConstructor_ShouldSetCorrectDefaults()
        {
            // Act
            var options = new PerMessageDeflateOptions();

            // Assert
            Assert.False(options.Enabled);
            Assert.Equal(ContextTakeoverMode.Allow, options.ServerContextTakeover);
            Assert.Equal(ContextTakeoverMode.Allow, options.ClientContextTakeover);
        }

        [Fact]
        public void PerMessageDeflateOptions_Enabled_ShouldGetAndSet()
        {
            // Arrange
            var options = new PerMessageDeflateOptions();

            // Act
            options.Enabled = true;

            // Assert
            Assert.True(options.Enabled);
        }

        [Theory]
        [InlineData(ContextTakeoverMode.Allow)]
        [InlineData(ContextTakeoverMode.DontAllow)]
        [InlineData(ContextTakeoverMode.ForceDisabled)]
        public void PerMessageDeflateOptions_ServerContextTakeover_ShouldGetAndSet(ContextTakeoverMode mode)
        {
            // Arrange
            var options = new PerMessageDeflateOptions();

            // Act
            options.ServerContextTakeover = mode;

            // Assert
            Assert.Equal(mode, options.ServerContextTakeover);
        }

        [Theory]
        [InlineData(ContextTakeoverMode.Allow)]
        [InlineData(ContextTakeoverMode.DontAllow)]
        [InlineData(ContextTakeoverMode.ForceDisabled)]
        public void PerMessageDeflateOptions_ClientContextTakeover_ShouldGetAndSet(ContextTakeoverMode mode)
        {
            // Arrange
            var options = new PerMessageDeflateOptions();

            // Act
            options.ClientContextTakeover = mode;

            // Assert
            Assert.Equal(mode, options.ClientContextTakeover);
        }

        [Fact]
        public void ContextTakeoverMode_AllEnumValues_ShouldBeDefined()
        {
            // Arrange & Act
            var modes = new[]
            {
                ContextTakeoverMode.Allow,
                ContextTakeoverMode.DontAllow,
                ContextTakeoverMode.ForceDisabled
            };

            // Assert
            foreach (var mode in modes)
            {
                Assert.True(Enum.IsDefined(typeof(ContextTakeoverMode), mode),
                    $"Mode {mode} should be properly defined");
            }
        }
    }
}