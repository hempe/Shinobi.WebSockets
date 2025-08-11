using System;
using System.Collections.Generic;
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
using Shinobi.WebSockets.Internal;
using Shinobi.WebSockets.Http;

namespace Shinobi.WebSockets
{
    public class WebSocketClient : IDisposable
    {
        internal WebSocket? webSocket;
        private bool isDisposed;
        private readonly ILogger<WebSocketClient>? logger;
        private readonly WebSocketClientOptions options;
        private Uri? currentUri;
        private CancellationTokenSource? connectionCancellationTokenSource;
        private Task? connectionTask;
        private readonly Random random = new Random();

        // Connection state management
        private WebSocketConnectionState connectionState = WebSocketConnectionState.Disconnected;
        private readonly object stateLock = new object();

        // Events
        public event WebSocketConnectionStateChangedHandler? ConnectionStateChanged;
        public event WebSocketReconnectingEventHandler? Reconnecting;

        // Chain handlers built from interceptor lists
        private readonly WebSocketConnectHandler OnConnectChainAsync;
        private readonly WebSocketCloseHandler OnCloseChainAsync;
        private readonly WebSocketErrorHandler OnErrorChainAsync;
        private readonly WebSocketMessageHandler OnMessageChainAsync;

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

        public WebSocketClient(
            WebSocketClientOptions options,
            ILogger<WebSocketClient>? logger = null)
        {
            this.logger = logger;
            this.options = options;

            // Use the specific builders to create handler chains
            this.OnConnectChainAsync = Builder.BuildWebSocketConnectChain(options.OnConnect);
            this.OnCloseChainAsync = Builder.BuildWebSocketCloseChain(options.OnClose);
            this.OnErrorChainAsync = Builder.BuildWebSocketErrorChain(options.OnError);
            this.OnMessageChainAsync = Builder.BuildWebSocketMessageChain(options.OnMessage);
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
            
            this.connectionTask = Task.Run(async () => await this.ManageConnectionAsync(this.connectionCancellationTokenSource.Token));
            
            // Wait for initial connection or failure
            var initialConnectionTimeout = TimeSpan.FromSeconds(30);
            var timeoutTask = Task.Delay(initialConnectionTimeout, cancellationToken);
            
            while (this.ConnectionState == WebSocketConnectionState.Connecting && !cancellationToken.IsCancellationRequested)
            {
                var completedTask = await Task.WhenAny(timeoutTask, Task.Delay(100, cancellationToken));
                if (completedTask == timeoutTask)
                {
                    throw new TimeoutException("Initial connection attempt timed out");
                }
                
                if (this.ConnectionState == WebSocketConnectionState.Connected)
                    break;
                    
                if (this.ConnectionState == WebSocketConnectionState.Failed)
                    throw new InvalidOperationException("Failed to establish WebSocket connection");
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
                    await this.webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client stopping", CancellationToken.None);
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
        /// Sends a text message asynchronously
        /// </summary>
        public async Task SendTextAsync(string message, CancellationToken cancellationToken = default)
        {
            if (this.webSocket?.State != WebSocketState.Open)
                throw new InvalidOperationException("WebSocket is not connected");

            var bytes = Encoding.UTF8.GetBytes(message);
            await this.webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
        }

        /// <summary>
        /// Sends a binary message asynchronously
        /// </summary>
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
                this.logger?.LogDebug("WebSocket connection state changed from {PreviousState} to {NewState}", previousState, newState);
                this.ConnectionStateChanged?.Invoke(this, new WebSocketConnectionStateChangedEventArgs(previousState, newState, exception));
            }
        }

