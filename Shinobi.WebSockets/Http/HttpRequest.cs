using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Shinobi.WebSockets.Exceptions;
using Shinobi.WebSockets.Extensions;

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
            if (contentLengthHeader != null &&
                int.TryParse(contentLengthHeader, out var contentLength) &&
                contentLength > 0)
            {
                // Allocate the pooled stream with the expected length
                var bodyStream = new ArrayPoolStream(contentLength);

                // Read exactly contentLength bytes into bodyStream
                await stream.ReadAsync(bodyStream, contentLength, cancellationToken).ConfigureAwait(false);

                // Reset position so consumers can read from the beginning
                bodyStream.Position = 0;

                // Attach to the request; it will be disposed with the request
                request.Body = bodyStream;
            }


            return request;
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
