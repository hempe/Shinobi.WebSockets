using System.Collections.Generic;
using System.IO;

namespace Samurai.WebSockets
{
    /// <summary>
    /// The WebSocket HTTP Context used to initiate a WebSocket handshake
    /// </summary>
    public class WebSocketHttpContext
    {
        /// <summary>
        /// True if this is a valid WebSocket request
        /// </summary>
        public bool IsWebSocketRequest { get; }

        /// <summary>
        /// The protocols requested by the client in the WebSocket handshake
        /// </summary>
        public IList<string> WebSocketRequestedProtocols { get; }

        /// <summary>
        /// The raw http header extracted from the stream
        /// </summary>
        public string HttpHeader { get; }

        /// <summary>
        /// The Path extracted from the http header
        /// </summary>
        public string? Path { get; }

        /// <summary>
        /// The stream AFTER the header has already been read
        /// </summary>
        public Stream Stream { get; }

        /// <summary>
        /// Initialises a new instance of the WebSocketHttpContext class
        /// </summary>
        /// <param name="httpHeader">The raw http header extracted from the stream</param>
        /// <param name="stream">The stream AFTER the header has already been read</param>
        public WebSocketHttpContext(string httpHeader, Stream stream)
        {
            this.IsWebSocketRequest = httpHeader.IsWebSocketUpgradeRequest();
            this.WebSocketRequestedProtocols = httpHeader.GetSubProtocols();
            this.Path = httpHeader.GetPathFromHeader();
            this.HttpHeader = httpHeader;
            this.Stream = stream;
        }
    }
}
