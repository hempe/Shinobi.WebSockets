using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Samurai.WebSockets.Exceptions;
using Samurai.WebSockets.Internal;

#if NET9_0_OR_GREATER
using System.Buffers.Text;
using System.Runtime.InteropServices;
#endif

namespace Samurai.WebSockets
{
    public readonly struct HttpHeader
    {
#if NET9_0_OR_GREATER
        private static readonly SearchValues<byte> CrLfBytes = SearchValues.Create(new byte[] { (byte)'\r', (byte)'\n' });
#else
        private const string NewLine = "\r\n";
#endif

        public readonly int? StatusCode;
        public readonly string? Method;
        public readonly string? Path;
        internal readonly IReadOnlyDictionary<string, HashSet<string>> headers;

        public HttpHeader(int? statusCode, string? method, string? path, IReadOnlyDictionary<string, HashSet<string>> headers)
        {
            this.StatusCode = statusCode;
            this.Method = method;
            this.Path = path;
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

#if NET9_0_OR_GREATER
            return string.Join(", ", values);
#else
            return string.Join(", ", values.ToArray());
#endif
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
        /// Reads an http header as per the HTTP spec
        /// </summary>
        /// <param name="stream">The stream to read UTF8 text from</param>
        /// <returns>The HTTP header</returns>
        public static async ValueTask<HttpHeader> ReadHttpHeaderAsync(Stream stream, CancellationToken cancellationToken)
        {
            const int MaxHeaderSize = 16 * 1024;
            const int InitialChunkSize = 1024; // Increased from 128 for better performance

            var headerBytes = Shared.Rent(MaxHeaderSize);
            var buffer = Shared.Rent(InitialChunkSize);

            int totalHeaderBytes = 0;
            int sequenceIndex = 0;

            try
            {
#if NET9_0_OR_GREATER
                // Use Memory<byte> for better performance on newer frameworks
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
                        return new HttpHeader();

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
                            return Parse(headerBytes.AsSpan(0, totalHeaderBytes));
#else
                            return Parse(Encoding.UTF8.GetString(headerBytes, 0, totalHeaderBytes));
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
                            return new HttpHeader();

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
                            return Parse(headerBytes.AsSpan(0, totalHeaderBytes));
#else
                            return Parse(Encoding.UTF8.GetString(headerBytes, 0, totalHeaderBytes));
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
                Shared.Return(headerBytes);
            }
        }

#if NET9_0_OR_GREATER
        /// <summary>
        /// Parse all HTTP headers from raw HTTP header bytes (optimized for .NET 9+)
        /// </summary>
        public static HttpHeader Parse(ReadOnlySpan<byte> httpHeaderBytes)
        {
            if (httpHeaderBytes.IsEmpty)
                return new HttpHeader();

            // Convert to string for parsing - in a real optimization, you'd parse directly from bytes
            var httpHeader = Encoding.UTF8.GetString(httpHeaderBytes);
            return ParseInternal(httpHeader.AsSpan());
        }

        /// <summary>
        /// Parse all HTTP headers from a raw HTTP request/response string (optimized for .NET 9+)
        /// </summary>
        public static HttpHeader Parse(string httpHeader)
        {
            if (string.IsNullOrEmpty(httpHeader))
                return new HttpHeader();

            return ParseInternal(httpHeader.AsSpan());
        }

        private static HttpHeader ParseInternal(ReadOnlySpan<char> httpHeader)
        {
            if (httpHeader.IsEmpty)
                return new HttpHeader();

            var newlineIndex = httpHeader.IndexOf("\r\n".AsSpan());
            if (newlineIndex == -1)
                return new HttpHeader();

            // Parse the first line
            var firstLine = httpHeader.Slice(0, newlineIndex);
            int? statusCode;
            string? method = null;
            string? path = null;
            var headers = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            if (firstLine.StartsWith("HTTP/".AsSpan()))
            {
                statusCode = ExtractStatusCodeSpan(firstLine);
            }
            else
            {
                statusCode = null;
                var spaceIndex = firstLine.IndexOf(' ');
                if (spaceIndex != -1)
                {
                    method = firstLine.Slice(0, spaceIndex).ToString();
                    var secondSpaceIndex = firstLine.Slice(spaceIndex + 1).IndexOf(' ');
                    if (secondSpaceIndex != -1)
                    {
                        path = firstLine.Slice(spaceIndex + 1, secondSpaceIndex).ToString();
                    }
                    else
                    {
                        path = firstLine.Slice(spaceIndex + 1).ToString();
                    }
                }
            }

            // Parse headers
            var pos = newlineIndex + 2;
            while (pos < httpHeader.Length)
            {
                var lineEnd = httpHeader.Slice(pos).IndexOf("\r\n".AsSpan());
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
                    var nextLineEnd = httpHeader.Slice(nextPos).IndexOf("\r\n".AsSpan());
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

            return new HttpHeader(statusCode, method, path, headers);
        }

        private static int? ExtractStatusCodeSpan(ReadOnlySpan<char> firstLine)
        {
            var firstSpace = firstLine.IndexOf(' ');
            if (firstSpace == -1) return null;

            var secondSpace = firstLine.Slice(firstSpace + 1).IndexOf(' ');
            var endPos = secondSpace == -1 ? firstLine.Length : firstSpace + 1 + secondSpace;

            var statusCodeSpan = firstLine.Slice(firstSpace + 1, endPos - firstSpace - 1);
            return int.TryParse(statusCodeSpan, out var statusCode) ? statusCode : (int?)null;
        }

#else
        /// <summary>
        /// Parse all HTTP headers from a raw HTTP request/response string
        /// </summary>
        public static HttpHeader Parse(string httpHeader)
        {
            const string NewLine = "\r\n";

            if (string.IsNullOrEmpty(httpHeader))
                return new HttpHeader();

            var headerStart = httpHeader.IndexOf(NewLine, StringComparison.Ordinal);
            if (headerStart == -1)
                return new HttpHeader();

            // Parse the first line
            var firstLine = httpHeader.Substring(0, headerStart);
            int? statusCode;
            string? method = null;
            string? path = null;
            var headers = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            if (firstLine.StartsWith("HTTP/", StringComparison.Ordinal))
            {
                statusCode = ExtractStatusCode(firstLine);
            }
            else
            {
                statusCode = null;
                var parts = firstLine.Split(new[] { ' ' }, 3, StringSplitOptions.None);
                if (parts.Length >= 2)
                {
                    method = parts[0];
                    path = parts[1];
                }
            }

            // Parse headers
            var pos = headerStart + 2;
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

            return new HttpHeader(statusCode, method, path, headers);
        }

        private static int? ExtractStatusCode(string firstLine)
        {
            if (!firstLine.StartsWith("HTTP/", StringComparison.Ordinal))
                return null;

            var firstSpace = firstLine.IndexOf(' ');
            if (firstSpace == -1) return null;

            var secondSpace = firstLine.IndexOf(' ', firstSpace + 1);
            var endPos = secondSpace == -1 ? firstLine.Length : secondSpace;

            var statusCodeStr = firstLine.Substring(firstSpace + 1, endPos - firstSpace - 1);
            return int.TryParse(statusCodeStr, out var statusCode) ? statusCode : (int?)null;
        }
#endif

        /// <summary>
        /// Build HTTP response string from this header
        /// </summary>
        /// <param name="reasonPhrase">HTTP reason phrase (e.g., "Switching Protocols")</param>
        /// <returns>Complete HTTP response string</returns>
        public string ToHttpResponse(string reasonPhrase = "OK")
        {
            if (!this.StatusCode.HasValue)
                throw new InvalidOperationException("Cannot build HTTP response without status code");

#if NET9_0_OR_GREATER
            var builder = new StringBuilder();
            builder.Append($"HTTP/1.1 {this.StatusCode.Value} {reasonPhrase}\r\n");

            if (this.headers != null)
            {
                foreach (var header in this.headers)
                {
                    foreach (var value in header.Value)
                    {
                        builder.Append($"{header.Key}: {value}\r\n");
                    }
                }
            }

            builder.Append("\r\n");
            return builder.ToString();
#else
            var builder = new StringBuilder();
            builder.AppendFormat("HTTP/1.1 {0} {1}\r\n", StatusCode.Value, reasonPhrase);
            
            if (headers != null)
            {
                foreach (var header in headers)
                {
                    foreach (var value in header.Value)
                    {
                        builder.AppendFormat("{0}: {1}\r\n", header.Key, value);
                    }
                }
            }
            
            builder.Append("\r\n");
            return builder.ToString();
#endif
        }

        /// <summary>
        /// Build HTTP request string from this header
        /// </summary>
        /// <param name="httpVersion">HTTP version (default: "HTTP/1.1")</param>
        /// <returns>Complete HTTP request string</returns>
        public string ToHttpRequest(string httpVersion = "HTTP/1.1")
        {
            if (string.IsNullOrEmpty(this.Method) || string.IsNullOrEmpty(this.Path))
                throw new InvalidOperationException("Cannot build HTTP request without method and path");

#if NET9_0_OR_GREATER
            var builder = new StringBuilder();
            builder.Append($"{this.Method} {this.Path} {httpVersion}\r\n");

            if (this.headers != null)
            {
                foreach (var header in this.headers)
                {
                    foreach (var value in header.Value)
                    {
                        builder.Append($"{header.Key}: {value}\r\n");
                    }
                }
            }

            builder.Append("\r\n");
            return builder.ToString();
#else
            var builder = new StringBuilder();
            builder.AppendFormat("{0} {1} {2}\r\n", Method, Path, httpVersion);
            
            if (headers != null)
            {
                foreach (var header in headers)
                {
                    foreach (var value in header.Value)
                    {
                        builder.AppendFormat("{0}: {1}\r\n", header.Key, value);
                    }
                }
            }
            
            builder.Append("\r\n");
            return builder.ToString();
#endif
        }

        /// <summary>
        /// Create a new HttpHeader for building responses
        /// </summary>
        public static HttpHeaderBuilder CreateResponse(int statusCode)
            => new HttpHeaderBuilder(statusCode, null, null);

        /// <summary>
        /// Create a new HttpHeader for building requests
        /// </summary>
        public static HttpHeaderBuilder CreateRequest(string? method, string? path)
            => new HttpHeaderBuilder(null, method, path);

        /// <summary>
        /// Validate WebSocket handshake response
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when handshake validation fails</exception>
        public void ValidateWebSocketHandshake()
        {
            if (this.StatusCode != 101)
                throw new InvalidOperationException($"WebSocket handshake failed: Expected status 101, got {this.StatusCode}");

            if (!this.HasHeader("Sec-WebSocket-Accept"))
                throw new InvalidOperationException("WebSocket handshake failed: Missing Sec-WebSocket-Accept header");
        }
    }
}