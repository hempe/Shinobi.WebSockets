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

namespace Samurai.WebSockets
{

    public readonly struct HttpHeader
    {
        private const string NewLine = "\r\n";
        public readonly int? StatusCode;
        public readonly string? Method;
        public readonly string? Path;
        private readonly IReadOnlyDictionary<string, HashSet<string>> headers;

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

            return string.Join(", ", values.ToArray());
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
            const int InitialChunkSize = 128;

            var headerBytes = Shared.Rent(MaxHeaderSize);
            var buffer = Shared.Rent(InitialChunkSize);

            int totalHeaderBytes = 0;
            int sequenceIndex = 0;

            try
            {
                // Phase 1: Chunked reads (until close to limit)
                while (MaxHeaderSize - totalHeaderBytes >= InitialChunkSize)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, InitialChunkSize, cancellationToken).ConfigureAwait(false);
                    if (bytesRead == 0)
                        return new HttpHeader();

                    for (int i = 0; i < bytesRead; i++)
                    {
                        byte currentByte = buffer[i];
                        headerBytes[totalHeaderBytes++] = currentByte;

                        switch ((char)currentByte)
                        {
                            case '\r':
                                sequenceIndex = sequenceIndex == 0 || sequenceIndex == 2 ? sequenceIndex + 1 : 1;
                                break;

                            case '\n':
                                if (sequenceIndex == 1)
                                    sequenceIndex = 2;
                                else if (sequenceIndex == 3)
                                    return Parse(Encoding.UTF8.GetString(headerBytes, 0, totalHeaderBytes));
                                else
                                    sequenceIndex = 0;
                                break;

                            default:
                                sequenceIndex = 0;
                                break;
                        }
                    }
                }

                // Phase 2: 1-byte reads (avoids overread near 16KB limit)
                var singleByteBuffer = Shared.Rent(1);
                try
                {
                    while (true)
                    {
                        int bytesRead = await stream.ReadAsync(singleByteBuffer, 0, 1, cancellationToken).ConfigureAwait(false);
                        if (bytesRead == 0)
                            return new HttpHeader();

                        byte currentByte = singleByteBuffer[0];
                        if (totalHeaderBytes >= MaxHeaderSize)
                            throw new HttpHeaderTooLargeException("Http header too large (16KB)");

                        headerBytes[totalHeaderBytes++] = currentByte;

                        switch ((char)currentByte)
                        {
                            case '\r':
                                sequenceIndex = sequenceIndex == 0 || sequenceIndex == 2 ? sequenceIndex + 1 : 1;
                                break;

                            case '\n':
                                if (sequenceIndex == 1)
                                    sequenceIndex = 2;
                                else if (sequenceIndex == 3)
                                    return Parse(Encoding.UTF8.GetString(headerBytes, 0, totalHeaderBytes));
                                else
                                    sequenceIndex = 0;
                                break;

                            default:
                                sequenceIndex = 0;
                                break;
                        }
                    }
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

        /// <summary>
        /// Parse all HTTP headers from a raw HTTP request/response string
        /// </summary>
        /// <param name="httpHeader">Raw HTTP header string</param>
        /// <returns>ParseResult with status code and all headers</returns>
        public static HttpHeader Parse(string httpHeader)
        {

            if (string.IsNullOrEmpty(httpHeader))
                return new HttpHeader();

            var headerStart = httpHeader.IndexOf(NewLine);

            if (headerStart == -1)
                return new HttpHeader();

            // Parse the first line to extract status code for responses
            var firstLine = httpHeader.Substring(0, headerStart);
            int? statusCode;
            string? method = null;
            string? path = null;
            var headers = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            if (firstLine.StartsWith("HTTP/"))
            {
                // Response
                statusCode = ExtractStatusCode(firstLine);
            }
            else
            {
                statusCode = null;
                // Request line: METHOD path HTTP/version
                var parts = firstLine.Split(' ');
                if (parts.Length >= 2)
                {
                    method = parts[0];
                    path = parts[1];
                }
            }

            // Skip the first line
            var pos = headerStart + 2;
            var length = httpHeader.Length;

            while (pos < length)
            {
                var lineEnd = httpHeader.IndexOf(NewLine, pos);
                if (lineEnd == -1) break;

                // Empty line indicates end of headers
                if (lineEnd == pos) break;

                var colonPos = httpHeader.IndexOf(':', pos);
                if (colonPos == -1 || colonPos > lineEnd)
                {
                    pos = lineEnd + 2;
                    continue;
                }

                // Extract header name and value
                var headerName = httpHeader.Substring(pos, colonPos - pos).Trim();
                var headerValue = new StringBuilder(httpHeader.Substring(colonPos + 1, lineEnd - colonPos - 1).Trim());

                // Handle multi-line headers (folded headers)
                var nextPos = lineEnd + 2;
                while (nextPos < length && (httpHeader[nextPos] == ' ' || httpHeader[nextPos] == '\t'))
                {
                    var nextLineEnd = httpHeader.IndexOf(NewLine, nextPos);
                    if (nextLineEnd == -1) break;

                    headerValue.Append(" " + httpHeader.Substring(nextPos, nextLineEnd - nextPos).Trim());
                    nextPos = nextLineEnd + 2;
                }

                // Add to headers list (supporting multiple values)
                HashSet<string>? valuesList;
                if (!headers.TryGetValue(headerName, out valuesList))
                {
                    valuesList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    headers[headerName] = valuesList;
                }

                valuesList.Add(headerValue.ToString());

                pos = lineEnd + 2;
            }

            return new HttpHeader(statusCode, method, path, headers);
        }


        /// <summary>
        /// Extract status code from HTTP response line
        /// </summary>
        /// <param name="firstLine">First line of HTTP response</param>
        /// <returns>Status code or null if not a response</returns>
        private static int? ExtractStatusCode(string firstLine)
        {
            // Check if it's an HTTP response (starts with HTTP/)
            if (!firstLine.StartsWith("HTTP/"))
                return null;

            // Find the first space after HTTP/1.1
            var firstSpace = firstLine.IndexOf(' ');
            if (firstSpace == -1) return null;

            // Find the second space (end of status code)
            var secondSpace = firstLine.IndexOf(' ', firstSpace + 1);
            var endPos = secondSpace == -1 ? firstLine.Length : secondSpace;

            // Extract status code
            var statusCodeStr = firstLine.Substring(firstSpace + 1, endPos - firstSpace - 1);

            int statusCode;
            return int.TryParse(statusCodeStr, out statusCode) ? statusCode : (int?)null;
        }

        /// <summary>
        /// Validate WebSocket handshake response
        /// </summary>
        /// <param name="result">Parse result from handshake response</param>
        /// <exception cref="InvalidOperationException">Thrown when handshake validation fails</exception>
        public static void ValidateWebSocketHandshake(HttpHeader result)
        {
            if (result.StatusCode != 101)
            {
                throw new InvalidOperationException(string.Format("WebSocket handshake failed: Expected status 101, got {0}", result.StatusCode));
            }

            if (!result.HasHeader("Sec-WebSocket-Accept"))
            {
                throw new InvalidOperationException("WebSocket handshake failed: Missing Sec-WebSocket-Accept header");
            }
        }
    }

}