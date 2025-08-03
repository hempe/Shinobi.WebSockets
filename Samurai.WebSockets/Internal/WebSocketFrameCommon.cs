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

        public static void ToggleMask(this ArraySegment<byte> maskKey, byte[] payloadArray, int payloadOffset, int payloadCount)
        {
            if (maskKey.Count != MaskKeyLength)
                throw new Exception($"MaskKey key must be {MaskKeyLength} bytes");

            var buffer = payloadArray;
            var maskKeyArray = maskKey.Array;
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
    }
}
