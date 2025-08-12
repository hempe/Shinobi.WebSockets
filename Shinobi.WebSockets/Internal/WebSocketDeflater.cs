
using System;
using System.IO.Compression;

namespace Shinobi.WebSockets.Internal
{
    /// <summary>
    /// Handles compression (deflation) of WebSocket messages according to RFC 7692
    /// </summary>
    public sealed class WebSocketDeflater : IDisposable
    {
        private readonly ArrayPoolStream compressBuffer = new ArrayPoolStream();
        private DeflateStream decompressor;
        private readonly bool noContextTakeover;
        private readonly CompressionLevel compressionLevel;
        private bool isDisposed;

        public WebSocketDeflater(
            bool noContextTakeover,
            CompressionLevel compressionLevel = CompressionLevel.Fastest)
        {
            this.decompressor = new DeflateStream(this.compressBuffer, compressionLevel, leaveOpen: true);
            this.noContextTakeover = noContextTakeover;
            this.compressionLevel = compressionLevel;
        }

        /// <summary>
        /// Compresses a message fragment using DEFLATE compression
        /// </summary>
        /// <param name="buffer">Input buffer to compress</param>
        public void Write(ArraySegment<byte> buffer)
        {
            this.ThrowIfDisposed();

            // Write data to compressor
            this.decompressor.Write(buffer.Array!, buffer.Offset, buffer.Count);
        }

        /// <summary>
        /// Compresses a message fragment using DEFLATE compression
        /// </summary>
        /// <returns>Compressed data as ArraySegment</returns>
        public ArrayPoolStream Read()
        {
            this.ThrowIfDisposed();

            // Write data to compressor            
            this.decompressor.Flush();

            // Partial message - return current compressed data
            var compressedData = this.compressBuffer.GetDataArraySegment();
            var compressedStream = new ArrayPoolStream();
            // Remove the DEFLATE end marker (last 4 bytes should be 0x00 0x00 0xFF 0xFF)
            if (compressedData.Count >= 4)
            {
                compressedStream.Write(compressedData.Array!, compressedData.Offset, compressedData.Count - 4);
            }
            else
            {
                compressedStream.Write(compressedData.Array!, compressedData.Offset, compressedData.Count);
            }

            if (this.noContextTakeover)
            {
                this.decompressor.Dispose();
                this.decompressor = new DeflateStream(this.compressBuffer, this.compressionLevel, leaveOpen: true);
            }

            this.compressBuffer.SetLength(0);

            return compressedStream;
        }

        public void Dispose()
        {
            if (!this.isDisposed)
            {
                this.decompressor?.Dispose();
                this.compressBuffer?.Dispose();
                this.isDisposed = true;
            }
        }

        private void ThrowIfDisposed()
        {
            if (this.isDisposed)
                throw new ObjectDisposedException(nameof(WebSocketDeflater));
        }
    }
}