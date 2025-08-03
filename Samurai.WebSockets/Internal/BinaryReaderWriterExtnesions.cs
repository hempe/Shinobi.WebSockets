// ---------------------------------------------------------------------
// Copyright 2025 Hempe
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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace Samurai.WebSockets.Internal
{
    internal static class BinaryReaderWriterExtnesions
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
                int bytesRead = await stream.ReadAsync(buffer.Array, buffer.Offset + offset, length - offset, cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                    throw new EndOfStreamException(string.Format("Unexpected end of stream encountered whilst attempting to read {0:#,##0} bytes", length));

                offset += bytesRead;
            } while (offset < length);

            return;
        }
        public static async ValueTask<ushort> ReadUShortAsync(this Stream stream, bool isLittleEndian, ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            await stream.ReadFixedLengthAsync(2, buffer, cancellationToken).ConfigureAwait(false);

            byte b0 = buffer.Array[buffer.Offset];
            byte b1 = buffer.Array[buffer.Offset + 1];

            return isLittleEndian
                ? (ushort)(b0 | (b1 << 8))
                : (ushort)((b0 << 8) | b1);
        }

        public static async ValueTask<ulong> ReadULongAsync(this Stream stream, bool isLittleEndian, ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            await stream.ReadFixedLengthAsync(8, buffer, cancellationToken).ConfigureAwait(false);

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
        }

        // Option 2: Using unsafe code with pointers (requires unsafe context)
        public static async ValueTask<ushort> ReadUShortUnsafeAsync(this Stream stream, bool isLittleEndian, ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            await stream.ReadFixedLengthAsync(2, buffer, cancellationToken).ConfigureAwait(false);
            return ReadUShortUnsafe(buffer, isLittleEndian);
        }

        public static async ValueTask<ulong> ReadULongUnsafeAsync(this Stream stream, bool isLittleEndian, ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            await stream.ReadFixedLengthAsync(8, buffer, cancellationToken).ConfigureAwait(false);
            return ReadULongUnsafe(buffer, isLittleEndian);
        }

        private static unsafe ushort ReadUShortUnsafe(ArraySegment<byte> buffer, bool isLittleEndian)
        {
            fixed (byte* ptr = &buffer.Array[buffer.Offset])
            {
                ushort value = *(ushort*)ptr;

                // Convert endianness if needed
                if (BitConverter.IsLittleEndian != isLittleEndian)
                {
                    value = (ushort)((value >> 8) | (value << 8));
                }

                return value;
            }
        }

        private static unsafe ulong ReadULongUnsafe(ArraySegment<byte> buffer, bool isLittleEndian)
        {
            fixed (byte* ptr = &buffer.Array[buffer.Offset])
            {
                ulong value = *(ulong*)ptr;

                // Convert endianness if needed
                if (BitConverter.IsLittleEndian != isLittleEndian)
                {
                    value = ReverseBytes(value);
                }

                return value;
            }
        }

        // Option 3: Using MemoryMarshal (requires System.Memory NuGet package for .NET Standard 2.0)
        // Install-Package System.Memory
        public static async ValueTask<ushort> ReadUShortMemoryMarshalAsync(this Stream stream, bool isLittleEndian, ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            await stream.ReadFixedLengthAsync(2, buffer, cancellationToken).ConfigureAwait(false);
            return ReadUShortMemoryMarshal(buffer, isLittleEndian);
        }

        private static unsafe ushort ReadUShortMemoryMarshal(ArraySegment<byte> buffer, bool isLittleEndian)
        {
            var span = new ReadOnlySpan<byte>(buffer.Array, buffer.Offset, 2);
            ushort value = MemoryMarshal.Read<ushort>(span);

            if (BitConverter.IsLittleEndian != isLittleEndian)
            {
                value = (ushort)((value >> 8) | (value << 8));
            }

            return value;
        }

        // Write methods without allocations (requires System.Memory NuGet package for Span<T>)
        // Install-Package System.Memory
        public static void WriteULong(this Stream stream, ulong value, bool isLittleEndian)
        {
            Span<byte> buffer = stackalloc byte[8];

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

            var arr = buffer.ToArray();
            stream.Write(arr, 0, arr.Length);
        }

        public static void WriteUShort(this Stream stream, ushort value, bool isLittleEndian)
        {
            Span<byte> buffer = stackalloc byte[2];

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

            var arr = buffer.ToArray();
            stream.Write(arr, 0, arr.Length);
        }

        // Alternative write methods for .NET Standard 2.0 without Span<T> support
        public static void WriteULongNetStandard20(this Stream stream, ulong value, bool isLittleEndian, byte[] buffer)
        {
            if (buffer.Length < 8)
                throw new ArgumentException("Buffer must be at least 8 bytes");

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
        }

        public static void WriteUShortNetStandard20(this Stream stream, ushort value, bool isLittleEndian, byte[] buffer)
        {
            if (buffer.Length < 2)
                throw new ArgumentException("Buffer must be at least 2 bytes");

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

            stream.Write(buffer, 0, 2);
        }

        // Helper method for byte reversal
        private static ulong ReverseBytes(ulong value)
        {
            return ((value & 0x00000000000000FFUL) << 56) |
                   ((value & 0x000000000000FF00UL) << 40) |
                   ((value & 0x0000000000FF0000UL) << 24) |
                   ((value & 0x00000000FF000000UL) << 8) |
                   ((value & 0x000000FF00000000UL) >> 8) |
                   ((value & 0x0000FF0000000000UL) >> 24) |
                   ((value & 0x00FF000000000000UL) >> 40) |
                   ((value & 0xFF00000000000000UL) >> 56);
        }
    }
}
