using System;
using System.Net.WebSockets;

namespace Samurai.WebSockets.Internal
{
    internal readonly struct WebSocketFrame
    {
        public readonly bool IsFinBitSet;

        public readonly WebSocketOpCode OpCode;

        public readonly int Count;

        public readonly WebSocketCloseStatus? CloseStatus;

        public readonly string? CloseStatusDescription;

        public readonly ArraySegment<byte> MaskKey;

        public WebSocketFrame(
            bool isFinBitSet,
            WebSocketOpCode webSocketOpCode,
            int count,
            ArraySegment<byte> maskKey,
            WebSocketCloseStatus? closeStatus = null,
            string? closeStatusDescription = null)
        {
            this.IsFinBitSet = isFinBitSet;
            this.OpCode = webSocketOpCode;
            this.Count = count;
            this.MaskKey = maskKey;
            this.CloseStatus = closeStatus;
            this.CloseStatusDescription = closeStatusDescription;
        }
    }
}
