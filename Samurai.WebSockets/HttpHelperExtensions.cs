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

using Samurai.WebSockets.Exceptions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Samurai.WebSockets
{
    public static class HttpHelperExtensions
    {
        private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        private static readonly Regex HttpGetHeader = new Regex(@"^GET(.*)HTTP\/1\.1", RegexOptions.IgnoreCase);
        private static readonly Regex Http = new Regex(@"HTTP\/1\.1 (.*)", RegexOptions.IgnoreCase);

        private static readonly Regex WebSocketUpgrade = new Regex("Upgrade: websocket", RegexOptions.IgnoreCase);
        private static readonly Regex SecWebSocketProtocol = new Regex(@"Sec-WebSocket-Protocol:(?<protocols>.+)", RegexOptions.IgnoreCase);

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
        /// <param name="token">The cancellation token</param>
        /// <returns>The HTTP header</returns>
        public static async Task<string> ReadHttpHeaderAsync(this Stream stream, CancellationToken cancellationToken)
        {
            var headerBytes = new List<byte>();
            var buffer = new byte[1];
            var sequenceIndex = 0;

            while (true)
            {
                var bytesRead = await stream.ReadAsync(buffer, 0, 1, cancellationToken);

                // End of stream reached
                if (bytesRead == 0)
                    return string.Empty;

                var currentByte = buffer[0];
                headerBytes.Add(currentByte);

                // Safety check for oversized headers
                if (headerBytes.Count > 16 * 1024)
                    throw new EntityTooLargeException("Http header message too large to fit in buffer (16KB)");

                // Check for header termination sequence \r\n\r\n
                switch ((char)currentByte)
                {
                    case @R:
                        sequenceIndex = sequenceIndex == 0 || sequenceIndex == 2
                            ? sequenceIndex + 1
                            : 1;
                        break;

                    case @N:
                        if (sequenceIndex == 1)
                            sequenceIndex = 2;
                        else if (sequenceIndex == 3)
                            return Encoding.UTF8.GetString(headerBytes.ToArray());
                        else
                            sequenceIndex = 0;
                        break;

                    default:
                        sequenceIndex = 0;
                        break;
                }
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
        public static string GetPathFromHeader(this string httpHeader)
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
                    throw new EntityTooLargeException($"Sec-WebSocket-Protocol exceeded the maximum of length of {MAX_LEN}");
                }

                // extract a csv list of sub protocols (in order of highest preference first)
                string csv = match.Groups["protocols"].Value.Trim();
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
        public static string ReadHttpResponseCode(this string response)
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
        public static async Task WriteHttpHeaderAsync(this Stream stream, string response, CancellationToken cancellationToken)
        {
            response = response.Trim() + "\r\n\r\n";
            var bytes = Encoding.UTF8.GetBytes(response);
            await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
        }
    }
}
