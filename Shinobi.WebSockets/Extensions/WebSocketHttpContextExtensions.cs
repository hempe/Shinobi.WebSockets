using System;
using System.Collections.Generic;
using System.Linq;

using Shinobi.WebSockets.Exceptions;
using Shinobi.WebSockets.Http;

namespace Shinobi.WebSockets.Extensions
{
    public static class WebSocketHttpContextExtensions
    {
        private const int WebSocketVersion = 13;
        public static HttpResponse HandshakeResponse(this WebSocketHttpContext context, WebSocketServerOptions options)
        {
#if NET8_0_OR_GREATER
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

            // Handle per-message deflate negotiation
            string? perMessageDeflateResponse = null;

#if NET8_0_OR_GREATER
            if (options.PerMessageDeflate.Enabled)
            {
                var clientRequestsDeflate = context.HttpRequest!.GetHeaderValue("Sec-WebSocket-Extensions").ParseExtension();

                if (clientRequestsDeflate != null)
                {
                    var clientRequestsServerNoContext = clientRequestsDeflate?.Parameters.ContainsKey("server_no_context_takeover") == true;
                    var clientRequestsClientNoContext = clientRequestsDeflate?.Parameters.ContainsKey("client_no_context_takeover") == true;

                    // Check if we should reject based on DontAllow settings
                    if ((options.PerMessageDeflate.ServerContextTakeover == ContextTakeoverMode.DontAllow && clientRequestsServerNoContext) ||
                        (options.PerMessageDeflate.ClientContextTakeover == ContextTakeoverMode.DontAllow && clientRequestsClientNoContext))
                    {
                        // Reject the connection - client requested something we don't allow
                        throw new InvalidOperationException("Client requested context takeover mode that is not allowed");
                    }

                    // Build the response extension string
                    var responseParams = new List<string> { "permessage-deflate" };

                    // Handle server context takeover
                    if (options.PerMessageDeflate.ServerContextTakeover == ContextTakeoverMode.ForceDisabled ||
                        (options.PerMessageDeflate.ServerContextTakeover == ContextTakeoverMode.Allow && clientRequestsServerNoContext))
                    {
                        responseParams.Add("server_no_context_takeover");
                    }

                    // Handle client context takeover  
                    if (options.PerMessageDeflate.ClientContextTakeover == ContextTakeoverMode.ForceDisabled ||
                        (options.PerMessageDeflate.ClientContextTakeover == ContextTakeoverMode.Allow && clientRequestsClientNoContext))
                    {
                        responseParams.Add("client_no_context_takeover");
                    }

                    perMessageDeflateResponse = string.Join("; ", responseParams);
                }
            }
#endif

            return HttpResponse.Create(101)
                    .AddHeader("Connection", "Upgrade")
                    .AddHeader("Upgrade", "websocket")
                    .AddHeaderIf(!string.IsNullOrEmpty(perMessageDeflateResponse), "Sec-WebSocket-Extensions", perMessageDeflateResponse!)
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