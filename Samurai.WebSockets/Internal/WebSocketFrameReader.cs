// ---------------------------------------------------------------------
// Copyright 2018 David Haig - Micro-optimized version (Careful)
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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Samurai.WebSockets.Extensions;

namespace Samurai.WebSockets.Internal
{
    /// <summary>
    /// Reads a WebSocket frame - micro-optimized version
    /// see http://tools.ietf.org/html/rfc6455 for specification
    /// </summary>
    internal static class WebSocketFrameReader
    {
        // Cache constants to avoid repeated calculations
        private const byte FinBitFlag = 0x80;
        private const byte OpCodeFlag = 0x0F;
        private const byte MaskFlag = 0x80;
        private const byte PayloadLenFlag = 0x7F;
        private const uint MaxLen = 2147483648; // 2GB

        private static readonly HashSet<int> ValidCloseStatusCodes = new HashSet<int>(
            Enum.GetValues(typeof(WebSocketCloseStatus))
                .Cast<WebSocketCloseStatus>()
                .Select(v => (int)v)
        );

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CalculateNumBytesToRead(int numBytesLeftToRead, int bufferSize)
        {
            // the count needs to be a multiple of the mask key
            return (bufferSize < numBytesLeftToRead)
                ? bufferSize - bufferSize % WebSocketFrameCommon.MaskKeyLength
                : numBytesLeftToRead;
        }

        /// <summary>
        /// The last read could not be completed because the read buffer was too small. 
        /// We need to continue reading bytes off the stream.
        /// Not to be confused with a continuation frame
        /// </summary>
        /// <param name="fromStream">The stream to read from</param>
        /// <param name="intoBuffer">The buffer to read into</param>
        /// <param name="readCursor">The previous partial websocket frame read plus cursor information</param>
        /// <param name="cancellationToken">the cancellation token</param>
        /// <returns>A websocket frame</returns>
        public static async ValueTask<WebSocketReadCursor> ReadFromCursorAsync(Stream fromStream, ArraySegment<byte> intoBuffer, WebSocketReadCursor readCursor, CancellationToken cancellationToken)
        {
            var remainingFrame = readCursor.WebSocketFrame;
            var minCount = CalculateNumBytesToRead(readCursor.NumBytesLeftToRead, intoBuffer.Count);
            await fromStream.ReadFixedLengthAsync(minCount, intoBuffer, cancellationToken).ConfigureAwait(false);
            if (remainingFrame.MaskKey.Count > 0)
                remainingFrame.MaskKey.ToggleMask(intoBuffer.Array!, intoBuffer.Offset, minCount);

            return new WebSocketReadCursor(remainingFrame, minCount, readCursor.NumBytesLeftToRead - minCount);
        }

        /// <summary>
        /// Read a WebSocket frame from the stream
        /// </summary>
        /// <param name="fromStream">The stream to read from</param>
        /// <param name="intoBuffer">The buffer to read into</param>
        /// <param name="cancellationToken">the cancellation token</param>
        /// <returns>A websocket frame</returns>
        public static async ValueTask<WebSocketReadCursor> ReadAsync(Stream fromStream, ArraySegment<byte> intoBuffer, CancellationToken cancellationToken)
        {
            // allocate a small buffer to read small chunks of data from the stream
            var smallBuffer = new ArraySegment<byte>(new byte[8]);

            await fromStream.ReadFixedLengthAsync(2, smallBuffer, cancellationToken).ConfigureAwait(false);
            var byte1 = smallBuffer.Array![0];
            var byte2 = smallBuffer.Array[1];

            // process first byte - use cached constants
            var isFinBitSet = (byte1 & FinBitFlag) == FinBitFlag;
            var opCode = (WebSocketOpCode)(byte1 & OpCodeFlag);

            // read and process second byte
            var isMaskBitSet = (byte2 & MaskFlag) == MaskFlag;
            var len = await ReadLengthAsync(byte2, smallBuffer, fromStream, cancellationToken);
            var count = (int)len;
            var minCount = CalculateNumBytesToRead(count, intoBuffer.Count);
            ArraySegment<byte> maskKey;

            try
            {
                // use the masking key to decode the data if needed
                if (isMaskBitSet)
                {
                    maskKey = smallBuffer.Array.AsMaskKey();
                    await fromStream.ReadFixedLengthAsync(maskKey.Count, maskKey, cancellationToken).ConfigureAwait(false);
                    await fromStream.ReadFixedLengthAsync(minCount, intoBuffer, cancellationToken).ConfigureAwait(false);
                    maskKey.ToggleMask(intoBuffer.Array!, intoBuffer.Offset, minCount);
                }
                else
                {
                    maskKey = new ArraySegment<byte>();
                    await fromStream.ReadFixedLengthAsync(minCount, intoBuffer, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (InternalBufferOverflowException e)
            {
                // Fixed the format string - this was broken in original with {0} but no parameter
                throw new InternalBufferOverflowException($"Supplied buffer too small to read {count} bytes from {Enum.GetName(typeof(WebSocketOpCode), opCode)} frame", e);
            }

            var frame = (opCode == WebSocketOpCode.ConnectionClose)
                ? DecodeCloseFrame(isFinBitSet, opCode, count, intoBuffer, maskKey)
                // note that by this point the payload will be populated
                : new WebSocketFrame(isFinBitSet, opCode, count, maskKey);

            return new WebSocketReadCursor(frame, minCount, count - minCount);
        }

        /// <summary>
        /// Extracts close status and close description information from the web socket frame
        /// </summary>
        private static WebSocketFrame DecodeCloseFrame(bool isFinBitSet, WebSocketOpCode opCode, int count, ArraySegment<byte> buffer, ArraySegment<byte> maskKey)
        {
            if (count < 2)
                return new WebSocketFrame(
                    isFinBitSet,
                    opCode,
                    count,
                    maskKey,
                    WebSocketCloseStatus.Empty);

            // IMPORTANT: Keep the original behavior - reverse then use BitConverter
            // This modifies the buffer in-place which might be important for the caller
            Array.Reverse(buffer.Array!, buffer.Offset, 2);

            var closeStatusCode = (int)BitConverter.ToUInt16(buffer.Array!, buffer.Offset);
            var closeStatus = ValidCloseStatusCodes.Contains(closeStatusCode)
                ? (WebSocketCloseStatus)closeStatusCode
                : WebSocketCloseStatus.Empty;

            var descCount = count - 2;
            var closeStatusDescription = (descCount > 0)
                ? Encoding.UTF8.GetString(buffer.Array!, buffer.Offset + 2, descCount) :
                null;

            return new WebSocketFrame(isFinBitSet, opCode, count, maskKey, closeStatus, closeStatusDescription);
        }

        /// <summary>
        /// Reads the length of the payload according to the contents of byte2
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static async ValueTask<uint> ReadLengthAsync(byte byte2, ArraySegment<byte> smallBuffer, Stream fromStream, CancellationToken cancellationToken)
        {
            var len = (uint)(byte2 & PayloadLenFlag);

            // read a short length or a long length depending on the value of len
            if (len == 126)
                return await fromStream.ReadUShortAsync(smallBuffer, false, cancellationToken).ConfigureAwait(false);

            if (len == 127)
            {
                len = (uint)await fromStream.ReadULongAsync(smallBuffer, false, cancellationToken).ConfigureAwait(false);

                // protect ourselves against bad data - use cached constant
                if (len > MaxLen)
                    throw new ArgumentOutOfRangeException($"Payload length out of range. Min 0 max 2GB. Actual {len:#,##0} bytes.");
            }

            return len;
        }
    }
}