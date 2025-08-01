using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Samurai.WebSockets.Internal
{
    public static class WebSocketFrameCommon
    {
        public const int MaskKeyLength = 4;

        public static ArraySegment<byte> AsMaskKey(this byte[] bytes)
            => new ArraySegment<byte>(bytes, 0, MaskKeyLength);

        public static void ToggleMask(this ArraySegment<byte> maskKey, ArraySegment<byte> payload)
        {
            if (maskKey.Count != MaskKeyLength)
                throw new Exception($"MaskKey key must be {MaskKeyLength} bytes");

            var buffer = payload.Array;
            var maskKeyArray = maskKey.Array;
            var payloadOffset = payload.Offset;
            var payloadCount = payload.Count;
            var maskKeyOffset = maskKey.Offset;

            if (payloadCount == 0) return;

            unsafe
            {
                fixed (byte* bufferPtr = &buffer[payloadOffset])
                fixed (byte* maskPtr = &maskKeyArray[maskKeyOffset])
                {
                    if (Environment.Is64BitProcess)
                    {
                        ToggleMask64Bit(bufferPtr, payloadCount, maskPtr);
                    }
                    else
                    {
                        ToggleMask32Bit(bufferPtr, payloadCount, maskPtr);
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void ToggleMask64Bit(byte* buffer, int length, byte* mask)
        {
            // Create 64-bit mask pattern (repeat 4-byte mask twice)
            uint mask32 = *(uint*)mask;
            ulong mask64 = mask32 | ((ulong)mask32 << 32);

            int processed = 0;

            // Process 8-byte chunks first
            ulong* ulongPtr = (ulong*)buffer;
            int ulongCount = length / 8;

            for (int i = 0; i < ulongCount; i++)
            {
                ulongPtr[i] ^= mask64;
            }
            processed += ulongCount * 8;

            // Process remaining 4-byte chunks
            uint* uintPtr = (uint*)(buffer + processed);
            int remainingUints = (length - processed) / 4;

            for (int i = 0; i < remainingUints; i++)
            {
                uintPtr[i] ^= mask32;
            }
            processed += remainingUints * 4;

            // Handle final 1-3 bytes
            for (int i = processed; i < length; i++)
            {
                buffer[i] ^= mask[i % MaskKeyLength];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void ToggleMask32Bit(byte* buffer, int length, byte* mask)
        {
            // Create 32-bit mask pattern
            uint mask32 = *(uint*)mask;

            int processed = 0;

            // Process 4-byte chunks
            uint* uintPtr = (uint*)buffer;
            int uintCount = length / 4;

            for (int i = 0; i < uintCount; i++)
            {
                uintPtr[i] ^= mask32;
            }
            processed += uintCount * 4;

            // Handle final 1-3 bytes
            for (int i = processed; i < length; i++)
            {
                buffer[i] ^= mask[i % MaskKeyLength];
            }
        }

        // SIMD version that works on both architectures
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void ToggleMaskSIMD(byte* buffer, int length, byte* mask)
        {
            // Create mask pattern
            uint mask32 = *(uint*)mask;

            int processed = 0;

            // Use SIMD if available and beneficial
            if (Vector.IsHardwareAccelerated && length >= Vector<uint>.Count * 4)
            {
                var maskVector = new Vector<uint>(mask32);
                int vectorSize = Vector<uint>.Count;
                int vectorBytes = vectorSize * 4;
                int vectorLoops = length / vectorBytes;

                uint* uintBuffer = (uint*)buffer;

                for (int i = 0; i < vectorLoops; i++)
                {
                    var dataVector = Unsafe.Read<Vector<uint>>(uintBuffer + i * vectorSize);
                    var result = dataVector ^ maskVector;
                    Unsafe.Write(uintBuffer + i * vectorSize, result);
                }

                processed = vectorLoops * vectorBytes;
            }

            // Process remaining bytes in 4-byte chunks
            uint* remainingUintPtr = (uint*)(buffer + processed);
            int remainingUints = (length - processed) / 4;

            for (int i = 0; i < remainingUints; i++)
            {
                remainingUintPtr[i] ^= mask32;
            }
            processed += remainingUints * 4;

            // Handle final 1-3 bytes
            for (int i = processed; i < length; i++)
            {
                buffer[i] ^= mask[i % MaskKeyLength];
            }
        }

        // Alternative method using SIMD approach
        public static void ToggleMaskSIMDVersion(ArraySegment<byte> maskKey, ArraySegment<byte> payload)
        {
            if (maskKey.Count != MaskKeyLength)
                throw new Exception($"MaskKey key must be {MaskKeyLength} bytes");

            var buffer = payload.Array;
            var maskKeyArray = maskKey.Array;
            var payloadOffset = payload.Offset;
            var payloadCount = payload.Count;
            var maskKeyOffset = maskKey.Offset;

            if (payloadCount == 0) return;

            unsafe
            {
                fixed (byte* bufferPtr = &buffer[payloadOffset])
                fixed (byte* maskPtr = &maskKeyArray[maskKeyOffset])
                {
                    ToggleMaskSIMD(bufferPtr, payloadCount, maskPtr);
                }
            }
        }

        // Safe fallback version that works on both architectures
        public static void ToggleMaskSafe(ArraySegment<byte> maskKey, ArraySegment<byte> payload)
        {
            if (maskKey.Count != MaskKeyLength)
                throw new Exception($"MaskKey key must be {MaskKeyLength} bytes");

            var buffer = payload.Array;
            var maskKeyArray = maskKey.Array;
            var payloadOffset = payload.Offset;
            var payloadCount = payload.Count;
            var maskKeyOffset = maskKey.Offset;

            if (payloadCount == 0) return;

            // Create mask patterns for different architectures
            uint mask32 = BitConverter.IsLittleEndian
                ? (uint)(maskKeyArray[maskKeyOffset] |
                        (maskKeyArray[maskKeyOffset + 1] << 8) |
                        (maskKeyArray[maskKeyOffset + 2] << 16) |
                        (maskKeyArray[maskKeyOffset + 3] << 24))
                : (uint)((maskKeyArray[maskKeyOffset] << 24) |
                        (maskKeyArray[maskKeyOffset + 1] << 16) |
                        (maskKeyArray[maskKeyOffset + 2] << 8) |
                        maskKeyArray[maskKeyOffset + 3]);

            int i = payloadOffset;
            int end = payloadOffset + payloadCount;

            if (Environment.Is64BitProcess)
            {
                // 64-bit: process 8 bytes at a time when possible
                ulong mask64 = mask32 | ((ulong)mask32 << 32);
                int aligned8End = end - 7;

                while (i < aligned8End)
                {
                    ulong data = BitConverter.ToUInt64(buffer, i);
                    ulong result = data ^ mask64;
                    byte[] resultBytes = BitConverter.GetBytes(result);
                    Array.Copy(resultBytes, 0, buffer, i, 8);
                    i += 8;
                }
            }

            // Process remaining 4-byte chunks
            int aligned4End = end - 3;
            while (i < aligned4End)
            {
                uint data = BitConverter.ToUInt32(buffer, i);
                uint result = data ^ mask32;
                byte[] resultBytes = BitConverter.GetBytes(result);
                Array.Copy(resultBytes, 0, buffer, i, 4);
                i += 4;
            }

            // Handle remaining bytes
            while (i < end)
            {
                var maskIndex = (i - payloadOffset) % MaskKeyLength;
                buffer[i] ^= maskKeyArray[maskKeyOffset + maskIndex];
                i++;
            }
        }
    }
}
