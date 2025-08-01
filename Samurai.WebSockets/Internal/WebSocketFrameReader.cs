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
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Samurai.WebSockets.Internal
{
    /// <summary>
    /// Reads a WebSocket frame
    /// see http://tools.ietf.org/html/rfc6455 for specification
    /// </summary>
    internal static class WebSocketFrameReader
    {
        private static int CalculateNumBytesToRead(int numBytesLetfToRead, int bufferSize)
        {
            // the count needs to be a multiple of the mask key
            return (bufferSize < numBytesLetfToRead)
                ? bufferSize - bufferSize % WebSocketFrameCommon.MaskKeyLength
                : numBytesLetfToRead;
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
                remainingFrame.MaskKey.ToggleMask(new ArraySegment<byte>(intoBuffer.Array, intoBuffer.Offset, minCount));

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
            var smallArray = ArrayPool<byte>.Shared.Rent(8);
            var smallBuffer = new ArraySegment<byte>(smallArray);
            try
            {

                await fromStream.ReadFixedLengthAsync(2, smallBuffer, cancellationToken).ConfigureAwait(false);
                var byte1 = smallBuffer.Array[0];
                var byte2 = smallBuffer.Array[1];

                // process first byte
                const byte finBitFlag = 0x80;
                const byte opCodeFlag = 0x0F;
                var isFinBitSet = (byte1 & finBitFlag) == finBitFlag;
                var opCode = (WebSocketOpCode)(byte1 & opCodeFlag);

                // read and process second byte
                const byte maskFlag = 0x80;
                var isMaskBitSet = (byte2 & maskFlag) == maskFlag;
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
                        maskKey.ToggleMask(new ArraySegment<byte>(intoBuffer.Array, intoBuffer.Offset, minCount));
                    }
                    else
                    {
                        maskKey = new ArraySegment<byte>();
                        await fromStream.ReadFixedLengthAsync(minCount, intoBuffer, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (InternalBufferOverflowException e)
                {
                    throw new InternalBufferOverflowException($"Supplied buffer too small to read {0} bytes from {Enum.GetName(typeof(WebSocketOpCode), opCode)} frame", e);
                }

                var frame = (opCode == WebSocketOpCode.ConnectionClose)
                    ? DecodeCloseFrame(isFinBitSet, opCode, count, intoBuffer, maskKey)
                    // note that by this point the payload will be populated
                    : new WebSocketFrame(isFinBitSet, opCode, count, maskKey);

                return new WebSocketReadCursor(frame, minCount, count - minCount);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(smallArray);
            }
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

            // network byte order
            Array.Reverse(buffer.Array, buffer.Offset, 2);

            var closeStatusCode = (int)BitConverter.ToUInt16(buffer.Array, buffer.Offset);
            var closeStatus = Enum.IsDefined(typeof(WebSocketCloseStatus), closeStatusCode)
                ? (WebSocketCloseStatus)closeStatusCode
                : WebSocketCloseStatus.Empty;

            var descCount = count - 2;
            var closeStatusDescription = (descCount > 0)
                ? Encoding.UTF8.GetString(buffer.Array, buffer.Offset + 2, descCount) :
                null;

            return new WebSocketFrame(isFinBitSet, opCode, count, maskKey, closeStatus, closeStatusDescription);
        }

        /// <summary>
        /// Reads the length of the payload according to the contents of byte2
        /// </summary>
        private static async ValueTask<uint> ReadLengthAsync(byte byte2, ArraySegment<byte> smallBuffer, Stream fromStream, CancellationToken cancellationToken)
        {
            const byte payloadLenFlag = 0x7F;
            var len = (uint)(byte2 & payloadLenFlag);

            // read a short length or a long length depending on the value of len
            if (len == 126)
                return await fromStream.ReadUShortAsync(false, smallBuffer, cancellationToken).ConfigureAwait(false);

            if (len == 127)
            {
                len = (uint)await fromStream.ReadULongAsync(false, smallBuffer, cancellationToken).ConfigureAwait(false);
                const uint maxLen = 2147483648; // 2GB - not part of the spec but just a precaution. Send large volumes of data in smaller frames.

                // protect ourselves against bad data
                if (len > maxLen)
                    throw new ArgumentOutOfRangeException($"Payload length out of range. Min 0 max 2GB. Actual {len:#,##0} bytes.");
            }

            return len;
        }
    }
}
