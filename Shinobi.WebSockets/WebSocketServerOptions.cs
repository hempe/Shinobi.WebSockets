using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

using Shinobi.WebSockets.Http;


namespace Shinobi.WebSockets
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
    public delegate ValueTask WebSocketConnectHandler(ShinobiWebSocket webSocket, CancellationToken cancellationToken);
    public delegate ValueTask WebSocketConnectInterceptor(ShinobiWebSocket webSocket, WebSocketConnectHandler next, CancellationToken cancellationToken);

    // WebSocket close delegates  
    public delegate ValueTask WebSocketCloseHandler(ShinobiWebSocket webSocket, WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken);
    public delegate ValueTask WebSocketCloseInterceptor(ShinobiWebSocket webSocket, WebSocketCloseStatus closeStatus, string? statusDescription, WebSocketCloseHandler next, CancellationToken cancellationToken);

    // WebSocket error delegates
    public delegate ValueTask WebSocketErrorHandler(ShinobiWebSocket webSocket, Exception exception, CancellationToken cancellationToken);
    public delegate ValueTask WebSocketErrorInterceptor(ShinobiWebSocket webSocket, Exception exception, WebSocketErrorHandler next, CancellationToken cancellationToken);

    // WebSocket message delegates
    public delegate ValueTask WebSocketMessageHandler(ShinobiWebSocket webSocket, MessageType messageType, Stream messageStream, CancellationToken cancellationToken);
    public delegate ValueTask WebSocketMessageInterceptor(ShinobiWebSocket webSocket, MessageType messageType, Stream messageStream, WebSocketMessageHandler next, CancellationToken cancellationToken);

    // Simplified message handlers (for convenience methods)
    public delegate ValueTask WebSocketTextMessageHandler(ShinobiWebSocket webSocket, string message, CancellationToken cancellationToken);
    public delegate ValueTask WebSocketBinaryMessageHandler(ShinobiWebSocket webSocket, byte[] message, CancellationToken cancellationToken);

    // Authentication delegate
    public delegate bool WebSocketAuthenticator(WebSocketHttpContext context);
#if NET8_0_OR_GREATER
    /// <summary>
    /// Configuration for WebSocket per-message deflate compression (RFC 7692)
    /// </summary>
    public class PerMessageDeflateOptions
    {
        /// <summary>
        /// Gets or sets whether per-message deflate compression is enabled.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Gets or sets the server's context takeover behavior.
        /// </summary>
        public ContextTakeoverMode ServerContextTakeover { get; set; } = ContextTakeoverMode.Allow;

        /// <summary>
        /// Gets or sets the client's context takeover behavior.
        /// </summary>
        public ContextTakeoverMode ClientContextTakeover { get; set; } = ContextTakeoverMode.Allow;
    }
#endif
    /// <summary>
    /// Defines context takeover behavior for per-message deflate compression
    /// </summary>
    public enum ContextTakeoverMode
    {
        /// <summary>
        /// Allow context takeover (better compression, more memory usage)
        /// </summary>
        Allow,

        /// <summary>
        /// Don't allow context takeover - reject if client requests it
        /// </summary>
        DontAllow,

        /// <summary>
        /// Force no context takeover (add the parameter even if client doesn't request it)
        /// </summary>
        ForceDisabled
    }

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
#if NET8_0_OR_GREATER
        /// <summary>
        /// Gets or sets the per-message deflate compression configuration.
        /// When enabled, this allows the WebSocket server to negotiate and use per-message
        /// deflate compression for WebSocket messages, which can reduce bandwidth usage.
        /// </summary>
        /// <remark>
        /// This is an experimental feature. Per-message deflate comes with CPU and memory costs.
        /// Context takeover provides better compression but uses more memory. Consider your use case carefully.
        /// </remark>
        public PerMessageDeflateOptions PerMessageDeflate { get; set; } = new PerMessageDeflateOptions();
#endif
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
        /// Handlers for after a WebSocket connection is established
        /// </summary>
        public IEnumerable<WebSocketConnectInterceptor>? OnConnected { get; set; }

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
        }
    }
}