        /// <summary>
        /// Manages the WebSocket connection with auto-reconnect
        /// </summary>
        private async Task ManageConnectionAsync(CancellationToken cancellationToken)
        {
            int attemptNumber = 0;
            
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    attemptNumber++;
                    
                    // Try to establish connection
                    await this.ConnectAsync(cancellationToken);
                    
                    // Connection successful, reset attempt counter and log success
                    if (attemptNumber > 1)
                    {
                        this.logger?.LogInformation("Successfully reconnected to {Uri} after {AttemptNumber} attempts", this.currentUri, attemptNumber);
                    }
                    attemptNumber = 0;
                    
                    // Handle messages until disconnection
                    await this.HandleMessagesAsync(cancellationToken);
                    
                    // If we get here, connection was closed
                    if (cancellationToken.IsCancellationRequested)
                        break;
                        
                    this.logger?.LogInformation("WebSocket connection closed, checking reconnect options");
                        
                    // Check if we should reconnect
                    if (!this.options.ReconnectOptions.Enabled)
                    {
                        this.logger?.LogInformation("Auto-reconnect is disabled, staying disconnected");
                        this.ChangeConnectionState(WebSocketConnectionState.Disconnected);
                        break;
                    }
                    
                    // Prepare for reconnect
                    this.logger?.LogInformation("Starting auto-reconnect sequence (attempt {AttemptNumber})", attemptNumber + 1);
                    this.ChangeConnectionState(WebSocketConnectionState.Reconnecting);
                    
                    // Check max attempts
                    if (this.options.ReconnectOptions.MaxAttempts > 0 && attemptNumber > this.options.ReconnectOptions.MaxAttempts)
                    {
                        this.logger?.LogWarning("Maximum reconnect attempts ({MaxAttempts}) exceeded", this.options.ReconnectOptions.MaxAttempts);
                        this.ChangeConnectionState(WebSocketConnectionState.Failed);
                        break;
                    }
                    
                    // Calculate delay with exponential backoff and jitter
                    var delay = this.CalculateReconnectDelay(attemptNumber);
                    
                    // Allow URL modification through OnReconnecting handler
                    var reconnectUri = this.currentUri!;
                    if (this.options.OnReconnecting != null)
                    {
                        this.logger?.LogDebug("Calling OnReconnecting handler for attempt {AttemptNumber}", attemptNumber);
                        reconnectUri = await this.options.OnReconnecting(this.currentUri!, attemptNumber, cancellationToken);
                        if (!reconnectUri.Equals(this.currentUri))
                        {
                            this.logger?.LogInformation("OnReconnecting handler changed URI from {OldUri} to {NewUri}", this.currentUri, reconnectUri);
                        }
                        this.currentUri = reconnectUri;
                    }
                    
                    // Raise reconnecting event
                    this.Reconnecting?.Invoke(this, new WebSocketReconnectingEventArgs(reconnectUri, attemptNumber, delay));
                    
                    this.logger?.LogInformation("Reconnecting to {Uri} in {Delay}ms (attempt {AttemptNumber})", 
                        reconnectUri, delay.TotalMilliseconds, attemptNumber);
                    
                    // Wait before reconnecting
                    await Task.Delay(delay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    this.logger?.LogInformation("WebSocket reconnection cancelled");
                    break;
                }
                catch (Exception ex)
                {
                    this.logger?.LogError(ex, "Connection error (attempt {AttemptNumber})", attemptNumber);
                    
                    if (!this.options.ReconnectOptions.Enabled)
                    {
                        this.ChangeConnectionState(WebSocketConnectionState.Failed, ex);
                        break;
                    }
                    
                    // Error occurred, will try to reconnect in next iteration
                    this.ChangeConnectionState(WebSocketConnectionState.Reconnecting, ex);
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
            var shinobiWebSocket = (ShinobiWebSocket)this.webSocket;
            
            this.ChangeConnectionState(WebSocketConnectionState.Connected);
            
            // Trigger connect interceptors
            if (this.OnConnectChainAsync != null)
            {
                await this.OnConnectChainAsync(shinobiWebSocket, cancellationToken);
            }
        }

        /// <summary>
        /// Calculates the delay for the next reconnect attempt using exponential backoff with jitter
        /// </summary>
        private TimeSpan CalculateReconnectDelay(int attemptNumber)
        {
            var baseDelay = this.options.ReconnectOptions.InitialDelay.TotalMilliseconds;
            var multiplier = Math.Pow(this.options.ReconnectOptions.BackoffMultiplier, attemptNumber - 1);
            var delayMs = baseDelay * multiplier;
            
            // Apply maximum delay
            delayMs = Math.Min(delayMs, this.options.ReconnectOptions.MaxDelay.TotalMilliseconds);
            
            // Apply jitter
            if (this.options.ReconnectOptions.Jitter > 0)
            {
                var jitterRange = delayMs * this.options.ReconnectOptions.Jitter;
                var jitter = (this.random.NextDouble() - 0.5) * 2 * jitterRange;
                delayMs += jitter;
            }
            
            return TimeSpan.FromMilliseconds(Math.Max(0, delayMs));
        }

        private async Task HandleMessagesAsync(CancellationToken cancellationToken)
        {
            if (this.webSocket == null)
                return;

            var shinobiWebSocket = (ShinobiWebSocket)this.webSocket;

            try
            {
                var buffer = new byte[4096];
                var messageStream = new MemoryStream();

                while (this.webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    var segment = new ArraySegment<byte>(buffer);
                    var result = await this.webSocket.ReceiveAsync(segment, cancellationToken);

                    switch (result.MessageType)
                    {
                        case WebSocketMessageType.Text:
                            messageStream.Write(buffer, 0, result.Count);
                            if (result.EndOfMessage)
                            {
                                messageStream.Position = 0;
                                if (this.OnMessageChainAsync != null)
                                {
                                    await this.OnMessageChainAsync(shinobiWebSocket, MessageType.Text, messageStream, cancellationToken);
                                }
                                messageStream = new MemoryStream();
                            }
                            break;

                        case WebSocketMessageType.Binary:
                            messageStream.Write(buffer, 0, result.Count);
                            if (result.EndOfMessage)
                            {
                                messageStream.Position = 0;
                                if (this.OnMessageChainAsync != null)
                                {
                                    await this.OnMessageChainAsync(shinobiWebSocket, MessageType.Binary, messageStream, cancellationToken);
                                }
                                messageStream = new MemoryStream();
                            }
                            break;

                        case WebSocketMessageType.Close:
                            if (this.OnCloseChainAsync != null)
                            {
                                await this.OnCloseChainAsync(shinobiWebSocket, cancellationToken);
                            }
                            return;
                    }
                }
            }
            catch (Exception ex)
            {
                if (this.OnErrorChainAsync != null)
                {
                    await this.OnErrorChainAsync(shinobiWebSocket, ex, cancellationToken);
                }
                throw; // Re-throw to trigger reconnect logic
            }
        }

        #region WebSocket Connection Logic (moved from WebSocketClientFactory)

        private const string NewLine = "\r\n";

        /// <summary>
        /// Performs the complete WebSocket connection process
        /// </summary>
        private async ValueTask<WebSocket> PerformWebSocketConnectionAsync(Uri uri, WebSocketClientOptions options, CancellationToken cancellationToken)
        {
            var guid = Guid.NewGuid();
            var uriScheme = uri.Scheme.ToLower();

            return await this.PerformHandshakeAsync(
                guid,
                uri,
                await this.GetStreamAsync(
                    guid,
                    uriScheme == "wss" || uriScheme == "https",
                    options.NoDelay,
                    uri.Host,
                    uri.Port,
                    cancellationToken).ConfigureAwait(false),
                options,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Connects with a stream that has already been opened and HTTP websocket upgrade request sent
        /// This function will check the handshake response from the server and proceed if successful
        /// </summary>
        private async ValueTask<WebSocket> ConnectAsync(Guid guid, Stream responseStream, string secWebSocketKey, TimeSpan keepAliveInterval, string? secWebSocketExtensions, bool includeExceptionInCloseResponse, CancellationToken cancellationToken)
        {
            Events.Log?.ReadingHttpResponse(guid);
            HttpResponse? response;

            try
            {
                response = await HttpResponse.ReadAsync(responseStream, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Events.Log?.ReadHttpResponseError(guid, ex);
                throw new WebSocketHandshakeFailedException("Handshake unexpected failure", ex);
            }

            this.ThrowIfInvalidResponseCode(response);
            this.ThrowIfInvalidAcceptString(guid, response!, secWebSocketKey);

            return new ShinobiWebSocket(
                new WebSocketHttpContext(response!, responseStream, guid),
                response!.GetHeaderValuesCombined("Sec-WebSocket-Extensions")?.ParseExtension(),
                keepAliveInterval,
                includeExceptionInCloseResponse,
                true,
                response.GetHeaderValuesCombined("Sec-WebSocket-Protocol"));
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
                Events.Log?.HandshakeFailure(guid, warning);
                throw new WebSocketHandshakeFailedException(warning);
            }

            Events.Log?.ClientHandshakeSuccess(guid);
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
        protected async virtual ValueTask<Stream> GetStreamAsync(Guid loggingGuid, bool isSecure, bool noDelay, string host, int port, CancellationToken cancellationToken)
        {
            var tcpClient = new TcpClient { NoDelay = noDelay };
            if (IPAddress.TryParse(host, out var ipAddress))
            {
                Events.Log?.ClientConnectingToIpAddress(loggingGuid, ipAddress.ToString(), port);
                await tcpClient.ConnectAsync(ipAddress, port).ConfigureAwait(false);
            }
            else
            {
                Events.Log?.ClientConnectingToHost(loggingGuid, host, port);
                await tcpClient.ConnectAsync(host, port).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var stream = tcpClient.GetStream();

            if (isSecure)
            {
                var sslStream = new SslStream(stream, false, new RemoteCertificateValidationCallback(ValidateServerCertificate), null);
                Events.Log?.AttemtingToSecureSslConnection(loggingGuid);

                // This will throw an AuthenticationException if the certificate is not valid
                this.TlsAuthenticateAsClient(sslStream, host);
                Events.Log?.ConnectionSecured(loggingGuid);
                return sslStream;
            }

            Events.Log?.ConnectionNotSecure(loggingGuid);
            return stream;
        }

        /// <summary>
        /// Invoked by the RemoteCertificateValidationDelegate
        /// If you want to ignore certificate errors (for debugging) then return true
        /// </summary>
        private static bool ValidateServerCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
                return true;

            Events.Log?.SslCertificateError(sslPolicyErrors);
            // TODO: Add option on new server to "ignore certificate errors"

            // Do not allow this client to communicate with unauthenticated servers.
            return false;
        }

        private ValueTask<WebSocket> PerformHandshakeAsync(Guid guid, Uri uri, Stream stream, WebSocketClientOptions options, CancellationToken cancellationToken)
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
            Events.Log?.HandshakeSent(guid, handshakeHttpRequest);
            return this.ConnectAsync(stream, secWebSocketKey, options, cancellationToken);
        }

        /// <summary>
        /// Connects with a stream and performs handshake validation
        /// </summary>
        private ValueTask<WebSocket> ConnectAsync(Stream responseStream, string secWebSocketKey, WebSocketClientOptions options, CancellationToken cancellationToken)
            => this.ConnectAsync(Guid.NewGuid(),
                responseStream,
                secWebSocketKey,
                options.KeepAliveInterval,
                options.SecWebSocketExtensions,
                options.IncludeExceptionInCloseResponse,
                cancellationToken);

        #endregion

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