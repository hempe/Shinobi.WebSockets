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
using System.IO;

using Shinobi.WebSockets.Extensions;

namespace Shinobi.WebSockets.Internal
{
    // see http://tools.ietf.org/html/rfc6455 for specification
    // see fragmentation section for sending multi part messages
    // EXAMPLE: For a text message sent as three fragments, 
    //   the first fragment would have an opcode of TextFrame and isLastFrame false,
    //   the second fragment would have an opcode of ContinuationFrame and isLastFrame false,
    //   the third fragment would have an opcode of ContinuationFrame and isLastFrame true.
    internal static class WebSocketFrameWriter
    {
        /// <summary>
        /// Writes a WebSocket frame with optional compression support
        /// </summary>
        /// <param name="opCode">The web socket opcode</param>
        /// <param name="fromPayload">Array segment to get payload data from</param>
        /// <param name="toStream">Stream to write to</param>
        /// <param name="isLastFrame">True is this is the last frame in this message (usually true)</param>
        /// <param name="isClient">Indicate if this is called from a client or server</param>
        /// <param name="isCompressed">True if this message is compressed</param>
        /// <param name="isFirstFrameOfMessage">True if this is the first frame of a message (for RSV1 bit)</param>
        public static void Write(
            WebSocketOpCode opCode,
            ArraySegment<byte> fromPayload,
            Stream toStream,
            bool isLastFrame,
            bool isClient,
            bool isCompressed = false,
            bool isFirstFrameOfMessage = false)
        {
            var finBitSetAsByte = isLastFrame ? (byte)0x80 : (byte)0x00;

            // RSV1 bit (0x40) indicates per-message-deflate compression
            // This should ONLY be set on the first frame of a compressed message
            var rsv1BitSetAsByte = (isCompressed && isFirstFrameOfMessage) ? (byte)0x40 : (byte)0x00;

            var byte1 = (byte)(finBitSetAsByte | rsv1BitSetAsByte | (byte)opCode);
            toStream.WriteByte(byte1);

            // NB, set the mask flag if we are constructing a client frame
            var maskBitSetAsByte = isClient ? (byte)0x80 : (byte)0x00;

            // depending on the size of the length we want to write it as a byte, ushort or ulong
            if (fromPayload.Count < 126)
            {
                toStream.WriteByte((byte)(maskBitSetAsByte | (byte)fromPayload.Count));
            }
            else if (fromPayload.Count <= ushort.MaxValue)
            {
                toStream.WriteByte((byte)(maskBitSetAsByte | 126));
                toStream.WriteUShort((ushort)fromPayload.Count, false);
            }
            else
            {
                toStream.WriteByte((byte)(maskBitSetAsByte | 127));
                toStream.WriteULong((ulong)fromPayload.Count, false);
            }

            // if we are creating a client frame then we MUST mask the payload as per the spec
            if (isClient)
            {
                var maskKey = Shared.NextRandomArraySegment(WebSocketFrameCommon.MaskKeyLength);
                try
                {
                    toStream.Write(maskKey.Array!, maskKey.Offset, maskKey.Count);
                    // mask the payload
                    maskKey.ToggleMask(fromPayload.Array!, fromPayload.Offset, fromPayload.Count);
                }
                finally
                {
                    Shared.Return(maskKey);
                }
            }

            toStream.Write(fromPayload.Array!, fromPayload.Offset, fromPayload.Count);
        }
    }
}
