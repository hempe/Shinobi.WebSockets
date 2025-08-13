using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Microsoft.Extensions.Logging;

namespace Shinobi.WebSockets.Builders
{
    /// <summary>
    /// A fluent builder for configuring and creating WebSocket clients with various options
    /// including message handlers, logging, keep-alive settings, and HTTP headers.
    /// </summary>
    public class WebSocketClientBuilder
    {
        internal readonly List<WebSocketConnectInterceptor> onConnect = new List<WebSocketConnectInterceptor>();
        internal readonly List<WebSocketCloseInterceptor> onClose = new List<WebSocketCloseInterceptor>();
        internal readonly List<WebSocketErrorInterceptor> onError = new List<WebSocketErrorInterceptor>();
        internal readonly List<WebSocketMessageInterceptor> onMessage = new List<WebSocketMessageInterceptor>();
        internal ILogger<WebSocketClient>? logger;
        internal WebSocketClientOptions configuration = new WebSocketClientOptions();

        /// <summary>
        /// Sets the keep-alive interval for ping/pong messages
        /// </summary>
        /// <param name="interval">Keep-alive interval (TimeSpan.Zero to disable)</param>
        public WebSocketClientBuilder UseKeepAlive(TimeSpan interval)
        {
            this.configuration.KeepAliveInterval = interval;
            return this;
        }

        /// <summary>
        /// Configures whether to include full exception details in close responses
        /// </summary>
        /// <param name="includeException">True to include exception details</param>
        public WebSocketClientBuilder IncludeExceptionInCloseResponse(bool includeException = true)
        {
            this.configuration.IncludeExceptionInCloseResponse = includeException;
            return this;
        }

        /// <summary>
        /// Sets the TCP NoDelay option for the underlying connection
        /// </summary>
        /// <param name="noDelay">True to send messages immediately with lowest latency (typical for chat)</param>
        public WebSocketClientBuilder UseNoDelay(bool noDelay = true)
        {
            this.configuration.NoDelay = noDelay;
            return this;
        }

        /// <summary>
        /// Adds additional HTTP headers to the WebSocket handshake request
        /// </summary>
        /// <param name="headers">Dictionary of header name/value pairs</param>
        public WebSocketClientBuilder UseHeaders(Dictionary<string, string> headers)
        {
            if (headers != null)
            {
                foreach (var header in headers)
                {
                    this.configuration.AdditionalHttpHeaders[header.Key] = header.Value;
                }
            }
            return this;
        }

        /// <summary>
        /// Adds a single HTTP header to the WebSocket handshake request
        /// </summary>
        /// <param name="name">Header name</param>
        /// <param name="value">Header value</param>
        public WebSocketClientBuilder AddHeader(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Header name cannot be null or whitespace", nameof(name));
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            this.configuration.AdditionalHttpHeaders[name] = value;
            return this;
        }

        /// <summary>
        /// Sets the sub protocol to request during handshake
        /// </summary>
        /// <param name="subProtocol">The sub protocol name</param>
        public WebSocketClientBuilder UseSubProtocol(string subProtocol)
        {
            if (string.IsNullOrWhiteSpace(subProtocol))
                throw new ArgumentException("Sub protocol cannot be null or whitespace", nameof(subProtocol));

            this.configuration.SecWebSocketProtocol = subProtocol;
            return this;
        }

        /// <summary>
        /// Sets the WebSocket extensions to request during handshake
        /// </summary>
        /// <param name="extensions">The extensions string (e.g., "permessage-deflate")</param>
        public WebSocketClientBuilder UseExtensions(string extensions)
        {
            this.configuration.SecWebSocketExtensions = extensions;
            return this;
        }

        /// <summary>
        /// Enables per-message deflate compression
        /// </summary>
        public WebSocketClientBuilder UsePerMessageDeflate()
        {
            this.configuration.SecWebSocketExtensions = "permessage-deflate";
            return this;
        }

        /// <summary>
        /// Configures WebSocket client options
        /// </summary>
        /// <param name="configureOptions">Action to configure the options</param>
        public WebSocketClientBuilder UseConfiguration(Action<WebSocketClientOptions> configureOptions)
        {
            if (configureOptions == null)
                throw new ArgumentNullException(nameof(configureOptions));
            configureOptions(this.configuration);
            return this;
        }

        /// <summary>
        /// Adds a handler for when a WebSocket connection is established
        /// </summary>
        /// <param name="handler">Connection handler</param>
        public WebSocketClientBuilder OnConnect(WebSocketConnectInterceptor handler)
        {
            this.onConnect.Add(handler ?? throw new ArgumentNullException(nameof(handler)));
            return this;
        }

        /// <summary>
        /// Adds a handler for when a WebSocket connection is closed
        /// </summary>
        /// <param name="handler">Close handler</param>
        public WebSocketClientBuilder OnClose(WebSocketCloseInterceptor handler)
        {
            this.onClose.Add(handler ?? throw new ArgumentNullException(nameof(handler)));
            return this;
        }

