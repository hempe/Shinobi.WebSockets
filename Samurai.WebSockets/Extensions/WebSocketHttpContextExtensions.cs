
using System;

using Samurai.WebSockets.Exceptions;

namespace Samurai.WebSockets.Extensions
{
    public static class WebSocketHttpContextExtensions
    {
        private const int WebSocketVersion = 13;

        public static HttpResponse HandshakeResponse(this WebSocketHttpContext context, WebSocketServerOptions options)
        {
#if NET9_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(context.HttpRequest);
#else
            if (context.HttpRequest is null)
                throw new ArgumentNullException(nameof(context.HttpRequest));
#endif
            CheckWebSocketVersion(context.HttpRequest);

            var secWebSocketKey = context.HttpRequest.GetHeaderValue("Sec-WebSocket-Key");

            if (string.IsNullOrEmpty(secWebSocketKey))
                throw new SecWebSocketKeyMissingException("Unable to read \"Sec-WebSocket-Key\" from http header");

            var setWebSocketAccept = secWebSocketKey!.ComputeSocketAcceptString();
            var compress = options.AllowPerMessageDeflate && context.WebSocketExtensions?.Contains("permessage-deflate") == true;
            return HttpResponse.Create(101)
                    .AddHeader("Connection", "Upgrade")
                    .AddHeader("Upgrade", "websocket")
                    .AddHeaderIf(compress, "Sec-WebSocket-Extensions", "permessage-deflate")
                    .AddHeader("Sec-WebSocket-Accept", setWebSocketAccept);
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
    }
}