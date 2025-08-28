using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Shinobi.WebSockets.Exceptions;
using Shinobi.WebSockets.Extensions;
using Shinobi.WebSockets.Http;
using Shinobi.WebSockets.Internal;

namespace Shinobi.WebSockets
{
    /// <summary>
    /// A high-level WebSocket client that provides connection management, auto-reconnect capabilities,
    /// and convenient methods for sending messages. This class manages the WebSocket connection 
    /// internally and provides events for connection state changes and reconnection attempts.
    /// </summary>
    public class WebSocketClient : IDisposable
    {
        internal ShinobiWebSocket? webSocket;
        private bool isDisposed;
        private readonly ILogger<WebSocketClient>? logger;
        private readonly WebSocketClientOptions options;
        private readonly ILoggerFactory? loggerFactory;
        private Uri? currentUri;
        private CancellationTokenSource? connectionCancellationTokenSource;
        private Task? connectionTask;
        private IBackoffCalculator backoffCalculator = new BackoffCalculator();

        // Connection state management
        private WebSocketConnectionState connectionState = WebSocketConnectionState.Disconnected;
        private readonly object stateLock = new object();

        /// <summary>
        /// Occurs when the WebSocket connection state changes (e.g., Connected, Disconnected, Reconnecting).
        /// </summary>
        public event WebSocketConnectionStateChangedHandler? ConnectionStateChanged;

        /// <summary>
        /// Occurs when the client is about to attempt a reconnection. Allows modification of the target URI.
        /// </summary>
        public event WebSocketReconnectingEventHandler? Reconnecting;

        // Chain handlers built from interceptor lists
        private readonly WebSocketConnectHandler OnConnectAsync;
        private readonly WebSocketCloseHandler OnCloseAsync;
        private readonly WebSocketErrorHandler OnErrorAsync;
        private readonly WebSocketMessageHandler OnMessageAsync;

        /// <summary>
        /// Gets the current connection state
        /// </summary>
        public WebSocketConnectionState ConnectionState
        {
            get
            {
                lock (this.stateLock)
                {
                    return this.connectionState;
                }
            }
        }

        /// <summary>
        /// Initializes a new instance of the WebSocketClient class.
        /// </summary>
        /// <param name="options">Configuration options for the WebSocket client</param>
        /// <param name="loggerFactory">Optional logger factory</param>
        public WebSocketClient(
            WebSocketClientOptions options,
            ILoggerFactory? loggerFactory = null)
        {
            this.logger = loggerFactory?.CreateLogger<WebSocketClient>();
            this.options = options;
            this.loggerFactory = loggerFactory;
            // Use the specific builders to create handler chains
            this.OnConnectAsync = Builder.BuildWebSocketConnectChain(options.OnConnect);
            this.OnCloseAsync = Builder.BuildWebSocketCloseChain(options.OnClose);
            this.OnErrorAsync = Builder.BuildWebSocketErrorChain(options.OnError);
            this.OnMessageAsync = Builder.BuildWebSocketMessageChain(options.OnMessage);
        }

        /// <summary>
        /// Starts the WebSocket connection with auto-reconnect support
        /// </summary>
        /// <param name="uri">The WebSocket URI to connect to</param>
        /// <param name="cancellationToken">Cancellation token</param>
        public async Task StartAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            this.currentUri = uri;
            this.connectionCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            this.ChangeConnectionState(WebSocketConnectionState.Connecting);

            if (this.options.ReconnectOptions.Enabled)
            {
                // Start connection management in background for auto-reconnect scenarios
                this.connectionTask = Task.Run(async () => await this.ManageConnectionAsync(this.connectionCancellationTokenSource.Token));
            }
            else
            {
                // Direct connection without auto-reconnect - throw exceptions on failure
                try
                {
                    await this.ConnectAsync(cancellationToken);
                }
                catch (Exception)
                {
                    this.ChangeConnectionState(WebSocketConnectionState.Failed);
                    throw;
                }

                // Start message handling task
                this.connectionTask = Task.Run(async () => await this.HandleMessagesAsync(this.connectionCancellationTokenSource.Token));
            }
        }

        /// <summary>
        /// Stops the WebSocket connection and disables auto-reconnect
        /// </summary>
        public async Task StopAsync()
        {
            this.ChangeConnectionState(WebSocketConnectionState.Disconnecting);

            this.connectionCancellationTokenSource?.Cancel();

            if (this.webSocket?.State == WebSocketState.Open)
            {
                try
                {
                    using var tsc = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await this.webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Client stopping", tsc.Token);
                }
                catch
                {
                    // Ignore close errors
                }
            }

            if (this.connectionTask != null)
            {
                try
                {
                    await this.connectionTask;
                }
                catch
                {
                    // Ignore task cancellation
                }
            }

            this.ChangeConnectionState(WebSocketConnectionState.Disconnected);
        }

