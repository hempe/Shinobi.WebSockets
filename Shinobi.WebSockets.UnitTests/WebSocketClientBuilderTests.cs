using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Shinobi.WebSockets.Builders;

using Xunit;

namespace Shinobi.WebSockets.UnitTests
{
    public class WebSocketClientBuilderTests
    {
        [Fact]
        public void Create_ShouldReturnNewInstance()
        {
            var builder = WebSocketClientBuilder.Create();

            Assert.NotNull(builder);
            Assert.Empty(builder.onConnect);
            Assert.Empty(builder.onClose);
            Assert.Empty(builder.onError);
            Assert.Empty(builder.onMessage);
            Assert.Null(builder.logger);
        }

        [Fact]
        public void UseKeepAlive_ShouldSetKeepAliveInterval()
        {
            var interval = TimeSpan.FromMinutes(5);
            var builder = WebSocketClientBuilder.Create();

            builder.UseKeepAlive(interval);

            Assert.Equal(interval, builder.configuration.KeepAliveInterval);
        }

        [Fact]
        public void UseNoDelay_ShouldSetNoDelayOption()
        {
            var builder = WebSocketClientBuilder.Create();

            builder.UseNoDelay(true);
            Assert.True(builder.configuration.NoDelay);

            builder.UseNoDelay(false);
            Assert.False(builder.configuration.NoDelay);
        }

        [Fact]
        public void IncludeExceptionInCloseResponse_ShouldSetOption()
        {
            var builder = WebSocketClientBuilder.Create();

            builder.IncludeExceptionInCloseResponse(true);
            Assert.True(builder.configuration.IncludeExceptionInCloseResponse);

            builder.IncludeExceptionInCloseResponse(false);
            Assert.False(builder.configuration.IncludeExceptionInCloseResponse);
        }

        [Fact]
        public void AddHeader_ShouldAddCustomHeader()
        {
            var builder = WebSocketClientBuilder.Create();

            builder.AddHeader("Custom-Header", "test-value");

            Assert.Contains("Custom-Header", builder.configuration.AdditionalHttpHeaders.Keys);
            Assert.Equal("test-value", builder.configuration.AdditionalHttpHeaders["Custom-Header"]);
        }

        [Fact]
        public void AddHeader_WithNullOrEmptyName_ShouldThrowArgumentException()
        {
            var builder = WebSocketClientBuilder.Create();

            Assert.Throws<ArgumentException>(() => builder.AddHeader(null!, "value"));
            Assert.Throws<ArgumentException>(() => builder.AddHeader("", "value"));
            Assert.Throws<ArgumentException>(() => builder.AddHeader("   ", "value"));
        }

        [Fact]
        public void AddHeader_WithNullValue_ShouldThrowArgumentNullException()
        {
            var builder = WebSocketClientBuilder.Create();

            Assert.Throws<ArgumentNullException>(() => builder.AddHeader("Header", null!));
        }

        [Fact]
        public void UseSubProtocol_ShouldSetSubProtocol()
        {
            var builder = WebSocketClientBuilder.Create();

            builder.UseSubProtocol("chat");

            Assert.Equal("chat", builder.configuration.SecWebSocketProtocol);
        }

        [Fact]
        public void UseSubProtocol_WithNullOrEmpty_ShouldThrowArgumentException()
        {
            var builder = WebSocketClientBuilder.Create();

            Assert.Throws<ArgumentException>(() => builder.UseSubProtocol(null!));
            Assert.Throws<ArgumentException>(() => builder.UseSubProtocol(""));
            Assert.Throws<ArgumentException>(() => builder.UseSubProtocol("   "));
        }

        [Fact]
        public void UseExtensions_ShouldSetExtensions()
        {
            var builder = WebSocketClientBuilder.Create();

            builder.UseExtensions("custom-extension");

            Assert.Equal("custom-extension", builder.configuration.SecWebSocketExtensions);
        }

        [Fact]
        public void UsePerMessageDeflate_ShouldSetExtensions()
        {
            var builder = WebSocketClientBuilder.Create();

            builder.UsePerMessageDeflate();

            Assert.Equal("permessage-deflate", builder.configuration.SecWebSocketExtensions);
        }

