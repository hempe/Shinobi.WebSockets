using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

using Samurai.WebSockets.Internal;


namespace Samurai.WebSockets.Extensions
{
    public static class WebSocketExtensions
    {
        public static async Task<WebSocketReceiveResult> ReceiveAsync(
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

        public static async Task<WebSocketReceiveResult> Receive2Async(
            this WebSocket webSocket,
            ArrayPoolStream ms,
            CancellationToken cancellationToken)
        {
            var buffer = Shared.Rent(16 * 1024);
            try
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer, 0, buffer.Length), cancellationToken);
                ms.Write(buffer, 0, result.Count);
                return result;
            }
            finally
            {
                Shared.Return(buffer);
            }
        }
    }
}