using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Shinobi.WebSockets.Internal;

using Xunit;

namespace Shinobi.WebSockets.UnitTests
{
    public class ArrayPoolStreamTests
    {
        [Fact]
        public void Constructor_WithDefaultSize_ShouldInitializeCorrectly()
        {
            // Act
            using var stream = new ArrayPoolStream();

            // Assert
            Assert.Equal(16384, stream.InitialSize);
            Assert.Equal(0, stream.Length);
            Assert.Equal(0, stream.Position);
            Assert.True(stream.CanRead);
            Assert.True(stream.CanWrite);
            Assert.True(stream.CanSeek);
        }

        [Fact]
        public void Constructor_WithCustomSize_ShouldInitializeWithCorrectSize()
        {
            // Arrange
            int customSize = 8192;

            // Act
            using var stream = new ArrayPoolStream(customSize);

            // Assert
            Assert.Equal(customSize, stream.InitialSize);
            Assert.Equal(0, stream.Length);
            Assert.Equal(0, stream.Position);
        }

        [Fact]
        public void Write_WithData_ShouldUpdateLengthAndPosition()
        {
            // Arrange
            using var stream = new ArrayPoolStream();
            var data = Encoding.UTF8.GetBytes("Hello, World!");

            // Act
            stream.Write(data, 0, data.Length);

            // Assert
            Assert.Equal(data.Length, stream.Length);
            Assert.Equal(data.Length, stream.Position);
        }

        [Fact]
        public void Read_AfterWrite_ShouldReturnWrittenData()
        {
            // Arrange
            using var stream = new ArrayPoolStream();
            var originalData = Encoding.UTF8.GetBytes("Hello, World!");
            stream.Write(originalData, 0, originalData.Length);
            stream.Position = 0;

            var buffer = new byte[originalData.Length];

            // Act
            var bytesRead = stream.Read(buffer, 0, buffer.Length);

            // Assert
            Assert.Equal(originalData.Length, bytesRead);
            Assert.Equal(originalData, buffer);
        }

        [Fact]
        public void WriteByte_ShouldWriteSingleByte()
        {
            // Arrange
            using var stream = new ArrayPoolStream();
            byte testByte = 0x42;

            // Act
            stream.WriteByte(testByte);

            // Assert
            Assert.Equal(1, stream.Length);
            Assert.Equal(1, stream.Position);

            // Verify the byte was written correctly
            stream.Position = 0;
            Assert.Equal(testByte, stream.ReadByte());
        }

        [Fact]
        public void ReadByte_FromEmptyStream_ShouldReturnMinusOne()
        {
            // Arrange
            using var stream = new ArrayPoolStream();

            // Act
            var result = stream.ReadByte();

            // Assert
            Assert.Equal(-1, result);
        }

        [Fact]
        public void Seek_WithValidParameters_ShouldUpdatePosition()
        {
            // Arrange
            using var stream = new ArrayPoolStream();
            var data = Encoding.UTF8.GetBytes("Hello, World!");
            stream.Write(data, 0, data.Length);

            // Act
            var newPosition = stream.Seek(5, SeekOrigin.Begin);

            // Assert
            Assert.Equal(5, newPosition);
            Assert.Equal(5, stream.Position);
        }

        [Fact]
        public void SetLength_WithLargerValue_ShouldEnlargeStream()
        {
            // Arrange
            using var stream = new ArrayPoolStream();
            var data = Encoding.UTF8.GetBytes("Hello");
            stream.Write(data, 0, data.Length);

            // Act
            stream.SetLength(100);

            // Assert
            Assert.Equal(100, stream.Length);
            Assert.Equal(5, stream.Position); // Position should remain unchanged
        }

