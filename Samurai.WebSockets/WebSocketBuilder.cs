using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

namespace Samurai.WebSockets
{
    public class WebSocketBuilder
    {
        private readonly List<Next<TcpClient, Stream>> onAcceptStream = new List<Next<TcpClient, Stream>>();
        private readonly List<Next<WebSocketHttpContext, HttpResponse>> onHandshake = new List<Next<WebSocketHttpContext, HttpResponse>>();
        private readonly List<On<SamuraiWebSocket>> onConnect = new List<On<SamuraiWebSocket>>();
        private readonly List<On<SamuraiWebSocket>> onClose = new List<On<SamuraiWebSocket>>();
        private readonly List<On<SamuraiWebSocket, Exception>> onError = new List<On<SamuraiWebSocket, Exception>>();
        private readonly List<On<SamuraiWebSocket, MessageType, Stream>> onMessage = new List<On<SamuraiWebSocket, MessageType, Stream>>();

        private ILogger<SamuraiServer>? logger;
        private X509Certificate2? certificate;
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
        public WebSocketBuilder UseSsl(X509Certificate2 certificate)
        {
            this.certificate = certificate ?? throw new ArgumentNullException(nameof(certificate));
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
        public WebSocketBuilder OnAcceptStream(Next<TcpClient, Stream> interceptor)
        {
            this.onAcceptStream.Add(interceptor ?? throw new ArgumentNullException(nameof(interceptor)));
            return this;
        }

        /// <summary>
        /// Adds an interceptor for WebSocket handshake (e.g., for authentication, custom headers, etc.)
        /// </summary>
        /// <param name="interceptor">Handshake interceptor</param>
        public WebSocketBuilder OnHandshake(Next<WebSocketHttpContext, HttpResponse> interceptor)
        {
            this.onHandshake.Add(interceptor ?? throw new ArgumentNullException(nameof(interceptor)));
            return this;
        }

        /// <summary>
        /// Adds a handler for when a WebSocket connection is established
        /// </summary>
        /// <param name="handler">Connection handler</param>
        public WebSocketBuilder OnConnect(On<SamuraiWebSocket> handler)
        {
            this.onConnect.Add(handler ?? throw new ArgumentNullException(nameof(handler)));
            return this;
        }

        /// <summary>
        /// Adds a handler for when a WebSocket connection is closed
        /// </summary>
        /// <param name="handler">Close handler</param>
        public WebSocketBuilder OnClose(On<SamuraiWebSocket> handler)
        {
            this.onClose.Add(handler ?? throw new ArgumentNullException(nameof(handler)));
            return this;
        }

        /// <summary>
        /// Adds a handler for WebSocket errors
        /// </summary>
        /// <param name="handler">Error handler</param>
        public WebSocketBuilder OnError(On<SamuraiWebSocket, Exception> handler)
        {
            this.onError.Add(handler ?? throw new ArgumentNullException(nameof(handler)));
            return this;
        }

        /// <summary>
        /// Adds a handler for incoming WebSocket messages
        /// </summary>
        /// <param name="handler">Message handler</param>
        public WebSocketBuilder OnMessage(On<SamuraiWebSocket, MessageType, Stream> handler)
        {
            this.onMessage.Add(handler ?? throw new ArgumentNullException(nameof(handler)));
            return this;
        }

        /// <summary>
        /// Adds a handler specifically for text messages
        /// </summary>
        /// <param name="handler">Text message handler</param>
        public WebSocketBuilder OnTextMessage(Func<SamuraiWebSocket, string, CancellationToken, ValueTask> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            return this.OnMessage(async (webSocket, messageType, stream, next, cancellationToken) =>
            {
                if (messageType == MessageType.Text)
                {
#if NET9_0_OR_GREATER
                    using var reader = new StreamReader(stream, leaveOpen: true);
#else
                    var reader = new StreamReader(stream);
#endif
                    var message = await reader.ReadToEndAsync();
                    await handler(webSocket, message, cancellationToken);
                    return;
                }

                await next(webSocket, messageType, stream, cancellationToken);
            });
        }

        /// <summary>
        /// Adds a handler specifically for binary messages
        /// </summary>
        /// <param name="handler">Binary message handler</param>
        public WebSocketBuilder OnBinaryMessage(Func<SamuraiWebSocket, byte[], CancellationToken, ValueTask> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            return this.OnMessage(async (webSocket, messageType, stream, next, cancellationToken) =>
            {
                if (messageType == MessageType.Binary)
                {
                    using (var memoryStream = new MemoryStream())
                    {
                        stream.CopyTo(memoryStream);
                        await handler(webSocket, memoryStream.ToArray(), cancellationToken);
                        return;
                    }
                }

                await next(webSocket, messageType, stream, cancellationToken);
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

            this.OnAcceptStream((client, next, cancellationToken) =>
            {
                this.logger.LogInformation("Server: Connection opened.");
                return next(client, cancellationToken);
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
        public WebSocketBuilder UseAuthentication(Func<WebSocketHttpContext, bool> authenticator)
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

            // Set the interceptors from our collected handlers
            this.configuration.OnAcceptStream = this.onAcceptStream.Count > 0 ? this.onAcceptStream : null;
            this.configuration.OnHandshake = this.onHandshake.Count > 0 ? this.onHandshake : null;
            this.configuration.OnConnect = this.onConnect.Count > 0 ? this.onConnect : null;
            this.configuration.OnClose = this.onClose.Count > 0 ? this.onClose : null;
            this.configuration.OnError = this.onError.Count > 0 ? this.onError : null;
            this.configuration.OnMessage = this.onMessage.Count > 0 ? this.onMessage : null;

            var server = new SamuraiServer(this.configuration, this.logger);

            if (this.certificate != null)
            {
                server.Certificate = this.certificate;
            }

            return server;
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
