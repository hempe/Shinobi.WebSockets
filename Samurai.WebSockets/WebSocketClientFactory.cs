// ---------------------------------------------------------------------
// Copyright 2018 David Haig
// Copyright 2025 Hempe
//
// Permission is hereby granted, free of charge, to any person obtaining a copy 
// of this software and associated documentation files (the "Software"), to deal 
// in the Software without restriction, including without limitation the rights 
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell 
// copies of the Software, and to permit persons to whom the Software is 
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR 
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, 
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN 
// THE SOFTWARE.
// ---------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Samurai.WebSockets.Exceptions;
using Samurai.WebSockets.Extensions;
using Samurai.WebSockets.Internal;

namespace Samurai.WebSockets
{
    /// <summary>
    /// Web socket client factory used to open web socket client connections
    /// </summary>
    public class WebSocketClientFactory : IWebSocketClientFactory
    {
        private const string NewLine = "\r\n";

        /// <summary>
        /// Connect with default options
        /// </summary>
        /// <param name="uri">The WebSocket uri to connect to (e.g. ws://example.com or wss://example.com for SSL)</param>
        /// <param name="cancellationToken">The optional cancellation token</param>
        /// <returns>A connected web socket instance</returns>
        public ValueTask<WebSocket> ConnectAsync(Uri uri, CancellationToken cancellationToken = default(CancellationToken))
            => this.ConnectAsync(uri, new WebSocketClientOptions(), cancellationToken);