        [Fact]
        public void GetBuffer_ShouldReturnInternalBuffer()
        {
            // Arrange
            using var stream = new ArrayPoolStream(1024);
            var data = Encoding.UTF8.GetBytes("Test");
            stream.Write(data, 0, data.Length);

            // Act
            var buffer = stream.GetBuffer();

            // Assert
            Assert.NotNull(buffer);
            Assert.True(buffer.Length >= 1024);
            // Compare arrays without range syntax (for .NET Framework compatibility)
            for (int i = 0; i < data.Length; i++)
            {
                Assert.Equal(data[i], buffer[i]);
            }
        }

        [Fact]
        public void GetDataArraySegment_ShouldReturnCorrectSegment()
        {
            // Arrange
            using var stream = new ArrayPoolStream();
            var data = Encoding.UTF8.GetBytes("Hello, World!");
            stream.Write(data, 0, data.Length);

            // Act
            var segment = stream.GetDataArraySegment();

            // Assert
            Assert.Equal(data.Length, segment.Count);
            Assert.Equal(0, segment.Offset);
            // Compare segment data without range syntax (for .NET Framework compatibility)
            for (int i = 0; i < segment.Count; i++)
            {
                Assert.Equal(data[i], segment.Array![segment.Offset + i]);
            }
        }

        [Fact]
        public void GetFreeArraySegment_WithValidMinSize_ShouldReturnFreeSpace()
        {
            // Arrange
            using var stream = new ArrayPoolStream(1024);
            var data = Encoding.UTF8.GetBytes("Hello");
            stream.Write(data, 0, data.Length);

            // Act
            var freeSegment = stream.GetFreeArraySegment(100);

            // Assert
            Assert.True(freeSegment.Count >= 100);
            Assert.Equal(data.Length, freeSegment.Offset); // Should start after written data
        }

        [Fact]
        public void GetFreeArraySegment_WithZeroMinSize_ShouldThrowArgumentOutOfRangeException()
        {
            // Arrange
            using var stream = new ArrayPoolStream();

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.GetFreeArraySegment(0));
        }

