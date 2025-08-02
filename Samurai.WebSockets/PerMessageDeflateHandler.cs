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
using System.IO.Compression;
using System.Net.WebSockets;


namespace Samurai.WebSockets.Internal
{
    internal sealed class PerMessageDeflateHandler : IDisposable
    {

        private readonly ArrayPoolStream compressedStream;
        private DeflateStream deflateStream;
        private bool isDisposed = false;

        private WebSocketMessageType? messageType;
        private WebSocketOpCode? opCode;

        public PerMessageDeflateHandler()
        {
            this.compressedStream = new ArrayPoolStream();
            this.deflateStream = new DeflateStream(this.compressedStream, CompressionMode.Compress, leaveOpen: true);
        }

        public void Write(ArraySegment<byte> buffer, WebSocketMessageType messageType, WebSocketOpCode opCode)
        {
            this.ThrowIfDisposed();

            if (this.messageType.HasValue && this.messageType != messageType)
                throw new ArgumentException($"Pending message has different messageType; {this.messageType}!={messageType}", nameof(messageType));

            if (this.opCode.HasValue && this.opCode != opCode)

                throw new ArgumentException($"Pending message has different opCode; {this.opCode}!={opCode}", nameof(opCode));

            this.messageType = messageType;
            this.opCode = opCode;
            this.deflateStream.Write(buffer.Array, buffer.Offset, buffer.Count);
        }

        public IEnumerable<DeflateFrame> GetFames(byte[] buffer)
        {
            this.ThrowIfDisposed();
            var messageType = this.messageType ?? throw new InvalidOperationException("No pending data.");
            var opCode = this.opCode ?? throw new InvalidOperationException("No pending data.");

            int bytesRead;
            this.deflateStream.Flush();
            this.compressedStream.Position = 0;
            while ((bytesRead = this.compressedStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                // Peek ahead to see if this is the last chunk
                var nextByte = this.compressedStream.ReadByte();
                bool isLastChunk = nextByte == -1;

                if (nextByte != -1)
                    this.compressedStream.Position--; // Put the byte back
                using var mx = new ArrayPoolStream();
                mx.Write(buffer, 0, bytesRead);

                yield return new DeflateFrame(messageType, isLastChunk ? opCode : WebSocketOpCode.ContinuationFrame, isLastChunk, bytesRead);
            }

            this.messageType = null;
            this.opCode = null;
            this.compressedStream.SetLength(0);
        }

        public void Reset()
        {
            this.deflateStream.Dispose();
            this.deflateStream = new DeflateStream(this.compressedStream, CompressionMode.Compress, leaveOpen: true);
            this.compressedStream.SetLength(0);
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