        /// <summary>
        /// Connect with options specified
        /// </summary>
        /// <param name="uri">The WebSocket uri to connect to (e.g. ws://example.com or wss://example.com for SSL)</param>
        /// <param name="options">The WebSocket client options</param>
        /// <param name="cancellationToken">The optional cancellation token</param>
        /// <returns>A connected web socket instance</returns>
        public async ValueTask<WebSocket> ConnectAsync(Uri uri, WebSocketClientOptions options, CancellationToken cancellationToken = default(CancellationToken))
        {
            var guid = Guid.NewGuid();
            var uriScheme = uri.Scheme.ToLower();

            return await this.PerformHandshakeAsync(
                guid,
                uri,
                await this.GetStreamAsync(
                    guid,
                    uriScheme == "wss" || uriScheme == "https",
                    options.NoDelay,
                    uri.Host,
                    uri.Port,
                    cancellationToken).ConfigureAwait(false),
                options,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Connect with a stream that has already been opened and HTTP websocket upgrade request sent
        /// This function will check the handshake response from the server and proceed if successful
        /// Use this function if you have specific requirements to open a conenction like using special http headers and cookies
        /// You will have to build your own HTTP websocket upgrade request
        /// You may not even choose to use TCP/IP and this function will allow you to do that
        /// </summary>
        /// <param name="responseStream">The full duplex response stream from the server</param>
        /// <param name="secWebSocketKey">The secWebSocketKey you used in the handshake request</param>
        /// <param name="options">The WebSocket client options</param>
        /// <param name="cancellationToken">The optional cancellation token</param>
        /// <returns></returns>
        public ValueTask<WebSocket> ConnectAsync(Stream responseStream, string secWebSocketKey, WebSocketClientOptions options, CancellationToken cancellationToken = default(CancellationToken))
            => this.ConnectAsync(Guid.NewGuid(),
                responseStream,
                secWebSocketKey,
                options.KeepAliveInterval,
                options.SecWebSocketExtensions,
                options.IncludeExceptionInCloseResponse,
                cancellationToken);

        private async ValueTask<WebSocket> ConnectAsync(Guid guid, Stream responseStream, string secWebSocketKey, TimeSpan keepAliveInterval, string? secWebSocketExtensions, bool includeExceptionInCloseResponse, CancellationToken cancellationToken)
        {
            Events.Log?.ReadingHttpResponse(guid);
            HttpResponse? response;

            try
            {
                response = await HttpResponse.ReadAsync(responseStream, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Events.Log?.ReadHttpResponseError(guid, ex);
                throw new WebSocketHandshakeFailedException("Handshake unexpected failure", ex);
            }

            this.ThrowIfInvalidResponseCode(response);
            this.ThrowIfInvalidAcceptString(guid, response!, secWebSocketKey);

            return new SamuraiWebSocket(
                new WebSocketHttpContext(response!, responseStream, guid),
                keepAliveInterval,
                response!.GetHeaderValuesCombined("Sec-WebSocket-Extensions")?.Contains("permessage-deflate") == true,
                includeExceptionInCloseResponse,
                true,
                response.GetHeaderValuesCombined("Sec-WebSocket-Protocol"));
        }

        private void ThrowIfInvalidAcceptString(Guid guid, HttpHeader response, string secWebSocketKey)
        {
            // make sure we escape the accept string which could contain special regex characters
            var actualAcceptString = response.GetHeaderValue("Sec-WebSocket-Accept");

            // check the accept string
            var expectedAcceptString = secWebSocketKey.ComputeSocketAcceptString();
            if (expectedAcceptString != actualAcceptString)
            {
                var warning = $"Handshake failed because the accept string from the server '{expectedAcceptString}' was not the expected string '{actualAcceptString}'";
                Events.Log?.HandshakeFailure(guid, warning);
                throw new WebSocketHandshakeFailedException(warning);
            }

            Events.Log?.ClientHandshakeSuccess(guid);
        }

        private void ThrowIfInvalidResponseCode(HttpResponse? repsonse)
        {
            if (repsonse?.StatusCode != 101)
                throw new InvalidHttpResponseCodeException(repsonse?.StatusCode);
        }

        /// <summary>
        /// Override this if you need more fine grained control over the TLS handshake like setting the SslProtocol or adding a client certificate
        /// </summary>
        protected virtual void TlsAuthenticateAsClient(SslStream sslStream, string host)
            => sslStream.AuthenticateAsClient(host, null, SslProtocols.Tls12, true);

        /// <summary>
        /// Override this if you need more control over how the stream used for the websocket is created. It does not event need to be a TCP stream
        /// </summary>
        /// <param name="loggingGuid">For logging purposes only</param>
        /// <param name="isSecure">Make a secure connection</param>
        /// <param name="noDelay">Set to true to send a message immediately with the least amount of latency (typical usage for chat)</param>
        /// <param name="host">The destination host (can be an IP address)</param>
        /// <param name="port">The destination port</param>
        /// <param name="cancellationToken">Used to cancel the request</param>
        /// <returns>A connected and open stream</returns>
        protected async virtual ValueTask<Stream> GetStreamAsync(Guid loggingGuid, bool isSecure, bool noDelay, string host, int port, CancellationToken cancellationToken)
        {
            var tcpClient = new TcpClient { NoDelay = noDelay };
            if (IPAddress.TryParse(host, out var ipAddress))
            {
                Events.Log?.ClientConnectingToIpAddress(loggingGuid, ipAddress.ToString(), port);
                await tcpClient.ConnectAsync(ipAddress, port).ConfigureAwait(false);
            }
            else
            {
                Events.Log?.ClientConnectingToHost(loggingGuid, host, port);
                await tcpClient.ConnectAsync(host, port).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var stream = tcpClient.GetStream();

            if (isSecure)
            {
                var sslStream = new SslStream(stream, false, new RemoteCertificateValidationCallback(ValidateServerCertificate), null);
                Events.Log?.AttemtingToSecureSslConnection(loggingGuid);

                // This will throw an AuthenticationException if the certificate is not valid
                this.TlsAuthenticateAsClient(sslStream, host);
                Events.Log?.ConnectionSecured(loggingGuid);
                return sslStream;
            }

            Events.Log?.ConnectionNotSecure(loggingGuid);
            return stream;
        }

        /// <summary>
        /// Invoked by the RemoteCertificateValidationDelegate
        /// If you want to ignore certificate errors (for debugging) then return true
        /// </summary>
        private static bool ValidateServerCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
                return true;

            Events.Log?.SslCertificateError(sslPolicyErrors);
            // TODO: Add option on new server to "ignore certificate errors"

            // Do not allow this client to communicate with unauthenticated servers.
            return false;
        }

        private static string GetAdditionalHeaders(Dictionary<string, string> additionalHeaders)
        {
            if (additionalHeaders == null || additionalHeaders.Count == 0)
                return string.Empty;

            var builder = new StringBuilder();
            foreach (KeyValuePair<string, string> pair in additionalHeaders)
                builder.Append($"{pair.Key}: {pair.Value}{NewLine}");

            return builder.ToString();
        }

        private ValueTask<WebSocket> PerformHandshakeAsync(Guid guid, Uri uri, Stream stream, WebSocketClientOptions options, CancellationToken cancellationToken)
        {
            var secWebSocketKey = Shared.SecWebSocketKey();
            var handshakeHttpRequest = HttpRequest.Create("GET", uri.PathAndQuery)
                .AddHeader("Host", $"{uri.Host}:{uri.Port}")
                .AddHeader("Upgrade", "websocket")
                .AddHeader("Connection", "Upgrade")
                .AddHeader("Sec-WebSocket-Key", secWebSocketKey)
                .AddHeader("Origin", $"http://{uri.Host}:{uri.Port}")
                .AddHeaderIf(!string.IsNullOrEmpty(options.SecWebSocketProtocol),
                            "Sec-WebSocket-Protocol", options.SecWebSocketProtocol!)
                .AddHeaderIf(!string.IsNullOrEmpty(options.SecWebSocketExtensions),
                            "Sec-WebSocket-Extensions", options.SecWebSocketExtensions!)
                .AddHeaders(options.AdditionalHttpHeaders)
                .AddHeader("Sec-WebSocket-Version", "13")
                .ToHttpRequest();



            var httpRequest = Encoding.UTF8.GetBytes(handshakeHttpRequest);
            stream.Write(httpRequest, 0, httpRequest.Length);
            Events.Log?.HandshakeSent(guid, handshakeHttpRequest);
            return this.ConnectAsync(stream, secWebSocketKey, options, cancellationToken);
        }
    }
}
