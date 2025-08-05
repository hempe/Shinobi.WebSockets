using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;



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
    }
}