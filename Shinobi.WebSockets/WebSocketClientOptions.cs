using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Shinobi.WebSockets
{
    /// <summary>
    /// Configuration for WebSocket auto-reconnect behavior
    /// </summary>
    public class WebSocketReconnectOptions
    {
        /// <summary>
        /// Whether auto-reconnect is enabled
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Initial delay before the first reconnect attempt
        /// </summary>
        public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Maximum delay between reconnect attempts
        /// </summary>
        public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Multiplier for exponential backoff
        /// </summary>
        public double BackoffMultiplier { get; set; } = 2.0;

        /// <summary>
        /// Maximum number of reconnect attempts (0 = infinite)
        /// </summary>
        public int MaxAttempts { get; set; } = 0;

        /// <summary>
        /// Jitter to add randomness to backoff delays (0 = no jitter, 1 = up to 100% jitter)
        /// </summary>
        public double Jitter { get; set; } = 0.1;
    }

    /// <summary>
    /// Delegate for the OnReconnecting event that allows URL modification
    /// </summary>
    /// <param name="currentUri">The current URI being used</param>
    /// <param name="attemptNumber">The current reconnect attempt number</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The URI to use for this reconnect attempt (can be the same or different)</returns>
    public delegate Task<Uri> WebSocketReconnectingHandler(Uri currentUri, int attemptNumber, CancellationToken cancellationToken);

    /// <summary>
    /// Connection states for WebSocket client
    /// </summary>
    public enum WebSocketConnectionState
    {
        /// <summary>
        /// The WebSocket is not connected
        /// </summary>
        Disconnected,
        
        /// <summary>
        /// The WebSocket is in the process of establishing a connection
        /// </summary>
        Connecting,
        
        /// <summary>
        /// The WebSocket is successfully connected and ready for communication
        /// </summary>
        Connected,
        
        /// <summary>
        /// The WebSocket is attempting to reconnect after a connection failure
        /// </summary>
        Reconnecting,
        
        /// <summary>
        /// The WebSocket is in the process of closing the connection
        /// </summary>
        Disconnecting,
        
        /// <summary>
        /// The WebSocket connection failed and will not attempt to reconnect
        /// </summary>
        Failed
    }

    /// <summary>
    /// Event arguments for connection state changes
    /// </summary>
    public class WebSocketConnectionStateChangedEventArgs : EventArgs
    {
        public WebSocketConnectionState PreviousState { get; }
        public WebSocketConnectionState NewState { get; }
        public Exception? Exception { get; }

        public WebSocketConnectionStateChangedEventArgs(WebSocketConnectionState previousState, WebSocketConnectionState newState, Exception? exception = null)
        {
            PreviousState = previousState;
            NewState = newState;
            Exception = exception;
        }
    }

    /// <summary>
    /// Event arguments for reconnecting events
    /// </summary>
    public class WebSocketReconnectingEventArgs : EventArgs
    {
        public Uri CurrentUri { get; set; }
        public int AttemptNumber { get; }
        public TimeSpan Delay { get; }

        public WebSocketReconnectingEventArgs(Uri currentUri, int attemptNumber, TimeSpan delay)
        {
            CurrentUri = currentUri;
            AttemptNumber = attemptNumber;
            Delay = delay;
        }
    }

    /// <summary>
    /// Delegate for connection state change events
    /// </summary>
    public delegate void WebSocketConnectionStateChangedHandler(WebSocketClient sender, WebSocketConnectionStateChangedEventArgs e);

    /// <summary>
    /// Delegate for reconnecting events
    /// </summary>
    public delegate void WebSocketReconnectingEventHandler(WebSocketClient sender, WebSocketReconnectingEventArgs e);
    /// <summary>
    /// Options for configuring the WebSocket client.
    /// These options can be used to control various aspects of the WebSocket connection,
    /// such as keep-alive intervals, no-delay settings, additional HTTP headers, and more
    /// </summary>
    public class WebSocketClientOptions
    {
        /// <summary>
        /// How often to send ping requests to the Server
        /// This is done to prevent proxy servers from closing your connection
        /// The default is TimeSpan.Zero meaning that it is disabled.
        /// WebSocket servers usually send ping messages so it is not normally necessary for the client to send them (hence the TimeSpan.Zero default)
        /// You can manually control ping pong messages using the PingPongManager class.
        /// If you do that it is advisible to set this KeepAliveInterval to zero for the WebSocketClient
        /// </summary>
        public TimeSpan KeepAliveInterval { get; set; }

        /// <summary>
        /// Set to true to send a message immediately with the least amount of latency (typical usage for chat)
        /// This will disable Nagle's algorithm which can cause high tcp latency for small packets sent infrequently
        /// However, if you are streaming large packets or sending large numbers of small packets frequently it is advisable to set NoDelay to false
        /// This way data will be bundled into larger packets for better throughput
        /// </summary>
        public bool NoDelay { get; set; }

        /// <summary>
        /// Add any additional http headers to this dictionary
        /// </summary>
        public Dictionary<string, string> AdditionalHttpHeaders { get; set; }

        /// <summary>
        /// Include the full exception (with stack trace) in the close response 
        /// when an exception is encountered and the WebSocket connection is closed
        /// The default is false
        /// </summary>
        public bool IncludeExceptionInCloseResponse { get; set; }

        /// <summary>
        /// WebSocket Extensions as an HTTP header value
        /// </summary>
        public string? SecWebSocketExtensions { get; set; }

        /// <summary>
        /// A comma separated list of sub protocols in preference order (first one being the most preferred)
        /// The server will return the first supported sub protocol (or none if none are supported)
        /// Can be null
        /// </summary>
        public string? SecWebSocketProtocol { get; set; }

        /// <summary>
        /// Configuration for auto-reconnect behavior
        /// </summary>
        public WebSocketReconnectOptions ReconnectOptions { get; set; }

        /// <summary>
        /// Interceptors for when a WebSocket connection is established
        /// </summary>
        public IList<WebSocketConnectInterceptor>? OnConnect { get; set; }

        /// <summary>
        /// Interceptors for when a WebSocket connection is closed
        /// </summary>
        public IList<WebSocketCloseInterceptor>? OnClose { get; set; }

        /// <summary>
        /// Interceptors for WebSocket errors
        /// </summary>
        public IList<WebSocketErrorInterceptor>? OnError { get; set; }

        /// <summary>
        /// Interceptors for incoming WebSocket messages
        /// </summary>
        public IList<WebSocketMessageInterceptor>? OnMessage { get; set; }

        /// <summary>
        /// Handler for when reconnection is about to start (allows URL modification)
        /// </summary>
        public WebSocketReconnectingHandler? OnReconnecting { get; set; }

        /// <summary>
        /// Initialises a new instance of the WebSocketClientOptions class
        /// </summary>
        public WebSocketClientOptions()
        {
            this.KeepAliveInterval = TimeSpan.FromSeconds(20);
            this.NoDelay = true;
            this.AdditionalHttpHeaders = new Dictionary<string, string>();
            this.IncludeExceptionInCloseResponse = false;
            this.SecWebSocketProtocol = null;
            this.ReconnectOptions = new WebSocketReconnectOptions();
        }
    }
}
