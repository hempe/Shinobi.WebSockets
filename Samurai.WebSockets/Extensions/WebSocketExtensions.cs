using System;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Samurai.WebSockets.Extensions
{
    public static class WebSocketExtensions
    {
        public static async ValueTask<WebSocketReceiveResult> ReceiveAsync(
            this WebSocket webSocket,
            ArrayPoolStream ms,
            CancellationToken cancellationToken)
        {
            var free = ms.GetFreeArraySegment(ms.InitialSize);
            ms.SetLength(ms.Length + free.Count);
            var result = await webSocket.ReceiveAsync(free, cancellationToken);
            ms.SetLength(ms.Length - free.Count + result.Count);
            ms.Position += result.Count;
            return result;
        }

        /// <summary>
        /// Sends a text message through the WebSocket connection (always UTF-8 as per RFC 6455)
        /// </summary>
        /// <param name="webSocket">The WebSocket instance</param>
        /// <param name="message">The text message to send</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the async operation</returns>
        public static async ValueTask SendTextAsync(this WebSocket? webSocket, string? message, CancellationToken cancellationToken = default)
        {
            if (webSocket == null)
                throw new ArgumentNullException(nameof(webSocket));

            if (message == null)
                throw new ArgumentNullException(nameof(message));

            // WebSocket text frames MUST be UTF-8 encoded per RFC 6455
            var bytes = Encoding.UTF8.GetBytes(message);
            var buffer = new ArraySegment<byte>(bytes);

            await webSocket.SendAsync(buffer, WebSocketMessageType.Text, true, cancellationToken);
            /*
                        var chunkSize = (int)Math.Ceiling((double)bytes.Length / 4);
                        var chunks = bytes
                            .Select((b, i) => new { Byte = b, Index = i })
                            .GroupBy(x => x.Index / chunkSize)
                            .Select(g => g.Select(x => x.Byte).ToArray())
                            .ToArray();

                        for (var i = 0; i < chunks.Length; i++)
                        {
                            var endOfMessage = true;
                            await webSocket.SendAsync(new ArraySegment<byte>(chunks[i]), WebSocketMessageType.Text, i == 1, cancellationToken);
                        }
            */
        }

        /// <summary>
        /// Sends binary data through the WebSocket connection
        /// </summary>
        /// <param name="webSocket">The WebSocket instance</param>
        /// <param name="data">The binary data to send</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the async operation</returns>
        public static async ValueTask SendBinaryAsync(this WebSocket? webSocket, byte[]? data, CancellationToken cancellationToken = default)
        {
            if (webSocket == null)
                throw new ArgumentNullException(nameof(webSocket));

            if (data == null)
                throw new ArgumentNullException(nameof(data));

            var buffer = new ArraySegment<byte>(data);

            await webSocket.SendAsync(buffer, WebSocketMessageType.Binary, true, cancellationToken);
        }
    }
}