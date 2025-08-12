using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Shinobi.WebSockets.Exceptions;
using Shinobi.WebSockets.Extensions;

namespace Shinobi.WebSockets.Http
{

    /// <summary>
    /// The WebSocket HTTP Context used to initiate a WebSocket handshake
    /// </summary>
    public class WebSocketHttpContext
    {
        /// <summary>
        /// True if this is a valid WebSocket request
        /// </summary>
        public readonly bool IsWebSocketRequest;

        /// <summary>
        /// The protocols requested by the client in the WebSocket handshake
        /// </summary>
        public readonly IList<string> WebSocketRequestedProtocols;

        /// <summary>
        /// Gets the Sec-WebSocket-Extensions requested by the WebSocket handshake.
        /// </summary>
        public readonly IList<string> WebSocketExtensions;

        /// <summary>
        /// The raw http header extracted from the stream
        /// </summary>
        public readonly HttpRequest? HttpRequest;

        /// <summary>
        /// The Path extracted from the http header
        /// </summary>
        public readonly string? Path;

        /// <summary>
        /// The stream AFTER the header has already been read
        /// </summary>
        public readonly Stream Stream;

        /// <summary>
        /// The connection identifier
        /// </summary>
        public readonly Guid Guid;

        /// <summary>
        /// Initialises a new instance of the WebSocketHttpContext class
        /// </summary>
        /// <param name="httpHeader">The raw http request extracted from the stream</param>
        /// <param name="stream">The stream AFTER the header has already been read</param>
        /// <param name="guid">Connection identifier</param>
        public WebSocketHttpContext(HttpHeader httpHeader, Stream stream, Guid guid)
        {
            this.Guid = guid;
            this.IsWebSocketRequest = httpHeader.GetHeaderValue("Upgrade") == "websocket";
            this.WebSocketRequestedProtocols = httpHeader.GetHeaderValue("Sec-WebSocket-Protocol").ParseCommaSeparated();
            this.WebSocketExtensions = httpHeader.GetHeaderValues("Sec-WebSocket-Extensions").ToList();
            this.Stream = stream;
        }

        public WebSocketHttpContext(HttpRequest httpRequest, Stream stream, Guid guid)
            : this((HttpHeader)httpRequest, stream, guid)
        {

            this.Path = httpRequest.Path;
            this.HttpRequest = httpRequest;
        }

        public async ValueTask TerminateAsync(HttpResponse response, CancellationToken cancellationToken)
        {
            if (!this.Stream.CanWrite)
                return;

            await response.WriteToStreamAsync(this.Stream, cancellationToken);
        }
    }
}
