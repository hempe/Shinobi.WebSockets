using System;
using System.Buffers;

namespace Samurai.WebSockets.Internal
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
            => Pool.Return(segment.Array, clearArray: true);
    }
}