        [Fact]
        public void UseBearerAuthentication_ShouldAddAuthHeader()
        {
            var builder = WebSocketClientBuilder.Create();

            builder.UseBearerAuthentication("test-token");

            Assert.Contains("Authorization", builder.configuration.AdditionalHttpHeaders.Keys);
            Assert.Equal("Bearer test-token", builder.configuration.AdditionalHttpHeaders["Authorization"]);
        }

        [Fact]
        public void UseBearerAuthentication_WithNullOrEmpty_ShouldThrowArgumentException()
        {
            var builder = WebSocketClientBuilder.Create();

            Assert.Throws<ArgumentException>(() => builder.UseBearerAuthentication(null!));
            Assert.Throws<ArgumentException>(() => builder.UseBearerAuthentication(""));
            Assert.Throws<ArgumentException>(() => builder.UseBearerAuthentication("   "));
        }

        [Fact]
        public void UseBasicAuthentication_ShouldAddAuthHeader()
        {
            var builder = WebSocketClientBuilder.Create();

            builder.UseBasicAuthentication("user", "pass");

            var expectedCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("user:pass"));
            Assert.Contains("Authorization", builder.configuration.AdditionalHttpHeaders.Keys);
            Assert.Equal($"Basic {expectedCredentials}", builder.configuration.AdditionalHttpHeaders["Authorization"]);
        }

        [Fact]
        public void UseBasicAuthentication_WithInvalidInputs_ShouldThrowExceptions()
        {
            var builder = WebSocketClientBuilder.Create();

            Assert.Throws<ArgumentException>(() => builder.UseBasicAuthentication(null!, "pass"));
            Assert.Throws<ArgumentException>(() => builder.UseBasicAuthentication("", "pass"));
            Assert.Throws<ArgumentException>(() => builder.UseBasicAuthentication("   ", "pass"));
            Assert.Throws<ArgumentNullException>(() => builder.UseBasicAuthentication("user", null!));
        }

        [Fact]
        public void OnConnect_ShouldRegisterConnectHandler()
        {
            var builder = WebSocketClientBuilder.Create();
            var handlerCalled = false;

            builder.OnConnect(async (webSocket, next, ct) =>
            {
                handlerCalled = true;
                await next(webSocket, ct);
            });

            Assert.Single(builder.onConnect);
            Assert.False(handlerCalled); // Handler not called yet
        }

        [Fact]
        public void OnConnect_WithNullHandler_ShouldThrowArgumentNullException()
        {
            var builder = WebSocketClientBuilder.Create();

            Assert.Throws<ArgumentNullException>(() => builder.OnConnect(null!));
        }

        [Fact]
        public void OnClose_ShouldRegisterCloseHandler()
        {
            var builder = WebSocketClientBuilder.Create();

            builder.OnClose(async (webSocket, statusDescription, next, ct) => await next(webSocket, statusDescription, ct));

            Assert.Single(builder.onClose);
        }

        [Fact]
        public void OnClose_WithNullHandler_ShouldThrowArgumentNullException()
        {
            var builder = WebSocketClientBuilder.Create();

            Assert.Throws<ArgumentNullException>(() => builder.OnClose(null!));
        }

        [Fact]
        public void OnError_ShouldRegisterErrorHandler()
        {
            var builder = WebSocketClientBuilder.Create();

            builder.OnError(async (webSocket, exception, next, ct) => await next(webSocket, exception, ct));

            Assert.Single(builder.onError);
        }

        [Fact]
        public void OnError_WithNullHandler_ShouldThrowArgumentNullException()
        {
            var builder = WebSocketClientBuilder.Create();

            Assert.Throws<ArgumentNullException>(() => builder.OnError(null!));
        }

        [Fact]
        public void OnMessage_ShouldRegisterMessageHandler()
        {
            var builder = WebSocketClientBuilder.Create();

            builder.OnMessage(async (webSocket, messageType, stream, next, ct) =>
                await next(webSocket, messageType, stream, ct));

            Assert.Single(builder.onMessage);
        }

        [Fact]
        public void OnMessage_WithNullHandler_ShouldThrowArgumentNullException()
        {
            var builder = WebSocketClientBuilder.Create();

            Assert.Throws<ArgumentNullException>(() => builder.OnMessage(null!));
        }

        [Fact]
        public void OnTextMessage_ShouldRegisterMessageHandler()
        {
            var builder = WebSocketClientBuilder.Create();

            builder.OnTextMessage((webSocket, message, ct) => new ValueTask());

            Assert.Single(builder.onMessage);
        }

