using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Samurai.WebSockets.Exceptions;
using Samurai.WebSockets.Extensions;
using Samurai.WebSockets.Internal;
using Samurai.WebSockets.Utils;

namespace Samurai.WebSockets
{
    public enum MessageType
    {
        /// <summary>
        /// The message is clear text.
        /// </summary>
        Text = 0,

        /// <summary>
        /// The message is clear text.
        /// </summary>
        Binary = 1,
    }

    public class SamuraiServer : IDisposable
    {
        private Task? runTask;
        private CancellationTokenSource? runToken;
        private TcpListener? listener; // Stop calls dispose, but dispose does not exist on net472

        private bool isDisposed;
        private readonly ILogger<SamuraiServer>? logger;
        private readonly WebSocketServerOptions options;

        private readonly AcceptStreamHandler OnConnectStreamsAsync;
        private readonly CertificateSelectionHandler SelectionCertificateAsync;
        private readonly HandshakeHandler OnHandshakeAsync;
        private readonly WebSocketConnectHandler OnConnectAsync;
        private readonly WebSocketCloseHandler OnCloseAsync;
        private readonly WebSocketErrorHandler OnErrorAsync;
        private readonly WebSocketMessageHandler OnMessageAsync;

        public SamuraiServer(
            WebSocketServerOptions options,
            ILogger<SamuraiServer>? logger = null)
        {
            this.logger = logger;
            this.options = options;

            // Use the specific builders
            this.OnConnectStreamsAsync = Builder.BuildAcceptStreamChain(this.AcceptStreamCoreAsync, options.OnAcceptStream);
            this.SelectionCertificateAsync = Builder.BuildCertificateSelectionChain(this.SelectionCertificateCoreAsync, options.OnSelectionCertificate);
            this.OnHandshakeAsync = Builder.BuildHandshakeChain(this.HandshakeCoreAsync, options.OnHandshake);
            this.OnConnectAsync = Builder.BuildWebSocketConnectChain(options.OnConnect);
            this.OnCloseAsync = Builder.BuildWebSocketCloseChain(options.OnClose);
            this.OnErrorAsync = Builder.BuildWebSocketErrorChain(options.OnError);
            this.OnMessageAsync = Builder.BuildWebSocketMessageChain(options.OnMessage);
        }
        public async Task StopAsync()
        {
            var task = this.runTask;
            this.runTask = null;
            if (task == null)
                return;

            this.runToken?.Cancel();

            try
            {
                this.listener?.Server?.Close();
                this.listener?.Stop();
            }
            catch
            {
                // No op
            }

            await task;
        }

        public Task StartAsync()
        {
            if (this.runTask != null)
                return Task.CompletedTask;

            this.runToken = new CancellationTokenSource();
            this.runTask = this.ListenAsync(this.options.Port, this.runToken);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            this.Dispose(disposing: true);
            GC.SuppressFinalize(this);

        }

        private ValueTask<HttpResponse> HandshakeCoreAsync(WebSocketHttpContext context, CancellationToken cancellationToken)
        {
            if (context.IsWebSocketRequest)
                return new ValueTask<HttpResponse>(context.HandshakeResponse(this.options));

            var response = HttpResponse.Create(426)
                .AddHeader("Upgrade", "websocket")
                .AddHeader("Connection", "close")
                .AddHeader("Content-Type", "text/plain")
                .WithBody("WebSocket connection required. Use a WebSocket client.");

            this.logger?.LogInformation("Http header contains no web socket upgrade request. Close");
            return new ValueTask<HttpResponse>(response);
        }

        private ValueTask<X509Certificate2?> SelectionCertificateCoreAsync(TcpClient tcpClient, CancellationToken _cancellationToken)
        {
            this.ThrowIfDisposed();
            return new ValueTask<X509Certificate2?>((X509Certificate2?)null);
        }


