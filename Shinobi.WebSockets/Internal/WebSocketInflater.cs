#if NET8_0_OR_GREATER
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
#endif