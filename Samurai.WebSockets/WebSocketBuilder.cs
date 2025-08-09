using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

namespace Samurai.WebSockets
{
    // Stream acceptance delegates
    public delegate ValueTask<Stream> AcceptStreamHandler(TcpClient tcpClient, CancellationToken cancellationToken);
    public delegate ValueTask<Stream> AcceptStreamInterceptor(TcpClient tcpClient, AcceptStreamHandler next, CancellationToken cancellationToken);

    // Certificate selection delegates  
    public delegate ValueTask<X509Certificate2?> CertificateSelectionHandler(TcpClient tcpClient, CancellationToken cancellationToken);
    public delegate ValueTask<X509Certificate2?> CertificateSelectionInterceptor(TcpClient tcpClient, CertificateSelectionHandler next, CancellationToken cancellationToken);

    // Handshake delegates
    public delegate ValueTask<HttpResponse> HandshakeHandler(WebSocketHttpContext context, CancellationToken cancellationToken);
    public delegate ValueTask<HttpResponse> HandshakeInterceptor(WebSocketHttpContext context, HandshakeHandler next, CancellationToken cancellationToken);

    // WebSocket connection delegates
    public delegate ValueTask WebSocketConnectHandler(SamuraiWebSocket webSocket, CancellationToken cancellationToken);
    public delegate ValueTask WebSocketConnectInterceptor(SamuraiWebSocket webSocket, WebSocketConnectHandler next, CancellationToken cancellationToken);

    // WebSocket close delegates  
    public delegate ValueTask WebSocketCloseHandler(SamuraiWebSocket webSocket, CancellationToken cancellationToken);
    public delegate ValueTask WebSocketCloseInterceptor(SamuraiWebSocket webSocket, WebSocketCloseHandler next, CancellationToken cancellationToken);

    // WebSocket error delegates
    public delegate ValueTask WebSocketErrorHandler(SamuraiWebSocket webSocket, Exception exception, CancellationToken cancellationToken);
    public delegate ValueTask WebSocketErrorInterceptor(SamuraiWebSocket webSocket, Exception exception, WebSocketErrorHandler next, CancellationToken cancellationToken);

    // WebSocket message delegates
    public delegate ValueTask WebSocketMessageHandler(SamuraiWebSocket webSocket, MessageType messageType, Stream messageStream, CancellationToken cancellationToken);
    public delegate ValueTask WebSocketMessageInterceptor(SamuraiWebSocket webSocket, MessageType messageType, Stream messageStream, WebSocketMessageHandler next, CancellationToken cancellationToken);

    // Simplified message handlers (for convenience methods)
    public delegate ValueTask WebSocketTextMessageHandler(SamuraiWebSocket webSocket, string message, CancellationToken cancellationToken);
    public delegate ValueTask WebSocketBinaryMessageHandler(SamuraiWebSocket webSocket, byte[] message, CancellationToken cancellationToken);

    // Authentication delegate
    public delegate bool WebSocketAuthenticator(WebSocketHttpContext context);

    public class WebSocketBuilder
    {
        private readonly List<AcceptStreamInterceptor> onAcceptStream = new List<AcceptStreamInterceptor>();
        private readonly List<CertificateSelectionInterceptor> onSelectionCertificate = new List<CertificateSelectionInterceptor>();
        private readonly List<HandshakeInterceptor> onHandshake = new List<HandshakeInterceptor>();
        private readonly List<WebSocketConnectInterceptor> onConnect = new List<WebSocketConnectInterceptor>();
        private readonly List<WebSocketCloseInterceptor> onClose = new List<WebSocketCloseInterceptor>();
        private readonly List<WebSocketErrorInterceptor> onError = new List<WebSocketErrorInterceptor>();
        private readonly List<WebSocketMessageInterceptor> onMessage = new List<WebSocketMessageInterceptor>();
        private ILogger<SamuraiServer>? logger;
        private WebSocketServerOptions configuration = new WebSocketServerOptions();

        /// <summary>
        /// Sets the port for the WebSocket server
        /// </summary>
        /// <param name="port">Port number (default: 8080)</param>
        public WebSocketBuilder UsePort(ushort port)
        {
            this.configuration.Port = port;
            return this;
        }

        /// <summary>
        /// Configures SSL/TLS support with the provided certificate
        /// </summary>
        /// <param name="certificate">X509 certificate for SSL/TLS</param>
        public WebSocketBuilder UseSsl(X509Certificate2? certificate)
        {
            this.onSelectionCertificate.Add((tcpClient, next, cancellationToken) => new ValueTask<X509Certificate2?>(certificate));
            return this;
        }

