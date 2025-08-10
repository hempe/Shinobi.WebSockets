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
        private readonly ArrayPoolStream decompressBuffer = new ArrayPoolStream();
        private readonly ArrayPoolStream partialCompressedData = new ArrayPoolStream();
        private readonly byte[] tempBuffer;
        private bool isDisposed;

        // Per RFC 7692, messages end with 0x00 0x00 0xFF 0xFF when using no_context_takeover
        private static readonly byte[] DEFLATE_TRAILER = { 0x00, 0x00, 0xFF, 0xFF };

        public WebSocketInflater()
        {
            this.tempBuffer = new byte[8192];
        }

        /// <summary>
        /// Decompresses a message fragment using DEFLATE decompression
        /// </summary>
        /// <param name="buffer">Compressed buffer to decompress</param>
        /// <param name="messageType">WebSocket message type</param>
        /// <param name="endOfMessage">Whether this is the end of the message</param>
        /// <returns>Decompressed data as ArraySegment</returns>
        public ArraySegment<byte> Read(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage)
        {
            this.ThrowIfDisposed();

            // Only decompress text and binary messages
            if (messageType != WebSocketMessageType.Text && messageType != WebSocketMessageType.Binary)
            {
                return buffer; // Return original buffer for control frames
            }

            try
            {
                // Add this fragment to our partial data
                this.partialCompressedData.Write(buffer.Array, buffer.Offset, buffer.Count);

                if (endOfMessage)
                {
                    // End of message - decompress the complete message
                    var completeCompressedData = this.partialCompressedData.GetDataArraySegment();
                    this.partialCompressedData.SetLength(0);

                    // Add DEFLATE trailer for proper decompression
                    var dataWithTrailer = new byte[completeCompressedData.Count + DEFLATE_TRAILER.Length];
                    Array.Copy(completeCompressedData.Array, completeCompressedData.Offset, dataWithTrailer, 0, completeCompressedData.Count);
                    Array.Copy(DEFLATE_TRAILER, 0, dataWithTrailer, completeCompressedData.Count, DEFLATE_TRAILER.Length);

                    // Reset decompression buffer
                    this.decompressBuffer.SetLength(0);

                    // Decompress the complete message
                    using (var compressedStream = new MemoryStream(dataWithTrailer))
                    using (var decompressor = new DeflateStream(compressedStream, CompressionMode.Decompress))
                    {
                        int bytesRead;
                        while ((bytesRead = decompressor.Read(this.tempBuffer, 0, this.tempBuffer.Length)) > 0)
                        {
                            this.decompressBuffer.Write(this.tempBuffer, 0, bytesRead);
                        }
                    }

                    return this.decompressBuffer.GetDataArraySegment();
                }
                else
                {
                    // Partial message - we can't decompress until we have the complete message
                    // Return empty segment to indicate no data available yet
                    return new ArraySegment<byte>(new byte[0]);
                }
            }
            catch
            {
                // If decompression fails, clean up and return original buffer
                this.partialCompressedData.SetLength(0);
                return buffer;
            }
        }

        public void Dispose()
        {
            if (!this.isDisposed)
            {
                this.decompressBuffer?.Dispose();
                this.partialCompressedData?.Dispose();
                this.isDisposed = true;
            }
        }

        private void ThrowIfDisposed()
        {
            if (this.isDisposed)
                throw new ObjectDisposedException(nameof(WebSocketInflater));
        }
    }


    /// <summary>
    /// Combined deflater and inflater for full WebSocket per-message-deflate support
    /// </summary>
    public sealed class WebSocketCompressionHandler : IDisposable
    {
        private readonly WebSocketDeflater deflater;
        private readonly WebSocketInflater inflater;

        public WebSocketCompressionHandler(CompressionLevel compressionLevel = CompressionLevel.Fastest)
        {
            this.deflater = new WebSocketDeflater(compressionLevel);
            this.inflater = new WebSocketInflater();
        }

        /// <summary>
        /// Compresses outgoing message data
        /// </summary>
        public ArraySegment<byte> Compress(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage)
        {
            return this.deflater.Write(buffer, messageType, endOfMessage);
        }

        /// <summary>
        /// Decompresses incoming message data
        /// </summary>
        public ArraySegment<byte> Decompress(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage)
        {
            return this.inflater.Read(buffer, messageType, endOfMessage);
        }

        public void Dispose()
        {
            this.deflater?.Dispose();
            this.inflater?.Dispose();
        }
    }
}