        /// <summary>
        /// Adds a handler for WebSocket errors
        /// </summary>
        /// <param name="handler">Error handler</param>
        public WebSocketClientBuilder OnError(WebSocketErrorInterceptor handler)
        {
            this.onError.Add(handler ?? throw new ArgumentNullException(nameof(handler)));
            return this;
        }

        /// <summary>
        /// Adds a handler for incoming WebSocket messages
        /// </summary>
        /// <param name="handler">Message handler</param>
        public WebSocketClientBuilder OnMessage(WebSocketMessageInterceptor handler)
        {
            this.onMessage.Add(handler ?? throw new ArgumentNullException(nameof(handler)));
            return this;
        }

        /// <summary>
        /// Adds a handler specifically for text messages
        /// </summary>
        /// <param name="handler">Text message handler</param>
        public WebSocketClientBuilder OnTextMessage(WebSocketTextMessageHandler handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            return this.OnMessage(async (webSocket, messageType, messageStream, next, cancellationToken) =>
            {
                if (messageType == MessageType.Text)
                {
                    var reader = new StreamReader(messageStream);
                    var message = await reader.ReadToEndAsync();
                    await handler(webSocket, message, cancellationToken);
                    return;
                }

                await next(webSocket, messageType, messageStream, cancellationToken);
            });
        }

        /// <summary>
        /// Adds a handler specifically for binary messages
        /// </summary>
        /// <param name="handler">Binary message handler</param>
        public WebSocketClientBuilder OnBinaryMessage(WebSocketBinaryMessageHandler handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            return this.OnMessage(async (webSocket, messageType, messageStream, next, cancellationToken) =>
            {
                if (messageType == MessageType.Binary)
                {
                    using (var memoryStream = new MemoryStream())
                    {
                        messageStream.CopyTo(memoryStream);
                        await handler(webSocket, memoryStream.ToArray(), cancellationToken);
                        return;
                    }
                }

                await next(webSocket, messageType, messageStream, cancellationToken);
            });
        }

        /// <summary>
        /// Adds logging.
        /// </summary>
        /// <param name="loggerFactory">The logger factory.</param>
        /// <returns></returns>
        public WebSocketClientBuilder UseLogging(ILoggerFactory loggerFactory)
        {
            Internal.Events.Log = new Internal.Events(loggerFactory.CreateLogger<Internal.Events>());
            this.logger = loggerFactory.CreateLogger<WebSocketClient>();

            this.OnConnect((webSocket, next, cancellationToken) =>
            {
                this.logger.LogInformation("WebSocket client connected: {ConnectionId}", webSocket.Context.Guid);
                return next(webSocket, cancellationToken);
            });

            this.OnClose((webSocket, next, cancellationToken) =>
            {
                this.logger.LogInformation("WebSocket client disconnected: {ConnectionId}", webSocket.Context.Guid);
                return next(webSocket, cancellationToken);
            });

            this.OnError((webSocket, exception, next, cancellationToken) =>
            {
                this.logger.LogError(exception, "WebSocket client error for connection: {ConnectionId}", webSocket.Context.Guid);
                return next(webSocket, exception, cancellationToken);
            });

            return this;
        }

        /// <summary>
        /// Adds basic authentication to the WebSocket handshake (using Authorization header)
        /// </summary>
        /// <param name="token">Bearer token for authentication</param>
        public WebSocketClientBuilder UseBearerAuthentication(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                throw new ArgumentException("Token cannot be null or whitespace", nameof(token));

            return this.AddHeader("Authorization", $"Bearer {token}");
        }

        /// <summary>
        /// Adds basic authentication to the WebSocket handshake (using Authorization header)
        /// </summary>
        /// <param name="username">Username</param>
        /// <param name="password">Password</param>
        public WebSocketClientBuilder UseBasicAuthentication(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentException("Username cannot be null or whitespace", nameof(username));
            if (password == null)
                throw new ArgumentNullException(nameof(password));

            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            return this.AddHeader("Authorization", $"Basic {credentials}");
        }

        /// <summary>
        /// Builds and returns the configured WebSocketClient instance
        /// </summary>
        /// <returns>Configured WebSocketClient</returns>
        public WebSocketClient Build()
        {
            // Convert explicit delegates back to generic delegates for the configuration
            this.configuration.OnConnect = this.onConnect.Count > 0 ? this.onConnect : null;
            this.configuration.OnClose = this.onClose.Count > 0 ? this.onClose : null;
            this.configuration.OnError = this.onError.Count > 0 ? this.onError : null;
            this.configuration.OnMessage = this.onMessage.Count > 0 ? this.onMessage : null;

            return new WebSocketClient(this.configuration, this.logger);
        }

        /// <summary>
        /// Creates a new WebSocketClientBuilder instance
        /// </summary>
        /// <returns>New WebSocketClientBuilder</returns>
        public static WebSocketClientBuilder Create()
        {
            return new WebSocketClientBuilder();
        }
    }
}
