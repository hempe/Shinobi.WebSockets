using System;
using System.Buffers;
using System.Net.WebSockets;
using System.Text;

namespace Shinobi.WebSockets.Internal
{
    /// <summary>
    /// This is not used for cryptography so doing something simple like the code below is okay.
    /// It is used to generate random bytes for WebSocket keys and other purposes.
    /// </summary>
    internal static class Shared
    {
        private static readonly ArrayPool<byte> Pool = ArrayPool<byte>.Create();
        //private static readonly FakePool Pool = new FakePool();
        private class FakePool
        {
            public byte[] Rent(int size) => new byte[size];
            public void Return(byte[] bytes, bool clearArray = false) { }
        }

        public static byte[] Rent(int size) => Pool.Rent(size);
        public static ArraySegment<byte> RentArraySegment(int size) => new ArraySegment<byte>(Pool.Rent(size), 0, size);

        private static Random Random => rand ??= new Random((int)DateTime.Now.Ticks);

        [ThreadStatic]
        private static Random? rand;
        public static byte[] NextRandomBytes(int size)
        {
            if (rand == null)
                rand = new Random((int)DateTime.Now.Ticks);

            var bytes = Pool.Rent(size);
            rand.NextBytes(bytes);
            return bytes;
        }

        public static void NextBytes(byte[] bytes)
        {
            Random.NextBytes(bytes);
        }

        public static void NextBytes(Span<byte> bytes)
        {

#if NET6_0_OR_GREATER
            Random.NextBytes(bytes);
#else
            var rand = Random;
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = (byte)rand.Next(0, 256);
            }
#endif
        }

        public static ArraySegment<byte> NextRandomArraySegment(int size)
            => new ArraySegment<byte>(NextRandomBytes(size), 0, size);

        public static string SecWebSocketKey()
        {
            var keyAsBytes = NextRandomBytes(16);
            try
            {
                return Convert.ToBase64String(keyAsBytes);
            }
            finally
            {
                Return(keyAsBytes);
            }
        }

        public static void Return(byte[] bytes)
            => Pool.Return(bytes, clearArray: true);

        public static void Return(ArraySegment<byte> segment)
            => Pool.Return(segment.Array!, clearArray: true);

        /// <summary>
        /// Parses the WebSocket close payload to extract close status and description
        /// </summary>
        public static (WebSocketCloseStatus closeStatus, string? statusDescription) ParseClosePayload(ArraySegment<byte> message, int count)
        {
            if (count == 0)
            {
                return (WebSocketCloseStatus.Empty, null);
            }

            // WebSocket close payload format:
            // - First 2 bytes: close status in network byte order (big endian)
            // - Remaining bytes: optional UTF-8 status description

            if (count < 2)
            {
                // Invalid payload - should have at least 2 bytes for status code
                return (WebSocketCloseStatus.ProtocolError, null);
            }

            // Extract close status from first 2 bytes (network byte order)
            var statusBytes = new byte[2];
            Array.Copy(message.Array!, message.Offset + (message.Count - count), statusBytes, 0, 2);
            Array.Reverse(statusBytes); // Convert from network byte order to host byte order
            var statusCode = BitConverter.ToUInt16(statusBytes, 0);
            var closeStatus = (WebSocketCloseStatus)statusCode;

            // Extract status description from remaining bytes (if any)
            string? statusDescription = null;
            if (count > 2)
            {
                var descriptionLength = count - 2;
                statusDescription = Encoding.UTF8.GetString(message.Array!, message.Offset + (message.Count - count) + 2, descriptionLength);
            }

            return (closeStatus, statusDescription);
        }
    }
}
