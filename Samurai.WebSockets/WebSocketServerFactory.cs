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
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

using Samurai.WebSockets.Exceptions;
using Samurai.WebSockets.Extensions;
using Samurai.WebSockets.Internal;

namespace Samurai.WebSockets
{
    /// <summary>
    /// Web socket server factory used to open web socket server connections
    /// </summary>
    public class WebSocketServerFactory : IWebSocketServerFactory
    {
        private const int WebSocketVersion = 13;

        /// <summary>
        /// Reads a http header information from a stream and decodes the parts relating to the WebSocket protocot upgrade
        /// </summary>
        /// <param name="stream">The network stream</param>
        /// <param name="cancellationToken">The optional cancellation token</param>
        /// <returns>Http data read from the stream</returns>
        public async ValueTask<WebSocketHttpContext> ReadHttpHeaderFromStreamAsync(Stream stream, CancellationToken cancellationToken = default(CancellationToken))
            => new WebSocketHttpContext(await HttpHeader.ReadHttpHeaderAsync(stream, cancellationToken).ConfigureAwait(false), stream);

        /// <summary>
        /// Accept web socket with default options
        /// Call ReadHttpHeaderFromStreamAsync first to get WebSocketHttpContext
        /// </summary>
        /// <param name="context">The http context used to initiate this web socket request</param>
        /// <param name="cancellationToken">The optional cancellation token</param>
        /// <returns>A connected web socket</returns>
        public ValueTask<WebSocket> AcceptWebSocketAsync(WebSocketHttpContext context, CancellationToken cancellationToken = default(CancellationToken))
            => this.AcceptWebSocketAsync(context, new WebSocketServerOptions(), cancellationToken);

        /// <summary>
        /// Accept web socket with options specified
        /// Call ReadHttpHeaderFromStreamAsync first to get WebSocketHttpContext
        /// </summary>
        /// <param name="context">The http context used to initiate this web socket request</param>
        /// <param name="options">The web socket options</param>
        /// <param name="cancellationToken">The optional cancellation token</param>
        /// <returns>A connected web socket</returns>
        public async ValueTask<WebSocket> AcceptWebSocketAsync(WebSocketHttpContext context, WebSocketServerOptions options, CancellationToken cancellationToken = default(CancellationToken))
        {
            var guid = Guid.NewGuid();
            Events.Log.AcceptWebSocketStarted(guid);
            var usePermessageDeflate = await PerformHandshakeAsync(guid, options, context, cancellationToken).ConfigureAwait(false);
            Events.Log.ServerHandshakeSuccess(guid);
            return new SamuraiWebSocket(guid, context.Stream, options.KeepAliveInterval, usePermessageDeflate, options.IncludeExceptionInCloseResponse, false, options.SubProtocol);
        }

        private static void CheckWebSocketVersion(HttpHeader httpHeader)
        {
            var version = httpHeader.GetHeaderValue("Sec-WebSocket-Version");
            if (!string.IsNullOrEmpty(version))
            {
                int secWebSocketVersion = Convert.ToInt32(version);
                if (secWebSocketVersion < WebSocketVersion)
                    throw new WebSocketVersionNotSupportedException(string.Format("WebSocket Version {0} not suported. Must be {1} or above", secWebSocketVersion, WebSocketVersion));
                return;
            }

            throw new WebSocketVersionNotSupportedException("Cannot find \"Sec-WebSocket-Version\" in http header");
        }

        private static async ValueTask<bool> PerformHandshakeAsync(Guid guid, WebSocketServerOptions options, WebSocketHttpContext context, CancellationToken cancellationToken)
        {
            try
            {
                CheckWebSocketVersion(context.HttpHeader);

                var secWebSocketKey = context.HttpHeader.GetHeaderValue("Sec-WebSocket-Key");

                if (string.IsNullOrEmpty(secWebSocketKey))
                    throw new SecWebSocketKeyMissingException("Unable to read \"Sec-WebSocket-Key\" from http header");

                var setWebSocketAccept = secWebSocketKey!.ComputeSocketAcceptString();
                var compress = options.AllowPerMessageDeflate && context.WebSocketExtensions?.Contains("permessage-deflate") == true;
                var response = "HTTP/1.1 101 Switching Protocols\r\n"
                                   + "Connection: Upgrade\r\n"
                                   + "Upgrade: websocket\r\n"
                                   + (options.SubProtocol != null ? $"Sec-WebSocket-Protocol: {options.SubProtocol}\r\n" : "")
                                   + (compress ? "Sec-WebSocket-Extensions: permessage-deflate\r\n" : "")
                                   + $"Sec-WebSocket-Accept: {setWebSocketAccept}";

                Events.Log.SendingHandshakeResponse(guid, response);
                await context.Stream.WriteHttpHeaderAsync(response, cancellationToken).ConfigureAwait(false);
                return compress;
            }
            catch (WebSocketVersionNotSupportedException ex)
            {
                Events.Log.WebSocketVersionNotSupported(guid, ex);
                await context.Stream.WriteHttpHeaderAsync($"HTTP/1.1 426 Upgrade Required\r\nSec-WebSocket-Version: 13\r\n{ex.Message}", cancellationToken).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                Events.Log.BadRequest(guid, ex);
                await context.Stream.WriteHttpHeaderAsync("HTTP/1.1 400 Bad Request", cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
    }
}
