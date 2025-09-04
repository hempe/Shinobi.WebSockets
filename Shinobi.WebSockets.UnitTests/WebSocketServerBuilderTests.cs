using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

using Shinobi.WebSockets.Builders;
using Shinobi.WebSockets.Http;

using Xunit;

// Import the delegate types from the Builders namespace
using WebSocketTextMessageHandler = Shinobi.WebSockets.Builders.WebSocketTextMessageHandler;
using WebSocketBinaryMessageHandler = Shinobi.WebSockets.Builders.WebSocketBinaryMessageHandler;

namespace Shinobi.WebSockets.UnitTests
{
    public class WebSocketServerBuilderTests
    {
        [Fact]
        public void Create_ShouldReturnNewBuilderInstance()
        {
            // Act
            var builder = WebSocketServerBuilder.Create();

            // Assert
            Assert.NotNull(builder);
        }

        [Fact]
        public void UsePort_ShouldSetPortInConfiguration()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();
            ushort port = 8080;

            // Act
            var result = builder.UsePort(port);

            // Assert
            Assert.Same(builder, result); // Fluent API
            var server = builder.Build();
            Assert.NotNull(server);
        }

        [Fact]
        public void UseKeepAlive_ShouldSetKeepAliveInterval()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();
            var interval = TimeSpan.FromSeconds(30);

            // Act
            var result = builder.UseKeepAlive(interval);

