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
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace Samurai.WebSockets.Extensions
{
    public static class HttpHeaderExtensions
    {
        private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        /// <summary>
        /// Parse comma-separated header values (like Sec-WebSocket-Protocol)
        /// </summary>
        /// <param name="headerValue">Header value to parse</param>
        /// <returns>Array of trimmed values</returns>
        public static string[] ParseCommaSeparated(this string? headerValue)
        {
            if (string.IsNullOrEmpty(headerValue))
                return new string[0];

            var values = new List<string>();
            var start = 0;
            var inQuotes = false;

            for (var i = 0; i < headerValue!.Length; i++)
            {
                var ch = headerValue[i];

                if (ch == '"' && (i == 0 || headerValue[i - 1] != '\\'))
                {
                    inQuotes = !inQuotes;
                }
                else if (ch == ',' && !inQuotes)
                {
                    var value = headerValue.Substring(start, i - start).Trim();
                    if (!string.IsNullOrEmpty(value))
                        values.Add(value);
                    start = i + 1;
                }
            }

            // Add the last value
            var lastValue = headerValue.Substring(start).Trim();
            if (!string.IsNullOrEmpty(lastValue))
                values.Add(lastValue);

            return values.ToArray();
        }

        /// <summary>
        /// Parse Sec-WebSocket-Extensions header
        /// </summary>
        /// <param name="extensionsHeader">Extensions header value</param>
        /// <returns>Array of extension objects with name and parameters</returns>
        public static WebSocketExtension[] ParseExtensions(this string? extensionsHeader)
        {
            if (string.IsNullOrEmpty(extensionsHeader))
                return new WebSocketExtension[0];

            var extensions = new List<WebSocketExtension>();
            foreach (var ext in ParseCommaSeparated(extensionsHeader))
            {
                var parts = ext.Split(';');
                var extension = new WebSocketExtension
                {
                    Name = parts[0].Trim()
                };

                for (var i = 1; i < parts.Length; i++)
                {
                    var param = parts[i].Trim();
                    var eqPos = param.IndexOf('=');

                    if (eqPos != -1)
                    {
                        var key = param.Substring(0, eqPos).Trim();
                        var value = param.Substring(eqPos + 1).Trim();

                        // Remove quotes if present
                        if (value.StartsWith("\"") && value.EndsWith("\""))
                            value = value.Substring(1, value.Length - 2);

                        extension.Parameters[key] = value;
                    }
                    else
                    {
                        extension.Parameters[param] = true;
                    }
                }

                extensions.Add(extension);
            }

            return extensions.ToArray();
        }

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
    }
}
