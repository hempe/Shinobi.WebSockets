using System;
using System.IO;
using System.IO.Compression;
using System.Net.WebSockets;

namespace Samurai.WebSockets.Internal
{
    /// <summary>
    /// Handles compression (deflation) of WebSocket messages according to RFC 7692
    /// </summary>
    public sealed class WebSocketDeflater : IDisposable
    {
        private readonly ArrayPoolStream compressBuffer = new ArrayPoolStream();
        private readonly DeflateStream deflateStream;
        private bool isDisposed;
        private WebSocketMessageType? currentMessageType;

        public WebSocketDeflater(CompressionLevel compressionLevel = CompressionLevel.Fastest)
        {
            this.deflateStream = new DeflateStream(this.compressBuffer, compressionLevel, leaveOpen: true);
        }

        /// <summary>
        /// Compresses a message fragment using DEFLATE compression
        /// </summary>
        /// <param name="buffer">Input buffer to compress</param>
        /// <param name="messageType">WebSocket message type</param>
        /// <param name="endOfMessage">Whether this is the end of the message</param>
        /// <returns>Compressed data as ArraySegment</returns>
        public ArraySegment<byte> Write(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage)
        {
            this.ThrowIfDisposed();

            // Only compress text and binary messages
            if (messageType != WebSocketMessageType.Text && messageType != WebSocketMessageType.Binary)
            {
                return buffer; // Return original buffer for control frames
            }

            // Check for message type consistency across fragments
            if (this.currentMessageType.HasValue && this.currentMessageType != messageType)
                throw new ArgumentException($"Pending message has different messageType; {this.currentMessageType}!={messageType}", nameof(messageType));

            this.currentMessageType = messageType;

            try
            {
                // Write data to compressor
                this.deflateStream.Write(buffer.Array, buffer.Offset, buffer.Count);
                this.deflateStream.Flush();

                // Partial message - return current compressed data
                var compressedData = this.compressBuffer.GetDataArraySegment();
                this.compressBuffer.SetLength(0);

                // Remove the DEFLATE end marker (last 4 bytes should be 0x00 0x00 0xFF 0xFF)
                if (compressedData.Count >= 4)
                {
                    return new ArraySegment<byte>(compressedData.Array, compressedData.Offset, compressedData.Count - 4);
                }

                return compressedData;
            }
            catch
            {
                // If compression fails, return original buffer
                return buffer;
            }
        }

        public void Dispose()
        {
            if (!this.isDisposed)
            {
                this.deflateStream?.Dispose();
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



    /// <summary>
    /// Handles decompression (inflation) of WebSocket messages according to RFC 7692
    /// </summary>
    public sealed class WebSocketInflater : IDisposable
    {
        private readonly byte[] tempBuffer;
        private readonly ArrayPoolStream compressedStream;
        private readonly DeflateStream decompressor;
        private bool isDisposed;

        // Per RFC 7692, messages end with 0x00 0x00 0xFF 0xFF when using no_context_takeover
        private static readonly byte[] DEFLATE_TRAILER = { 0x00, 0x00, 0xFF, 0xFF };

        public WebSocketInflater()
        {
            this.tempBuffer = new byte[8192];
            this.compressedStream = new ArrayPoolStream();
            this.decompressor = new DeflateStream(this.compressedStream, CompressionMode.Decompress);
        }


        /// <summary>
        /// Decompresses a message fragment using DEFLATE decompression
        /// </summary>
        /// <param name="buffer">Compressed buffer to decompress</param>
        /// <returns>Decompressed data as ArraySegment</returns>
        public void Write(ArraySegment<byte> buffer)
        {
            this.ThrowIfDisposed();
            this.compressedStream.Write(buffer.Array, buffer.Offset, buffer.Count);
        }

        public ArrayPoolStream Read()
        {
            var decompressedStream = new ArrayPoolStream();
            this.compressedStream.Write(DEFLATE_TRAILER, 0, DEFLATE_TRAILER.Length);
            this.compressedStream.Position = 0;

            this.decompressor.CopyTo(decompressedStream);
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