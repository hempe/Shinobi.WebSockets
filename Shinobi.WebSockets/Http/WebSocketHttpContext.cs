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

        private static bool IsValidWebSocketRequest(HttpHeader httpHeader)
        {
            // Check if it's a WebSocket upgrade request - only validate Upgrade header here
            // Method and Connection header validation is handled in HandshakeCoreAsync with proper HTTP responses
            return string.Equals(httpHeader.GetHeaderValue("Upgrade"), "websocket", StringComparison.OrdinalIgnoreCase);
        }

        private static IList<string> ProcessSubprotocolHeaders(IList<string> requestedProtocols, HttpRequest httpRequest, HashSet<string> allowedSubprotocolHeaders)
        {
            var filteredProtocols = new List<string>();
            
            foreach (var protocol in requestedProtocols)
            {
                // Check if this is the base header transport protocol
                if (protocol == "|h|")
                {
                    filteredProtocols.Add(protocol);
                    continue;
                }
                
                // Check if this is a subprotocol header using |h|base58_name|base58_value syntax
                if (protocol.StartsWith("|h|") && protocol.Length > 3)
                {
                    var parts = protocol.Split('|');
                    if (parts.Length == 4 && parts[0] == "" && parts[1] == "h") // Must have |h|name|value format
                    {
                        var encodedHeaderName = parts[2];
                        var encodedHeaderValue = parts[3];
                        
                        string headerName;
                        string headerValue;
                        
                        // Try to base58 decode both name and value
                        try
                        {
                            headerName = Base58Decode(encodedHeaderName);
                            headerValue = Base58Decode(encodedHeaderValue);
                        }
                        catch
                        {
                            // If base58 decode fails, skip this subprotocol header and don't add to filtered protocols
                            continue;
                        }
                        
                        // Check if this header is allowed
                        if (allowedSubprotocolHeaders.Contains(headerName))
                        {
                            // Add as HTTP header to the request
                            httpRequest.AddHeader(headerName, headerValue);
                            // Don't add this specific |h|name|value to filtered protocols - it's been processed
                            // The base |h| protocol (if sent) will handle the response
                            continue;
                        }
                        else
                        {
                            // Don't add unauthorized header subprotocols to filtered list
                            continue;
                        }
                    }
                }
                
                // Keep regular subprotocols
                filteredProtocols.Add(protocol);
            }
            
            return filteredProtocols;
        }

        private static string Base58Decode(string encoded)
        {
            const string alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
            
            if (string.IsNullOrEmpty(encoded))
                return string.Empty;
                
            var num = System.Numerics.BigInteger.Zero;
            
            // Convert from base58
            foreach (char c in encoded)
            {
                int charIndex = alphabet.IndexOf(c);
                if (charIndex == -1)
                    throw new ArgumentException($"Invalid base58 character: {c}");
                    
                num = num * 58 + charIndex;
            }
            
            // Convert to bytes
            var bytes = new List<byte>();
            while (num > 0)
            {
                bytes.Insert(0, (byte)(num % 256));
                num /= 256;
            }
            
            // Add leading zeros
            foreach (char c in encoded)
            {
                if (c == '1')
                    bytes.Insert(0, 0);
                else
                    break;
            }
            
            return System.Text.Encoding.UTF8.GetString(bytes.ToArray());
        }

        private static void ProcessQueryParamHeaders(System.Collections.Specialized.NameValueCollection query, HttpRequest httpRequest, HashSet<string> allowedQueryParamHeaders)
        {
            foreach (string? paramName in query.AllKeys)
            {
                if (paramName != null && allowedQueryParamHeaders.Contains(paramName))
                {
                    var paramValue = query[paramName];
                    if (!string.IsNullOrEmpty(paramValue))
                    {
                        // Add as HTTP header to the request
                        httpRequest.AddHeader(paramName, paramValue);
                    }
                }
            }
        }

        /// <summary>
        /// Initialises a new instance of the WebSocketHttpContext class
        /// </summary>
        /// <param name="httpHeader">The raw http request extracted from the stream</param>
        /// <param name="stream">The stream AFTER the header has already been read</param>
        /// <param name="guid">Connection identifier</param>
        public WebSocketHttpContext(TcpClient? tcpClient, HttpHeader httpHeader, Stream stream, Guid guid, ILoggerFactory? loggerFactory = null, HashSet<string>? allowedSubprotocolHeaders = null, HashSet<string>? allowedQueryParamHeaders = null)
        {
            this.LoggerFactory = loggerFactory;
            this.TcpClient = tcpClient;
            this.Guid = guid;
            this.IsWebSocketRequest = IsValidWebSocketRequest(httpHeader);
            
            var requestedProtocols = httpHeader.GetHeaderValue("Sec-WebSocket-Protocol").ParseCommaSeparated();
            
            // Process subprotocol headers if enabled
            if (allowedSubprotocolHeaders != null && httpHeader is HttpRequest httpRequest)
            {
                this.WebSocketRequestedProtocols = ProcessSubprotocolHeaders(requestedProtocols, httpRequest, allowedSubprotocolHeaders);
            }
            else
            {
                this.WebSocketRequestedProtocols = requestedProtocols;
            }
            
            this.WebSocketExtensions = httpHeader.GetHeaderValues("Sec-WebSocket-Extensions").ToList();
            this.Stream = stream;
        }

        public WebSocketHttpContext(TcpClient? tcpClient, HttpRequest httpRequest, Stream stream, Guid guid, ILoggerFactory? loggerFactory = null, HashSet<string>? allowedSubprotocolHeaders = null, HashSet<string>? allowedQueryParamHeaders = null)
            : this(tcpClient, (HttpHeader)httpRequest, stream, guid, loggerFactory, allowedSubprotocolHeaders, allowedQueryParamHeaders)
        {

            this.Path = httpRequest.Path;
            if (!string.IsNullOrWhiteSpace(this.Path) && this.Path.Contains('?'))
            {
                var queryStartIndex = this.Path.IndexOf('?');
                var queryString = this.Path.Substring(queryStartIndex);
                this.Query = HttpUtility.ParseQueryString(queryString);
                
                // Process query parameter headers if enabled and strip processed headers from query
                if (allowedQueryParamHeaders != null && this.Query != null)
                {
                    ProcessQueryParamHeaders(this.Query, httpRequest, allowedQueryParamHeaders);
                    
                    // Remove processed headers from query string and update path
                    foreach (string headerName in allowedQueryParamHeaders)
                    {
                        this.Query.Remove(headerName);
                    }
                }
                
                // Strip query parameters from path (like we do for subprotocols)
                this.Path = this.Path.Substring(0, queryStartIndex);
                if (this.Query?.Count > 0)
                {
                    // Re-add remaining query parameters
                    this.Path += "?" + this.Query.ToString();
                }
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
