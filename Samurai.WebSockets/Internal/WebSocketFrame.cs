using System;
using System.Net.WebSockets;

namespace Samurai.WebSockets.Internal
{
    internal class WebSocketFrame
    {
        public bool IsFinBitSet { get; }

        public WebSocketOpCode OpCode { get; }

        public int Count { get; }

        public WebSocketCloseStatus? CloseStatus { get; }

        public string? CloseStatusDescription { get; }

        public ArraySegment<byte> MaskKey { get; }

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
