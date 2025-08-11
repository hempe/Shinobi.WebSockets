using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Shinobi.WebSockets.Exceptions;
using Shinobi.WebSockets.Internal;

#if NET9_0_OR_GREATER
#else 
using Shinobi.WebSockets.Extensions;
#endif

namespace Shinobi.WebSockets
{
    /// <summary>
    /// Base class containing shared HTTP header functionality
    /// </summary>
    public abstract class HttpHeader
    {
        private const string NewLine = "\r\n";

        internal readonly IDictionary<string, HashSet<string>> headers;

        protected HttpHeader(IDictionary<string, HashSet<string>> headers)
        {
            this.headers = headers;
        }

        /// <summary>
        /// Get all values for a header as IEnumerable
        /// </summary>
        public IEnumerable<string> GetHeaderValues(string headerName)
            => this.headers?.TryGetValue(headerName, out var values) == true ? values : Enumerable.Empty<string>();

        /// <summary>
        /// Get first value for a header (convenience method)
        /// </summary>
        public string? GetHeaderValue(string headerName)
            => this.headers?.TryGetValue(headerName, out var values) == true ? values.FirstOrDefault() : null;


        /// <summary>
        /// Get all header values as comma-separated string (HTTP standard format)
        /// </summary>
        public string? GetHeaderValuesCombined(string headerName)
        {
            if (this.headers is null)
                return null;

            if (!this.headers.TryGetValue(headerName, out var values) || !values.Any())
                return null;

            return string.Join(", ", values);
        }

        /// <summary>
        /// Check if header exists
        /// </summary>
        public bool HasHeader(string headerName)
            => this.headers?.ContainsKey(headerName) ?? false;

        /// <summary>
        /// Get headers as IEnumerable<KeyValuePair<string, IEnumerable<string>>> for compatibility
        /// </summary>
        public IEnumerable<KeyValuePair<string, IEnumerable<string>>> AsKeyValuePairs()
            => this.headers?.Select(kvp => new KeyValuePair<string, IEnumerable<string>>(kvp.Key, kvp.Value)) ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>();

