using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;


namespace Samurai.WebSockets
{
    // 1 input
    public delegate ValueTask InvokeOn<TInput>(TInput input, CancellationToken cancellationToken);

    public delegate ValueTask On<TInput>(
        TInput input,
        InvokeOn<TInput> next,
        CancellationToken cancellationToken);


    // 2 inputs
    public delegate ValueTask InvokeOn<TInput1, TInput2>(
        TInput1 input1,
        TInput2 input2,
        CancellationToken cancellationToken);

    public delegate ValueTask On<TInput1, TInput2>(
        TInput1 input1,
        TInput2 input2,
        InvokeOn<TInput1, TInput2> next,
        CancellationToken cancellationToken);


    // 3 inputs
    public delegate ValueTask InvokeOn<TInput1, TInput2, TInput3>(
        TInput1 input1,
        TInput2 input2,
        TInput3 input3,
        CancellationToken cancellationToken);

    public delegate ValueTask On<TInput1, TInput2, TInput3>(
        TInput1 input1,
        TInput2 input2,
        TInput3 input3,
        InvokeOn<TInput1, TInput2, TInput3> next,
        CancellationToken cancellationToken);

    public delegate ValueTask<TResult> Invoke<TInput, TResult>(TInput input, CancellationToken cancellationToken);
    public delegate ValueTask<TResult> Next<TInput, TResult>(TInput input, Invoke<TInput, TResult> next, CancellationToken cancellationToken);

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
        /// If you do that it is advisable to set this KeepAliveInterval to zero in the WebSocketServerFactory
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

        // Interceptors
        /// <summary>
        /// Stream acceptance interceptors (e.g., for SSL/TLS, logging, etc.)
        /// </summary>
        public IEnumerable<Next<TcpClient, Stream>>? OnAcceptStream { get; set; }

        /// <summary>
        /// WebSocket handshake interceptors (e.g., for authentication, custom headers, etc.)
        /// </summary>
        public IEnumerable<Next<WebSocketHttpContext, HttpResponse>>? OnHandshake { get; set; }

        /// <summary>
        /// Handlers for when a WebSocket connection is established
        /// </summary>
        public IEnumerable<On<SamuraiWebSocket>>? OnConnect { get; set; }

        /// <summary>
        /// Handlers for when a WebSocket connection is closed
        /// </summary>
        public IEnumerable<On<SamuraiWebSocket>>? OnClose { get; set; }

        /// <summary>
        /// Handlers for WebSocket errors
        /// </summary>
        public IEnumerable<On<SamuraiWebSocket, Exception>>? OnError { get; set; }

        /// <summary>
        /// Handlers for incoming WebSocket messages
        /// </summary>
        public IEnumerable<On<SamuraiWebSocket, MessageType, Stream>>? OnMessage { get; set; }

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