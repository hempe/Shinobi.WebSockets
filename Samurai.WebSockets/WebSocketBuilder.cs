using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

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


    public class WebSocketBuilder
    {
        private readonly List<Next<TcpClient, Stream>> onAcceptStreamInterceptors = new List<Next<TcpClient, Stream>>();
        private readonly List<Next<WebSocketHttpContext, HttpResponse>> onHandshakeInterceptors = new List<Next<WebSocketHttpContext, HttpResponse>>();
        private readonly List<On<SamuraiWebSocket>> onConnectHandlers = new List<On<SamuraiWebSocket>>();
        private readonly List<On<SamuraiWebSocket>> onCloseHandlers = new List<On<SamuraiWebSocket>>();
        private readonly List<On<SamuraiWebSocket, Exception>> onErrorHandlers = new List<On<SamuraiWebSocket, Exception>>();
        private readonly List<On<SamuraiWebSocket, MessageType, Stream>> onMessageHandlers = new List<On<SamuraiWebSocket, MessageType, Stream>>();

        private ILogger<SamuraiServer>? logger;
        private ushort port = 8080;
        private X509Certificate2? certificate;

        /// <summary>
        /// Sets the port for the WebSocket server
        /// </summary>
        /// <param name="port">Port number (default: 8080)</param>
        public WebSocketBuilder UsePort(ushort port)
        {
            this.port = port;
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
        /// Adds an interceptor for stream acceptance (e.g., for SSL/TLS, logging, etc.)
        /// </summary>
        /// <param name="interceptor">Stream acceptance interceptor</param>
        public WebSocketBuilder OnAcceptStream(Next<TcpClient, Stream> interceptor)
        {
            this.onAcceptStreamInterceptors.Add(interceptor ?? throw new ArgumentNullException(nameof(interceptor)));
            return this;
        }

        /// <summary>
        /// Adds an interceptor for WebSocket handshake (e.g., for authentication, custom headers, etc.)
        /// </summary>
        /// <param name="interceptor">Handshake interceptor</param>
        public WebSocketBuilder OnHandshake(Next<WebSocketHttpContext, HttpResponse> interceptor)
        {
            this.onHandshakeInterceptors.Add(interceptor ?? throw new ArgumentNullException(nameof(interceptor)));
            return this;
        }

        /// <summary>
        /// Adds a handler for when a WebSocket connection is established
        /// </summary>
        /// <param name="handler">Connection handler</param>
        public WebSocketBuilder OnConnect(On<SamuraiWebSocket> handler)
        {
            this.onConnectHandlers.Add(handler ?? throw new ArgumentNullException(nameof(handler)));
            return this;
        }

        /// <summary>
        /// Adds a handler for when a WebSocket connection is closed
        /// </summary>
        /// <param name="handler">Close handler</param>
        public WebSocketBuilder OnClose(On<SamuraiWebSocket> handler)
        {
            this.onCloseHandlers.Add(handler ?? throw new ArgumentNullException(nameof(handler)));
            return this;
        }

        /// <summary>
        /// Adds a handler for WebSocket errors
        /// </summary>
        /// <param name="handler">Error handler</param>
        public WebSocketBuilder OnError(On<SamuraiWebSocket, Exception> handler)
        {
            this.onErrorHandlers.Add(handler ?? throw new ArgumentNullException(nameof(handler)));
            return this;
        }

        /// <summary>
        /// Adds a handler for incoming WebSocket messages
        /// </summary>
        /// <param name="handler">Message handler</param>
        public WebSocketBuilder OnMessage(On<SamuraiWebSocket, MessageType, Stream> handler)
        {
            this.onMessageHandlers.Add(handler ?? throw new ArgumentNullException(nameof(handler)));
            return this;
        }

        /// <summary>
        /// Adds a handler specifically for text messages
        /// </summary>
        /// <param name="handler">Text message handler</param>
        public WebSocketBuilder OnTextMessage(Func<SamuraiWebSocket, string, CancellationToken, ValueTask> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            return this.OnMessage(async (webSocket, messageType, stream, _next, cancellationToken) =>
            {
                if (messageType == MessageType.Text)
                {
                    using (var reader = new StreamReader(stream, leaveOpen: true))
                    {
                        var message = await reader.ReadToEndAsync();
                        await handler(webSocket, message, cancellationToken);
                    }
                }
            });
        }

        /// <summary>
        /// Adds a handler specifically for binary messages
        /// </summary>
        /// <param name="handler">Binary message handler</param>
        public WebSocketBuilder OnBinaryMessage(Func<SamuraiWebSocket, byte[], CancellationToken, Task> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            return this.OnMessage(async (webSocket, messageType, stream, next, cancellationToken) =>
            {
                if (messageType == MessageType.Binary)
                {
                    using (var memoryStream = new MemoryStream())
                    {
                        await stream.CopyToAsync(memoryStream, cancellationToken);
                        await handler(webSocket, memoryStream.ToArray(), cancellationToken);
                    }
                }
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
        /// Builds and returns the configured SamuraiServer instance
        /// </summary>
        /// <returns>Configured SamuraiServer</returns>
        public SamuraiServer Build()
        {
            var interceptors = new Interceptors
            {
                OnAcceptStream = this.onAcceptStreamInterceptors.Count > 0 ? this.onAcceptStreamInterceptors : null,
                OnHandshake = this.onHandshakeInterceptors.Count > 0 ? this.onHandshakeInterceptors : null,
                OnConnect = this.onConnectHandlers.Count > 0 ? this.onConnectHandlers : null,
                OnClose = this.onCloseHandlers.Count > 0 ? this.onCloseHandlers : null,
                OnError = this.onErrorHandlers.Count > 0 ? this.onErrorHandlers : null,
                OnMessage = this.onMessageHandlers.Count > 0 ? this.onMessageHandlers : null
            };

            var server = new SamuraiServer(this.logger, interceptors, this.port);

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