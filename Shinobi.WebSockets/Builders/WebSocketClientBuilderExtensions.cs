using System;
using System.Threading;
using System.Threading.Tasks;

namespace Shinobi.WebSockets.Builders
{
    /// <summary>
    /// Delegate for text message handlers
    /// </summary>
    public delegate ValueTask WebSocketTextMessageHandler(ShinobiWebSocket webSocket, string message, CancellationToken cancellationToken);

    /// <summary>
    /// Delegate for binary message handlers
    /// </summary>
    public delegate ValueTask WebSocketBinaryMessageHandler(ShinobiWebSocket webSocket, byte[] data, CancellationToken cancellationToken);

    /// <summary>
    /// Extension methods for WebSocketClientBuilder to support new features
    /// </summary>
    public static class WebSocketClientBuilderExtensions
    {
        /// <summary>
        /// Enables auto-reconnect with default settings
        /// </summary>
        public static WebSocketClientBuilder EnableAutoReconnect(this WebSocketClientBuilder builder)
        {
            return builder.UseConfiguration(options => 
            {
                options.ReconnectOptions.Enabled = true;
            });
        }

        /// <summary>
        /// Configures auto-reconnect options
        /// </summary>
        public static WebSocketClientBuilder UseAutoReconnect(this WebSocketClientBuilder builder, Action<WebSocketReconnectOptions> configure)
        {
            if (configure == null) throw new ArgumentNullException(nameof(configure));

            return builder.UseConfiguration(options => 
            {
                options.ReconnectOptions.Enabled = true;
                configure(options.ReconnectOptions);
            });
        }

        /// <summary>
        /// Adds a handler for when reconnection is about to start
        /// </summary>
        public static WebSocketClientBuilder OnReconnecting(this WebSocketClientBuilder builder, WebSocketReconnectingHandler handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            return builder.UseConfiguration(options => 
            {
                options.OnReconnecting = handler;
            });
        }

        /// <summary>
        /// Adds a URL interceptor that can modify the connection URL during reconnection
        /// </summary>
        /// <param name="builder">The builder</param>
        /// <param name="urlSelector">Function that selects the URL to use for reconnection</param>
        public static WebSocketClientBuilder UseUrlSelector(this WebSocketClientBuilder builder, Func<Uri, int, Uri> urlSelector)
        {
            if (urlSelector == null) throw new ArgumentNullException(nameof(urlSelector));

            return builder.OnReconnecting((currentUri, attemptNumber, cancellationToken) => 
                Task.FromResult(urlSelector(currentUri, attemptNumber)));
        }

        /// <summary>
        /// Configures connection fallback URLs for high availability
        /// </summary>
        /// <param name="builder">The builder</param>
        /// <param name="fallbackUrls">Array of fallback URLs to try in order</param>
        public static WebSocketClientBuilder UseFallbackUrls(this WebSocketClientBuilder builder, params Uri[] fallbackUrls)
        {
            if (fallbackUrls == null || fallbackUrls.Length == 0)
                throw new ArgumentException("At least one fallback URL must be provided", nameof(fallbackUrls));

            return builder.UseUrlSelector((currentUri, attemptNumber) =>
            {
                // Use original URL for first attempt, then cycle through fallback URLs
                if (attemptNumber <= 1)
                    return currentUri;

                var fallbackIndex = (attemptNumber - 2) % fallbackUrls.Length;
                return fallbackUrls[fallbackIndex];
            });
        }

        /// <summary>
        /// Configures exponential backoff with custom settings
        /// </summary>
        public static WebSocketClientBuilder UseExponentialBackoff(
            this WebSocketClientBuilder builder,
            TimeSpan initialDelay,
            TimeSpan maxDelay,
            double multiplier = 2.0,
            double jitter = 0.1)
        {
            return builder.UseAutoReconnect(options =>
            {
                options.InitialDelay = initialDelay;
                options.MaxDelay = maxDelay;
                options.BackoffMultiplier = multiplier;
                options.Jitter = jitter;
            });
        }

        /// <summary>
        /// Sets maximum number of reconnect attempts
        /// </summary>
        public static WebSocketClientBuilder UseMaxReconnectAttempts(this WebSocketClientBuilder builder, int maxAttempts)
        {
            return builder.UseAutoReconnect(options =>
            {
                options.MaxAttempts = maxAttempts;
            });
        }

        /// <summary>
        /// Adds connection state change monitoring
        /// </summary>
        public static WebSocketClientBuilder OnConnectionStateChanged(this WebSocketClientBuilder builder, WebSocketConnectionStateChangedHandler handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            // We'll need to add this after Build() is called since the client handles events
            // For now, we can store it in configuration and wire it up later
            return builder.UseConfiguration(options => 
            {
                // Store the handler in a custom property that we can retrieve after building
                options.AdditionalHttpHeaders["__ConnectionStateHandler"] = handler.Method.Name;
            });
        }

        /// <summary>
        /// Convenience method for basic auto-reconnect with sensible defaults
        /// </summary>
        public static WebSocketClientBuilder UseReliableConnection(this WebSocketClientBuilder builder)
        {
            return builder
                .EnableAutoReconnect()
                .UseExponentialBackoff(
                    initialDelay: TimeSpan.FromSeconds(1),
                    maxDelay: TimeSpan.FromSeconds(30),
                    multiplier: 2.0,
                    jitter: 0.1)
                .UseMaxReconnectAttempts(0); // Unlimited attempts
        }

        /// <summary>
        /// Adds a simple text message handler that doesn't need the WebSocket instance
        /// </summary>
        public static WebSocketClientBuilder OnTextMessage(this WebSocketClientBuilder builder, Func<string, CancellationToken, ValueTask> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            return builder.OnTextMessage((ws, message, ct) => handler(message, ct));
        }

        /// <summary>
        /// Adds a simple binary message handler that doesn't need the WebSocket instance
        /// </summary>
        public static WebSocketClientBuilder OnBinaryMessage(this WebSocketClientBuilder builder, Func<byte[], CancellationToken, ValueTask> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            return builder.OnBinaryMessage((ws, data, ct) => handler(data, ct));
        }
    }
}