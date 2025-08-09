using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;


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

    /// <summary>
    /// Comprehensive WebSocket server configuration including interceptors and options
    /// </summary>
    public class WebSocketServerOptions
    {
        // Server Configuration
        /// <summary>
        /// The port number for the WebSocket server (default: 8080)
        /// </summary>
        public ushort Port { get; set; }

        // WebSocket Options
        /// <summary>
        /// How often to send ping requests to the Client
        /// The default is 60 seconds
        /// This is done to prevent proxy servers from closing your connection
        /// A timespan of zero will disable the automatic ping pong mechanism
        /// You can manually control ping pong messages using the PingPongManager class.
        /// </summary>
        public TimeSpan KeepAliveInterval { get; set; }

        /// <summary>
        /// Include the full exception (with stack trace) in the close response 
        /// when an exception is encountered and the WebSocket connection is closed
        /// The default is false
        /// </summary>
        public bool IncludeExceptionInCloseResponse { get; set; }

        /// <summary>
        /// Specifies the supported sub protocols for WebSocket handshake negotiation.
        /// The server will intersect this list with the client's requested protocols
        /// and select the first matching protocol from the client's preference order.
        /// Can be null or empty if no sub protocols are supported.
        /// </summary>
        public HashSet<string>? SupportedSubProtocols { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether per-message deflate compression is allowed.
        /// When enabled, this option allows the WebSocket server to negotiate and use per-message
        /// deflate compression for WebSocket messages, which can reduce bandwidth usage.
        /// </summary>
        /// <remark>
        /// This is an experimental feature, and in general per message deflate comes with a high cpu and memory usage cost.
        /// Apply only after verifying its useful for your usecase.
        /// </remark>
        public bool AllowPerMessageDeflate { get; set; }

        /// <summary>
        /// SSL X509Certificate2 interceptors.
        /// </summary>
        public IEnumerable<CertificateSelectionInterceptor>? OnSelectionCertificate { get; set; }

        /// <summary>
        /// Stream acceptance interceptors (e.g., for SSL/TLS, logging, etc.)
        /// </summary>
        public IEnumerable<AcceptStreamInterceptor>? OnAcceptStream { get; set; }

        /// <summary>
        /// WebSocket handshake interceptors (e.g., for authentication, custom headers, etc.)
        /// </summary>
        public IEnumerable<HandshakeInterceptor>? OnHandshake { get; set; }

        /// <summary>
        /// Handlers for when a WebSocket connection is established
        /// </summary>
        public IEnumerable<WebSocketConnectInterceptor>? OnConnect { get; set; }

        /// <summary>
        /// Handlers for when a WebSocket connection is closed
        /// </summary>
        public IEnumerable<WebSocketCloseInterceptor>? OnClose { get; set; }

        /// <summary>
        /// Handlers for WebSocket errors
        /// </summary>
        public IEnumerable<WebSocketErrorInterceptor>? OnError { get; set; }

        /// <summary>
        /// Handlers for incoming WebSocket messages
        /// </summary>
        public IEnumerable<WebSocketMessageInterceptor>? OnMessage { get; set; }

        /// <summary>
        /// Initialises a new instance of the WebSocketServerConfiguration class
        /// </summary>
        public WebSocketServerOptions()
        {
            this.Port = 8080;
            this.KeepAliveInterval = TimeSpan.FromSeconds(60);
            this.IncludeExceptionInCloseResponse = false;
            this.SupportedSubProtocols = null;
            this.AllowPerMessageDeflate = false;
        }
    }
}