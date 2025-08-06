using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Samurai.WebSockets.Extensions;

namespace Samurai.WebSockets
{
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

        public SamuraiServer(ILogger<SamuraiServer> logger, ushort port)
        {
            this.logger = logger;
            this.port = port;
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

        public Func<WebSocketHttpContext, ValueTask<HttpErrorr?>>? OnAcceptAsync { get; }

        private async Task ProcessTcpClientAsync(TcpClient tcpClient, CancellationToken cancellationToken)
        {
            using (var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                WebSocketHttpContext? context = null;
                try
                {
                    if (this.isDisposed)
                        return;


                    // this worker thread stays alive until either of the following happens:
                    // Client sends a close connection request OR
                    // An unhandled exception is thrown OR
                    // The server is disposed
                    this.logger.LogInformation("Server: Connection opened.");
                    var stream = this.Certificate is null ? tcpClient.GetStream() : await this.GetStreamAsync(tcpClient.GetStream(), this.Certificate);

                    context = new WebSocketHttpContext(await HttpRequest.ReadAsync(stream, cancellationToken).ConfigureAwait(false), stream);

                    if (context.IsWebSocketRequest)
                    {
                        if (this.OnAcceptAsync != null)
                        {
                            var error = await this.OnAcceptAsync(context);
                            if (error.HasValue)
                            {
                                var response = error.Value.Header.ToHttpResponse(error.Value.Messsage, error.Value.Body);
                                this.logger.LogInformation("Http accept was declined: {StatusCode}, {Message}", error.Value.Header.StatusCode, error.Value.Messsage);
                                await context.Stream.WriteHttpHeaderAsync(response, cancellationToken).ConfigureAwait(false);
                                context.Stream.Close();
                                return;
                            }

                            await this.HandleWebSocketAsync(new SamuraiServerClient(await context.AcceptWebSocketAsync(this.options, cancellationToken).ConfigureAwait(false)), source.Token).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        var response = HttpResponse.Create(426)
                            .AddHeader("Upgrade", "websocket")
                            .AddHeader("Connection", "close")
                            .AddHeader("Content-Type", "text/plain")
                            .ToHttpResponse("Upgrade Required", "WebSocket connection required. Use a WebSocket client.");

                        this.logger.LogInformation("Http header contains no web socket upgrade request. Close");
                        await context.Stream.WriteHttpHeaderAsync(response, cancellationToken).ConfigureAwait(false);
                        context.Stream.Close();
                    }

                    this.logger.LogInformation("Server: Connection closed");
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
                            .ToHttpResponse("Internal Server Error", ex.Message);

                        await context.Stream.WriteHttpHeaderAsync(response, cancellationToken).ConfigureAwait(false);
                        context.Stream.Close();
                    }

                    this.logger.LogError(ex, "Failure at the TCP connection");
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
            var guid = client.WebSocket.Guid;
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


    public struct HttpErrorr
    {
        public HttpResponse Header { get; }
        public string Messsage { get; }
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
