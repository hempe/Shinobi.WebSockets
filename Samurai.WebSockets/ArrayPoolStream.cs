using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Samurai.WebSockets.Internal;

namespace Samurai.WebSockets
{
    public sealed class ArrayPoolStream : Stream
    {
        private byte[]? buffer;
        private MemoryStream? innerStream;
        private bool isDisposed;
        public readonly int InitialSize;

        public ArrayPoolStream(int size = 16384)
        {
            this.InitialSize = size;
            this.buffer = Shared.Rent(size);
            this.innerStream = new MemoryStream(this.buffer, 0, this.buffer.Length, true, true);
            this.innerStream.SetLength(0);
        }

        public override long Length => this.innerStream?.Length ?? 0;

        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
        {
            this.ThrowIfDisposed();
            return this.innerStream!.BeginRead(buffer, offset, count, callback, state);
        }

        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
        {
            this.ThrowIfDisposed();
            return this.innerStream!.BeginWrite(buffer, offset, count, callback, state);
        }

        public override bool CanRead => !this.isDisposed && this.innerStream!.CanRead;
        public override bool CanSeek => !this.isDisposed && this.innerStream!.CanSeek;
        public override bool CanTimeout => !this.isDisposed && this.innerStream!.CanTimeout;
        public override bool CanWrite => !this.isDisposed && this.innerStream!.CanWrite;

        public int Capacity
        {
            get => this.innerStream?.Capacity ?? 0;
            set
            {
                this.ThrowIfDisposed();
                // Note: Setting capacity on the inner MemoryStream won't help us
                // because we manage the buffer ourselves
                if (value > this.buffer!.Length)
                {
                    this.EnlargeBuffer(value - (int)this.innerStream!.Position);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!this.isDisposed && disposing)
            {
                if (this.buffer != null)
                {
                    Shared.Return(this.buffer);
                    this.buffer = null;
                }

                this.innerStream?.Dispose();
                this.innerStream = null;
                this.isDisposed = true;
            }
            base.Dispose(disposing);
        }

        public override void Close()
        {
            this.Dispose(true);
        }

        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            this.ThrowIfDisposed();
            return this.innerStream!.CopyToAsync(destination, bufferSize, cancellationToken);
        }

        public override int EndRead(IAsyncResult asyncResult)
        {
            this.ThrowIfDisposed();
            return this.innerStream!.EndRead(asyncResult);
        }

        public override void EndWrite(IAsyncResult asyncResult)
        {
            this.ThrowIfDisposed();
            this.innerStream!.EndWrite(asyncResult);
        }

        public override void Flush()
        {
            this.ThrowIfDisposed();
            this.innerStream!.Flush();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            this.ThrowIfDisposed();
            return this.innerStream!.FlushAsync(cancellationToken);
        }

        public byte[] GetBuffer()
        {
            this.ThrowIfDisposed();
            return this.buffer!;
        }

