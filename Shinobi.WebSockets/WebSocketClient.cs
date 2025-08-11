using System;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Shinobi.WebSockets.Internal;
using Shinobi.WebSockets.Http;

namespace Shinobi.WebSockets
{
    public class WebSocketClient : IDisposable
    {
        private WebSocket? webSocket;
        private bool isDisposed;
        private readonly ILogger<WebSocketClient>? logger;
        private readonly WebSocketClientOptions options;

        private readonly WebSocketConnectHandler OnConnectAsync;
        private readonly WebSocketCloseHandler OnCloseAsync;
        private readonly WebSocketErrorHandler OnErrorAsync;
        private readonly WebSocketMessageHandler OnMessageAsync;

        public WebSocketClient(
            WebSocketClientOptions options,
            ILogger<WebSocketClient>? logger = null)
        {
            this.logger = logger;
            this.options = options;

            // Use the specific builders
            this.OnConnectAsync = Builder.BuildWebSocketConnectChain(options.OnConnect);
            this.OnCloseAsync = Builder.BuildWebSocketCloseChain(options.OnClose);
            this.OnErrorAsync = Builder.BuildWebSocketErrorChain(options.OnError);
            this.OnMessageAsync = Builder.BuildWebSocketMessageChain(options.OnMessage);
        }

        public async Task<WebSocket> ConnectAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            var factory = new WebSocketClientFactory();
            this.webSocket = await factory.ConnectAsync(uri, this.options, cancellationToken);

            // Cast to ShinobiWebSocket since we know the factory returns ShinobiWebSocket
            var shinobiWebSocket = (ShinobiWebSocket)this.webSocket;

            // Trigger connect interceptors
            if (this.OnConnectAsync != null)
            {
                await this.OnConnectAsync(shinobiWebSocket, cancellationToken);
            }

            // Start message handling if we have message interceptors
            if (this.OnMessageAsync != null)
            {
                _ = Task.Run(async () => await this.HandleMessagesAsync(cancellationToken), cancellationToken);
            }

            return this.webSocket;
        }

        private async Task HandleMessagesAsync(CancellationToken cancellationToken)
        {
            if (this.webSocket == null || this.OnMessageAsync == null)
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
                                await this.OnMessageAsync(shinobiWebSocket, MessageType.Text, messageStream, cancellationToken);
                                messageStream = new MemoryStream();
                            }
                            break;

                        case WebSocketMessageType.Binary:
                            messageStream.Write(buffer, 0, result.Count);
                            if (result.EndOfMessage)
                            {
                                messageStream.Position = 0;
                                await this.OnMessageAsync(shinobiWebSocket, MessageType.Binary, messageStream, cancellationToken);
                                messageStream = new MemoryStream();
                            }
                            break;

                        case WebSocketMessageType.Close:
                            if (this.OnCloseAsync != null)
                            {
                                await this.OnCloseAsync(shinobiWebSocket, cancellationToken);
                            }
                            return;
                    }
                }
            }
            catch (Exception ex) when (this.OnErrorAsync != null)
            {
                await this.OnErrorAsync(shinobiWebSocket, ex, cancellationToken);
            }
        }

        public async Task CloseAsync(WebSocketCloseStatus closeStatus = WebSocketCloseStatus.NormalClosure, string? statusDescription = null, CancellationToken cancellationToken = default)
        {
            if (this.webSocket?.State == WebSocketState.Open)
            {
                await this.webSocket.CloseAsync(closeStatus, statusDescription, cancellationToken);
                
                if (this.OnCloseAsync != null)
                {
                    var shinobiWebSocket = (ShinobiWebSocket)this.webSocket;
                    await this.OnCloseAsync(shinobiWebSocket, cancellationToken);
                }
            }
        }

        public void Dispose()
        {
            if (this.isDisposed)
                return;

            this.webSocket?.Dispose();
            this.isDisposed = true;
        }
    }
}