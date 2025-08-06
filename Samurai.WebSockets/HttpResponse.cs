using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#if NET9_0_OR_GREATER
#else 
using Samurai.WebSockets.Extensions;
#endif

namespace Samurai.WebSockets
{
    /// <summary>
    /// Represents an HTTP response with status code
    /// </summary>
    public sealed class HttpResponse : HttpHeader
    {
        public readonly int StatusCode;

        public HttpResponse(int statusCode, IReadOnlyDictionary<string, HashSet<string>> headers)
            : base(headers)
        {
            this.StatusCode = statusCode;
        }


        /// <summary>
        /// Reads an HTTP response from stream
        /// </summary>
        /// <param name="stream">The stream to read UTF8 text from</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The HTTP response</returns>
        public static async ValueTask<HttpResponse?> ReadHttpResponseAsync(Stream stream, CancellationToken cancellationToken)
        {
            var headerData = await ReadHttpHeaderDataAsync(stream, cancellationToken).ConfigureAwait(false);

            if (headerData.Count == 0)
                return null;

#if NET9_0_OR_GREATER
            return ParseResponse(headerData);
#else
            return ParseResponse(Encoding.UTF8.GetString(headerData.Array!, headerData.Offset, headerData.Count));
#endif
        }

#if NET9_0_OR_GREATER
        /// <summary>
        /// Parse HTTP response from raw HTTP header bytes (optimized for .NET 9+)
        /// </summary>
        public static HttpResponse? ParseResponse(ReadOnlySpan<byte> httpHeaderBytes)
        {
            if (httpHeaderBytes.IsEmpty)
                return null;

            var httpHeader = Encoding.UTF8.GetString(httpHeaderBytes);
            return ParseResponseInternal(httpHeader.AsSpan());
        }

        /// <summary>
        /// Parse HTTP response from a raw HTTP response string (optimized for .NET 9+)
        /// </summary>
        public static HttpResponse? ParseResponse(string httpHeader)
        {
            if (string.IsNullOrEmpty(httpHeader))
                return null;

            return ParseResponseInternal(httpHeader.AsSpan());
        }

        private static HttpResponse? ParseResponseInternal(ReadOnlySpan<char> httpHeader)
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
        public static HttpResponse? ParseResponse(string httpHeader)
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
        /// <param name="reasonPhrase">HTTP reason phrase (e.g., "Switching Protocols")</param>
        /// <param name="body">Optional response body</param>
        /// <returns>Complete HTTP response string</returns>
        public string ToHttpResponse(string reasonPhrase = "OK", string? body = null)
        {
#if NET9_0_OR_GREATER
            var builder = new StringBuilder();
            builder.Append($"HTTP/1.1 {this.StatusCode} {reasonPhrase}\r\n");

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

            builder.Append("\r\n").Append(body);
            return builder.ToString();
#else
            var builder = new StringBuilder();
            builder.AppendFormat("HTTP/1.1 {0} {1}\r\n", StatusCode, reasonPhrase);

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
            if (!string.IsNullOrEmpty(body))
            {
                builder.Append(body);
            }
            return builder.ToString();
#endif
        }

        /// <summary>
        /// Create a new HttpResponse builder
        /// </summary>
        public static HttpResponseBuilder Create(int statusCode)
            => new HttpResponseBuilder(statusCode);

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
