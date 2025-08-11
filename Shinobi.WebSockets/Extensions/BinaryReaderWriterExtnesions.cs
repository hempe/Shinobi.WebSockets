// ---------------------------------------------------------------------
// Copyright 2018 David Haig
// Copyright 2025 Hansueli Burri
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
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Shinobi.WebSockets.Extensions
{
    internal static class BinaryReaderWriterExtensions
    {
        public static async ValueTask ReadFixedLengthAsync(this Stream stream, int length, ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            if (length == 0)
                return;

            // This will happen if the calling function supplied a buffer that was too small to fit the payload of the websocket frame.
            // Note that this can happen on the close handshake where the message size can be larger than the regular payload
            if (buffer.Count < length)
                throw new InternalBufferOverflowException($"Unable to read {length} bytes into buffer (offset: {buffer.Offset} size: {buffer.Count}). Use a larger read buffer");

            int offset = 0;
            do
            {
                int bytesRead = await stream.ReadAsync(buffer.Array!, buffer.Offset + offset, length - offset, cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                    throw new EndOfStreamException($"Unexpected end of stream encountered whilst attempting to read {length:#,##0} bytes");

                offset += bytesRead;
            } while (offset < length);
        }

        public static async ValueTask<ushort> ReadUShortAsync(this Stream stream, ArraySegment<byte> buffer, bool isLittleEndian, CancellationToken cancellationToken)
        {
            await stream.ReadFixedLengthAsync(2, buffer, cancellationToken).ConfigureAwait(false);
            return ReadUShort(buffer, isLittleEndian);

        }

        public static async ValueTask<ulong> ReadULongAsync(this Stream stream, ArraySegment<byte> buffer, bool isLittleEndian, CancellationToken cancellationToken)
        {
            await stream.ReadFixedLengthAsync(8, buffer, cancellationToken).ConfigureAwait(false);
            return ReadULong(buffer, isLittleEndian);
        }

        public static void WriteUShort(this Stream stream, ushort value, bool isLittleEndian)
        {

            Span<byte> buffer = stackalloc byte[2];
            if (isLittleEndian)
                BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
            else
                BinaryPrimitives.WriteUInt16BigEndian(buffer, value);

#if NET6_0_OR_GREATER
            stream.Write(buffer);
#else
            stream.Write(buffer.ToArray(), 0, 2);
#endif

        }

        public static void WriteULong(this Stream stream, ulong value, bool isLittleEndian)
        {
#if NET6_0_OR_GREATER
            Span<byte> buffer = stackalloc byte[8];
            if (isLittleEndian)
                BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
            else
                BinaryPrimitives.WriteUInt64BigEndian(buffer, value);
            stream.Write(buffer);
#else
            // .NET Standard 2.0 fallback
            var buffer = new byte[8];
            if (isLittleEndian)
            {
                buffer[0] = (byte)value;
                buffer[1] = (byte)(value >> 8);
                buffer[2] = (byte)(value >> 16);
                buffer[3] = (byte)(value >> 24);
                buffer[4] = (byte)(value >> 32);
                buffer[5] = (byte)(value >> 40);
                buffer[6] = (byte)(value >> 48);
                buffer[7] = (byte)(value >> 56);
            }
            else
            {
                buffer[0] = (byte)(value >> 56);
                buffer[1] = (byte)(value >> 48);
                buffer[2] = (byte)(value >> 40);
                buffer[3] = (byte)(value >> 32);
                buffer[4] = (byte)(value >> 24);
                buffer[5] = (byte)(value >> 16);
                buffer[6] = (byte)(value >> 8);
                buffer[7] = (byte)value;
            }
            stream.Write(buffer, 0, 8);
#endif
        }

        // Async write methods for better performance
        public static async ValueTask WriteUShortAsync(this Stream stream, ushort value, bool isLittleEndian, CancellationToken cancellationToken = default)
        {
#if NET6_0_OR_GREATER
            var buffer = new byte[2];
            if (isLittleEndian)
                BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
            else
                BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
            await stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
#else
            var buffer = new byte[2];
            if (isLittleEndian)
            {
                buffer[0] = (byte)value;
                buffer[1] = (byte)(value >> 8);
            }
            else
            {
                buffer[0] = (byte)(value >> 8);
                buffer[1] = (byte)value;
            }
            await stream.WriteAsync(buffer, 0, 2, cancellationToken).ConfigureAwait(false);
#endif
        }

        public static async ValueTask WriteULongAsync(this Stream stream, ulong value, bool isLittleEndian, CancellationToken cancellationToken = default)
        {
#if NET6_0_OR_GREATER
            var buffer = new byte[8];
            if (isLittleEndian)
                BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
            else
                BinaryPrimitives.WriteUInt64BigEndian(buffer, value);
            await stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
#else
            var buffer = new byte[8];
            if (isLittleEndian)
            {
                buffer[0] = (byte)value;
                buffer[1] = (byte)(value >> 8);
                buffer[2] = (byte)(value >> 16);
                buffer[3] = (byte)(value >> 24);
                buffer[4] = (byte)(value >> 32);
                buffer[5] = (byte)(value >> 40);
                buffer[6] = (byte)(value >> 48);
                buffer[7] = (byte)(value >> 56);
            }
            else
            {
                buffer[0] = (byte)(value >> 56);
                buffer[1] = (byte)(value >> 48);
                buffer[2] = (byte)(value >> 40);
                buffer[3] = (byte)(value >> 32);
                buffer[4] = (byte)(value >> 24);
                buffer[5] = (byte)(value >> 16);
                buffer[6] = (byte)(value >> 8);
                buffer[7] = (byte)value;
            }
            await stream.WriteAsync(buffer, 0, 8, cancellationToken).ConfigureAwait(false);
#endif
        }

        // High-performance buffer pooling versions for frequent operations
        public static void WriteUShortPooled(this Stream stream, ushort value, bool isLittleEndian, byte[] sharedBuffer)
        {
            if (sharedBuffer.Length < 2)
                throw new ArgumentException("Buffer must be at least 2 bytes", nameof(sharedBuffer));

#if NET6_0_OR_GREATER
            var span = sharedBuffer.AsSpan(0, 2);
            if (isLittleEndian)
                BinaryPrimitives.WriteUInt16LittleEndian(span, value);
            else
                BinaryPrimitives.WriteUInt16BigEndian(span, value);
#else
            if (isLittleEndian)
            {
                sharedBuffer[0] = (byte)value;
                sharedBuffer[1] = (byte)(value >> 8);
            }
            else
            {
                sharedBuffer[0] = (byte)(value >> 8);
                sharedBuffer[1] = (byte)value;
            }
#endif
            stream.Write(sharedBuffer, 0, 2);
        }

        public static void WriteULongPooled(this Stream stream, ulong value, bool isLittleEndian, byte[] sharedBuffer)
        {
            if (sharedBuffer.Length < 8)
                throw new ArgumentException("Buffer must be at least 8 bytes", nameof(sharedBuffer));

#if NET6_0_OR_GREATER
            var span = sharedBuffer.AsSpan(0, 8);
            if (isLittleEndian)
                BinaryPrimitives.WriteUInt64LittleEndian(span, value);
            else
                BinaryPrimitives.WriteUInt64BigEndian(span, value);
#else
            if (isLittleEndian)
            {
                sharedBuffer[0] = (byte)value;
                sharedBuffer[1] = (byte)(value >> 8);
                sharedBuffer[2] = (byte)(value >> 16);
                sharedBuffer[3] = (byte)(value >> 24);
                sharedBuffer[4] = (byte)(value >> 32);
                sharedBuffer[5] = (byte)(value >> 40);
                sharedBuffer[6] = (byte)(value >> 48);
                sharedBuffer[7] = (byte)(value >> 56);
            }
            else
            {
                sharedBuffer[0] = (byte)(value >> 56);
                sharedBuffer[1] = (byte)(value >> 48);
                sharedBuffer[2] = (byte)(value >> 40);
                sharedBuffer[3] = (byte)(value >> 32);
                sharedBuffer[4] = (byte)(value >> 24);
                sharedBuffer[5] = (byte)(value >> 16);
                sharedBuffer[6] = (byte)(value >> 8);
                sharedBuffer[7] = (byte)value;
            }
#endif
            stream.Write(sharedBuffer, 0, 8);
        }


        private static ushort ReadUShort(ArraySegment<byte> buffer, bool isLittleEndian)
        {
#if NET6_0_OR_GREATER
            var span = buffer.AsSpan(0, 2);
            return isLittleEndian
                ? BinaryPrimitives.ReadUInt16LittleEndian(span)
                : BinaryPrimitives.ReadUInt16BigEndian(span);
#else
            // Fallback for .NET Standard 2.0
            byte b0 = buffer.Array[buffer.Offset];
            byte b1 = buffer.Array[buffer.Offset + 1];

            return isLittleEndian
                ? (ushort)(b0 | (b1 << 8))
                : (ushort)((b0 << 8) | b1);
#endif
        }
        private static ulong ReadULong(ArraySegment<byte> buffer, bool isLittleEndian)
        {

#if NET6_0_OR_GREATER
            var span = buffer.AsSpan(0, 8);
            return isLittleEndian
                ? BinaryPrimitives.ReadUInt64LittleEndian(span)
                : BinaryPrimitives.ReadUInt64BigEndian(span);
#else
            // Fallback for .NET Standard 2.0 - optimized manual implementation
            if (isLittleEndian)
            {
                return (ulong)buffer.Array[buffer.Offset] |
                       ((ulong)buffer.Array[buffer.Offset + 1] << 8) |
                       ((ulong)buffer.Array[buffer.Offset + 2] << 16) |
                       ((ulong)buffer.Array[buffer.Offset + 3] << 24) |
                       ((ulong)buffer.Array[buffer.Offset + 4] << 32) |
                       ((ulong)buffer.Array[buffer.Offset + 5] << 40) |
                       ((ulong)buffer.Array[buffer.Offset + 6] << 48) |
                       ((ulong)buffer.Array[buffer.Offset + 7] << 56);
            }
            else
            {
                return ((ulong)buffer.Array[buffer.Offset] << 56) |
                       ((ulong)buffer.Array[buffer.Offset + 1] << 48) |
                       ((ulong)buffer.Array[buffer.Offset + 2] << 40) |
                       ((ulong)buffer.Array[buffer.Offset + 3] << 32) |
                       ((ulong)buffer.Array[buffer.Offset + 4] << 24) |
                       ((ulong)buffer.Array[buffer.Offset + 5] << 16) |
                       ((ulong)buffer.Array[buffer.Offset + 6] << 8) |
                       (ulong)buffer.Array[buffer.Offset + 7];
            }
#endif
        }
    }
}