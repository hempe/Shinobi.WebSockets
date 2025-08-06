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
        private const int WebSocketVersion = 13;

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
                CheckWebSocketVersion(context.HttpRequest);

                var secWebSocketKey = context.HttpRequest.GetHeaderValue("Sec-WebSocket-Key");

                if (string.IsNullOrEmpty(secWebSocketKey))
                    throw new SecWebSocketKeyMissingException("Unable to read \"Sec-WebSocket-Key\" from http header");

                var setWebSocketAccept = secWebSocketKey!.ComputeSocketAcceptString();
                var compress = options.AllowPerMessageDeflate && context.WebSocketExtensions?.Contains("permessage-deflate") == true;
                var response = HttpResponse.Create(101)
                        .AddHeader("Connection", "Upgrade")
                        .AddHeader("Upgrade", "websocket")
                        .AddHeaderIf(options.SubProtocol != null, "Sec-WebSocket-Protocol", options.SubProtocol!)
                        .AddHeaderIf(compress, "Sec-WebSocket-Extensions", "permessage-deflate")
                        .AddHeader("Sec-WebSocket-Accept", setWebSocketAccept)
                        .Build();

                Events.Log?.SendingHandshakeResponse(guid, response);
                await context.Stream.WriteHttpHeaderAsync(response, cancellationToken).ConfigureAwait(false);
                return compress;
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
