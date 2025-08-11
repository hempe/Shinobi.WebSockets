// ---------------------------------------------------------------------
// Copyright 2018 David Haig
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
using System.Linq;
using System.Threading.Tasks;

namespace Shinobi.WebSockets.Internal
{
    internal static class Builder
    {
        /// <summary>
        /// Builds a chain of stream acceptance interceptors
        /// </summary>
        public static AcceptStreamHandler BuildAcceptStreamChain(
            AcceptStreamHandler terminal,
            IEnumerable<AcceptStreamInterceptor>? interceptors)
        {
            if (interceptors == null)
                return terminal;

            var chain = terminal;
            foreach (var interceptor in interceptors.Reverse())
            {
                var next = chain;
                chain = (tcpClient, cancellationToken) => interceptor(tcpClient, next, cancellationToken);
            }

            return chain;
        }

        /// <summary>
        /// Builds a chain of certificate selection interceptors
        /// </summary>
        public static CertificateSelectionHandler BuildCertificateSelectionChain(
            CertificateSelectionHandler terminal,
            IEnumerable<CertificateSelectionInterceptor>? interceptors)
        {
            if (interceptors == null)
                return terminal;

            var chain = terminal;
            foreach (var interceptor in interceptors.Reverse())
            {
                var next = chain;
                chain = (tcpClient, cancellationToken) => interceptor(tcpClient, next, cancellationToken);
            }

            return chain;
        }

        /// <summary>
        /// Builds a chain of handshake interceptors
        /// </summary>
        public static HandshakeHandler BuildHandshakeChain(
            HandshakeHandler terminal,
            IEnumerable<HandshakeInterceptor>? interceptors)
        {
            if (interceptors == null)
                return terminal;

            var chain = terminal;
            foreach (var interceptor in interceptors.Reverse())
            {
                var next = chain;
                chain = (context, cancellationToken) => interceptor(context, next, cancellationToken);
            }

            return chain;
        }

        /// <summary>
        /// Builds a chain of WebSocket connect interceptors
        /// </summary>
        public static WebSocketConnectHandler BuildWebSocketConnectChain(
            IEnumerable<WebSocketConnectInterceptor>? interceptors)
        {
            WebSocketConnectHandler terminal = (webSocket, cancellationToken) => new ValueTask();
            if (interceptors == null)
                return terminal;

            var chain = terminal;
            foreach (var interceptor in interceptors.Reverse())
            {
                var next = chain;
                chain = (webSocket, cancellationToken) => interceptor(webSocket, next, cancellationToken);
            }

            return chain;
        }

        /// <summary>
        /// Builds a chain of WebSocket close interceptors
        /// </summary>
        public static WebSocketCloseHandler BuildWebSocketCloseChain(
            IEnumerable<WebSocketCloseInterceptor>? interceptors)
        {
            WebSocketCloseHandler terminal = (webSocket, cancellationToken) => new ValueTask();
            if (interceptors == null)
                return terminal;

            var chain = terminal;
            foreach (var interceptor in interceptors.Reverse())
            {
                var next = chain;
                chain = (webSocket, cancellationToken) => interceptor(webSocket, next, cancellationToken);
            }

            return chain;
        }

        /// <summary>
        /// Builds a chain of WebSocket error interceptors
        /// </summary>
        public static WebSocketErrorHandler BuildWebSocketErrorChain(
            IEnumerable<WebSocketErrorInterceptor>? interceptors)
        {
            WebSocketErrorHandler terminal = (webSocket, exception, cancellationToken) => new ValueTask();
            if (interceptors == null)
                return terminal;

            var chain = terminal;
            foreach (var interceptor in interceptors.Reverse())
            {
                var next = chain;
                chain = (webSocket, exception, cancellationToken) => interceptor(webSocket, exception, next, cancellationToken);
            }

            return chain;
        }

        /// <summary>
        /// Builds a chain of WebSocket message interceptors
        /// </summary>
        public static WebSocketMessageHandler BuildWebSocketMessageChain(
            IEnumerable<WebSocketMessageInterceptor>? interceptors)
        {
            WebSocketMessageHandler terminal = (webSocket, messageType, messageStream, cancellationToken) => new ValueTask();
            if (interceptors == null)
                return terminal;

            var chain = terminal;
            foreach (var interceptor in interceptors.Reverse())
            {
                var next = chain;
                chain = (webSocket, messageType, messageStream, cancellationToken) => interceptor(webSocket, messageType, messageStream, next, cancellationToken);
            }

            return chain;
        }
    }
}