        [Fact]
        public void OnTextMessage_WithNullHandler_ShouldThrowArgumentNullException()
        {
            var builder = WebSocketClientBuilder.Create();

            Assert.Throws<ArgumentNullException>(() => builder.OnTextMessage(null!));
        }

        [Fact]
        public void OnBinaryMessage_ShouldRegisterMessageHandler()
        {
            var builder = WebSocketClientBuilder.Create();

            builder.OnBinaryMessage((webSocket, data, ct) => new ValueTask());

            Assert.Single(builder.onMessage);
        }

        [Fact]
        public void OnBinaryMessage_WithNullHandler_ShouldThrowArgumentNullException()
        {
            var builder = WebSocketClientBuilder.Create();

            Assert.Throws<ArgumentNullException>(() => builder.OnBinaryMessage(null!));
        }

        [Fact]
        public void UseLogging_ShouldSetupLogging()
        {
            using var loggerFactory = LoggerFactory.Create(builder => builder
                .SetMinimumLevel(LogLevel.Warning)
                .AddConsole());

            var builder = WebSocketClientBuilder.Create();

            builder.UseLogging(loggerFactory);

            Assert.NotNull(builder.logger);
            Assert.Single(builder.onConnect); // Logging adds 1 connect handler
            Assert.Single(builder.onClose); // Logging adds 1 close handler  
            Assert.Single(builder.onError); // Logging adds 1 error handler
        }

        [Fact]
        public void UseConfiguration_ShouldConfigureOptions()
        {
            var builder = WebSocketClientBuilder.Create();

            builder.UseConfiguration(options =>
            {
                options.KeepAliveInterval = TimeSpan.FromMinutes(10);
                options.NoDelay = false;
            });

            Assert.Equal(TimeSpan.FromMinutes(10), builder.configuration.KeepAliveInterval);
            Assert.False(builder.configuration.NoDelay);
        }

        [Fact]
        public void UseConfiguration_WithNullAction_ShouldThrowArgumentNullException()
        {
            var builder = WebSocketClientBuilder.Create();

            Assert.Throws<ArgumentNullException>(() => builder.UseConfiguration(null!));
        }

        [Fact]
        public void FluentAPI_ShouldChainMethodCallsAndConfigureCorrectly()
        {
            using var loggerFactory = LoggerFactory.Create(builder => builder
                .SetMinimumLevel(LogLevel.Warning)
                .AddConsole());

            var builder = WebSocketClientBuilder.Create()
                .UseKeepAlive(TimeSpan.FromSeconds(30))
                .UseNoDelay(true)
                .UsePerMessageDeflate()
                .AddHeader("User-Agent", "TestClient/1.0")
                .UseBearerAuthentication("test-token")
                .UseLogging(loggerFactory)
                .OnConnect(async (ws, next, ct) => await next(ws, ct))
                .OnTextMessage((ws, msg, ct) => new ValueTask())
                .OnClose(async (ws, statusDescription, next, ct) => await next(ws, statusDescription, ct));

            Assert.Equal(TimeSpan.FromSeconds(30), builder.configuration.KeepAliveInterval);
            Assert.True(builder.configuration.NoDelay);
            Assert.Equal("permessage-deflate", builder.configuration.SecWebSocketExtensions);
            Assert.Equal("TestClient/1.0", builder.configuration.AdditionalHttpHeaders["User-Agent"]);
            Assert.Equal("Bearer test-token", builder.configuration.AdditionalHttpHeaders["Authorization"]);
            Assert.NotNull(builder.logger);
            Assert.Equal(2, builder.onConnect.Count); // 1 user + 1 logging handler
            Assert.Equal(2, builder.onClose.Count); // 1 user + 1 logging handler
            Assert.Single(builder.onError); // 1 logging handler
            Assert.Single(builder.onMessage); // 1 text message handler

            var client = builder.Build();
            Assert.NotNull(client);
        }

        [Fact]
        public void Build_ShouldTransferConfigurationToClient()
        {
            var builder = WebSocketClientBuilder.Create()
                .UseKeepAlive(TimeSpan.FromMinutes(2))
                .OnConnect(async (ws, next, ct) => await next(ws, ct));

            var client = builder.Build();

            Assert.NotNull(client);
            // Verify that handlers were transferred to configuration
            Assert.Single(builder.configuration.OnConnect!);
        }
    }
}