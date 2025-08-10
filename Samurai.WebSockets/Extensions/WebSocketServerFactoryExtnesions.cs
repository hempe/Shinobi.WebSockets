using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Samurai.WebSockets.Exceptions;
using Samurai.WebSockets.Internal;

namespace Samurai.WebSockets.Extensions
{
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
            => new WebSocketHttpContext(await HttpRequest.ReadAsync(stream, cancellationToken).ConfigureAwait(false) ?? throw new Exception("Invalid request"), stream, Guid.NewGuid());

        /// <summary>
        /// Accept web socket with default options
        /// </summary>
        /// <param name="context">The http context used to initiate this web socket request</param>
        /// <param name="cancellationToken">The optional cancellation token</param>
        /// <returns>A connected web socket</returns>
        public static ValueTask<SamuraiWebSocket> AcceptWebSocketAsync(this WebSocketHttpContext context, CancellationToken cancellationToken = default(CancellationToken))
            => AcceptWebSocketAsync(context, new WebSocketServerOptions(), cancellationToken);

        /// <summary>
        /// Accept web socket with options specified
        /// </summary>
        /// <param name="context">The http context used to initiate this web socket request</param>
        /// <param name="options">The web socket options</param>
        /// <param name="cancellationToken">The optional cancellation token</param>
        /// <returns>A connected web socket</returns>
        public static async ValueTask<SamuraiWebSocket> AcceptWebSocketAsync(this WebSocketHttpContext context, WebSocketServerOptions options, CancellationToken cancellationToken = default(CancellationToken))
        {
            var guid = Guid.NewGuid();
            Events.Log?.AcceptWebSocketStarted(guid);
            var response = await PerformHandshakeAsync(guid, options, context, cancellationToken).ConfigureAwait(false);
            Events.Log?.ServerHandshakeSuccess(guid);
            return new SamuraiWebSocket(
                context,
                response.GetHeaderValue("Sec-WebSocket-Extensions")?.ParseExtension(),
                options.KeepAliveInterval,
                options.IncludeExceptionInCloseResponse,
                false,
                response.GetHeaderValuesCombined("Sec-WebSocket-Protocol"));
        }

        private static async ValueTask<HttpResponse> PerformHandshakeAsync(Guid guid, WebSocketServerOptions options, WebSocketHttpContext context, CancellationToken cancellationToken)
        {
            try
            {
                var response = context.HandshakeResponse(options);
                Events.Log?.SendingHandshakeResponse(guid, response.StatusCode);
                await response.WriteToStreamAsync(context.Stream, cancellationToken).ConfigureAwait(false);
                return response;
            }
            catch (WebSocketVersionNotSupportedException ex)
            {
                Events.Log?.WebSocketVersionNotSupported(guid, ex);
                var response = HttpResponse.Create(426).AddHeader("Sec-WebSocket-Version", "13").WithBody(ex.Message);
                await context.TerminateAsync(response, cancellationToken).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                Events.Log?.BadRequest(guid, ex);
                var response = HttpResponse.Create(400);
                await context.TerminateAsync(response, cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
    }
}
