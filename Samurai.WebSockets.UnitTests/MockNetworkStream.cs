using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Samurai.WebSockets.UnitTests
{
    internal class MockNetworkStream : Stream
    {
        private readonly string streamName;
        private readonly ChannelWriter<byte[]> writer;
        private readonly ChannelReader<byte[]> reader;
        private byte[]? currentBuffer;
        private int currentPosition = 0;

        public MockNetworkStream(string streamName, ChannelWriter<byte[]> writer, ChannelReader<byte[]> reader)
        {
            this.streamName = streamName;
            this.writer = writer;
            this.reader = reader;
        }

        public async override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            // If we don't have a current buffer or it's exhausted, get the next one
            if (this.currentBuffer == null || this.currentPosition >= this.currentBuffer.Length)
            {
                if (await this.reader.WaitToReadAsync(cancellationToken))
                {
                    this.currentBuffer = await this.reader.ReadAsync(cancellationToken);
                    this.currentPosition = 0;
                }
                else
                {
                    return 0; // Channel closed
                }
            }

            // Copy from current buffer
            int bytesToCopy = Math.Min(count, this.currentBuffer.Length - this.currentPosition);
            Array.Copy(this.currentBuffer, this.currentPosition, buffer, offset, bytesToCopy);
            this.currentPosition += bytesToCopy;

            return bytesToCopy;
        }

        public async override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var data = new byte[count];
            Array.Copy(buffer, offset, data, 0, count);
            await this.writer.WriteAsync(data, cancellationToken);
        }

        public override bool CanRead => throw new NotImplementedException();

        public override bool CanSeek => throw new NotImplementedException();

        public override bool CanWrite => throw new NotImplementedException();

        public override long Length => throw new NotImplementedException();

        public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public override void Flush()
        {
            throw new NotImplementedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override string ToString()
        {
            return this.streamName;
        }
    }
}