        public override long Position
        {
            get => this.innerStream?.Position ?? 0;
            set
            {
                this.ThrowIfDisposed();
                this.innerStream!.Position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            this.ThrowIfDisposed();
            return this.innerStream!.Read(buffer, offset, count);
        }

        public Span<byte> GetFreeSpan(int sizeHint)
        {
            this.ThrowIfDisposed();

            if (sizeHint <= 0)
                sizeHint = 1; // At least 1 byte requested

            // Ensure there is enough free space in buffer
            var freeSegment = this.GetFreeArraySegment(sizeHint);

            // Return the span over the free buffer space
            return new Span<byte>(freeSegment.Array!, freeSegment.Offset, sizeHint);
        }

        private void EnlargeBuffer(int additionalBytesNeeded)
        {
            this.ThrowIfDisposed();
            // We cannot fit the data into the existing buffer, time for a new buffer
            if (additionalBytesNeeded > (this.buffer!.Length - this.innerStream!.Position))
            {
                var position = (int)this.innerStream.Position;

                // Calculate new size - double the buffer size or accommodate required size
                var newSize = Math.Max((long)this.buffer.Length * 2, (long)this.buffer.Length + additionalBytesNeeded);

                // Ensure we don't exceed int.MaxValue
                if (newSize > int.MaxValue)
                {
                    long requiredSize = (long)additionalBytesNeeded + this.buffer.Length;
                    if (requiredSize > int.MaxValue)
                    {
                        throw new InvalidOperationException($"Tried to create a buffer ({requiredSize:#,##0} bytes) that was larger than the max allowed size ({int.MaxValue:#,##0})");
                    }
                    newSize = requiredSize;
                }

                // Round up to next power of 2 for better memory allocation
                if (newSize < int.MaxValue)
                {
                    long candidateSize = (long)Math.Pow(2, Math.Ceiling(Math.Log(newSize) / Math.Log(2)));
                    if (candidateSize <= int.MaxValue)
                    {
                        newSize = candidateSize;
                    }
                }

                // Get new buffer from ArrayPool
                var newBuffer = Shared.Rent((int)newSize);

                // Copy existing data
                Buffer.BlockCopy(this.buffer, 0, newBuffer, 0, position);

                // Return old buffer to pool
                Shared.Return(this.buffer);

                // Create new MemoryStream with the new buffer
                this.innerStream.Dispose();
                this.innerStream = new MemoryStream(newBuffer, 0, newBuffer.Length, true, true)
                {
                    Position = position
                };
                this.innerStream.SetLength(position);
                this.buffer = newBuffer;
            }
        }

        public override void WriteByte(byte value)
        {
            this.EnlargeBuffer(1);
            this.innerStream!.WriteByte(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            this.EnlargeBuffer(count);
            this.innerStream!.Write(buffer, offset, count);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            this.EnlargeBuffer(count);
            return this.innerStream!.WriteAsync(buffer, offset, count, cancellationToken);
        }
#if NET6_0_OR_GREATER
        [Obsolete("This Remoting API is not supported and throws PlatformNotSupportedException.")]
#endif
        public override object InitializeLifetimeService()
        {
            this.ThrowIfDisposed();
            return this.innerStream!.InitializeLifetimeService();
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            this.ThrowIfDisposed();
            return this.innerStream!.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override int ReadByte()
        {
            this.ThrowIfDisposed();
            return this.innerStream!.ReadByte();
        }

        public override int ReadTimeout
        {
            get => this.innerStream?.ReadTimeout ?? 0;
            set
            {
                this.ThrowIfDisposed();
                this.innerStream!.ReadTimeout = value;
            }
        }

        public override long Seek(long offset, SeekOrigin loc)
        {
            this.ThrowIfDisposed();
            return this.innerStream!.Seek(offset, loc);
        }

        /// <summary>
        /// Note: This will not make the MemoryStream any smaller, only larger
        /// </summary>
        public override void SetLength(long value)
        {
            this.ThrowIfDisposed();
            if (value > this.buffer!.Length)
            {
                this.EnlargeBuffer((int)(value - this.innerStream!.Position));
            }

            this.innerStream!.SetLength(value);
        }

        public override int WriteTimeout
        {
            get => this.innerStream?.WriteTimeout ?? 0;
            set
            {
                this.ThrowIfDisposed();
                this.innerStream!.WriteTimeout = value;
            }
        }

        public ArraySegment<byte> GetDataArraySegment()
        {
            this.ThrowIfDisposed();
            return new ArraySegment<byte>(this.buffer!, 0, (int)this.innerStream!.Position);
        }

        public ArraySegment<byte> GetFreeArraySegment(int minSize)
        {
            this.ThrowIfDisposed();
            if (minSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(minSize));

            var currentPosition = (int)this.innerStream!.Position;
            var free = this.buffer!.Length - currentPosition;

            if (free < minSize)
                this.EnlargeBuffer(minSize - free);

            return new ArraySegment<byte>(this.buffer, currentPosition, this.buffer.Length - currentPosition);
        }

        public void WriteTo(Stream stream)
        {
            this.ThrowIfDisposed();
            // Write only the used portion of the buffer
            stream.Write(this.buffer!, 0, (int)this.innerStream!.Position);
        }

        private void ThrowIfDisposed()
        {
            if (this.isDisposed)
                throw new ObjectDisposedException(nameof(ArrayPoolStream));

        }
    }
}