        /// <summary>
        /// Adds an interceptor for selecting the certificate.
        /// </summary>
        /// <param name="interceptor">Certificate selection interceptor</param>
        public WebSocketBuilder UseSsl(CertificateSelectionInterceptor interceptor)
        {
            this.onSelectionCertificate.Add(interceptor ?? throw new ArgumentNullException(nameof(interceptor)));
            return this;
        }

        /// <summary>
        /// Configures WebSocket server configuration
        /// </summary>
        /// <param name="configureOptions">Action to configure the configuration</param>
        public WebSocketBuilder UseConfiguration(Action<WebSocketServerOptions> configureOptions)
        {
            if (configureOptions == null) throw new ArgumentNullException(nameof(configureOptions));
            configureOptions(this.configuration);
            return this;
        }

        /// <summary>
        /// Sets the keep-alive interval for ping/pong messages
        /// </summary>
        /// <param name="interval">Keep-alive interval (TimeSpan.Zero to disable)</param>
        public WebSocketBuilder UseKeepAlive(TimeSpan interval)
        {
            this.configuration.KeepAliveInterval = interval;
            return this;
        }

        /// <summary>
        /// Configures whether to include full exception details in close responses
        /// </summary>
        /// <param name="includeException">True to include exception details</param>
        public WebSocketBuilder IncludeExceptionInCloseResponse(bool includeException = true)
        {
            this.configuration.IncludeExceptionInCloseResponse = includeException;
            return this;
        }

        /// <summary>
        /// Adds supported sub protocols for WebSocket negotiation
        /// </summary>
        /// <param name="subProtocols">Supported sub protocols</param>
        public WebSocketBuilder UseSupportedSubProtocols(params string[] subProtocols)
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
        public WebSocketBuilder AddSupportedSubProtocol(string subProtocol)
        {
            if (string.IsNullOrWhiteSpace(subProtocol)) throw new ArgumentException("Sub protocol cannot be null or whitespace", nameof(subProtocol));

            if (this.configuration.SupportedSubProtocols == null)
                this.configuration.SupportedSubProtocols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            this.configuration.SupportedSubProtocols.Add(subProtocol);
            return this;
        }

        /// <summary>
        /// Enables or disables per-message deflate compression
        /// </summary>
        /// <param name="allow">True to allow per-message deflate compression</param>
        public WebSocketBuilder UsePerMessageDeflate(bool allow = true)
        {
            this.configuration.AllowPerMessageDeflate = allow;
            return this;
        }

        /// <summary>
        /// Adds an interceptor for stream acceptance (e.g., for SSL/TLS, logging, etc.)
        /// </summary>
        /// <param name="interceptor">Stream acceptance interceptor</param>
        public WebSocketBuilder OnAcceptStream(AcceptStreamInterceptor interceptor)
        {
            this.onAcceptStream.Add(interceptor ?? throw new ArgumentNullException(nameof(interceptor)));
            return this;
        }

        /// <summary>
        /// Adds an interceptor for WebSocket handshake (e.g., for authentication, custom headers, etc.)
        /// </summary>
        /// <param name="interceptor">Handshake interceptor</param>
        public WebSocketBuilder OnHandshake(HandshakeInterceptor interceptor)
        {
            this.onHandshake.Add(interceptor ?? throw new ArgumentNullException(nameof(interceptor)));
            return this;
        }

        /// <summary>
        /// Adds a handler for when a WebSocket connection is established
        /// </summary>
        /// <param name="handler">Connection handler</param>
        public WebSocketBuilder OnConnect(WebSocketConnectInterceptor handler)
        {
            this.onConnect.Add(handler ?? throw new ArgumentNullException(nameof(handler)));
            return this;
        }

        /// <summary>
        /// Adds a handler for when a WebSocket connection is closed
        /// </summary>
        /// <param name="handler">Close handler</param>
        public WebSocketBuilder OnClose(WebSocketCloseInterceptor handler)
        {
            this.onClose.Add(handler ?? throw new ArgumentNullException(nameof(handler)));
            return this;
        }

        /// <summary>
        /// Adds a handler for WebSocket errors
        /// </summary>
        /// <param name="handler">Error handler</param>
        public WebSocketBuilder OnError(WebSocketErrorInterceptor handler)
        {
            this.onError.Add(handler ?? throw new ArgumentNullException(nameof(handler)));
            return this;
        }

