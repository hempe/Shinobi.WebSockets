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
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.WebSockets;
using System.Threading.Tasks;


namespace Samurai.WebSockets.Internal
{
    public sealed class PerMessageDeflateHandler : IDisposable
    {
        private readonly ArrayPoolStream compressedStream = new ArrayPoolStream();
        private List<byte[]> cachedData = new List<byte[]>();

        private DeflateStream deflateStream;
        private bool isDisposed = false;

        private WebSocketMessageType? messageType;

        public PerMessageDeflateHandler()
        {
            this.deflateStream = new DeflateStream(this.compressedStream, CompressionMode.Compress, leaveOpen: true);
        }

        internal ArraySegment<byte> Write(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage)
        {
            this.ThrowIfDisposed();

            if (this.messageType.HasValue && this.messageType != messageType)
                throw new ArgumentException($"Pending message has different messageType; {this.messageType}!={messageType}", nameof(messageType));

            this.messageType = messageType;

            this.deflateStream.Write(buffer.Array, buffer.Offset, buffer.Count);
            this.deflateStream.Flush();
            if (endOfMessage)
            {
                this.deflateStream.Dispose();
                this.deflateStream = new DeflateStream(this.compressedStream, CompressionMode.Compress, leaveOpen: true);
                this.messageType = null;
            }

            var segment = this.compressedStream.GetDataArraySegment();
            this.compressedStream.SetLength(0);
            return segment;
        }

        public void Dispose()
        {
            if (this.isDisposed) return;
            this.isDisposed = true;
            this.deflateStream.Dispose();
            this.compressedStream.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (this.isDisposed)
                throw new ObjectDisposedException(nameof(ArrayPoolStream));
        }
    }
}
