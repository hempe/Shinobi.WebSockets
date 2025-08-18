using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Shinobi.WebSockets.Extensions;

using Xunit;

namespace Shinobi.WebSockets.UnitTests
{
    public class BinaryReaderWriterExtensionsTests
    {
        [Fact]
        public async Task ReadFixedLengthAsync_WithZeroLength_ShouldReturnImmediatelyAsync()
        {
            // Arrange
            var stream = new MemoryStream();
            var buffer = new ArraySegment<byte>(new byte[10]);

            // Act & Assert - should not throw and complete immediately
            await stream.ReadFixedLengthAsync(0, buffer, CancellationToken.None);
        }

        [Fact]
        public async Task ReadFixedLengthAsync_WithBufferTooSmall_ShouldThrowInternalBufferOverflowExceptionAsync()
        {
            // Arrange
            var stream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
            var buffer = new ArraySegment<byte>(new byte[3]); // Buffer too small for 5 bytes

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InternalBufferOverflowException>(
                async () => await stream.ReadFixedLengthAsync(5, buffer, CancellationToken.None));

            Assert.Contains("Unable to read 5 bytes into buffer", exception.Message);
            Assert.Contains("size: 3", exception.Message);
        }

        [Fact]
        public async Task ReadFixedLengthAsync_WithEndOfStream_ShouldThrowEndOfStreamExceptionAsync()
        {
            // Arrange
            var stream = new MemoryStream(new byte[] { 1, 2 }); // Only 2 bytes available
            var buffer = new ArraySegment<byte>(new byte[10]);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<EndOfStreamException>(
                async () => await stream.ReadFixedLengthAsync(5, buffer, CancellationToken.None));

            Assert.Contains("Unexpected end of stream", exception.Message);
            Assert.Contains("5", exception.Message);
        }

        [Fact]
        public async Task ReadFixedLengthAsync_WithValidData_ShouldReadIntoBufferAsync()
        {
            // Arrange
            var sourceData = new byte[] { 0x12, 0x34, 0x56, 0x78, 0x9A };
            var stream = new MemoryStream(sourceData);
            var buffer = new ArraySegment<byte>(new byte[10], 2, 5); // Offset buffer

            // Act
            await stream.ReadFixedLengthAsync(5, buffer, CancellationToken.None);

            // Assert
            Assert.Equal(0x12, buffer.Array![2]);
            Assert.Equal(0x34, buffer.Array![3]);
            Assert.Equal(0x56, buffer.Array![4]);
            Assert.Equal(0x78, buffer.Array![5]);
            Assert.Equal(0x9A, buffer.Array![6]);
        }

        [Theory]
        [InlineData(true)] // Little endian
        [InlineData(false)] // Big endian
        public async Task ReadUShortAsync_ShouldReadCorrectEndiannessAsync(bool isLittleEndian)
        {
            // Arrange
            ushort expectedValue = 0x1234;
            var sourceData = isLittleEndian
                ? new byte[] { 0x34, 0x12 } // Little endian: LSB first
                : new byte[] { 0x12, 0x34 }; // Big endian: MSB first

            var stream = new MemoryStream(sourceData);
            var buffer = new ArraySegment<byte>(new byte[10]);

            // Act
            var result = await stream.ReadUShortAsync(buffer, isLittleEndian, CancellationToken.None);

            // Assert
            Assert.Equal(expectedValue, result);
        }

        [Theory]
        [InlineData(true)] // Little endian
        [InlineData(false)] // Big endian
        public async Task ReadULongAsync_ShouldReadCorrectEndiannessAsync(bool isLittleEndian)
        {
            // Arrange
            ulong expectedValue = 0x123456789ABCDEF0;
            var sourceData = isLittleEndian
                ? new byte[] { 0xF0, 0xDE, 0xBC, 0x9A, 0x78, 0x56, 0x34, 0x12 } // Little endian
                : new byte[] { 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0 }; // Big endian

            var stream = new MemoryStream(sourceData);
            var buffer = new ArraySegment<byte>(new byte[10]);

            // Act
            var result = await stream.ReadULongAsync(buffer, isLittleEndian, CancellationToken.None);

            // Assert
            Assert.Equal(expectedValue, result);
        }

        [Theory]
        [InlineData(true)] // Little endian
        [InlineData(false)] // Big endian
        public void WriteUShort_ShouldWriteCorrectEndianness(bool isLittleEndian)
        {
            // Arrange
            ushort value = 0x1234;
            var stream = new MemoryStream();

            // Act
            stream.WriteUShort(value, isLittleEndian);

            // Assert
            var result = stream.ToArray();
            var expected = isLittleEndian
                ? new byte[] { 0x34, 0x12 } // Little endian: LSB first
                : new byte[] { 0x12, 0x34 }; // Big endian: MSB first

            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(true)] // Little endian
        [InlineData(false)] // Big endian
        public void WriteULong_ShouldWriteCorrectEndianness(bool isLittleEndian)
        {
            // Arrange
            ulong value = 0x123456789ABCDEF0;
            var stream = new MemoryStream();

            // Act
            stream.WriteULong(value, isLittleEndian);

            // Assert
            var result = stream.ToArray();
            var expected = isLittleEndian
                ? new byte[] { 0xF0, 0xDE, 0xBC, 0x9A, 0x78, 0x56, 0x34, 0x12 } // Little endian
                : new byte[] { 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0 }; // Big endian

            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(true)] // Little endian
        [InlineData(false)] // Big endian
        public async Task WriteUShortAsync_ShouldWriteCorrectEndiannessAsync(bool isLittleEndian)
        {
            // Arrange
            ushort value = 0x1234;
            var stream = new MemoryStream();

            // Act
            await stream.WriteUShortAsync(value, isLittleEndian, CancellationToken.None);

            // Assert
            var result = stream.ToArray();
            var expected = isLittleEndian
                ? new byte[] { 0x34, 0x12 } // Little endian: LSB first
                : new byte[] { 0x12, 0x34 }; // Big endian: MSB first

            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(true)] // Little endian
        [InlineData(false)] // Big endian
        public async Task WriteULongAsync_ShouldWriteCorrectEndiannessAsync(bool isLittleEndian)
        {
            // Arrange
            ulong value = 0x123456789ABCDEF0;
            var stream = new MemoryStream();

            // Act
            await stream.WriteULongAsync(value, isLittleEndian, CancellationToken.None);

            // Assert
            var result = stream.ToArray();
            var expected = isLittleEndian
                ? new byte[] { 0xF0, 0xDE, 0xBC, 0x9A, 0x78, 0x56, 0x34, 0x12 } // Little endian
                : new byte[] { 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0 }; // Big endian

            Assert.Equal(expected, result);
        }

