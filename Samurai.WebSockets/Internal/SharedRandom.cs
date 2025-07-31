using System;
using System.Buffers;

namespace Samurai.WebSockets.Internal
{
    /// <summary>
    /// This is not used for cryptography so doing something simple like the code below is okay.
    /// It is used to generate random bytes for WebSocket keys and other purposes.
    /// </summary>
    internal static class SharedRandom
    {
        [ThreadStatic]
        private static Random rand;
        public static byte[] NextBytes(int size)
        {
            if (rand == null)
                rand = new Random((int)DateTime.Now.Ticks);

            var bytes = ArrayPool<byte>.Shared.Rent(size);
            rand.NextBytes(bytes);
            return bytes;
        }

        public static ArraySegment<byte> MaskKey()
        {
            if (rand == null)
                rand = new Random((int)DateTime.Now.Ticks);
            var bytes = new byte[WebSocketFrameCommon.MaskKeyLength];
            rand.NextBytes(bytes);
            return new ArraySegment<byte>(bytes, 0, WebSocketFrameCommon.MaskKeyLength);
        }

        public static string SecWebSocketKey()
        {
            var keyAsBytes = NextBytes(16);
            try
            {
                return Convert.ToBase64String(keyAsBytes);
            }
            finally
            {
                ReturnBytes(keyAsBytes);
            }
        }

        public static void ReturnBytes(byte[] bytes)
            => ArrayPool<byte>.Shared.Return(bytes, clearArray: false);
    }
}