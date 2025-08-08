using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
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


    public delegate ValueTask<Stream> AcceptStreamHandler(TcpClient tcpClient, CancellationToken cancellationToken);
    public delegate ValueTask<Stream> AcceptStreamInterceptor(TcpClient tcpClient, CancellationToken cancellationToken, AcceptStreamHandler next);

    public class Interceptors
    {
        public IEnumerable<Next<TcpClient, Stream>>? AcceptStream { get; set; }
        public IEnumerable<Next<WebSocketHttpContext, HttpResponse>>? Handshake { get; set; }
    }

    public class SamuraiServer : IDisposable
    {
        private readonly ConcurrentDictionary<Guid, SamuraiServerClient> clients = new ConcurrentDictionary<Guid, SamuraiServerClient>();

        private Task? runTask;
        private CancellationTokenSource? runToken;
        private TcpListener? listener; // Stop calls dispose, but dispose does not exist on net472

        private bool isDisposed;
        private readonly ushort port;
        private readonly ILogger<SamuraiServer> logger;
        private readonly WebSocketServerOptions options = new WebSocketServerOptions();
        private readonly Invoke<TcpClient, Stream> ConnectStreamsAsync;
        private readonly Invoke<WebSocketHttpContext, HttpResponse> HandshakeAsync;


        public SamuraiServer(
            ILogger<SamuraiServer> logger,
            Interceptors interceptors,
            ushort port)
        {
            this.logger = logger;
            this.port = port;

            this.ConnectStreamsAsync = Builder.BuildInterceptorChain(this.AcceptStreamCoreAsync, interceptors.AcceptStream);
            this.HandshakeAsync = Builder.BuildInterceptorChain(this.HandshakeCoreAsync, interceptors.Handshake);
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

            this.logger.LogInformation("Http header contains no web socket upgrade request. Close");
            return new ValueTask<HttpResponse>(response);
        }


        private async ValueTask<Stream> AcceptStreamCoreAsync(TcpClient tcpClient, CancellationToken _cancellationToken)
        {
            this.ThrowIfDisposed();
            return this.Certificate is null ? tcpClient.GetStream() : await this.GetStreamAsync(tcpClient.GetStream(), this.Certificate);
        }

        public X509Certificate2? Certificate { private get; set; }

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
            this.runTask = this.ListenAsync(this.port, this.runToken);
            return Task.CompletedTask;
        }
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            this.Dispose(disposing: true);
            GC.SuppressFinalize(this);

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
                    this.logger.LogInformation("Server started listening on port {Port}", port);

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        TcpClient tcpClient = await this.listener.AcceptTcpClientAsync();
                        this.ProcessTcpClient(tcpClient, cancellationToken.Token);
                    }
                }
                catch (SocketException ex)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    this.logger.LogError(ex, "Error listening on port {Port}. Make sure IIS or another application is not running and consuming your port.", port);
                }
            }
        }


        private void ProcessTcpClient(TcpClient tcpClient, CancellationToken cancellationToken)
        {
            Task.Run(() => this.ProcessTcpClientAsync(tcpClient, cancellationToken));
        }


        private async Task<Stream> GetStreamAsync(Stream stream, X509Certificate2 certificate)
        {
            try
            {
                var sslStream = new SslStream(stream, true);
                this.logger.LogInformation("Attempting to secure connection...");

#if NET8_0_OR_GREATER
                await sslStream.AuthenticateAsServerAsync(certificate, false, SslProtocols.Tls12 | SslProtocols.Tls13, false);
#else
                await sslStream.AuthenticateAsServerAsync(certificate, false, SslProtocols.Tls12, false);
#endif
                this.logger.LogInformation("Connection successfully secured");
                return sslStream;
            }
            catch (ObjectDisposedException)
            {
                throw;
            }
            catch (Exception e)
            {
                this.logger.LogError(e, "Failed to upgrade stream to ssl stream.");
                if (e.InnerException != null)
                {
                    this.logger.LogError(e.InnerException, "Failed to upgrade stream to ssl stream, inner exception: {Message}.", e.Message);
                }

                throw;
            }
        }

        private void ThrowIfDisposed()
        {
            if (this.isDisposed)
                throw new ObjectDisposedException(nameof(ArrayPoolStream));
        }

        private async Task ProcessTcpClientAsync(TcpClient tcpClient, CancellationToken cancellationToken)
        {
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
                    this.logger.LogInformation("Server: Connection opened.");
                    var stream = await this.ConnectStreamsAsync(tcpClient, source.Token);

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
                    var handshakeResponse = await this.HandshakeAsync(context, source.Token);
                    if (handshakeResponse.StatusCode == 101)
                    {
                        try
                        {
                            Events.Log?.SendingHandshakeResponse(guid, handshakeResponse.StatusCode);
                            await handshakeResponse.WriteToStreamAsync(context.Stream, cancellationToken).ConfigureAwait(false);
                            var usePermessageDeflate = handshakeResponse.GetHeaderValue("Sec-WebSocket-Extensions")?.Contains("permessage-deflate") == true;
                            var webSocket = new SamuraiWebSocket(
                                context,
                                this.options.KeepAliveInterval,
                                usePermessageDeflate, this.options.IncludeExceptionInCloseResponse,
                                false,
                                this.options.SubProtocol);



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

                    this.logger.LogError(ex, "Failure at the TCP connection");
                }
                finally
                {
                    this.logger.LogInformation("Server: Connection closed");

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

        private async Task HandleWebSocketAsync(SamuraiServerClient client, CancellationToken cancellationToken)
        {
            var guid = client.WebSocket.Context.Guid;
            try
            {
                this.clients.TryAdd(guid, client);
                // TODO OnConnectAsync?
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    //                    var listener = Task.Run(async () => await this.HandleWebSocketMessagesAsync(client, cts).ConfigureAwait(false));
                    //                    var sender = Task.Run(async () => await this.HandleWebSocketHeartbeatSendingAsync(client, cts.Token).ConfigureAwait(false));
                    //                    await Task.WhenAny(listener, sender).ConfigureAwait(false);
                    //                    cts.Cancel();

                    //                    await Task.WhenAll(listener, sender).ConfigureAwait(false);
                }
            }
            finally
            {
                this.clients.TryRemove(guid, out _);
            }
        }
    }


    public struct HttpError
    {
        public HttpResponse Header { get; }
        public string? Body { get; }
    }

    public class SamuraiServerClient
    {
        public readonly SamuraiWebSocket WebSocket;
        public Dictionary<string, string> Info = new Dictionary<string, string>();

        public SamuraiServerClient(SamuraiWebSocket webSocket)
        {
            this.WebSocket = webSocket;
        }
    }
}
