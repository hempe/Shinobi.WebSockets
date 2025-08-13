using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#if NET8_0_OR_GREATER
#else 
using Shinobi.WebSockets.Extensions;
#endif

using Shinobi.WebSockets.Internal;

namespace Shinobi.WebSockets.Http
{
    /// <summary>
    /// Represents an HTTP response with status code
    /// </summary>
    public sealed class HttpResponse : HttpHeader
    {

#if NET8_0_OR_GREATER
        private static ReadOnlySpan<char> GetReasonPhrase(int statusCode) => statusCode switch
#else
        private static string GetReasonPhrase(int statusCode) => statusCode switch
#endif
        {
            100 => "Continue",
            101 => "Switching Protocols",
            102 => "Processing",
            200 => "OK",
            201 => "Created",
            202 => "Accepted",
            203 => "Non-Authoritative Information",
            204 => "No Content",
            205 => "Reset Content",
            206 => "Partial Content",
            207 => "Multi-Status",
            300 => "Multiple Choices",
            301 => "Moved Permanently",
            302 => "Found",
            303 => "See Other",
            304 => "Not Modified",
            305 => "Use Proxy",
            307 => "Temporary Redirect",
            308 => "Permanent Redirect",
            400 => "Bad Request",
            401 => "Unauthorized",
            402 => "Payment Required",
            403 => "Forbidden",
            404 => "Not Found",
            405 => "Method Not Allowed",
            406 => "Not Acceptable",
            407 => "Proxy Authentication Required",
            408 => "Request Timeout",
            409 => "Conflict",
            410 => "Gone",
            411 => "Length Required",
            412 => "Precondition Failed",
            413 => "Payload Too Large",
            414 => "URI Too Long",
            415 => "Unsupported Media Type",
            416 => "Range Not Satisfiable",
            417 => "Expectation Failed",
            426 => "Upgrade Required",
            500 => "Internal Server Error",
            501 => "Not Implemented",
            502 => "Bad Gateway",
            503 => "Service Unavailable",
            504 => "Gateway Timeout",
            505 => "HTTP Version Not Supported",
            _ => "Unknown Status"
        };


        public readonly int StatusCode;
        internal string? reasonPhrase = null;
        internal string? body = null;

        internal HttpResponse(
            int statusCode,
            IDictionary<string, HashSet<string>> headers,
            string? body = null
            )
            : base(headers)
        {
            this.StatusCode = statusCode;
#if NET8_0_OR_GREATER
            this.reasonPhrase = GetReasonPhrase(statusCode).ToString();
#else
            this.reasonPhrase = GetReasonPhrase(statusCode);
#endif
            this.body = body;
        }


        /// <summary>
        /// Reads an HTTP response from stream
        /// </summary>
        /// <param name="stream">The stream to read UTF8 text from</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The HTTP response</returns>
        public static async ValueTask<HttpResponse?> ReadAsync(Stream stream, CancellationToken cancellationToken)
        {
            var headerData = await ReadHttpHeaderDataAsync(stream, cancellationToken).ConfigureAwait(false);

            if (headerData.Count == 0)
                return null;

#if NET8_0_OR_GREATER
            return Parse(headerData);
#else
            return Parse(Encoding.UTF8.GetString(headerData.Array!, headerData.Offset, headerData.Count));
#endif
        }

#if NET8_0_OR_GREATER
        /// <summary>
        /// Parse HTTP response from raw HTTP header bytes (optimized for .NET 9+)
        /// </summary>
        public static HttpResponse? Parse(ReadOnlySpan<byte> httpHeaderBytes)
        {
            if (httpHeaderBytes.IsEmpty)
                return null;

            var httpHeader = Encoding.UTF8.GetString(httpHeaderBytes);
            return ParseInternal(httpHeader.AsSpan());
        }

        /// <summary>
        /// Parse HTTP response from a raw HTTP response string (optimized for .NET 9+)
        /// </summary>
        public static HttpResponse? Parse(string httpHeader)
        {
            if (string.IsNullOrEmpty(httpHeader))
                return null;

            return ParseInternal(httpHeader.AsSpan());
        }