        /// <summary>
        /// Adds a handler for incoming WebSocket messages
        /// </summary>
        /// <param name="handler">Message handler</param>
        public WebSocketBuilder OnMessage(WebSocketMessageInterceptor handler)
        {
            this.onMessage.Add(handler ?? throw new ArgumentNullException(nameof(handler)));
            return this;
        }

        /// <summary>
        /// Adds a handler specifically for text messages
        /// </summary>
        /// <param name="handler">Text message handler</param>
        public WebSocketBuilder OnTextMessage(WebSocketTextMessageHandler handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            return this.OnMessage(async (webSocket, messageType, messageStream, next, cancellationToken) =>
            {
                if (messageType == MessageType.Text)
                {
#if NET9_0_OR_GREATER
                    using var reader = new StreamReader(messageStream, leaveOpen: true);
#else
                    var reader = new StreamReader(messageStream);
#endif
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
        public WebSocketBuilder OnBinaryMessage(WebSocketBinaryMessageHandler handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

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
        public WebSocketBuilder UseLogging(ILoggerFactory loggerFactory)
        {
            Internal.Events.Log = new Internal.Events(loggerFactory.CreateLogger<Internal.Events>());
            this.logger = loggerFactory.CreateLogger<SamuraiServer>();

            this.OnAcceptStream((tcpClient, next, cancellationToken) =>
            {
                this.logger.LogInformation("Server: Connection opened.");
                return next(tcpClient, cancellationToken);
            });

            this.OnConnect((webSocket, next, cancellationToken) =>
            {
                this.logger.LogInformation("WebSocket connected: {ConnectionId}", webSocket.Context.Guid);
                return next(webSocket, cancellationToken);
            });

            this.OnClose((webSocket, next, cancellationToken) =>
            {
                this.logger.LogInformation("WebSocket disconnected: {ConnectionId}", webSocket.Context.Guid);
                return next(webSocket, cancellationToken);
            });

            this.OnError((webSocket, exception, next, cancellationToken) =>
            {
                this.logger.LogError(exception, "WebSocket error for connection: {ConnectionId}", webSocket.Context.Guid);
                return next(webSocket, exception, cancellationToken);
            });

            return this;
        }

        /// <summary>
        /// Adds basic authentication to the WebSocket handshake
        /// </summary>
        /// <param name="authenticator">Authentication function that returns true if authentication succeeds</param>
        public WebSocketBuilder UseAuthentication(WebSocketAuthenticator authenticator)
        {
            if (authenticator == null) throw new ArgumentNullException(nameof(authenticator));

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
        public WebSocketBuilder UseCors(params string[] allowedOrigins)
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
        private WebSocketBuilder AddSubProtocolNegotiation()
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
        /// Builds and returns the configured SamuraiServer instance
        /// </summary>
        /// <returns>Configured SamuraiServer</returns>
        public SamuraiServer Build()
        {
            // Add sub protocol negotiation if we have supported protocols
            if (this.configuration.SupportedSubProtocols != null && this.configuration.SupportedSubProtocols.Count > 0)
            {
                this.AddSubProtocolNegotiation();
            }

            // Convert explicit delegates back to generic delegates for the configuration
            this.configuration.OnAcceptStream = this.onAcceptStream.Count > 0 ?
                this.onAcceptStream.Cast<Next<TcpClient, Stream>>().ToList() : null;

            this.configuration.OnSelectionCertificate = this.onSelectionCertificate.Count > 0 ?
                this.onSelectionCertificate.Cast<Next<TcpClient, X509Certificate2?>>().ToList() : null;

            this.configuration.OnHandshake = this.onHandshake.Count > 0 ?
                this.onHandshake.Cast<Next<WebSocketHttpContext, HttpResponse>>().ToList() : null;

            this.configuration.OnConnect = this.onConnect.Count > 0 ?
                this.onConnect.Cast<On<SamuraiWebSocket>>().ToList() : null;

            this.configuration.OnClose = this.onClose.Count > 0 ?
                this.onClose.Cast<On<SamuraiWebSocket>>().ToList() : null;

            this.configuration.OnError = this.onError.Count > 0 ?
                this.onError.Cast<On<SamuraiWebSocket, Exception>>().ToList() : null;

            this.configuration.OnMessage = this.onMessage.Count > 0 ?
                this.onMessage.Cast<On<SamuraiWebSocket, MessageType, Stream>>().ToList() : null;

            return new SamuraiServer(this.configuration, this.logger);
        }

        /// <summary>
        /// Creates a new WebSocketBuilder instance
        /// </summary>
        /// <returns>New WebSocketBuilder</returns>
        public static WebSocketBuilder Create()
        {
            return new WebSocketBuilder();
        }
    }
}