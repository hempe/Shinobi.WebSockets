// ---------------------------------------------------------------------
// Copyright 2018 David Haig
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
using System.Net.WebSockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Samurai.WebSockets.Exceptions;
using Samurai.WebSockets.Internal;

namespace Samurai.WebSockets
{
    /// <summary>
    /// Web socket server factory used to open web socket server connections
    /// </summary>
    public class WebSocketServerFactory : IWebSocketServerFactory
    {
        private const int WebSocketVersion = 13;
        private static readonly Regex WebSocketVersionRegex = new Regex("Sec-WebSocket-Version: (.*)", RegexOptions.IgnoreCase);
        private static readonly Regex WebSocketKeyRegex = new Regex("Sec-WebSocket-Key: (.*)", RegexOptions.IgnoreCase);

        /// <summary>
        /// Reads a http header information from a stream and decodes the parts relating to the WebSocket protocot upgrade
        /// </summary>
        /// <param name="stream">The network stream</param>
        /// <param name="cancellationToken">The optional cancellation token</param>
        /// <returns>Http data read from the stream</returns>
        public async Task<WebSocketHttpContext> ReadHttpHeaderFromStreamAsync(Stream stream, CancellationToken cancellationToken = default(CancellationToken))
            => new WebSocketHttpContext(await stream.ReadHttpHeaderAsync(cancellationToken), stream);

        /// <summary>
        /// Accept web socket with default options
        /// Call ReadHttpHeaderFromStreamAsync first to get WebSocketHttpContext
        /// </summary>
        /// <param name="context">The http context used to initiate this web socket request</param>
        /// <param name="cancellationToken">The optional cancellation token</param>
        /// <returns>A connected web socket</returns>
        public async Task<WebSocket> AcceptWebSocketAsync(WebSocketHttpContext context, CancellationToken cancellationToken = default(CancellationToken))
            => await this.AcceptWebSocketAsync(context, new WebSocketServerOptions(), cancellationToken);

        /// <summary>
        /// Accept web socket with options specified
        /// Call ReadHttpHeaderFromStreamAsync first to get WebSocketHttpContext
        /// </summary>
        /// <param name="context">The http context used to initiate this web socket request</param>
        /// <param name="options">The web socket options</param>
        /// <param name="cancellationToken">The optional cancellation token</param>
        /// <returns>A connected web socket</returns>
        public async Task<WebSocket> AcceptWebSocketAsync(WebSocketHttpContext context, WebSocketServerOptions options, CancellationToken cancellationToken = default(CancellationToken))
        {
            var guid = Guid.NewGuid();
            Events.Log.AcceptWebSocketStarted(guid);
            await PerformHandshakeAsync(guid, context.HttpHeader, options.SubProtocol, context.Stream, cancellationToken);
            Events.Log.ServerHandshakeSuccess(guid);
            return new SamuraiWebSocket(guid, context.Stream, options.KeepAliveInterval, null, options.IncludeExceptionInCloseResponse, false, options.SubProtocol);
        }

        private static void CheckWebSocketVersion(string httpHeader)
        {
            var match = WebSocketVersionRegex.Match(httpHeader);
            if (match.Success)
            {
                int secWebSocketVersion = Convert.ToInt32(match.Groups[1].Value.Trim());
                if (secWebSocketVersion < WebSocketVersion)
                    throw new WebSocketVersionNotSupportedException(string.Format("WebSocket Version {0} not suported. Must be {1} or above", secWebSocketVersion, WebSocketVersion));
                return;
            }

            throw new WebSocketVersionNotSupportedException("Cannot find \"Sec-WebSocket-Version\" in http header");
        }

        private static async Task PerformHandshakeAsync(Guid guid, String httpHeader, string subProtocol, Stream stream, CancellationToken cancellationToken)
        {
            try
            {
                CheckWebSocketVersion(httpHeader);

                var match = WebSocketKeyRegex.Match(httpHeader);
                if (match.Success)
                {
                    var secWebSocketKey = match.Groups[1].Value.Trim();
                    var setWebSocketAccept = secWebSocketKey.ComputeSocketAcceptString();
                    var response = "HTTP/1.1 101 Switching Protocols\r\n"
                                       + "Connection: Upgrade\r\n"
                                       + "Upgrade: websocket\r\n"
                                       + (subProtocol != null ? $"Sec-WebSocket-Protocol: {subProtocol}\r\n" : "")
                                       + $"Sec-WebSocket-Accept: {setWebSocketAccept}";

                    Events.Log.SendingHandshakeResponse(guid, response);
                    await stream.WriteHttpHeaderAsync(response, cancellationToken);
                    return;
                }

                throw new SecWebSocketKeyMissingException("Unable to read \"Sec-WebSocket-Key\" from http header");
            }
            catch (WebSocketVersionNotSupportedException ex)
            {
                Events.Log.WebSocketVersionNotSupported(guid, ex);
                await stream.WriteHttpHeaderAsync($"HTTP/1.1 426 Upgrade Required\r\nSec-WebSocket-Version: 13\r\n{ex.Message}", cancellationToken);
                throw;
            }
            catch (Exception ex)
            {
                Events.Log.BadRequest(guid, ex);
                await stream.WriteHttpHeaderAsync("HTTP/1.1 400 Bad Request", cancellationToken);
                throw;
            }
        }
    }
}