        private static HttpResponse? ParseInternal(ReadOnlySpan<char> httpHeader)
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

            // Parse status line
            var statusCode = ExtractStatusCodeSpan(firstLine);
            return statusCode.HasValue
                ? new HttpResponse(statusCode.Value, headers)
                : null;

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
        /// Parse HTTP response from a raw HTTP response string
        /// </summary>
        public static HttpResponse? Parse(string httpHeader)
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

            // Parse status line
            var statusCode = ExtractStatusCode(firstLine);
            return statusCode.HasValue
                ? new HttpResponse(statusCode.Value, headers)
                : null;
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
        /// <returns>Complete HTTP response string</returns>
        public string Build()
        {
#if NET8_0_OR_GREATER
            var builder = new StringBuilder();
            builder.Append($"HTTP/1.1 {this.StatusCode} {this.reasonPhrase}\r\n");

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

            builder.Append("\r\n").Append(this.body);
            return builder.ToString();
#else
            var builder = new StringBuilder();
            builder.AppendFormat("HTTP/1.1 {0} {1}\r\n", this.StatusCode, this.reasonPhrase);

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
            if (!string.IsNullOrEmpty(this.body))
            {
                builder.Append(this.body);
            }
            return builder.ToString();
#endif
        }
        public async ValueTask WriteToStreamAsync(Stream stream, CancellationToken cancellationToken)
        {
            // Create your pooled buffer stream
            using var bufferStream = new ArrayPoolStream();

            // Helper local to encode & write a string chunk
            void EncodeAndWrite(string text)
            {
                if (string.IsNullOrEmpty(text)) return;
#if NET8_0_OR_GREATER 
                var maxBytes = Encoding.UTF8.GetMaxByteCount(text.Length);
                var span = bufferStream.GetFreeSpan(maxBytes);
                var bytesEncoded = Encoding.UTF8.GetBytes(text.AsSpan(), span);
                bufferStream.Position += bytesEncoded;
#else

                byte[] bytes = Encoding.UTF8.GetBytes(text);
                bufferStream.Write(bytes, 0, bytes.Length);
#endif
            }

            // Write status line
            EncodeAndWrite($"HTTP/1.1 {this.StatusCode} {this.reasonPhrase}\r\n");

            // Write headers
            if (this.headers != null)
            {
                foreach (var header in this.headers)
                {
                    foreach (var value in header.Value)
                    {
                        EncodeAndWrite($"{header.Key}: {value}\r\n");
                    }
                }
            }

            // Blank line to separate headers and body
            EncodeAndWrite("\r\n");

            // Write body if any
            if (!string.IsNullOrEmpty(this.body))
            {
                EncodeAndWrite(this.body!);
            }

            // Get the buffered data segment
            var segment = bufferStream.GetDataArraySegment();

            // Write all buffered data to the output stream at once, with cancellation support
            await stream.WriteAsync(segment.Array!, segment.Offset, segment.Count, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Create a new HttpResponse builder
        /// </summary>
        public static HttpResponse Create(int statusCode)
            => new HttpResponse(statusCode, new Dictionary<string, HashSet<string>>());

        /// <summary>
        /// Validate WebSocket handshake response
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when handshake validation fails</exception>
        public void ValidateWebSocketHandshakeResponse()
        {
            if (this.StatusCode != 101)
                throw new InvalidOperationException($"WebSocket handshake failed: Expected status 101, got {this.StatusCode}");

            if (!this.HasHeader("Sec-WebSocket-Accept"))
                throw new InvalidOperationException("WebSocket handshake failed: Missing Sec-WebSocket-Accept header");

            if (!this.HasHeader("Upgrade") || !string.Equals(this.GetHeaderValue("Upgrade"), "websocket", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("WebSocket handshake failed: Missing or invalid Upgrade header");

            if (!this.HasHeader("Connection") || !this.GetHeaderValue("Connection")?.Contains("Upgrade", StringComparison.OrdinalIgnoreCase) == true)
                throw new InvalidOperationException("WebSocket handshake failed: Missing or invalid Connection header");
        }
    }
}
