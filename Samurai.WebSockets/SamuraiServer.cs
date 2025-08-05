using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

namespace Samurai.WebSockets
{
    public class SamuraiServer : IDisposable
    {
        private Task? runTask;
        private CancellationTokenSource? runToken;
        private TcpListener? listener; // Stop calls dispose, but dispose does not exist on net472

        private bool isDisposed;
        private readonly ushort port;
        private readonly ILogger<SamuraiServer> logger;

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



        private async Task<Stream> GetStreamAsync(TcpClient tcpClient)
        {
            var stream = tcpClient.GetStream();
            try
            {
                var sslStream = new SslStream(stream, true);
                this.logger.LogInformation("Attempting to secure connection...");
                var cert = this.Certificate ?? throw new InvalidOperationException("No valid certificate available.");

#if NET8_0_OR_GREATER
                await sslStream.AuthenticateAsServerAsync(cert, false, SslProtocols.Tls12 | SslProtocols.Tls13, false);
#else
                await sslStream.AuthenticateAsServerAsync(cert, false, SslProtocols.Tls12, false);
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


        private async Task ProcessTcpClientAsync(TcpClient tcpClient, CancellationToken cancellationToken)
        {
            using (var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                try
                {
                    if (this.isDisposed)
                        return;

                    // this worker thread stays alive until either of the following happens:
                    // Client sends a close connection request OR
                    // An unhandled exception is thrown OR
                    // The server is disposed
                    this.logger.LogInformation("Server: Connection opened.");
                    var stream = await this.GetStreamAsync(tcpClient);
                    var context = new WebSocketHttpContext(await HttpHeader.ReadHttpHeaderAsync(stream, cancellationToken).ConfigureAwait(false), stream);

                    if (context.IsWebSocketRequest)
                    {
                    }
                    else
                    {
                        this.logger.LogInformation("Http header contains no web socket upgrade request. Ignoring");
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

                    this.logger.LogError(ex, "Failure at the TCP connection");
                    // TODO  return proper rturn 500.
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
    }
}