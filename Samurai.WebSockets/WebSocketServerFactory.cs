
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Samurai.WebSockets.Extensions;

namespace Samurai.WebSockets
{
    public class WebSocketServerFactory : IWebSocketServerFactory
    {
        public ValueTask<SamuraiWebSocket> AcceptWebSocketAsync(WebSocketHttpContext context, CancellationToken cancellationToken = default)
            => context.AcceptWebSocketAsync(cancellationToken);

        public ValueTask<SamuraiWebSocket> AcceptWebSocketAsync(WebSocketHttpContext context, WebSocketServerOptions options, CancellationToken cancellationToken = default)
            => context.AcceptWebSocketAsync(options, cancellationToken);

        public ValueTask<WebSocketHttpContext> ReadHttpHeaderFromStreamAsync(Stream stream, CancellationToken cancellationToken = default)
            => stream.ReadHttpHeaderFromStreamAsync(cancellationToken);
    }
}