        [Fact]
        public void GetFreeArraySegment_WithNegativeMinSize_ShouldThrowArgumentOutOfRangeException()
        {
            // Arrange
            using var stream = new ArrayPoolStream();

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.GetFreeArraySegment(-1));
        }

        [Fact]
        public void GetFreeSpan_ShouldReturnSpanOverFreeSpace()
        {
            // Arrange
            using var stream = new ArrayPoolStream();
            var data = Encoding.UTF8.GetBytes("Hello");
            stream.Write(data, 0, data.Length);

            // Act
            var freeSpan = stream.GetFreeSpan(10);

            // Assert
            Assert.Equal(10, freeSpan.Length);

            // Write to the span and verify
            var testData = Encoding.UTF8.GetBytes("World");
            testData.CopyTo(freeSpan);
        }

        [Fact]
        public void GetFreeSpan_WithZeroSizeHint_ShouldReturnSpanOfSizeOne()
        {
            // Arrange
            using var stream = new ArrayPoolStream();

            // Act
            var freeSpan = stream.GetFreeSpan(0);

            // Assert
            Assert.Equal(1, freeSpan.Length);
        }

        [Fact]
        public void WriteTo_ShouldWriteUsedPortionToDestination()
        {
            // Arrange
            using var source = new ArrayPoolStream();
            using var destination = new MemoryStream();
            var data = Encoding.UTF8.GetBytes("Hello, World!");
            source.Write(data, 0, data.Length);

            // Act
            source.WriteTo(destination);

            // Assert
            Assert.Equal(data.Length, destination.Length);
            Assert.Equal(data, destination.ToArray());
        }

        [Fact]
        public void Flush_ShouldNotThrow()
        {
            // Arrange
            using var stream = new ArrayPoolStream();
            var data = Encoding.UTF8.GetBytes("Test");
            stream.Write(data, 0, data.Length);

            // Act & Assert
            stream.Flush(); // Should not throw
        }

        [Fact]
        public async Task FlushAsync_ShouldNotThrowAsync()
        {
            // Arrange
            using var stream = new ArrayPoolStream();
            var data = Encoding.UTF8.GetBytes("Test");
            await stream.WriteAsync(data, 0, data.Length);

            // Act & Assert
            await stream.FlushAsync(); // Should not throw
        }

        [Fact]
        public async Task WriteAsync_ShouldWriteDataAsync()
        {
            // Arrange
            using var stream = new ArrayPoolStream();
            var data = Encoding.UTF8.GetBytes("Hello Async");

            // Act
            await stream.WriteAsync(data, 0, data.Length);

            // Assert
            Assert.Equal(data.Length, stream.Length);
            Assert.Equal(data.Length, stream.Position);
        }

        [Fact]
        public async Task ReadAsync_ShouldReadDataAsync()
        {
            // Arrange
            using var stream = new ArrayPoolStream();
            var originalData = Encoding.UTF8.GetBytes("Hello Async");
            await stream.WriteAsync(originalData, 0, originalData.Length);
            stream.Position = 0;

            var buffer = new byte[originalData.Length];

            // Act
            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);

            // Assert
            Assert.Equal(originalData.Length, bytesRead);
            Assert.Equal(originalData, buffer);
        }

        [Fact]
        public async Task CopyToAsync_ShouldCopyDataToDestinationAsync()
        {
            // Arrange
            using var source = new ArrayPoolStream();
            using var destination = new MemoryStream();
            var data = Encoding.UTF8.GetBytes("Copy me async!");
            await source.WriteAsync(data, 0, data.Length);
            source.Position = 0;

            // Act
            await source.CopyToAsync(destination);

            // Assert
            Assert.Equal(data.Length, destination.Length);
            Assert.Equal(data, destination.ToArray());
        }

        [Fact]
        public void Capacity_Get_ShouldReturnCurrentCapacity()
        {
            // Arrange
            using var stream = new ArrayPoolStream(1024);

            // Act
            var capacity = stream.Capacity;

            // Assert
            Assert.True(capacity >= 1024);
        }

        [Fact]
        public void Capacity_Set_WithLargerValue_ShouldEnlargeBuffer()
        {
            // Arrange
            using var stream = new ArrayPoolStream(1024);
            var initialCapacity = stream.Capacity;

            // Act
            stream.Capacity = initialCapacity * 2;

            // Assert
            Assert.True(stream.Capacity >= initialCapacity * 2);
        }

        [Fact]
        public void EnlargeBuffer_WithLargeData_ShouldExpandCorrectly()
        {
            // Arrange
            using var stream = new ArrayPoolStream(64); // Small initial size
            var largeData = new byte[1024]; // Larger than initial buffer

            // Act
            stream.Write(largeData, 0, largeData.Length);

            // Assert
            Assert.Equal(largeData.Length, stream.Length);
            Assert.True(stream.GetBuffer().Length >= largeData.Length);
        }

        [Fact]
        public void BeginRead_EndRead_ShouldWorkCorrectly()
        {
            // Arrange
            using var stream = new ArrayPoolStream();
            var data = Encoding.UTF8.GetBytes("Async Test");
            stream.Write(data, 0, data.Length);
            stream.Position = 0;

            var buffer = new byte[data.Length];

            // Act
            var result = stream.BeginRead(buffer, 0, buffer.Length, null, null);
            var bytesRead = stream.EndRead(result);

            // Assert
            Assert.Equal(data.Length, bytesRead);
            Assert.Equal(data, buffer);
        }

        [Fact]
        public void BeginWrite_EndWrite_ShouldWorkCorrectly()
        {
            // Arrange
            using var stream = new ArrayPoolStream();
            var data = Encoding.UTF8.GetBytes("Async Write Test");

            // Act
            var result = stream.BeginWrite(data, 0, data.Length, null, null);
            stream.EndWrite(result);

            // Assert
            Assert.Equal(data.Length, stream.Length);
            Assert.Equal(data.Length, stream.Position);
        }

        [Fact]
        public void ReadTimeout_SetOnMemoryStream_ShouldThrowInvalidOperationException()
        {
            // Arrange
            using var stream = new ArrayPoolStream();
            var timeout = 5000;

            // Act & Assert - MemoryStream doesn't support timeouts
            Assert.Throws<InvalidOperationException>(() => stream.ReadTimeout = timeout);
        }

        [Fact]
        public void WriteTimeout_SetOnMemoryStream_ShouldThrowInvalidOperationException()
        {
            // Arrange
            using var stream = new ArrayPoolStream();
            var timeout = 3000;

            // Act & Assert - MemoryStream doesn't support timeouts
            Assert.Throws<InvalidOperationException>(() => stream.WriteTimeout = timeout);
        }

        [Fact]
        public void ReadTimeout_Get_AfterDispose_ShouldReturnZero()
        {
            // Arrange
            var stream = new ArrayPoolStream();
            stream.Dispose();

            // Act & Assert - After dispose, innerStream is null so should return 0
            Assert.Equal(0, stream.ReadTimeout);
        }

        [Fact]
        public void WriteTimeout_Get_AfterDispose_ShouldReturnZero()
        {
            // Arrange
            var stream = new ArrayPoolStream();
            stream.Dispose();

            // Act & Assert - After dispose, innerStream is null so should return 0
            Assert.Equal(0, stream.WriteTimeout);
        }

        [Fact]
        public void Dispose_ShouldMakeStreamUnusable()
        {
            // Arrange
            var stream = new ArrayPoolStream();
            var data = Encoding.UTF8.GetBytes("Test");
            stream.Write(data, 0, data.Length);

            // Act
            stream.Dispose();

            // Assert
            Assert.False(stream.CanRead);
            Assert.False(stream.CanWrite);
            Assert.False(stream.CanSeek);
            Assert.Throws<ObjectDisposedException>(() => stream.Write(data, 0, data.Length));
            Assert.Throws<ObjectDisposedException>(() => stream.Read(new byte[10], 0, 10));
            Assert.Throws<ObjectDisposedException>(() => stream.GetBuffer());
        }

        [Fact]
        public void Close_ShouldDisposeStream()
        {
            // Arrange
            var stream = new ArrayPoolStream();

            // Act
            stream.Close();

            // Assert
            Assert.False(stream.CanRead);
            Assert.False(stream.CanWrite);
            Assert.False(stream.CanSeek);
        }

        [Fact]
        public void CanTimeout_ShouldReturnCorrectValue()
        {
            // Arrange & Act
            using var stream = new ArrayPoolStream();

            // Assert
            // MemoryStream's CanTimeout should be false
            Assert.False(stream.CanTimeout);
        }

        [Fact]
        public void Position_AfterDispose_ShouldReturnZero()
        {
            // Arrange
            var stream = new ArrayPoolStream();
            stream.Write(new byte[] { 1, 2, 3 }, 0, 3);
            stream.Dispose();

            // Act & Assert
            Assert.Equal(0, stream.Position);
        }

        [Fact]
        public void Length_AfterDispose_ShouldReturnZero()
        {
            // Arrange
            var stream = new ArrayPoolStream();
            stream.Write(new byte[] { 1, 2, 3 }, 0, 3);
            stream.Dispose();

            // Act & Assert
            Assert.Equal(0, stream.Length);
        }

        [Fact]
        public void Multiple_WriteOperations_ShouldExpandBufferAsNeeded()
        {
            // Arrange
            using var stream = new ArrayPoolStream(16); // Very small buffer
            var chunk = new byte[10];

            // Act - Write multiple chunks that will exceed initial buffer size
            for (int i = 0; i < 5; i++)
            {
                stream.Write(chunk, 0, chunk.Length);
            }

            // Assert
            Assert.Equal(50, stream.Length);
            Assert.True(stream.GetBuffer().Length >= 50);
        }
    }
}