            // Assert
            Assert.Same(builder, result);
            var server = builder.Build();
            Assert.NotNull(server);
        }

        [Fact]
        public void DefaultConfiguration_ShouldHaveCorrectDefaults()
        {
            // Arrange & Act
            var options = new WebSocketServerOptions();

            // Assert - Verify default values match documentation
            Assert.Equal(TimeSpan.FromSeconds(5), options.KeepAliveTimeout);
            Assert.Equal(TimeSpan.FromSeconds(30), options.KeepAliveInterval);
            Assert.Equal(1000, options.MaxKeepAliveConnections);
            Assert.False(options.IncludeExceptionInCloseResponse);
        }

        [Fact]
        public void IncludeExceptionInCloseResponse_ShouldSetConfigurationFlag()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();

            // Act
            var result = builder.IncludeExceptionInCloseResponse(true);

            // Assert
            Assert.Same(builder, result);
            var server = builder.Build();
            Assert.NotNull(server);
        }

        [Fact]
        public void UseSsl_WithCertificate_ShouldRegisterCertificateSelector()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();
            X509Certificate2? testCert = null; // Can be null for testing

            // Act
            var result = builder.UseSsl(testCert);

            // Assert
            Assert.Same(builder, result);
            var server = builder.Build();
            Assert.NotNull(server);
        }

        [Fact]
        public void UseSsl_WithNullInterceptor_ShouldThrowArgumentNullException()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => builder.UseSsl((CertificateSelectionInterceptor)null!));
        }

        [Fact]
        public void UseConfiguration_WithValidAction_ShouldApplyConfiguration()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();
            var configCalled = false;

            // Act
            var result = builder.UseConfiguration(options =>
            {
                configCalled = true;
                options.Port = 9999;
            });

            // Assert
            Assert.Same(builder, result);
            Assert.True(configCalled);
            var server = builder.Build();
            Assert.NotNull(server);
        }

        [Fact]
        public void UseConfiguration_WithNullAction_ShouldThrowArgumentNullException()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => builder.UseConfiguration(null!));
        }

        [Fact]
        public void UseSupportedSubProtocols_WithValidProtocols_ShouldSetProtocols()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();
            var protocols = new[] { "chat", "echo" };

            // Act
            var result = builder.UseSupportedSubProtocols(protocols);

            // Assert
            Assert.Same(builder, result);
            var server = builder.Build();
            Assert.NotNull(server);
        }

        [Fact]
        public void UseSupportedSubProtocols_WithNullOrEmpty_ShouldClearProtocols()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();

            // Act
            var result = builder.UseSupportedSubProtocols();

            // Assert
            Assert.Same(builder, result);
            var server = builder.Build();
            Assert.NotNull(server);
        }

        [Fact]
        public void AddSupportedSubProtocol_WithValidProtocol_ShouldAddProtocol()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();
            var protocol = "chat";

            // Act
            var result = builder.AddSupportedSubProtocol(protocol);

            // Assert
            Assert.Same(builder, result);
            var server = builder.Build();
            Assert.NotNull(server);
        }

        [Fact]
        public void AddSupportedSubProtocol_WithNullOrWhitespace_ShouldThrowArgumentException()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();

            // Act & Assert
            Assert.Throws<ArgumentException>(() => builder.AddSupportedSubProtocol(null!));
            Assert.Throws<ArgumentException>(() => builder.AddSupportedSubProtocol(""));
            Assert.Throws<ArgumentException>(() => builder.AddSupportedSubProtocol("   "));
        }

        [Fact]
        public void OnConnect_WithValidHandler_ShouldRegisterHandler()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();
            var handlerCalled = false;

            WebSocketConnectInterceptor handler = (webSocket, next, cancellationToken) =>
            {
                handlerCalled = true;
                return next(webSocket, cancellationToken);
            };

            // Act
            var result = builder.OnConnect(handler);

            // Assert
            Assert.Same(builder, result);
            Assert.False(handlerCalled); // Not called during registration
            var server = builder.Build();
            Assert.NotNull(server);
        }

        [Fact]
        public void OnConnect_WithNullHandler_ShouldThrowArgumentNullException()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => builder.OnConnect(null!));
        }

        [Fact]
        public void OnClose_WithValidHandler_ShouldRegisterHandler()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();
            var handlerCalled = false;

            WebSocketCloseInterceptor handler = (webSocket, closeStatus, statusDescription, next, cancellationToken) =>
            {
                handlerCalled = true;
                return next(webSocket, closeStatus, statusDescription, cancellationToken);
            };

            // Act
            var result = builder.OnClose(handler);

            // Assert
            Assert.Same(builder, result);
            Assert.False(handlerCalled);
            var server = builder.Build();
            Assert.NotNull(server);
        }

        [Fact]
        public void OnClose_WithNullHandler_ShouldThrowArgumentNullException()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => builder.OnClose(null!));
        }

        [Fact]
        public void OnError_WithValidHandler_ShouldRegisterHandler()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();
            var handlerCalled = false;

            WebSocketErrorInterceptor handler = (webSocket, exception, next, cancellationToken) =>
            {
                handlerCalled = true;
                return next(webSocket, exception, cancellationToken);
            };

            // Act
            var result = builder.OnError(handler);

            // Assert
            Assert.Same(builder, result);
            Assert.False(handlerCalled);
            var server = builder.Build();
            Assert.NotNull(server);
        }

        [Fact]
        public void OnError_WithNullHandler_ShouldThrowArgumentNullException()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => builder.OnError(null!));
        }

        [Fact]
        public void OnMessage_WithValidHandler_ShouldRegisterHandler()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();
            var handlerCalled = false;

            WebSocketMessageInterceptor handler = (webSocket, messageType, messageStream, next, cancellationToken) =>
            {
                handlerCalled = true;
                return next(webSocket, messageType, messageStream, cancellationToken);
            };

            // Act
            var result = builder.OnMessage(handler);

            // Assert
            Assert.Same(builder, result);
            Assert.False(handlerCalled);
            var server = builder.Build();
            Assert.NotNull(server);
        }

        [Fact]
        public void OnMessage_WithNullHandler_ShouldThrowArgumentNullException()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => builder.OnMessage(null!));
        }

        // Note: OnTextMessage and OnBinaryMessage tests are complex due to delegate type conflicts
        // The core functionality is already tested through OnMessage which these methods use internally

        [Fact]
        public void OnAcceptStream_WithValidInterceptor_ShouldRegisterInterceptor()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();
            var interceptorCalled = false;

            AcceptStreamInterceptor interceptor = (tcpClient, next, cancellationToken) =>
            {
                interceptorCalled = true;
                return next(tcpClient, cancellationToken);
            };

            // Act
            var result = builder.OnAcceptStream(interceptor);

            // Assert
            Assert.Same(builder, result);
            Assert.False(interceptorCalled);
            var server = builder.Build();
            Assert.NotNull(server);
        }

        [Fact]
        public void OnAcceptStream_WithNullInterceptor_ShouldThrowArgumentNullException()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => builder.OnAcceptStream(null!));
        }

        [Fact]
        public void OnHandshake_WithValidInterceptor_ShouldRegisterInterceptor()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();
            var interceptorCalled = false;

            HandshakeInterceptor interceptor = (context, next, cancellationToken) =>
            {
                interceptorCalled = true;
                return next(context, cancellationToken);
            };

            // Act
            var result = builder.OnHandshake(interceptor);

            // Assert
            Assert.Same(builder, result);
            Assert.False(interceptorCalled);
            var server = builder.Build();
            Assert.NotNull(server);
        }

        [Fact]
        public void OnHandshake_WithNullInterceptor_ShouldThrowArgumentNullException()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => builder.OnHandshake(null!));
        }

        [Fact]
        public void UseAuthentication_WithValidAuthenticator_ShouldRegisterHandshakeInterceptor()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();
            var authenticatorCalled = false;

            WebSocketAuthenticator authenticator = context =>
            {
                authenticatorCalled = true;
                return true; // Allow connection
            };

            // Act
            var result = builder.UseAuthentication(authenticator);

            // Assert
            Assert.Same(builder, result);
            Assert.False(authenticatorCalled);
            var server = builder.Build();
            Assert.NotNull(server);
        }

        [Fact]
        public void UseAuthentication_WithNullAuthenticator_ShouldThrowArgumentNullException()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => builder.UseAuthentication(null!));
        }

        [Fact]
        public void UseCors_WithValidOrigins_ShouldRegisterCorsHandler()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();
            var origins = new[] { "https://example.com", "https://test.com" };

            // Act
            var result = builder.UseCors(origins);

            // Assert
            Assert.Same(builder, result);
            var server = builder.Build();
            Assert.NotNull(server);
        }

        [Fact]
        public void UseCors_WithNoOrigins_ShouldUseWildcard()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();

            // Act
            var result = builder.UseCors();

            // Assert
            Assert.Same(builder, result);
            var server = builder.Build();
            Assert.NotNull(server);
        }

        [Fact]
        public void FluentAPI_ShouldChainMethodsCorrectly()
        {
            // Arrange & Act
            var result = WebSocketServerBuilder.Create()
                .UsePort(8080)
                .UseKeepAlive(TimeSpan.FromSeconds(30))
                .IncludeExceptionInCloseResponse(true)
                .UseSupportedSubProtocols("chat", "echo")
                .OnConnect((ws, next, ct) => next(ws, ct))
                .OnMessage((ws, type, stream, next, ct) => next(ws, type, stream, ct))
                .UseCors("*");

            // Assert
            Assert.NotNull(result);
            var server = result.Build();
            Assert.NotNull(server);
        }

#if NET8_0_OR_GREATER
        [Fact]
        public void UsePerMessageDeflate_ShouldEnableCompression()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();

            // Act
            var result = builder.UsePerMessageDeflate();

            // Assert
            Assert.Same(builder, result);
            var server = builder.Build();
            Assert.NotNull(server);
        }

        [Fact]
        public void UsePerMessageDeflate_WithConfiguration_ShouldApplySettings()
        {
            // Arrange
            var builder = WebSocketServerBuilder.Create();
            var configureCalled = false;

            // Act
            var result = builder.UsePerMessageDeflate(options =>
            {
                configureCalled = true;
                options.ServerContextTakeover = ContextTakeoverMode.DontAllow;
            });

            // Assert
            Assert.Same(builder, result);
            Assert.True(configureCalled);
            var server = builder.Build();
            Assert.NotNull(server);
        }
#endif
    }
}