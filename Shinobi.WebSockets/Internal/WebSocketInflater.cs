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
using System.IO.Compression;

namespace Shinobi.WebSockets.Internal
{
    /// <summary>
    /// Handles decompression (inflation) of WebSocket messages according to RFC 7692
    /// </summary>
    public sealed class WebSocketInflater : IDisposable
    {
        private readonly ArrayPoolStream compressedStream;
        private DeflateStream decompressor;
        private bool isDisposed;

        // Per RFC 7692, messages end with 0x00 0x00 0xFF 0xFF when using no_context_takeover
        private static readonly byte[] DEFLATE_TRAILER = { 0x00, 0x00, 0xFF, 0xFF };
        private readonly bool noContextTakeover;

        public WebSocketInflater(bool noContextTakeover)
        {
            this.compressedStream = new ArrayPoolStream();
            this.decompressor = new DeflateStream(this.compressedStream, CompressionMode.Decompress, leaveOpen: true);
            this.noContextTakeover = noContextTakeover;
        }

        /// <summary>
        /// Decompresses a message fragment using DEFLATE decompression
        /// </summary>
        /// <param name="buffer">Compressed buffer to decompress</param>
        /// <returns>Decompressed data as ArraySegment</returns>
        public void Write(ArraySegment<byte> buffer)
        {
            this.ThrowIfDisposed();
            this.compressedStream.Write(buffer.Array!, buffer.Offset, buffer.Count);
        }

        public ArrayPoolStream Read()
        {
            var decompressedStream = new ArrayPoolStream();
            this.compressedStream.Write(DEFLATE_TRAILER, 0, DEFLATE_TRAILER.Length);
            this.compressedStream.Position = 0;
            this.decompressor.Flush();
            this.decompressor.CopyTo(decompressedStream);
            if (this.noContextTakeover)
            {
                this.decompressor.Dispose();
                this.decompressor = new DeflateStream(this.compressedStream, CompressionMode.Decompress, leaveOpen: true);
            }

            this.compressedStream.SetLength(0);
            return decompressedStream;
        }

        public void Dispose()
        {
            if (!this.isDisposed)
            {
                this.compressedStream.Dispose();
                this.decompressor.Dispose();
                this.isDisposed = true;
            }
        }

        private void ThrowIfDisposed()
        {
            if (this.isDisposed)
                throw new ObjectDisposedException(nameof(WebSocketInflater));
        }
    }
}
