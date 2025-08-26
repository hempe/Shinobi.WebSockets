using System;
using System.Linq;
using Shinobi.WebSockets.Internal;
using Xunit;

namespace Shinobi.WebSockets.UnitTests
{
    public class WebSocketFrameCommonTests
    {
        [Fact]
        public void AsMaskKey_WithValidByteArray_ShouldReturnCorrectSegment()
        {
            // Arrange
            var bytes = new byte[] { 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC };

            // Act
            var maskKey = bytes.AsMaskKey();

            // Assert
            Assert.Equal(4, maskKey.Count);
            Assert.Equal(0, maskKey.Offset);
            Assert.Same(bytes, maskKey.Array);
        }

        [Fact]
        public void ToggleMask_WithInvalidMaskKeyLength_ShouldThrowException()
        {
            // Arrange
            var invalidMaskKey = new ArraySegment<byte>(new byte[] { 0x12, 0x34, 0x56 }); // Only 3 bytes
            var payload = new byte[] { 0x01, 0x02, 0x03, 0x04 };

            // Act & Assert
            var exception = Assert.Throws<Exception>(() => 
                invalidMaskKey.ToggleMask(payload, 0, payload.Length));
            
            Assert.Contains("MaskKey key must be 4 bytes", exception.Message);
        }

        [Fact]
        public void ToggleMask_WithZeroPayloadCount_ShouldNotModifyBuffer()
        {
            // Arrange
            var maskKey = new byte[] { 0x12, 0x34, 0x56, 0x78 }.AsMaskKey();
            var payload = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            var originalPayload = payload.ToArray();

            // Act
            maskKey.ToggleMask(payload, 0, 0);

            // Assert
            Assert.Equal(originalPayload, payload);
        }

        [Fact]
        public void ToggleMask_SingleByte_ShouldApplyMaskCorrectly()
        {
            // Arrange
            var maskKey = new byte[] { 0x12, 0x34, 0x56, 0x78 }.AsMaskKey();
            var payload = new byte[] { 0xFF };
            var expected = new byte[] { (byte)(0xFF ^ 0x12) };

            // Act
            maskKey.ToggleMask(payload, 0, 1);

            // Assert
            Assert.Equal(expected, payload);
        }

        [Fact]
        public void ToggleMask_FourBytes_ShouldApplyMaskCorrectly()
        {
            // Arrange
            var maskKey = new byte[] { 0x12, 0x34, 0x56, 0x78 }.AsMaskKey();
            var payload = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
            var expected = new byte[] 
            { 
                (byte)(0xAA ^ 0x12), 
                (byte)(0xBB ^ 0x34), 
                (byte)(0xCC ^ 0x56), 
                (byte)(0xDD ^ 0x78) 
            };

            // Act
            maskKey.ToggleMask(payload, 0, 4);

            // Assert
            Assert.Equal(expected, payload);
        }

        [Fact]
        public void ToggleMask_EightBytes_ShouldApplyMaskCorrectly()
        {
            // Arrange
            var maskKey = new byte[] { 0x12, 0x34, 0x56, 0x78 }.AsMaskKey();
            var payload = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x11, 0x22 };
            var expected = new byte[] 
            { 
                (byte)(0xAA ^ 0x12), 
                (byte)(0xBB ^ 0x34), 
                (byte)(0xCC ^ 0x56), 
                (byte)(0xDD ^ 0x78),
                (byte)(0xEE ^ 0x12), // Mask repeats
                (byte)(0xFF ^ 0x34), 
                (byte)(0x11 ^ 0x56), 
                (byte)(0x22 ^ 0x78) 
            };

            // Act
            maskKey.ToggleMask(payload, 0, 8);

            // Assert
            Assert.Equal(expected, payload);
        }

        [Fact]
        public void ToggleMask_LargePayload_ShouldApplyMaskCorrectly()
        {
            // Arrange
            var maskKey = new byte[] { 0x12, 0x34, 0x56, 0x78 }.AsMaskKey();
            var payload = new byte[1000];
            
            // Fill with pattern
            for (int i = 0; i < payload.Length; i++)
            {
                payload[i] = (byte)(i % 256);
            }

            var originalPayload = payload.ToArray();

            // Act
            maskKey.ToggleMask(payload, 0, payload.Length);

            // Assert - verify each byte is correctly masked
            for (int i = 0; i < payload.Length; i++)
            {
                var expectedMaskedByte = (byte)(originalPayload[i] ^ maskKey.Array![i % 4]);
                Assert.Equal(expectedMaskedByte, payload[i]);
            }
        }

        [Fact]
        public void ToggleMask_WithOffset_ShouldApplyMaskToCorrectBytes()
        {
            // Arrange
            var maskKey = new byte[] { 0x12, 0x34, 0x56, 0x78 }.AsMaskKey();
            var payload = new byte[] { 0x00, 0x00, 0xAA, 0xBB, 0xCC, 0xDD, 0x00, 0x00 };
            var originalPayload = payload.ToArray();

            // Act - mask only bytes 2-5 (4 bytes starting at offset 2)
            maskKey.ToggleMask(payload, 2, 4);

            // Assert
            Assert.Equal(originalPayload[0], payload[0]); // Unchanged
            Assert.Equal(originalPayload[1], payload[1]); // Unchanged
            Assert.Equal((byte)(0xAA ^ 0x12), payload[2]); // Masked
            Assert.Equal((byte)(0xBB ^ 0x34), payload[3]); // Masked
            Assert.Equal((byte)(0xCC ^ 0x56), payload[4]); // Masked
            Assert.Equal((byte)(0xDD ^ 0x78), payload[5]); // Masked
            Assert.Equal(originalPayload[6], payload[6]); // Unchanged
            Assert.Equal(originalPayload[7], payload[7]); // Unchanged
        }