        private async ValueTask<Stream> AcceptStreamCoreAsync(TcpClient tcpClient, CancellationToken cancellationToken)
        {
            this.ThrowIfDisposed();
            var certificate = await this.SelectionCertificateAsync(tcpClient, cancellationToken);
            return certificate is null ? tcpClient.GetStream() : await this.GetStreamAsync(tcpClient.GetStream(), certificate);
        }

        /// <summary>
        /// Task listens to incoming messages on the defined port
        /// </summary>
        /// <param name="port">Port to be listened to</param>
        /// <returns>returns the listener</returns>
        private async Task ListenAsync(int port, CancellationTokenSource cancellationToken)
        {
            using (cancellationToken)
            {
                try
                {
                    this.listener = new TcpListener(IPAddress.Any, port);
                    this.listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    this.listener.Start();
                    this.logger?.LogInformation("Server started listening on port {Port}", port);

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        TcpClient tcpClient = await this.listener.AcceptTcpClientAsync();
                        _ = Task.Run(() => this.ProcessTcpClientAsync(tcpClient, cancellationToken.Token));
                    }
                }
                catch (SocketException ex)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    this.logger?.LogError(ex, "Error listening on port {Port}. Make sure IIS or another application is not running and consuming your port.", port);
                }
            }
        }


        private async Task<Stream> GetStreamAsync(Stream stream, X509Certificate2 certificate)
        {
            try
            {
                var sslStream = new SslStream(stream, true);
                this.logger?.LogInformation("Attempting to secure connection...");

#if NET8_0_OR_GREATER
                await sslStream.AuthenticateAsServerAsync(certificate, false, SslProtocols.Tls12 | SslProtocols.Tls13, false);
#else
                await sslStream.AuthenticateAsServerAsync(certificate, false, SslProtocols.Tls12, false);
#endif
                this.logger?.LogInformation("Connection successfully secured");
                return sslStream;
            }
            catch (ObjectDisposedException)
            {
                throw;
            }
            catch (Exception e)
            {
                this.logger?.LogError(e, "Failed to upgrade stream to ssl stream.");
                if (e.InnerException != null)
                {
                    this.logger?.LogError(e.InnerException, "Failed to upgrade stream to ssl stream, inner exception: {Message}.", e.Message);
                }

                throw;
            }
        }

        private void ThrowIfDisposed()
        {
            if (this.isDisposed)
                throw new ObjectDisposedException(nameof(SamuraiServer));
        }

        private async Task ProcessTcpClientAsync(TcpClient tcpClient, CancellationToken cancellationToken)
        {
            using (tcpClient)
            using (var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                WebSocketHttpContext? context = null;
                try
                {
                    this.ThrowIfDisposed();

                    // this worker thread stays alive until either of the following happens:
                    // Client sends a close connection request OR
                    // An unhandled exception is thrown OR
                    // The server is disposed

                    var stream = await this.OnConnectStreamsAsync(tcpClient, source.Token);

                    var httpRequest = await HttpRequest.ReadAsync(stream, source.Token).ConfigureAwait(false);
                    if (httpRequest is null)
                    {
                        var response = HttpResponse.Create(400).AddHeader("Content-Type", "text/plain");

                        await response.WriteToStreamAsync(stream, source.Token).ConfigureAwait(false);
                        stream.Close();
                        return;
                    }

                    var guid = Guid.NewGuid();
                    Events.Log?.AcceptWebSocketStarted(guid);
                    context = new WebSocketHttpContext(httpRequest, stream, guid);
                    var handshakeResponse = await this.OnHandshakeAsync(context, source.Token);
                    if (handshakeResponse.StatusCode == 101)
                    {
                        SamuraiWebSocket? webSocket = null;
                        try
                        {
                            Events.Log?.SendingHandshakeResponse(guid, handshakeResponse.StatusCode);
                            await handshakeResponse.WriteToStreamAsync(context.Stream, source.Token).ConfigureAwait(false);
                            var usePermessageDeflate = handshakeResponse.GetHeaderValue("Sec-WebSocket-Extensions")?.Contains("permessage-deflate") == true;
                            webSocket = new SamuraiWebSocket(
                                context,
                                this.options.KeepAliveInterval,
                                usePermessageDeflate, this.options.IncludeExceptionInCloseResponse,
                                false,
                                handshakeResponse.GetHeaderValuesCombined("Sec-WebSocket-Protocol"));

                            await this.OnConnectAsync(webSocket, source.Token);
                            await this.HandleWebSocketAsync(webSocket, source.Token);

                        }
                        catch (WebSocketVersionNotSupportedException ex)
                        {
                            Events.Log?.WebSocketVersionNotSupported(guid, ex);
                            var response = HttpResponse.Create(426)
                                .AddHeader("Sec-WebSocket-Version", "13")
                                .WithBody(ex.Message);
                            Events.Log?.SendingHandshakeResponse(guid, response.StatusCode);
                            await context.TerminateAsync(response, source.Token).ConfigureAwait(false);
                            throw;
                        }
                        catch (Exception ex)
                        {
                            Events.Log?.BadRequest(guid, ex);

                            if (webSocket?.State == WebSocketState.Open)
                            {
                                await webSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, ex.Message, source.Token).ConfigureAwait(false);
                                throw;
                            }

                            var response = HttpResponse.Create(400)
                                .WithBody(ex.Message);

                            Events.Log?.SendingHandshakeResponse(guid, response.StatusCode);
                            await context.TerminateAsync(response, cancellationToken).ConfigureAwait(false);
                            throw;
                        }
                    }
                    else
                    {
                        Events.Log?.SendingHandshakeResponse(guid, handshakeResponse.StatusCode);
                        await context.TerminateAsync(handshakeResponse, source.Token).ConfigureAwait(false);
                    }
                }
                catch (ObjectDisposedException)
                {
                    // do nothing. This will be thrown if the Listener has been stopped
                }
                catch (Exception ex)
                {
                    if (source.IsCancellationRequested)
                        return;

                    if (context != null && context.Stream.CanWrite)
                    {
                        var response = HttpResponse.Create(500)
                            .AddHeader("Content-Type", "text/plain")
                            .WithBody(ex.Message);

                        await context.TerminateAsync(response, cancellationToken).ConfigureAwait(false);
                    }

                    this.logger?.LogError(ex, "Failure at the TCP connection");
                }
                finally
                {
                    this.logger?.LogInformation("Server: Connection closed");
                }
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!this.isDisposed)
            {
                if (disposing)
                {
                    this.runToken?.Dispose();
                }

                this.isDisposed = true;
            }
        }

        private async Task HandleWebSocketAsync(SamuraiWebSocket client, CancellationToken cancellationToken)
        {
            using var receiveBuffer = new ArrayPoolStream();
            WebSocketMessageType? currentMessageType = null;

            try
            {
                while (!cancellationToken.IsCancellationRequested && !this.isDisposed)
                {
                    var result = await client.ReceiveAsync(receiveBuffer, cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await this.OnCloseAsync(client, cancellationToken);
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
                        if (client.PermessageDeflate && false)
                        {
                            using var deflateStream = new DeflateStream(receiveBuffer, CompressionMode.Decompress, leaveOpen: true);
                            using var decompressedStream = new Samurai.WebSockets.ArrayPoolStream();
                            deflateStream.CopyTo(decompressedStream);
                            decompressedStream.Position = 0;
                            await this.OnMessageAsync(client, (MessageType)result.MessageType, decompressedStream, cancellationToken);
                        }
                        else
                        {
                            await this.OnMessageAsync(client, (MessageType)result.MessageType, receiveBuffer, cancellationToken);
                        }
                        receiveBuffer.SetLength(0);
                        currentMessageType = null; // Reset for next message
                    }
                }
            }
            catch when (cancellationToken.IsCancellationRequested)
            {
                await this.OnCloseAsync(client, cancellationToken);
            }
            catch (Exception e)
            {
                await this.OnErrorAsync(client, e, cancellationToken);
            }
        }
    }
}
