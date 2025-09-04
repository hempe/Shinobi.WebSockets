using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Shinobi.WebSockets.Http;
using Shinobi.WebSockets.Internal;

namespace Shinobi.WebSockets.Builders
{

    public class WebSocketServerBuilder
    {
        private readonly List<AcceptStreamInterceptor> onAcceptStream = new();
        private readonly List<CertificateSelectionInterceptor> onSelectionCertificate = new();
        private readonly List<HandshakeInterceptor> onHandshake = new();
        private readonly List<WebSocketConnectInterceptor> onConnect = new();
        private readonly List<WebSocketConnectInterceptor> onConnected = new();
        private readonly List<WebSocketCloseInterceptor> onClose = new();
        private readonly List<WebSocketErrorInterceptor> onError = new();
        private readonly List<WebSocketMessageInterceptor> onMessage = new();
        private ILoggerFactory? loggerFactory;
        private WebSocketServerOptions configuration = new();

        /// <summary>
        /// Sets the port for the WebSocket server
        /// </summary>
        /// <param name="port">Port number (default: 8080)</param>
        public WebSocketServerBuilder UsePort(ushort port)
        {
            this.configuration.Port = port;
            return this;
        }

        /// <summary>
        /// Configures SSL/TLS support with the provided certificate
        /// </summary>
        /// <param name="certificate">X509 certificate for SSL/TLS</param>
        public WebSocketServerBuilder UseSsl(X509Certificate2? certificate)
        {
            this.onSelectionCertificate.Add((tcpClient, next, cancellationToken) => new ValueTask<X509Certificate2?>(certificate));
            return this;
        }

        /// <summary>
        /// Adds an interceptor for selecting the certificate.
        /// </summary>
        /// <param name="interceptor">Certificate selection interceptor</param>
        public WebSocketServerBuilder UseSsl(CertificateSelectionInterceptor interceptor)
        {
            this.onSelectionCertificate.Add(interceptor ?? throw new ArgumentNullException(nameof(interceptor)));
            return this;
        }

        /// <summary>
        /// Configures WebSocket server configuration
        /// </summary>
        /// <param name="configureOptions">Action to configure the configuration</param>
        public WebSocketServerBuilder UseConfiguration(Action<WebSocketServerOptions> configureOptions)
        {
            if (configureOptions == null)
                throw new ArgumentNullException(nameof(configureOptions));
            configureOptions(this.configuration);
            return this;
        }

        /// <summary>
        /// Sets the keep-alive interval for ping/pong messages
        /// </summary>
        /// <param name="interval">Keep-alive interval (TimeSpan.Zero to disable)</param>
        public WebSocketServerBuilder UseKeepAlive(TimeSpan interval)
        {
            this.configuration.KeepAliveInterval = interval;
            return this;
        }

        /// <summary>
        /// Sets the timeout for detecting idle HTTP keep-alive connections (first-byte timeout only)
        /// </summary>
        /// <param name="timeout">Keep-alive idle detection timeout (TimeSpan.Zero to disable)</param>
        public WebSocketServerBuilder UseKeepAliveTimeout(TimeSpan timeout)
        {
            this.configuration.KeepAliveTimeout = timeout;
            return this;
        }

        /// <summary>
        /// Sets the maximum number of concurrent HTTP keep-alive connections
        /// </summary>
        /// <param name="maxConnections">Maximum connections (0 for unlimited)</param>
        public WebSocketServerBuilder UseMaxKeepAliveConnections(int maxConnections)
        {
            this.configuration.MaxKeepAliveConnections = maxConnections;
            return this;
        }

        /// <summary>
        /// Configures whether to include full exception details in close responses
        /// </summary>
        /// <param name="includeException">True to include exception details</param>
        public WebSocketServerBuilder IncludeExceptionInCloseResponse(bool includeException = true)
        {
            this.configuration.IncludeExceptionInCloseResponse = includeException;
            return this;
        }

        /// <summary>
        /// Adds supported sub protocols for WebSocket negotiation
        /// </summary>
        /// <param name="subProtocols">Supported sub protocols</param>
        public WebSocketServerBuilder UseSupportedSubProtocols(params string[] subProtocols)
        {
            if (subProtocols == null || subProtocols.Length == 0)
            {
                this.configuration.SupportedSubProtocols = null;
                return this;
            }

            this.configuration.SupportedSubProtocols = new HashSet<string>(subProtocols, StringComparer.OrdinalIgnoreCase);
            return this;
        }