        [Fact]
        public void ToggleMask_IsReversible_ShouldRestoreOriginalData()
        {
            // Arrange
            var maskKey = new byte[] { 0x12, 0x34, 0x56, 0x78 }.AsMaskKey();
            var originalPayload = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x11, 0x22, 0x33 };
            var payload = originalPayload.ToArray();

            // Act - mask then unmask
            maskKey.ToggleMask(payload, 0, payload.Length);
            maskKey.ToggleMask(payload, 0, payload.Length);

            // Assert - should be back to original
            Assert.Equal(originalPayload, payload);
        }

        [Fact]
        public void ToggleMask_OddLength_ShouldHandleRemainingBytes()
        {
            // Arrange
            var maskKey = new byte[] { 0x12, 0x34, 0x56, 0x78 }.AsMaskKey();
            var payload = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x11 }; // 7 bytes (odd)
            var expected = new byte[] 
            { 
                (byte)(0xAA ^ 0x12), 
                (byte)(0xBB ^ 0x34), 
                (byte)(0xCC ^ 0x56), 
                (byte)(0xDD ^ 0x78),
                (byte)(0xEE ^ 0x12), 
                (byte)(0xFF ^ 0x34), 
                (byte)(0x11 ^ 0x56)
            };

            // Act
            maskKey.ToggleMask(payload, 0, 7);

            // Assert
            Assert.Equal(expected, payload);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(9)]
        [InlineData(10)]
        [InlineData(11)]
        [InlineData(15)]
        [InlineData(16)]
        [InlineData(17)]
        [InlineData(31)]
        [InlineData(32)]
        [InlineData(33)]
        [InlineData(63)]
        [InlineData(64)]
        [InlineData(65)]
        public void ToggleMask_VariousLengths_ShouldApplyMaskCorrectly(int length)
        {
            // Arrange
            var maskKey = new byte[] { 0x12, 0x34, 0x56, 0x78 }.AsMaskKey();
            var payload = new byte[length];
            
            // Fill with predictable pattern
            for (int i = 0; i < length; i++)
            {
                payload[i] = (byte)(i % 256);
            }
            
            var originalPayload = payload.ToArray();

            // Act
            maskKey.ToggleMask(payload, 0, length);

            // Assert - verify each byte is correctly masked using the cycling mask
            for (int i = 0; i < length; i++)
            {
                var maskByte = maskKey.Array![i % 4];
                var expectedMaskedByte = (byte)(originalPayload[i] ^ maskByte);
                Assert.Equal(expectedMaskedByte, payload[i]);
            }
        }

        [Fact]
        public void ToggleMask_LargePayloadPerformance_ShouldHandleEfficiently()
        {
            // Arrange - Large payload to test performance path
            var maskKey = new byte[] { 0x12, 0x34, 0x56, 0x78 }.AsMaskKey();
            var payload = new byte[1024 * 1024]; // 1MB payload
            
            // Fill with pattern
            for (int i = 0; i < payload.Length; i++)
            {
                payload[i] = (byte)(i % 256);
            }

            var originalPayload = payload.ToArray();

            // Act
            maskKey.ToggleMask(payload, 0, payload.Length);

            // Assert - spot check various positions to ensure correctness
            for (int i = 0; i < payload.Length; i += 1000) // Check every 1000th byte
            {
                var expectedMaskedByte = (byte)(originalPayload[i] ^ maskKey.Array![i % 4]);
                Assert.Equal(expectedMaskedByte, payload[i]);
            }
        }

        [Fact]
        public void ToggleMask_AlignedAndUnalignedData_ShouldProduceSameResult()
        {
            // Arrange - Test both aligned and unaligned memory access patterns
            var maskKey = new byte[] { 0x12, 0x34, 0x56, 0x78 }.AsMaskKey();
            
            // Create the same data pattern for both tests
            var testData = new byte[64];
            for (int i = 0; i < testData.Length; i++)
                testData[i] = (byte)(i % 256);
            
            // Test 1: Apply mask to aligned data (starting at offset 0)
            var alignedPayload = testData.ToArray();
            maskKey.ToggleMask(alignedPayload, 0, alignedPayload.Length);
            
            // Test 2: Apply mask to the same data but at an unaligned offset
            var largerArray = new byte[70];
            Array.Copy(testData, 0, largerArray, 3, testData.Length); // Copy test data to offset 3
            maskKey.ToggleMask(largerArray, 3, testData.Length);

            // Assert - Results should be identical
            var unalignedResult = new byte[testData.Length];
            Array.Copy(largerArray, 3, unalignedResult, 0, testData.Length);
            Assert.Equal(alignedPayload, unalignedResult);
        }
    }
}