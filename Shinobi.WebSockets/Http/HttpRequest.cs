using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Shinobi.WebSockets.Exceptions;


#if NET8_0_OR_GREATER
#else
using Shinobi.WebSockets.Extensions;
#endif

using Shinobi.WebSockets.Internal;

namespace Shinobi.WebSockets.Http
{
    /// <summary>
    /// Represents an HTTP request with method and path
    /// </summary>
    public sealed class HttpRequest : HttpHeader, IDisposable
    {
        public readonly string Method;
        public readonly string Path;

        /// <summary>
        /// The request body stream if present, null otherwise
        /// </summary>
        public Stream? Body { get; internal set; }

        public HttpRequest(string method, string path, IDictionary<string, HashSet<string>> headers, Stream? body = null)
            : base(headers)
        {
            this.Method = method;
            this.Path = path;
            this.Body = body;
        }

        /// <summary>
        /// Reads an HTTP request from stream, including body if Content-Length is specified
        /// </summary>
        /// <param name="stream">The stream to read UTF8 text from</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The HTTP request with body stream if present</returns>
        public static async ValueTask<HttpRequest?> ReadAsync(Stream stream, CancellationToken cancellationToken)
        {
            // Read headers without over-reading into the body
            var headerData = await ReadHttpHeaderDataAsync(stream, cancellationToken).ConfigureAwait(false);

            if (headerData.Count == 0)
                return null;

            // Parse the headers first
#if NET8_0_OR_GREATER
            var request = Parse(headerData);
#else
            var request = Parse(Encoding.UTF8.GetString(headerData.Array!, headerData.Offset, headerData.Count));
#endif

            if (request == null)
                return null;

            // Check for request body based on Content-Length header
            var contentLengthHeader = request.GetHeaderValue("Content-Length");
            if (contentLengthHeader != null && int.TryParse(contentLengthHeader, out var contentLength) && contentLength > 0)
            {
                // Use ArrayPoolStream for efficient body reading
                var bodyStream = new ArrayPoolStream();
                var totalBytesRead = 0;

                // Read body data from stream (no over-read to handle since ReadHttpRequestDataAsync doesn't over-read)
                var buffer = new byte[4096];
                while (totalBytesRead < contentLength)
                {
                    var bytesToRead = Math.Min(buffer.Length, contentLength - totalBytesRead);
                    var bytesRead = await stream.ReadAsync(buffer, 0, bytesToRead, cancellationToken).ConfigureAwait(false);

                    if (bytesRead == 0)
                        break; // EOF reached before reading full content

                    bodyStream.Write(buffer, 0, bytesRead);
                    totalBytesRead += bytesRead;
                }

                // Set the position to the beginning for reading
                bodyStream.Position = 0;

                // Give the ArrayPoolStream directly to the request - it will be disposed when the request is disposed
                request.Body = bodyStream;
            }

            return request;
        }

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
#if NET8_0_OR_GREATER
                var headerMemory = headerBytes.AsMemory(0, MaxHeaderSize);
                var bufferMemory = buffer.AsMemory(0, InitialChunkSize);
#endif

                // Phase 1: Chunked reads (until close to limit)
                while (MaxHeaderSize - totalHeaderBytes >= InitialChunkSize)
                {
#if NET8_0_OR_GREATER
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
#if NET8_0_OR_GREATER
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
#if NET8_0_OR_GREATER
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
#if NET8_0_OR_GREATER
                            var resultBytes = new byte[totalHeaderBytes];
                            Array.Copy(headerBytes, resultBytes, totalHeaderBytes);
                            return new ArraySegment<byte>(resultBytes);
#else
                            return new ArraySegment<byte>(headerBytes, 0, totalHeaderBytes);
#endif
                        }
                    }

                    throw new HttpHeaderTooLargeException(totalHeaderBytes, MaxHeaderSize);
                }
                finally
                {
                    Shared.Return(singleByteBuffer);
                }
            }
            finally
            {
                Shared.Return(buffer);
#if !NET8_0_OR_GREATER
                // Only return the rented array for pre-.NET 9, since we're copying for .NET 9+
                if (totalHeaderBytes == 0) // Only return if we didn't use it in the result
#endif
                Shared.Return(headerBytes);
            }
        }