        /// <summary>
        /// Adds a single supported sub protocol
        /// </summary>
        /// <param name="subProtocol">Sub protocol to add</param>
        public WebSocketServerBuilder AddSupportedSubProtocol(string subProtocol)
        {
            if (string.IsNullOrWhiteSpace(subProtocol))
                throw new ArgumentException("Sub protocol cannot be null or whitespace", nameof(subProtocol));

            if (this.configuration.SupportedSubProtocols == null)
                this.configuration.SupportedSubProtocols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            this.configuration.SupportedSubProtocols.Add(subProtocol);
            return this;
        }

#if NET8_0_OR_GREATER
        /// <summary>
        /// Enables per-message deflate compression with default settings
        /// </summary>
        public WebSocketServerBuilder UsePerMessageDeflate()
        {
            this.configuration.PerMessageDeflate.Enabled = true;
            return this;
        }

        /// <summary>
        /// Configures per-message deflate compression
        /// </summary>
        /// <param name="configure">Action to configure per-message deflate options</param>
        public WebSocketServerBuilder UsePerMessageDeflate(Action<PerMessageDeflateOptions> configure)
        {
            this.configuration.PerMessageDeflate.Enabled = true;
            configure(this.configuration.PerMessageDeflate);
            return this;
        }
#endif
        /// <summary>
        /// Adds an interceptor for stream acceptance (e.g., for SSL/TLS, logging, etc.)
        /// </summary>
        /// <param name="interceptor">Stream acceptance interceptor</param>
        public WebSocketServerBuilder OnAcceptStream(AcceptStreamInterceptor interceptor)
        {
            this.onAcceptStream.Add(interceptor ?? throw new ArgumentNullException(nameof(interceptor)));
            return this;
        }

        /// <summary>
        /// Adds an interceptor for WebSocket handshake (e.g., for authentication, custom headers, etc.)
        /// </summary>
        /// <param name="interceptor">Handshake interceptor</param>
        public WebSocketServerBuilder OnHandshake(HandshakeInterceptor interceptor)
        {
            this.onHandshake.Add(interceptor ?? throw new ArgumentNullException(nameof(interceptor)));
            return this;
        }

        /// <summary>
        /// Adds a handler for when a WebSocket connection is established
        /// </summary>
        /// <param name="handler">Connection handler</param>
        public WebSocketServerBuilder OnConnect(WebSocketConnectInterceptor handler)
        {
            this.onConnect.Add(handler ?? throw new ArgumentNullException(nameof(handler)));
            return this;
        }

        /// <summary>
        /// Adds a handler for after a WebSocket connection is established
        /// </summary>
        /// <param name="handler">Connection handler</param>
        public WebSocketServerBuilder OnConnected(WebSocketConnectInterceptor handler)
        {
            this.onConnected.Add(handler ?? throw new ArgumentNullException(nameof(handler)));
            return this;
        }

        /// <summary>
        /// Adds a handler for when a WebSocket connection is closed
        /// </summary>
        /// <param name="handler">Close handler</param>
        public WebSocketServerBuilder OnClose(WebSocketCloseInterceptor handler)
        {
            this.onClose.Add(handler ?? throw new ArgumentNullException(nameof(handler)));
            return this;
        }

        /// <summary>
        /// Adds a handler for WebSocket errors
        /// </summary>
        /// <param name="handler">Error handler</param>
        public WebSocketServerBuilder OnError(WebSocketErrorInterceptor handler)
        {
            this.onError.Add(handler ?? throw new ArgumentNullException(nameof(handler)));
            return this;
        }

        /// <summary>
        /// Adds a handler for incoming WebSocket messages
        /// </summary>
        /// <param name="handler">Message handler</param>
        public WebSocketServerBuilder OnMessage(WebSocketMessageInterceptor handler)
        {
            this.onMessage.Add(handler ?? throw new ArgumentNullException(nameof(handler)));
            return this;
        }

        /// <summary>
        /// Adds a handler specifically for text messages
        /// </summary>
        /// <param name="handler">Text message handler</param>
        public WebSocketServerBuilder OnTextMessage(WebSocketTextMessageHandler handler)
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
        public WebSocketServerBuilder OnBinaryMessage(WebSocketBinaryMessageHandler handler)
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
        public WebSocketServerBuilder UseLogging(ILoggerFactory loggerFactory)
        {
            this.loggerFactory = loggerFactory;
            var logger = loggerFactory.CreateLogger<WebSocketServer>();

            this.OnAcceptStream((tcpClient, next, cancellationToken) =>
            {
                logger.ServerConnectionOpened();
                return next(tcpClient, cancellationToken);
            });

            this.OnConnect((webSocket, next, cancellationToken) =>
            {
                logger.WebSocketConnected(webSocket.Context.Guid);
                return next(webSocket, cancellationToken);
            });

            this.OnClose((webSocket, closeStatus, statusDescription, next, cancellationToken) =>
            {
                logger.WebSocketDisconnected(webSocket.Context.Guid, closeStatus, statusDescription);
                return next(webSocket, closeStatus, statusDescription, cancellationToken);
            });

            this.OnError((webSocket, exception, next, cancellationToken) =>
            {
                logger.WebSocketError(webSocket.Context.Guid, exception);
                return next(webSocket, exception, cancellationToken);
            });

            return this;
        }