        [Fact]
        public async Task RoundTrip_UShort_ShouldPreserveValueAsync()
        {
            // Arrange
            ushort[] testValues = { 0, 1, 255, 256, 32767, 65535 };

            foreach (var originalValue in testValues)
            {
                foreach (var isLittleEndian in new[] { true, false })
                {
                    // Act - Write then read
                    var stream = new MemoryStream();
                    await stream.WriteUShortAsync(originalValue, isLittleEndian);

                    stream.Position = 0;
                    var buffer = new ArraySegment<byte>(new byte[10]);
                    var readValue = await stream.ReadUShortAsync(buffer, isLittleEndian, CancellationToken.None);

                    // Assert
                    Assert.Equal(originalValue, readValue);
                }
            }
        }

        [Fact]
        public async Task RoundTrip_ULong_ShouldPreserveValueAsync()
        {
            // Arrange
            ulong[] testValues = {
                0,
                1,
                255,
                256,
                65535,
                65536,
                0x123456789ABCDEF0,
                ulong.MaxValue
            };

            foreach (var originalValue in testValues)
            {
                foreach (var isLittleEndian in new[] { true, false })
                {
                    // Act - Write then read
                    var stream = new MemoryStream();
                    await stream.WriteULongAsync(originalValue, isLittleEndian);

                    stream.Position = 0;
                    var buffer = new ArraySegment<byte>(new byte[10]);
                    var readValue = await stream.ReadULongAsync(buffer, isLittleEndian, CancellationToken.None);

                    // Assert
                    Assert.Equal(originalValue, readValue);
                }
            }
        }

        [Fact]
        public async Task ReadFixedLengthAsync_WithPartialReads_ShouldEventuallyReadAllDataAsync()
        {
            // Arrange - Create a stream that returns data in chunks
            var sourceData = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
            var slowStream = new SlowReadMemoryStream(sourceData, maxBytesPerRead: 3);
            var buffer = new ArraySegment<byte>(new byte[15], 2, 10);

            // Act
            await slowStream.ReadFixedLengthAsync(10, buffer, CancellationToken.None);

            // Assert
            for (int i = 0; i < 10; i++)
            {
                Assert.Equal(sourceData[i], buffer.Array![i + 2]);
            }
        }

        [Fact]
        public async Task BinaryReadWrite_WithCancellation_ShouldRespectCancellationTokenAsync()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            cts.Cancel(); // Pre-cancelled token

            var stream = new MemoryStream(new byte[100]);
            var buffer = new ArraySegment<byte>(new byte[10]);

            // Act & Assert - TaskCanceledException is a subtype of OperationCanceledException
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await stream.ReadFixedLengthAsync(5, buffer, cts.Token));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await stream.WriteUShortAsync(0x1234, true, cts.Token));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await stream.WriteULongAsync(0x123456789ABCDEF0, true, cts.Token));
        }

        [Fact]
        public async Task SyncAndAsyncMethods_ShouldProduceSameResultsAsync()
        {
            // Arrange
            ushort ushortValue = 0x1234;
            ulong ulongValue = 0x123456789ABCDEF0;

            foreach (var isLittleEndian in new[] { true, false })
            {
                // Test UShort
                var syncStream = new MemoryStream();
                var asyncStream = new MemoryStream();

                // Act
                syncStream.WriteUShort(ushortValue, isLittleEndian);
                await asyncStream.WriteUShortAsync(ushortValue, isLittleEndian);

                // Assert
                Assert.Equal(syncStream.ToArray(), asyncStream.ToArray());

                // Test ULong
                syncStream = new MemoryStream();
                asyncStream = new MemoryStream();

                syncStream.WriteULong(ulongValue, isLittleEndian);
                await asyncStream.WriteULongAsync(ulongValue, isLittleEndian);

                Assert.Equal(syncStream.ToArray(), asyncStream.ToArray());
            }
        }
    }

    /// <summary>
    /// Helper class that simulates slow network reads by limiting bytes per read operation
    /// </summary>
    internal class SlowReadMemoryStream : MemoryStream
    {
        private readonly int maxBytesPerRead;

        public SlowReadMemoryStream(byte[] buffer, int maxBytesPerRead = 1) : base(buffer)
        {
            this.maxBytesPerRead = maxBytesPerRead;
        }

        public async override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            // Limit the number of bytes read per operation to simulate slow/chunked reading
            var actualCount = Math.Min(count, this.maxBytesPerRead);
            return await base.ReadAsync(buffer, offset, actualCount, cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            // Limit the number of bytes read per operation to simulate slow/chunked reading
            var actualCount = Math.Min(count, this.maxBytesPerRead);
            return base.Read(buffer, offset, actualCount);
        }
    }
}