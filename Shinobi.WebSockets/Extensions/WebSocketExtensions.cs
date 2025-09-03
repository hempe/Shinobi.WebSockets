using System;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Shinobi.WebSockets.Internal;

namespace Shinobi.WebSockets.Extensions
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

        public static async ValueTask ReadAsync(
            this Stream source,
            ArrayPoolStream ms,
            int expectedLength,
            CancellationToken cancellationToken)
        {
            int totalRead = 0;

            while (totalRead < expectedLength)
            {
                // Get a free buffer segment from ms
                var free = ms.GetFreeArraySegment(ms.InitialSize);

                // Limit read size to what’s still needed
                int toRead = Math.Min(free.Count, expectedLength - totalRead);

                // Extend ms so we can write into it
                ms.SetLength(ms.Length + toRead);

                int read;

#if NET8_0_OR_GREATER
                // Modern API: span/memory-based overload
                read = await source.ReadAsync(free.AsMemory(0, toRead), cancellationToken)
                                   .ConfigureAwait(false);
#else
                // Older API: use array-based overload
                read = await source.ReadAsync(free.Array!, free.Offset, toRead, cancellationToken)
                           .ConfigureAwait(false);
#endif
                if (read == 0)
                {
                    throw new EndOfStreamException(
                        $"Stream ended early: expected {expectedLength}, got {totalRead}.");
                }

                // Adjust stream length back if we didn’t fill the full free buffer
                ms.SetLength(ms.Length - toRead + read);
                ms.Position += read;

                totalRead += read;
            }
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