        /// <summary>
        /// Adds basic authentication to the WebSocket handshake
        /// </summary>
        /// <param name="authenticator">Authentication function that returns true if authentication succeeds</param>
        public WebSocketServerBuilder UseAuthentication(WebSocketAuthenticator authenticator)
        {
            if (authenticator == null)
                throw new ArgumentNullException(nameof(authenticator));

            return this.OnHandshake(async (context, next, cancellationToken) =>
            {
                if (!authenticator(context))
                {
                    return HttpResponse.Create(401)
                        .AddHeader("Content-Type", "text/plain")
                        .WithBody("Unauthorized");
                }

                return await next(context, cancellationToken);
            });
        }

        /// <summary>
        /// Adds CORS headers to the handshake response
        /// </summary>
        /// <param name="allowedOrigins">Allowed origins (use "*" for all origins)</param>
        public WebSocketServerBuilder UseCors(params string[] allowedOrigins)
        {
            if (allowedOrigins == null || allowedOrigins.Length == 0)
                allowedOrigins = new[] { "*" };

            var allowedOriginsValue = string.Join(", ", allowedOrigins);

            return this.OnHandshake(async (context, next, cancellationToken) =>
            {
                var response = await next(context, cancellationToken);

                if (response.StatusCode == 101) // Only add CORS headers for successful WebSocket upgrades
                {
                    response.AddHeader("Access-Control-Allow-Origin", allowedOriginsValue)
                           .AddHeader("Access-Control-Allow-Credentials", "true");
                }

                return response;
            });
        }

        /// <summary>
        /// Adds automatic sub protocol negotiation based on supported protocols
        /// </summary>
        /// <returns></returns>
        private WebSocketServerBuilder AddSubProtocolNegotiation()
        {
            if (this.configuration.SupportedSubProtocols == null || this.configuration.SupportedSubProtocols.Count == 0)
                return this;

            return this.OnHandshake(async (context, next, cancellationToken) =>
            {
                var response = await next(context, cancellationToken);

                if (response.StatusCode == 101) // Only negotiate for successful WebSocket upgrades
                {
                    // Get client's requested sub protocols from the handshake
                    var clientProtocols = context.HttpRequest!.GetHeaderValues("Sec-WebSocket-Protocol");
                    if (clientProtocols != null && clientProtocols.Any())
                    {
                        // Parse all requested protocols (they might be comma-separated in a single header)
                        var requestedProtocols = clientProtocols
                            .SelectMany(header => header.Split(','))
                            .Select(p => p.Trim())
                            .Where(p => !string.IsNullOrEmpty(p))
                            .ToList();

                        // Find the first client-requested protocol that we support
                        var selectedProtocol = requestedProtocols.FirstOrDefault(p => this.configuration.SupportedSubProtocols.Contains(p));

                        if (!string.IsNullOrEmpty(selectedProtocol))
                        {
                            response.AddHeader("Sec-WebSocket-Protocol", selectedProtocol);
                        }
                    }
                }

                return response;
            });
        }

        /// <summary>
        /// Builds and returns the configured WebSocketServer instance
        /// </summary>
        /// <returns>Configured WebSocketServer</returns>
        public WebSocketServer Build()
        {
            // Add sub protocol negotiation if we have supported protocols
            if (this.configuration.SupportedSubProtocols != null && this.configuration.SupportedSubProtocols.Count > 0)
            {
                this.AddSubProtocolNegotiation();
            }

            // Convert explicit delegates back to generic delegates for the configuration
            this.configuration.OnAcceptStream = this.onAcceptStream.Count > 0 ? this.onAcceptStream : null;
            this.configuration.OnSelectionCertificate = this.onSelectionCertificate.Count > 0 ? this.onSelectionCertificate : null;
            this.configuration.OnHandshake = this.onHandshake.Count > 0 ? this.onHandshake : null;
            this.configuration.OnConnect = this.onConnect.Count > 0 ? this.onConnect : null;
            this.configuration.OnConnected = this.onConnected.Count > 0 ? this.onConnected : null;
            this.configuration.OnClose = this.onClose.Count > 0 ? this.onClose : null;
            this.configuration.OnError = this.onError.Count > 0 ? this.onError : null;
            this.configuration.OnMessage = this.onMessage.Count > 0 ? this.onMessage : null;

            return new WebSocketServer(this.configuration, this.loggerFactory);
        }

        /// <summary>
        /// Creates a new WebSocketBuilder instance
        /// </summary>
        /// <returns>New WebSocketBuilder</returns>
        public static WebSocketServerBuilder Create()
        {
            return new WebSocketServerBuilder();
        }
    }
}
