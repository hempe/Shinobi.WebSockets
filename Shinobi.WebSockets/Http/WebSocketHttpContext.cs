using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

using Microsoft.Extensions.Logging;

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
        /// The original tcp client.
        /// </summary>
        public readonly TcpClient? TcpClient;

        /// <summary>
        /// The Path extracted from the http header
        /// </summary>
        public readonly string? Path;

        /// <summary>
        /// The Query extracted from the http header path
        /// </summary>
        public NameValueCollection? Query;

        /// <summary>
        /// The stream AFTER the header has already been read
        /// </summary>
        public readonly Stream Stream;

        /// <summary>
        /// The connection identifier
        /// </summary>
        public readonly Guid Guid;

        /// <summary>
        /// Gets a dictionary containing custom metadata associated with this object.
        /// This collection allows users to store arbitrary key-value pairs for 
        /// application-specific purposes.
        /// </summary>
        /// <remarks>
        ///   <para>
        ///     The metadata dictionary is mutable and can be modified after object creation.
        ///     Keys are case-sensitive strings, and values can be any object type.
        ///   </para>
        ///   <para>
        ///   Common use cases include:
        ///   <list type="bullet">
        ///     <item><description>Storing user-defined tags or labels</description></item>
        ///     <item><description>Adding application-specific configuration data</description></item>
        ///     <item><description>Associating custom properties for business logic</description></item>
        ///     <item><description>Tracking additional context information</description></item>
        ///   </list>
        ///   </para>
        ///   <para>
        ///     Note: This collection is initialized as empty and is never null.
        ///   </para>
        /// </remarks>
        public readonly IDictionary<string, object> Metadata = new Dictionary<string, object>();

        internal ILoggerFactory? LoggerFactory { get; }

        /// <summary>
        /// Initialises a new instance of the WebSocketHttpContext class
        /// </summary>
        /// <param name="httpHeader">The raw http request extracted from the stream</param>
        /// <param name="stream">The stream AFTER the header has already been read</param>
        /// <param name="guid">Connection identifier</param>
        public WebSocketHttpContext(TcpClient? tcpClient, HttpHeader httpHeader, Stream stream, Guid guid, ILoggerFactory? loggerFactory = null)
        {
            this.LoggerFactory = loggerFactory;
            this.TcpClient = tcpClient;
            this.Guid = guid;
            this.IsWebSocketRequest = httpHeader.GetHeaderValue("Upgrade") == "websocket";
            this.WebSocketRequestedProtocols = httpHeader.GetHeaderValue("Sec-WebSocket-Protocol").ParseCommaSeparated();
            this.WebSocketExtensions = httpHeader.GetHeaderValues("Sec-WebSocket-Extensions").ToList();
            this.Stream = stream;
        }

        public WebSocketHttpContext(TcpClient? tcpClient, HttpRequest httpRequest, Stream stream, Guid guid, ILoggerFactory? loggerFactory = null)
            : this(tcpClient, (HttpHeader)httpRequest, stream, guid, loggerFactory)
        {

            this.Path = httpRequest.Path;
            if (!string.IsNullOrWhiteSpace(this.Path) && this.Path.Contains('?'))
            {
                this.Query = HttpUtility.ParseQueryString(this.Path.Substring(this.Path.IndexOf('?')));
            }

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