#if NET8_0_OR_GREATER
        /// <summary>
        /// Parse HTTP request from raw HTTP header bytes (optimized for .NET 9+)
        /// </summary>
        public static HttpRequest? Parse(ReadOnlySpan<byte> httpHeaderBytes)
        {
            if (httpHeaderBytes.IsEmpty)
                return null;

            var httpHeader = Encoding.UTF8.GetString(httpHeaderBytes);
            return ParseInternal(httpHeader.AsSpan());
        }

        /// <summary>
        /// Parse HTTP request from a raw HTTP request string (optimized for .NET 9+)
        /// </summary>
        public static HttpRequest? Parse(string httpHeader)
        {
            if (string.IsNullOrEmpty(httpHeader))
                return null;

            return ParseInternal(httpHeader.AsSpan());
        }

        private static HttpRequest? ParseInternal(ReadOnlySpan<char> httpHeader)
        {
            if (httpHeader.IsEmpty)
                return null;

            var newlineIndex = httpHeader.IndexOf("\r\n".AsSpan());
            if (newlineIndex == -1)
                return null;

            // Parse the first line
            var firstLine = httpHeader.Slice(0, newlineIndex);
            var headers = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            // Parse headers
            ParseHeaders(httpHeader, newlineIndex + 2, headers);

            // Parse request line
            string? method = null;
            string? path = null;
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
            if (method is null)
                return null;

            return new HttpRequest(method, path ?? string.Empty, headers);
        }

#else
        /// <summary>
        /// Parse HTTP request from a raw HTTP request string
        /// </summary>
        public static HttpRequest? Parse(string httpHeader)
        {
            const string NewLine = "\r\n";

            if (string.IsNullOrEmpty(httpHeader))
                return null;

            var headerStart = httpHeader.IndexOf(NewLine, StringComparison.Ordinal);
            if (headerStart == -1)
                return null;

            // Parse the first line
            var firstLine = httpHeader.Substring(0, headerStart);
            var headers = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            // Parse headers
            ParseHeaders(httpHeader, headerStart + 2, headers);

            // Parse request line
            string? method = null;
            string? path = null;
            var parts = firstLine.Split(new[] { ' ' }, 3, StringSplitOptions.None);
            if (parts.Length >= 2)
            {
                method = parts[0];
                path = parts[1];
            }

            if (method is null)
                return null;

            return new HttpRequest(method, path ?? string.Empty, headers);
        }
#endif

        /// <summary>
        /// Build HTTP request string from this header
        /// </summary>
        /// <param name="httpVersion">HTTP version (default: "HTTP/1.1")</param>
        /// <returns>Complete HTTP request string</returns>
        public string ToHttpRequest(string httpVersion = "HTTP/1.1")
        {
            if (string.IsNullOrEmpty(this.Method) || string.IsNullOrEmpty(this.Path))
                throw new InvalidOperationException("Cannot build HTTP request without method and path");

#if NET8_0_OR_GREATER
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

            // Add body if present
            if (this.Body != null)
            {
                if (this.Body.CanSeek)
                    this.Body.Position = 0;

                using var reader = new StreamReader(this.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
                builder.Append(reader.ReadToEnd());

                if (this.Body.CanSeek)
                    this.Body.Position = 0; // Reset position for potential future use
            }

            return builder.ToString();
#else
            var builder = new StringBuilder();
            builder.AppendFormat("{0} {1} {2}\r\n", this.Method, this.Path, httpVersion);

            if (this.headers != null)
            {
                foreach (var header in this.headers)
                {
                    foreach (var value in header.Value)
                    {
                        builder.AppendFormat("{0}: {1}\r\n", header.Key, value);
                    }
                }
            }

            builder.Append("\r\n");
            
            // Add body if present
            if (this.Body != null)
            {
                if (this.Body.CanSeek)
                    this.Body.Position = 0;
                    
                using var reader = new StreamReader(this.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
                builder.Append(reader.ReadToEnd());
                
                if (this.Body.CanSeek)
                    this.Body.Position = 0; // Reset position for potential future use
            }
            
            return builder.ToString();
#endif
        }

        /// <summary>
        /// Create a new HttpRequest builder
        /// </summary>
        public static HttpRequest Create(string method, string path)
            => new HttpRequest(method, path, new Dictionary<string, HashSet<string>>());

        /// <summary>
        /// Disposes the request and its body stream if present
        /// </summary>
        public void Dispose()
        {
            this.Body?.Dispose();
        }
    }
}
