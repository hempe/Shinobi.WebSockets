using System;
using System.Buffers;
using System.IO;

namespace Samurai.WebSockets
{
    public class ArrayPoolMemoryStream : MemoryStream
    {
        private readonly byte[] buffer;

        public ArrayPoolMemoryStream(int size = 16384)
            : base(ArrayPool<byte>.Shared.Rent(size), 0, size, true, true)
        {
            this.buffer = this.GetBuffer();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            ArrayPool<byte>.Shared.Return(this.buffer, clearArray: true);
        }
    }
}