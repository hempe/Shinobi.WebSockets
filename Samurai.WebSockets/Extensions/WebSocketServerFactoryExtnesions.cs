using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Samurai.WebSockets.Exceptions;
using Samurai.WebSockets.Internal;

namespace Samurai.WebSockets.Extensions
{
    public class WebSocketServerFactory
    {

    }
    /// <summary>
    /// Web socket server factory used to open web socket server connections
    /// </summary>
    public static class WebSocketServerFactoryExtnesions
    {

        /// <summary>
        /// Reads a http header information from a stream and decodes the parts relating to the WebSocket protocot upgrade
        /// </summary>
        /// <param name="stream">The network stream</param>
        /// <param name="cancellationToken">The optional cancellation token</param>
        /// <returns>Http data read from the stream</returns>
        public static async ValueTask<WebSocketHttpContext> ReadHttpHeaderFromStreamAsync(this Stream stream, CancellationToken cancellationToken = default(CancellationToken))
            => new WebSocketHttpContext(await HttpRequest.ReadAsync(stream, cancellationToken).ConfigureAwait(false) ?? throw new Exception("Invalid request"), stream);

        /// <summary>
        /// Accept web socket with default options
        /// Call ReadHttpHeaderFromStreamAsync first to get WebSocketHttpContext
        /// </summary>
        /// <param name="context">The http context used to initiate this web socket request</param>
        /// <param name="cancellationToken">The optional cancellation token</param>
        /// <returns>A connected web socket</returns>
        public static ValueTask<SamuraiWebSocket> AcceptWebSocketAsync(this WebSocketHttpContext context, CancellationToken cancellationToken = default(CancellationToken))
            => AcceptWebSocketAsync(context, new WebSocketServerOptions(), cancellationToken);

        /// <summary>
        /// Accept web socket with options specified
        /// Call ReadHttpHeaderFromStreamAsync first to get WebSocketHttpContext
        /// </summary>
        /// <param name="context">The http context used to initiate this web socket request</param>
        /// <param name="options">The web socket options</param>
        /// <param name="cancellationToken">The optional cancellation token</param>
        /// <returns>A connected web socket</returns>
        public static async ValueTask<SamuraiWebSocket> AcceptWebSocketAsync(this WebSocketHttpContext context, WebSocketServerOptions options, CancellationToken cancellationToken = default(CancellationToken))
        {
            var guid = Guid.NewGuid();
            Events.Log?.AcceptWebSocketStarted(guid);
            var usePermessageDeflate = await PerformHandshakeAsync(guid, options, context, cancellationToken).ConfigureAwait(false);
            Events.Log?.ServerHandshakeSuccess(guid);
            return new SamuraiWebSocket(guid, context.Stream, options.KeepAliveInterval, usePermessageDeflate, options.IncludeExceptionInCloseResponse, false, options.SubProtocol);
        }

        private static async ValueTask<bool> PerformHandshakeAsync(Guid guid, WebSocketServerOptions options, WebSocketHttpContext context, CancellationToken cancellationToken)
        {
            try
            {
                var response = context.HandshakeResponse(options);
                var message = response.Build();

                Events.Log?.SendingHandshakeResponse(guid, message);
                await context.Stream.WriteHttpHeaderAsync(message, cancellationToken).ConfigureAwait(false);
                return response.GetHeaderValue("Sec-WebSocket-Extensions")?.Contains("permessage-deflate") == true;
            }
            catch (WebSocketVersionNotSupportedException ex)
            {
                Events.Log?.WebSocketVersionNotSupported(guid, ex);
                await context.Stream.WriteHttpHeaderAsync($"HTTP/1.1 426 Upgrade Required\r\nSec-WebSocket-Version: 13\r\n{ex.Message}", cancellationToken).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                Events.Log?.BadRequest(guid, ex);
                await context.Stream.WriteHttpHeaderAsync("HTTP/1.1 400 Bad Request", cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
    }
}