        /// <summary>
        /// Aborts the WebSocket without sending a Close frame
        /// </summary>
        public void Abort()
        {
            this.webSocket?.Abort();
        }

        /// <summary>
        /// Sends a text message asynchronously to the connected WebSocket server.
        /// </summary>
        /// <param name="message">The text message to send</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
        /// <exception cref="InvalidOperationException">Thrown when the WebSocket is not connected</exception>
        public async Task SendTextAsync(string message, CancellationToken cancellationToken = default)
        {
            if (this.webSocket?.State != WebSocketState.Open)
                throw new InvalidOperationException("WebSocket is not connected");

            var bytes = Encoding.UTF8.GetBytes(message);
            await this.webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
        }

        /// <summary>
        /// Sends a binary message asynchronously to the connected WebSocket server.
        /// </summary>
        /// <param name="data">The binary data to send</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
        /// <exception cref="InvalidOperationException">Thrown when the WebSocket is not connected</exception>
        public async Task SendBinaryAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            if (this.webSocket?.State != WebSocketState.Open)
                throw new InvalidOperationException("WebSocket is not connected");

            await this.webSocket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Binary, true, cancellationToken);
        }

        /// <summary>
        /// Changes the connection state and raises the event
        /// </summary>
        private void ChangeConnectionState(WebSocketConnectionState newState, Exception? exception = null)
        {
            WebSocketConnectionState previousState;

            lock (this.stateLock)
            {
                previousState = this.connectionState;
                this.connectionState = newState;
            }

            if (previousState != newState)
            {
                this.logger?.ConnectionStateChanged(previousState, newState);
                this.ConnectionStateChanged?.Invoke(this, new WebSocketConnectionStateChangedEventArgs(previousState, newState, exception));
            }
        }

        /// <summary>
        /// Manages the WebSocket connection with auto-reconnect
        /// </summary>
        private async Task ManageConnectionAsync(CancellationToken cancellationToken)
        {
            var attemptNumber = 0;
            var reconnectAttemptNumber = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                DateTime? connectionTime = null;

                try
                {
                    attemptNumber++;

                    // If this is a reconnection attempt, handle delay and events
                    if (reconnectAttemptNumber > 0)
                    {
                        // Check if we should reconnect
                        if (!this.options.ReconnectOptions.Enabled)
                        {
                            this.logger?.AutoReconnectDisabled();
                            this.ChangeConnectionState(WebSocketConnectionState.Disconnected);
                            break;
                        }

                        // Prepare for reconnect
                        this.logger?.StartingAutoReconnect(reconnectAttemptNumber);
                        this.ChangeConnectionState(WebSocketConnectionState.Reconnecting);

                        // Calculate delay with exponential backoff and jitter
                        var delay = this.CalculateReconnectDelay(reconnectAttemptNumber - 1);

                        // Allow URL modification through OnReconnecting handler
                        var reconnectUri = this.currentUri!;
                        if (this.options.OnReconnecting != null)
                        {
                            this.logger?.CallingOnReconnectingHandler(reconnectAttemptNumber);
                            reconnectUri = await this.options.OnReconnecting(this.currentUri!, reconnectAttemptNumber, cancellationToken);
                            if (!reconnectUri.Equals(this.currentUri))
                            {
                                this.logger?.ReconnectingUriChanged(this.currentUri!, reconnectUri);
                            }
                            this.currentUri = reconnectUri;
                        }

                        // Raise reconnecting event
                        this.Reconnecting?.Invoke(this, new WebSocketReconnectingEventArgs(reconnectUri, reconnectAttemptNumber, delay));

                        this.logger?.ReconnectingWithDelay(reconnectUri, (int)delay.TotalMilliseconds, reconnectAttemptNumber);

                        // Wait before reconnecting
                        await Task.Delay(delay, cancellationToken);
                    }

                    // Try to establish connection
                    await this.ConnectAsync(cancellationToken);
                    connectionTime = DateTime.Now;

                    if (this.connectionState != WebSocketConnectionState.Connected)
                    {
                        // Connection failed during ConnectAsync
                        throw new InvalidOperationException("WebSocket connection failed during ConnectAsync");
                    }

                    var bufferedReconnectAttemptNumber = reconnectAttemptNumber;

                    // Connection successful, log success but don't reset reconnect attempt counter yet
                    // (we'll reset it only if the connection turns out to be stable)
                    if (reconnectAttemptNumber > 0)
                    {
                        this.logger?.ReconnectedSuccessfully(this.currentUri!, reconnectAttemptNumber);
                    }

                    // Handle messages until disconnection
                    await this.HandleMessagesAsync(cancellationToken);

                    // If we get here, connection was closed
                    if (cancellationToken.IsCancellationRequested)
                        continue;

                    // Check if this connection was considered stable using the callback
                    var connectionDuration = DateTime.Now - connectionTime.Value;
                    var isStableConnection = this.options.ReconnectOptions.IsStableConnection(connectionDuration);

                    if (!isStableConnection)
                    {
                        this.logger?.ConnectionClosedUnstable(connectionDuration.TotalMilliseconds);
                        reconnectAttemptNumber = bufferedReconnectAttemptNumber + 1;
                    }
                    else
                    {
                        this.logger?.ConnectionClosedStable(connectionDuration.TotalMilliseconds);
                        reconnectAttemptNumber = 0; // Reset to 0, so next attempt will be 1 and use BackoffCalculator with attempt 0
                    }
                }
                catch (OperationCanceledException)
                {
                    this.logger?.ReconnectionCancelled();
                    continue;
                }
                catch (Exception ex)
                {
                    this.logger?.ConnectionError(attemptNumber, ex);

                    if (!this.options.ReconnectOptions.Enabled)
                    {
                        this.logger?.ConnectionFailedPermanently(ex);
                        this.ChangeConnectionState(WebSocketConnectionState.Failed, ex);
                        break;
                    }

                    // Error occurred, will retry with delay on next iteration
                    this.ChangeConnectionState(WebSocketConnectionState.Reconnecting, ex);

                    // Set reconnect attempt number
                    if (reconnectAttemptNumber == 0)
                    {
                        reconnectAttemptNumber = 1; // First reconnection attempt
                    }
                    else
                    {
                        reconnectAttemptNumber++; // Subsequent reconnection attempts
                    }
                }
            }
        }

        /// <summary>
        /// Establishes a single WebSocket connection
        /// </summary>
        private async Task ConnectAsync(CancellationToken cancellationToken)
        {
            this.ChangeConnectionState(WebSocketConnectionState.Connecting);

            this.webSocket = await this.PerformWebSocketConnectionAsync(this.currentUri!, this.options, cancellationToken);

            // Cast to ShinobiWebSocket since we know we return ShinobiWebSocket
            var shinobiWebSocket = this.webSocket;

            this.ChangeConnectionState(WebSocketConnectionState.Connected);

            // Trigger connect interceptors
            if (this.OnConnectAsync != null)
                await this.OnConnectAsync(shinobiWebSocket, cancellationToken);
        }

        /// <summary>
        /// Calculates the delay for the next reconnect attempt using exponential backoff with jitter
        /// </summary>
        private TimeSpan CalculateReconnectDelay(int attemptNumber)
        {
            return this.backoffCalculator.CalculateDelay(
                attemptNumber, // attemptNumber is already 0-based when passed to this method
                this.options.ReconnectOptions.InitialDelay,
                this.options.ReconnectOptions.MaxDelay,
                this.options.ReconnectOptions.Jitter,
                this.options.ReconnectOptions.BackoffMultiplier);
        }

        private async Task HandleMessagesAsync(CancellationToken cancellationToken)
        {
            if (this.webSocket == null)
                return;

            var shinobiWebSocket = this.webSocket;
            WebSocketMessageType? currentMessageType = null;

            try
            {
                var receiveBuffer = new ArrayPoolStream();
                try
                {
                    while (this.webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested && !this.isDisposed)
                    {
                        var result = await this.webSocket.ReceiveAsync(receiveBuffer, cancellationToken);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            var message = receiveBuffer.GetDataArraySegment();
                            var (closeStatus, statusDescription) = Shared.ParseClosePayload(message, result.Count);
                            await this.OnCloseAsync(shinobiWebSocket, closeStatus, statusDescription, cancellationToken);
                            return;
                        }

                        // If we're in the middle of a message, ensure message type consistency
                        if (currentMessageType.HasValue && result.MessageType != currentMessageType.Value)
                        {
                            throw new InvalidOperationException($"WebSocket message type changed from {currentMessageType.Value} to {result.MessageType} during partial message.");
                        }

                        // Set the message type for the first frame of a new message
                        if (!currentMessageType.HasValue)
                        {
                            currentMessageType = result.MessageType;
                        }

                        if (result.EndOfMessage)
                        {
                            receiveBuffer.Position = 0;
                            await this.OnMessageAsync(shinobiWebSocket, (MessageType)result.MessageType, receiveBuffer, cancellationToken);
                            currentMessageType = null; // Reset for next message
                            receiveBuffer.Dispose();
                            receiveBuffer = new ArrayPoolStream();
                        }
                    }
                }
                finally
                {
                    receiveBuffer.Dispose();
                }
            }
            catch (Exception ex)
            {
                if (this.OnErrorAsync != null)
                    await this.OnErrorAsync(shinobiWebSocket, ex, cancellationToken);
                throw; // Re-throw to trigger reconnect logic
            }
        }

        #region WebSocket Connection Logic (moved from WebSocketClientFactory)

        /// <summary>
        /// Performs the complete WebSocket connection process
        /// </summary>
        private async ValueTask<ShinobiWebSocket> PerformWebSocketConnectionAsync(Uri uri, WebSocketClientOptions options, CancellationToken cancellationToken)
        {
            var guid = Guid.NewGuid();
            var uriScheme = uri.Scheme.ToLower();
            var client = await this.GetClientAsync(
                    guid,
                    uriScheme == "wss" || uriScheme == "https",
                    options.NoDelay,
                    uri.Host,
                    uri.Port,
                    cancellationToken).ConfigureAwait(false);

            return await this.PerformHandshakeAsync(
                client.TcpClient,
                guid,
                uri,
                client.Stream,
                options,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Connects with a stream that has already been opened and HTTP websocket upgrade request sent
        /// This function will check the handshake response from the server and proceed if successful
        /// </summary>
        private async ValueTask<ShinobiWebSocket> ConnectAsync(TcpClient tcpClient, Guid guid, Stream responseStream, string secWebSocketKey, TimeSpan keepAliveInterval, string? secWebSocketExtensions, bool includeExceptionInCloseResponse, CancellationToken cancellationToken)
        {
            this.logger?.ReadingHttpResponse(guid);
            HttpResponse? response;

            try
            {
                response = await HttpResponse.ReadAsync(responseStream, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                this.logger?.ReadHttpResponseError(guid, ex);
                throw new WebSocketHandshakeFailedException("Handshake unexpected failure", ex);
            }

            this.ThrowIfInvalidResponseCode(response);
            this.ThrowIfInvalidAcceptString(guid, response!, secWebSocketKey);

            return new ShinobiWebSocket(
                new WebSocketHttpContext(tcpClient, response!, responseStream, guid, this.loggerFactory),
#if NET8_0_OR_GREATER
                response!.GetHeaderValuesCombined("Sec-WebSocket-Extensions")?.ParseExtension(),
#endif
                keepAliveInterval,
                includeExceptionInCloseResponse,
                true,
                response!.GetHeaderValuesCombined("Sec-WebSocket-Protocol"));
        }

        private void ThrowIfInvalidAcceptString(Guid guid, HttpHeader response, string secWebSocketKey)
        {
            // make sure we escape the accept string which could contain special regex characters
            var actualAcceptString = response.GetHeaderValue("Sec-WebSocket-Accept");

            // check the accept string
            var expectedAcceptString = secWebSocketKey.ComputeSocketAcceptString();
            if (expectedAcceptString != actualAcceptString)
            {
                var warning = $"Handshake failed because the accept string from the server '{expectedAcceptString}' was not the expected string '{actualAcceptString}'";
                this.logger?.HandshakeFailure(guid, warning);
                throw new WebSocketHandshakeFailedException(warning);
            }

            this.logger?.ClientHandshakeSuccess(guid);
        }

        private void ThrowIfInvalidResponseCode(HttpResponse? repsonse)
        {
            if (repsonse?.StatusCode != 101)
                throw new InvalidHttpResponseCodeException(repsonse?.StatusCode);
        }

        /// <summary>
        /// Override this if you need more fine grained control over the TLS handshake like setting the SslProtocol or adding a client certificate
        /// </summary>
        protected virtual void TlsAuthenticateAsClient(SslStream sslStream, string host)
            => sslStream.AuthenticateAsClient(host, null, SslProtocols.Tls12, true);

        /// <summary>
        /// Creates the underlying stream for WebSocket connection
        /// </summary>
        protected async virtual ValueTask<(Stream Stream, TcpClient TcpClient)> GetClientAsync(Guid loggingGuid, bool isSecure, bool noDelay, string host, int port, CancellationToken cancellationToken)
        {
            var tcpClient = new TcpClient { NoDelay = noDelay };
            using (cancellationToken.Register(() => tcpClient.Close()))
            {

                if (IPAddress.TryParse(host, out var ipAddress))
                {
                    this.logger?.ClientConnectingToIpAddress(loggingGuid, ipAddress.ToString(), port);
                    await tcpClient.ConnectAsync(ipAddress, port).ConfigureAwait(false);
                }
                else
                {
                    this.logger?.ClientConnectingToHost(loggingGuid, host, port);
                    await tcpClient.ConnectAsync(host, port).ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                var stream = tcpClient.GetStream();

                if (isSecure)
                {
                    var sslStream = new SslStream(stream, false, new RemoteCertificateValidationCallback(this.ValidateServerCertificate), null);
                    this.logger?.AttemptingToSecureSslConnection(loggingGuid);

                    // This will throw an AuthenticationException if the certificate is not valid
                    this.TlsAuthenticateAsClient(sslStream, host);
                    this.logger?.ConnectionSecured(loggingGuid);
                    return (sslStream, tcpClient);
                }

                this.logger?.ConnectionNotSecure(loggingGuid);
                return (stream, tcpClient);
            }
        }

        /// <summary>
        /// Invoked by the RemoteCertificateValidationDelegate
        /// If you want to ignore certificate errors (for debugging) then return true
        /// </summary>
        private bool ValidateServerCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
                return true;

            this.logger?.SslCertificateError(sslPolicyErrors);
            // TODO: Add option on new server to "ignore certificate errors"

            // Do not allow this client to communicate with unauthenticated servers.
            return false;
        }

        private ValueTask<ShinobiWebSocket> PerformHandshakeAsync(TcpClient tcpClient, Guid guid, Uri uri, Stream stream, WebSocketClientOptions options, CancellationToken cancellationToken)
        {
            var secWebSocketKey = Shared.SecWebSocketKey();
            var handshakeHttpRequest = HttpRequest.Create("GET", uri.PathAndQuery)
                .AddHeader("Host", $"{uri.Host}:{uri.Port}")
                .AddHeader("Upgrade", "websocket")
                .AddHeader("Connection", "Upgrade")
                .AddHeader("Sec-WebSocket-Key", secWebSocketKey)
                .AddHeader("Origin", $"http://{uri.Host}:{uri.Port}")
                .AddHeaderIf(!string.IsNullOrEmpty(options.SecWebSocketProtocol),
                            "Sec-WebSocket-Protocol", options.SecWebSocketProtocol!)
                .AddHeaderIf(!string.IsNullOrEmpty(options.SecWebSocketExtensions),
                            "Sec-WebSocket-Extensions", options.SecWebSocketExtensions!)
                .AddHeaders(options.AdditionalHttpHeaders)
                .AddHeader("Sec-WebSocket-Version", "13")
                .ToHttpRequest();

            var httpRequest = Encoding.UTF8.GetBytes(handshakeHttpRequest);
            stream.Write(httpRequest, 0, httpRequest.Length);
            this.logger?.HandshakeSent(guid, handshakeHttpRequest);
            return this.ConnectAsync(tcpClient, stream, secWebSocketKey, options, cancellationToken);
        }

        /// <summary>
        /// Connects with a stream and performs handshake validation
        /// </summary>
        private ValueTask<ShinobiWebSocket> ConnectAsync(TcpClient tcpClient, Stream responseStream, string secWebSocketKey, WebSocketClientOptions options, CancellationToken cancellationToken)
            => this.ConnectAsync(
                tcpClient,
                Guid.NewGuid(),
                responseStream,
                secWebSocketKey,
                options.KeepAliveInterval,
                options.SecWebSocketExtensions,
                options.IncludeExceptionInCloseResponse,
                cancellationToken);

        #endregion

        /// <summary>
        /// Internal method for testing: allows replacement of the BackoffCalculator
        /// </summary>
        internal void WithBackoffCalculator(IBackoffCalculator calculator)
        {
            this.backoffCalculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
        }

        public void Dispose()
        {
            if (this.isDisposed)
                return;

            // Stop the connection if still running
            try
            {
                this.StopAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // Ignore disposal errors
            }

            this.connectionCancellationTokenSource?.Dispose();
            this.webSocket?.Dispose();
            this.isDisposed = true;
        }
    }
}