        /// <summary>
        /// Reads raw HTTP header data from stream (shared implementation)
        /// </summary>
        /// <param name="stream">The stream to read UTF8 text from</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Raw header bytes as ArraySegment</returns>
        internal static async ValueTask<ArraySegment<byte>> ReadHttpHeaderDataAsync(Stream stream, CancellationToken cancellationToken)
        {
            const int MaxHeaderSize = 16 * 1024;
            const int InitialChunkSize = 1024;

            var headerBytes = Shared.Rent(MaxHeaderSize);
            var buffer = Shared.Rent(InitialChunkSize);

            int totalHeaderBytes = 0;
            int sequenceIndex = 0;

            try
            {
#if NET9_0_OR_GREATER
                var headerMemory = headerBytes.AsMemory(0, MaxHeaderSize);
                var bufferMemory = buffer.AsMemory(0, InitialChunkSize);
#endif

                // Phase 1: Chunked reads (until close to limit)
                while (MaxHeaderSize - totalHeaderBytes >= InitialChunkSize)
                {
#if NET9_0_OR_GREATER
                    int bytesRead = await stream.ReadAsync(bufferMemory.Slice(0, Math.Min(InitialChunkSize, MaxHeaderSize - totalHeaderBytes)), cancellationToken).ConfigureAwait(false);
#else
                    int bytesRead = await stream.ReadAsync(buffer, 0, Math.Min(InitialChunkSize, MaxHeaderSize - totalHeaderBytes), cancellationToken).ConfigureAwait(false);
#endif
                    if (bytesRead == 0)
                        return new ArraySegment<byte>();

                    // Check for end sequence more efficiently
                    for (int i = 0; i < bytesRead; i++)
                    {
                        byte currentByte = buffer[i];
                        headerBytes[totalHeaderBytes++] = currentByte;

                        // State machine for \r\n\r\n detection
                        sequenceIndex = currentByte switch
                        {
                            (byte)'\r' when sequenceIndex == 0 || sequenceIndex == 2 => sequenceIndex + 1,
                            (byte)'\r' => 1,
                            (byte)'\n' when sequenceIndex == 1 => 2,
                            (byte)'\n' when sequenceIndex == 3 => 4, // Found complete sequence
                            _ => 0
                        };

                        if (sequenceIndex == 4)
                        {
#if NET9_0_OR_GREATER
                            // For .NET 9+, we'll return the bytes and let the caller handle the span
                            var resultBytes = new byte[totalHeaderBytes];
                            Array.Copy(headerBytes, resultBytes, totalHeaderBytes);
                            return new ArraySegment<byte>(resultBytes);
#else
                            return new ArraySegment<byte>(headerBytes, 0, totalHeaderBytes);
#endif
                        }
                    }
                }

                // Phase 2: 1-byte reads (avoids overread near 16KB limit)
                var singleByteBuffer = Shared.Rent(1);
                try
                {
                    while (totalHeaderBytes < MaxHeaderSize)
                    {
#if NET9_0_OR_GREATER
                        int bytesRead = await stream.ReadAsync(singleByteBuffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
#else
                        int bytesRead = await stream.ReadAsync(singleByteBuffer, 0, 1, cancellationToken).ConfigureAwait(false);
#endif
                        if (bytesRead == 0)
                            return new ArraySegment<byte>();

                        byte currentByte = singleByteBuffer[0];
                        headerBytes[totalHeaderBytes++] = currentByte;

                        sequenceIndex = currentByte switch
                        {
                            (byte)'\r' when sequenceIndex == 0 || sequenceIndex == 2 => sequenceIndex + 1,
                            (byte)'\r' => 1,
                            (byte)'\n' when sequenceIndex == 1 => 2,
                            (byte)'\n' when sequenceIndex == 3 => 4,
                            _ => 0
                        };

                        if (sequenceIndex == 4)
                        {
#if NET9_0_OR_GREATER
                            var resultBytes = new byte[totalHeaderBytes];
                            Array.Copy(headerBytes, resultBytes, totalHeaderBytes);
                            return new ArraySegment<byte>(resultBytes);
#else
                            return new ArraySegment<byte>(headerBytes, 0, totalHeaderBytes);
#endif
                        }
                    }

                    throw new HttpHeaderTooLargeException("Http header too large (16KB)");
                }
                finally
                {
                    Shared.Return(singleByteBuffer);
                }
            }
            finally
            {
                Shared.Return(buffer);
#if !NET9_0_OR_GREATER
                // Only return the rented array for pre-.NET 9, since we're copying for .NET 9+
                if (totalHeaderBytes == 0) // Only return if we didn't use it in the result
#endif
                Shared.Return(headerBytes);
            }
        }


#if NET9_0_OR_GREATER
        internal static void ParseHeaders(ReadOnlySpan<char> httpHeader, int startPos, Dictionary<string, HashSet<string>> headers)
        {
            var pos = startPos;
            while (pos < httpHeader.Length)
            {
                var lineEnd = httpHeader.Slice(pos).IndexOf(NewLine.AsSpan());
                if (lineEnd == -1) break;
                lineEnd += pos;

                if (lineEnd == pos) break; // Empty line

                var colonPos = httpHeader.Slice(pos, lineEnd - pos).IndexOf(':');
                if (colonPos == -1)
                {
                    pos = lineEnd + 2;
                    continue;
                }
                colonPos += pos;

                var headerName = httpHeader.Slice(pos, colonPos - pos).Trim().ToString();
                var headerValue = httpHeader.Slice(colonPos + 1, lineEnd - colonPos - 1).Trim();

                // Handle multi-line headers
                var nextPos = lineEnd + 2;
                var valueBuilder = new StringBuilder(headerValue.ToString());

                while (nextPos < httpHeader.Length && (httpHeader[nextPos] == ' ' || httpHeader[nextPos] == '\t'))
                {
                    var nextLineEnd = httpHeader.Slice(nextPos).IndexOf(NewLine.AsSpan());
                    if (nextLineEnd == -1) break;
                    nextLineEnd += nextPos;

                    valueBuilder.Append(" ").Append(httpHeader.Slice(nextPos, nextLineEnd - nextPos).Trim().ToString());
                    nextPos = nextLineEnd + 2;
                }

                if (!headers.TryGetValue(headerName, out var valuesList))
                {
                    valuesList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    headers[headerName] = valuesList;
                }

                valuesList.Add(valueBuilder.ToString());
                pos = lineEnd + 2;
            }
        }

#else
        internal static void ParseHeaders(string httpHeader, int startPos, Dictionary<string, HashSet<string>> headers)
        {
            var pos = startPos;
            var length = httpHeader.Length;

            while (pos < length)
            {
                var lineEnd = httpHeader.IndexOf(NewLine, pos, StringComparison.Ordinal);
                if (lineEnd == -1) break;

                if (lineEnd == pos) break; // Empty line

                var colonPos = httpHeader.IndexOf(':', pos);
                if (colonPos == -1 || colonPos > lineEnd)
                {
                    pos = lineEnd + 2;
                    continue;
                }

                var headerName = httpHeader.Substring(pos, colonPos - pos).Trim();
                var headerValueBuilder = new StringBuilder(httpHeader.Substring(colonPos + 1, lineEnd - colonPos - 1).Trim());

                // Handle multi-line headers
                var nextPos = lineEnd + 2;
                while (nextPos < length && (httpHeader[nextPos] == ' ' || httpHeader[nextPos] == '\t'))
                {
                    var nextLineEnd = httpHeader.IndexOf(NewLine, nextPos, StringComparison.Ordinal);
                    if (nextLineEnd == -1) break;

                    headerValueBuilder.Append(" ").Append(httpHeader.Substring(nextPos, nextLineEnd - nextPos).Trim());
                    nextPos = nextLineEnd + 2;
                }

                if (!headers.TryGetValue(headerName, out var valuesList))
                {
                    valuesList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    headers[headerName] = valuesList;
                }

                valuesList.Add(headerValueBuilder.ToString());
                pos = lineEnd + 2;
            }
        }
#endif
    }
}
