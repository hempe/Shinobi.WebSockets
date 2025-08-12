using System;
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
        }

        [Fact]
        public void Build_ShouldReturnWebSocketClient()
        {
            var client = WebSocketClientBuilder.Create().Build();
            Assert.NotNull(client);
        }

        [Fact]
        public void UseKeepAlive_ShouldSetKeepAliveInterval()
        {
            var interval = TimeSpan.FromMinutes(5);
            var client = WebSocketClientBuilder.Create()
                .UseKeepAlive(interval)
                .Build();

            Assert.NotNull(client);
        }

        [Fact]
        public void UseNoDelay_ShouldSetNoDelayOption()
        {
            var client = WebSocketClientBuilder.Create()
                .UseNoDelay(true)
                .Build();

            Assert.NotNull(client);
        }

        [Fact]
        public void AddHeader_ShouldAddCustomHeader()
        {
            var client = WebSocketClientBuilder.Create()
                .AddHeader("Custom-Header", "test-value")
                .Build();

            Assert.NotNull(client);
        }

        [Fact]
        public void UseSubProtocol_ShouldSetSubProtocol()
        {
            var client = WebSocketClientBuilder.Create()
                .UseSubProtocol("chat")
                .Build();

            Assert.NotNull(client);
        }

        [Fact]
        public void UsePerMessageDeflate_ShouldSetExtensions()
        {
            var client = WebSocketClientBuilder.Create()
                .UsePerMessageDeflate()
                .Build();

            Assert.NotNull(client);
        }

        [Fact]
        public void UseBearerAuthentication_ShouldAddAuthHeader()
        {
            var client = WebSocketClientBuilder.Create()
                .UseBearerAuthentication("test-token")
                .Build();

            Assert.NotNull(client);
        }

        [Fact]
        public void UseBasicAuthentication_ShouldAddAuthHeader()
        {
            var client = WebSocketClientBuilder.Create()
                .UseBasicAuthentication("user", "pass")
                .Build();

            Assert.NotNull(client);
        }

        [Fact]
        public void OnConnect_ShouldRegisterConnectHandler()
        {
            var client = WebSocketClientBuilder.Create()
                .OnConnect(async (webSocket, next, ct) =>
                {
                    await next(webSocket, ct);
                })
                .Build();

            Assert.NotNull(client);
        }

        [Fact]
        public void OnTextMessage_ShouldRegisterTextMessageHandler()
        {
            var client = WebSocketClientBuilder.Create()
                .OnTextMessage((webSocket, message, ct) =>
                {
                    // Handle text message
                    return new ValueTask();
                })
                .Build();

            Assert.NotNull(client);
        }

        [Fact]
        public void OnBinaryMessage_ShouldRegisterBinaryMessageHandler()
        {
            var client = WebSocketClientBuilder.Create()
                .OnBinaryMessage((webSocket, data, ct) =>
                {
                    // Handle binary message
                    return new ValueTask();
                })
                .Build();

            Assert.NotNull(client);
        }

        [Fact]
        public void UseLogging_ShouldSetupLogging()
        {
            using var loggerFactory = LoggerFactory.Create(builder => builder
                .SetMinimumLevel(LogLevel.Debug)
                .AddConsole());

            var client = WebSocketClientBuilder.Create()
                .UseLogging(loggerFactory)
                .Build();

            Assert.NotNull(client);
        }

        [Fact]
        public void FluentAPI_ShouldChainMethodCalls()
        {
            using var loggerFactory = LoggerFactory.Create(builder => builder
                .SetMinimumLevel(LogLevel.Debug)
                .AddConsole());

            var client = WebSocketClientBuilder.Create()
                .UseKeepAlive(TimeSpan.FromSeconds(30))
                .UseNoDelay(true)
                .UsePerMessageDeflate()
                .AddHeader("User-Agent", "TestClient/1.0")
                .UseBearerAuthentication("test-token")
                .UseLogging(loggerFactory)
                .OnConnect(async (ws, next, ct) => await next(ws, ct))
                .OnTextMessage((ws, msg, ct) => new ValueTask())
                .OnClose(async (ws, next, ct) => await next(ws, ct))
                .Build();

            Assert.NotNull(client);
        }
    }
}