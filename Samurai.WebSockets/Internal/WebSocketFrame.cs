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

        public readonly bool IsCompressed;

        public WebSocketFrame(
            bool isFinBitSet,
            WebSocketOpCode webSocketOpCode,
            int count,
            ArraySegment<byte> maskKey,
            bool isCompressed = false,
            WebSocketCloseStatus? closeStatus = null,
            string? closeStatusDescription = null)
        {
            this.IsFinBitSet = isFinBitSet;
            this.OpCode = webSocketOpCode;
            this.Count = count;
            this.MaskKey = maskKey;
            this.IsCompressed = isCompressed;
            this.CloseStatus = closeStatus;
            this.CloseStatusDescription = closeStatusDescription;
        }
    }
}