// ---------------------------------------------------------------------
// Copyright 2018 David Haig
// Copyright 2025 Hansueli Burri
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
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Shinobi.WebSockets.Exceptions;
using Shinobi.WebSockets.Http;
using Shinobi.WebSockets.Internal;

namespace Shinobi.WebSockets.Extensions
{
    /// <summary>
    /// Web socket server factory used to open web socket server connections
    /// </summary>
    public static class WebSocketServerFactoryExtensions
    {
        /// <summary>
        /// Accept web socket with options specified
        /// </summary>
        /// <param name="context">The http context used to initiate this web socket request</param>
        /// <param name="options">The web socket options</param>
        /// <param name="cancellationToken">The optional cancellation token</param>
        /// <returns>A connected web socket</returns>
        public static async ValueTask<ShinobiWebSocket> AcceptWebSocketAsync(this WebSocketHttpContext context, WebSocketServerOptions options, CancellationToken cancellationToken = default(CancellationToken))
        {
            var guid = Guid.NewGuid();
            var logger = context.LoggerFactory?.CreateLogger<ShinobiWebSocket>();
            logger?.AcceptWebSocketStarted(guid);
            var response = await PerformHandshakeAsync(guid, options, context, cancellationToken).ConfigureAwait(false);
            logger?.ServerHandshakeSuccess(guid);
            return new ShinobiWebSocket(
                context,
#if NET8_0_OR_GREATER
                response.GetHeaderValue("Sec-WebSocket-Extensions")?.ParseExtension(),
#endif
                options.KeepAliveInterval,
                options.IncludeExceptionInCloseResponse,
                false,
                response.GetHeaderValuesCombined("Sec-WebSocket-Protocol"));
        }

        private static async ValueTask<HttpResponse> PerformHandshakeAsync(Guid guid, WebSocketServerOptions options, WebSocketHttpContext context, CancellationToken cancellationToken)
        {
            var logger = context.LoggerFactory?.CreateLogger<ShinobiWebSocket>();
            try
            {
                var response = context.HandshakeResponse(options);
                logger?.SendingHandshakeResponse(guid, response.StatusCode);
                await response.WriteToStreamAsync(context.Stream, cancellationToken).ConfigureAwait(false);
                return response;
            }
            catch (WebSocketVersionNotSupportedException ex)
            {
                logger?.WebSocketVersionNotSupported(guid, ex);
                var response = HttpResponse.Create(426)
                    .AddHeader("Sec-WebSocket-Version", "13")
                    .WithBody(ex.Message);
                await context.TerminateAsync(response, cancellationToken).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                logger?.BadRequest(guid, ex);
                var response = HttpResponse.Create(400);
                await context.TerminateAsync(response, cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
    }
}
