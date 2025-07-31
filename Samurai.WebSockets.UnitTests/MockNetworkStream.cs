using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Samurai.WebSockets.UnitTests
{
    class MockNetworkStream : Stream
    {
        private readonly string streamName;
        private readonly MemoryStream remoteStream;
        private readonly MemoryStream localStream;
        private readonly ManualResetEventSlim localReadSlim;
        private readonly ManualResetEventSlim remoteReadSlim;
        private readonly ManualResetEventSlim localWriteSlim;
        private readonly ManualResetEventSlim remoteWriteSlim;

        public MockNetworkStream(
            string streamName,
            MemoryStream localStream,
            MemoryStream remoteStream,
            ManualResetEventSlim localReadSlim,
            ManualResetEventSlim remoteReadSlim,
            ManualResetEventSlim localWriteSlim,
            ManualResetEventSlim remoteWriteSlim)
        {
            this.streamName = streamName;
            this.localStream = localStream;
            this.remoteStream = remoteStream;
            this.localReadSlim = localReadSlim;
            this.remoteReadSlim = remoteReadSlim;
            this.localWriteSlim = localWriteSlim;
            this.remoteWriteSlim = remoteWriteSlim;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            this.remoteReadSlim.Wait(cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return 0;
            }

            int numBytesRead = await this.remoteStream.ReadAsync(buffer, offset, count, cancellationToken);

            if (this.remoteStream.Position >= this.remoteStream.Length)
            {
                this.remoteReadSlim.Reset();
                this.remoteWriteSlim.Set();
            }

            return numBytesRead;
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            this.localWriteSlim.Wait(cancellationToken);
            this.localWriteSlim.Reset();
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            this.localStream.Position = 0;
            await this.localStream.WriteAsync(buffer, offset, count, cancellationToken);
            this.localStream.Position = 0;
            this.localReadSlim.Set();
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
