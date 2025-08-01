using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Samurai.WebSockets;

namespace WebSockets.DemoServer
{
    public class WebServer : IDisposable
    {
        private TcpListener? listener;
        private bool isDisposed = false;
        private ILogger logger;
        private readonly IWebSocketServerFactory webSocketServerFactory;
        private readonly HashSet<string> supportedSubProtocols;

        // const int BUFFER_SIZE = 1 * 1024 * 1024 * 1024; // 1GB
        private const int BUFFER_SIZE = 4 * 1024 * 1024; // 4MB

        public WebServer(IWebSocketServerFactory webSocketServerFactory, ILoggerFactory loggerFactory, IList<string>? supportedSubProtocols = null)
        {
            this.logger = loggerFactory.CreateLogger<WebServer>();
            this.webSocketServerFactory = webSocketServerFactory;
            this.supportedSubProtocols = new HashSet<string>(supportedSubProtocols ?? new string[0]);
        }

        private void ProcessTcpClient(TcpClient tcpClient)
        {
            Task.Run(() => this.ProcessTcpClientAsync(tcpClient));
        }

        private string? GetSubProtocol(IList<string> requestedSubProtocols)
        {
            foreach (string subProtocol in requestedSubProtocols)
            {
                // match the first sub protocol that we support (the client should pass the most preferable sub protocols first)
                if (this.supportedSubProtocols.Contains(subProtocol))
                {
                    this.logger.LogInformation($"Http header has requested sub protocol {subProtocol} which is supported");
                    return subProtocol;
                }
            }

            if (requestedSubProtocols.Count > 0)
            {
                this.logger.LogWarning($"Http header has requested the following sub protocols: {string.Join(", ", requestedSubProtocols)}. There are no supported protocols configured that match.");
            }

            return null;
        }

        private async Task ProcessTcpClientAsync(TcpClient tcpClient)
        {
            using var source = new CancellationTokenSource();
            using (tcpClient)
            {
                try
                {
                    if (this.isDisposed)
                    {
                        return;
                    }

                    // this worker thread stays alive until either of the following happens:
                    // Client sends a close conection request OR
                    // An unhandled exception is thrown OR
                    // The server is disposed
                    this.logger.LogInformation("Server: Connection opened. Reading Http header from stream");

                    // get a secure or insecure stream
                    Stream stream = tcpClient.GetStream();
                    WebSocketHttpContext context = await this.webSocketServerFactory.ReadHttpHeaderFromStreamAsync(stream);
                    if (context.IsWebSocketRequest)
                    {
                        var subProtocol = this.GetSubProtocol(context.WebSocketRequestedProtocols);
                        var options = new WebSocketServerOptions() { KeepAliveInterval = TimeSpan.FromSeconds(30), SubProtocol = subProtocol };
                        this.logger.LogInformation("Http header has requested an upgrade to Web Socket protocol. Negotiating Web Socket handshake");

                        WebSocket webSocket = await this.webSocketServerFactory.AcceptWebSocketAsync(context, options);

                        this.logger.LogInformation("Web Socket handshake response sent. Stream ready.");
                        await this.RespondToWebSocketRequestAsync(webSocket, source.Token);
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
                    this.logger.LogError(ex.ToString());
                }
                finally
                {
                    try
                    {
                        tcpClient.Client.Close();
                        tcpClient.Close();
                        source.Cancel();
                    }
                    catch (Exception ex)
                    {
                        this.logger.LogError($"Failed to close TCP connection: {ex}");
                    }
                }
            }
        }

        public async Task RespondToWebSocketRequestAsync(WebSocket webSocket, CancellationToken cancellationToken)
        {
            var buffer = new ArraySegment<byte>(new byte[BUFFER_SIZE]);

            while (true)
            {
                WebSocketReceiveResult result = await webSocket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    this.logger.LogInformation($"Client initiated close. Status: {result.CloseStatus} Description: {result.CloseStatusDescription}");
                    break;
                }

                if (result.Count > BUFFER_SIZE)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.MessageTooBig,
                        $"Web socket frame cannot exceed buffer size of {BUFFER_SIZE:#,##0} bytes. Send multiple frames instead.",
                        cancellationToken);
                    break;
                }

                // just echo the message back to the client
                ArraySegment<byte> toSend = new ArraySegment<byte>(buffer.Array!, buffer.Offset, result.Count);
                await webSocket.SendAsync(toSend, WebSocketMessageType.Binary, true, cancellationToken);
            }
        }

        public async Task ListenAsync(int port)
        {
            try
            {
                var localAddress = IPAddress.Any;
                this.listener = new TcpListener(localAddress, port);
                this.listener.Start();
                this.logger.LogInformation($"Server started listening on port {port}");
                while (true)
                {
                    var tcpClient = await this.listener.AcceptTcpClientAsync();
                    this.ProcessTcpClient(tcpClient);
                }
            }
            catch (SocketException ex)
            {
                string message = string.Format("Error listening on port {0}. Make sure IIS or another application is not running and consuming your port.", port);
                throw new Exception(message, ex);
            }
        }

        public void Dispose()
        {
            if (!this.isDisposed)
            {
                this.isDisposed = true;

                // safely attempt to shut down the listener
                try
                {
                    if (this.listener != null)
                    {
                        if (this.listener.Server != null)
                        {
                            this.listener.Server.Close();
                        }

                        this.listener.Stop();
                    }
                }
                catch (Exception ex)
                {
                    this.logger.LogError(ex.ToString());
                }

                this.logger.LogInformation("Web Server disposed");
            }
        }
    }
}
