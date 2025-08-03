// ---------------------------------------------------------------------
// Copyright 2018 David Haig
// Copyright 2023 Hempe
//
// Permission is hereby granted, free of charge, to any person obtaining a copy 
// of this software and associated documentation files (the "Software"), to deal 
// in the Software without restriction, including without limitation the rights 
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell 
// copies of the Software, and to permit persons to whom the Software is 
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR 
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, 
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN 
// THE SOFTWARE.
// ---------------------------------------------------------------------

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Samurai.WebSockets.Exceptions;

namespace Samurai.WebSockets
{
    public static class HttpHelperExtensions
    {
        private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        private static readonly Regex HttpGetHeader = new Regex(@"^GET(.*)HTTP\/1\.1", RegexOptions.IgnoreCase);
        private static readonly Regex Http = new Regex(@"HTTP\/1\.1 (.*)", RegexOptions.IgnoreCase);

        private static readonly Regex WebSocketUpgrade = new Regex("Upgrade: websocket", RegexOptions.IgnoreCase);
        private static readonly Regex SecWebSocketProtocol = new Regex(@"Sec-WebSocket-Protocol:(?<protocols>.+)", RegexOptions.IgnoreCase);
        private static readonly Regex SecWebSocketExtensions = new Regex(@"Sec-WebSocket-Extensions:(?<extensions>.+)", RegexOptions.IgnoreCase);

        private const char @R = '\r';
        private const char @N = '\n';

        /// <summary>
        /// Computes a WebSocket accept string from a given key
        /// </summary>
        /// <param name="secWebSocketKey">The web socket key to base the accept string on</param>
        /// <returns>A web socket accept string</returns>
        public static string ComputeSocketAcceptString(this string secWebSocketKey)
        {
            // this is a guid as per the web socket spec
            var concatenated = $"{secWebSocketKey}{WebSocketGuid}";
            var concatenatedAsBytes = Encoding.UTF8.GetBytes(concatenated);

            // note an instance of SHA1 is not threadsafe so we have to create a new one every time here
            var sha1Hash = SHA1.Create().ComputeHash(concatenatedAsBytes);
            return Convert.ToBase64String(sha1Hash);
        }

        /// <summary>
        /// Reads an http header as per the HTTP spec
        /// </summary>
        /// <param name="stream">The stream to read UTF8 text from</param>
        /// <returns>The HTTP header</returns>
        public static async ValueTask<string> ReadHttpHeaderAsync(this Stream stream, CancellationToken cancellationToken)
        {
            const int MaxHeaderSize = 16 * 1024;
            const int InitialChunkSize = 128;

            var headerBytes = ArrayPool<byte>.Shared.Rent(MaxHeaderSize);
            var buffer = ArrayPool<byte>.Shared.Rent(InitialChunkSize);

            int totalHeaderBytes = 0;
            int sequenceIndex = 0;

            try
            {
                // Phase 1: Chunked reads (until close to limit)
                while (MaxHeaderSize - totalHeaderBytes >= InitialChunkSize)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, InitialChunkSize, cancellationToken).ConfigureAwait(false);
                    if (bytesRead == 0)
                        return string.Empty;

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
                                    return Encoding.UTF8.GetString(headerBytes, 0, totalHeaderBytes);
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
                var singleByteBuffer = ArrayPool<byte>.Shared.Rent(1);
                try
                {
                    while (true)
                    {
                        int bytesRead = await stream.ReadAsync(singleByteBuffer, 0, 1, cancellationToken).ConfigureAwait(false);
                        if (bytesRead == 0)
                            return string.Empty;

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
                                    return Encoding.UTF8.GetString(headerBytes, 0, totalHeaderBytes);
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
                    ArrayPool<byte>.Shared.Return(singleByteBuffer);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
                ArrayPool<byte>.Shared.Return(headerBytes);
            }
        }


        /// <summary>
        /// Decodes the header to detect is this is a web socket upgrade response
        /// </summary>
        /// <param name="header">The HTTP header</param>
        /// <returns>True if this is an http WebSocket upgrade response</returns>
        public static bool IsWebSocketUpgradeRequest(this string header)
        {
            // check if this is a web socket upgrade request
            if (HttpGetHeader.Match(header).Success)
                return WebSocketUpgrade.Match(header).Success;

            return false;
        }

        /// <summary>
        /// Gets the path from the HTTP header
        /// </summary>
        /// <param name="httpHeader">The HTTP header to read</param>
        /// <returns>The path</returns>
        public static string? GetPathFromHeader(this string httpHeader)
        {
            var getRegexMatch = HttpGetHeader.Match(httpHeader);
            // extract the path attribute from the first line of the header
            return getRegexMatch.Success
                ? getRegexMatch.Groups[1].Value.Trim()
                : null;
        }

        public static IList<string> GetSubProtocols(this string httpHeader)
        {
            var match = SecWebSocketProtocol.Match(httpHeader);
            if (match.Success)
            {
                const int MAX_LEN = 2048;
                if (match.Length > MAX_LEN)
                {
                    throw new HttpHeaderTooLargeException($"Sec-WebSocket-Protocol exceeded the maximum of length of {MAX_LEN}");
                }

                // extract a csv list of sub protocols (in order of highest preference first)
                string csv = match.Groups["protocols"].Value.Trim();
                return csv.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .ToList();
            }

            return new List<string>();
        }

        public static IList<string> GetWebSocketExtensions(this string httpHeader)
        {
            var match = SecWebSocketExtensions.Match(httpHeader);
            if (match.Success)
            {
                const int MAX_LEN = 2048;
                if (match.Length > MAX_LEN)
                {
                    throw new HttpHeaderTooLargeException($"Sec-WebSocket-Extensions exceeded the maximum of length of {MAX_LEN}");
                }

                // extract a csv list of sub protocols (in order of highest preference first)
                string csv = match.Groups["extensions"].Value.Trim();
                return csv.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .ToList();
            }

            return new List<string>();
        }

        /// <summary>
        /// Reads the HTTP response code from the http response string
        /// </summary>
        /// <param name="response">The response string</param>
        /// <returns>the response code</returns>
        public static string? ReadHttpResponseCode(this string response)
        {
            var getRegexMatch = Http.Match(response);

            // extract the path attribute from the first line of the header
            return getRegexMatch.Success
                ? getRegexMatch.Groups[1].Value.Trim()
                : null;
        }

        /// <summary>
        /// Writes an HTTP response string to the stream
        /// </summary>
        /// <param name="stream">The stream to write to</param>
        /// <param name="response">The response (without the new line characters)</param>
        /// <param name="cancellationToken">The cancellation token</param>
        public static async ValueTask WriteHttpHeaderAsync(this Stream stream, string response, CancellationToken cancellationToken)
        {
            response = response.Trim() + "\r\n\r\n";
            var bytes = Encoding.UTF8.GetBytes(response);
            await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
        }
    }
}
