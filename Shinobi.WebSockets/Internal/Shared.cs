// ---------------------------------------------------------------------
// Copyright 2018 David Haig
//
// Permission is hereby granted, free of charge, to any person obtaining a copy 
// of this software and associated documentation files (the "Software"), to deal 
// in the Software without restriction, including without limitation the rights 
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell 
// copies of the Software, and to permit persons to whom the Software is 
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR 
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, 
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN 
// THE SOFTWARE.
// ---------------------------------------------------------------------


using System;
using System.Buffers;

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
    }
}
