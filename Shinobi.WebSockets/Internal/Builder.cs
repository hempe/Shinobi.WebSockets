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
            WebSocketConnectHandler terminal = (webSocket, cancellationToken) => default(ValueTask);
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
            WebSocketCloseHandler terminal = (webSocket, closeStatus, statusDescription, cancellationToken) => default(ValueTask);
            if (interceptors == null)
                return terminal;

            var chain = terminal;
            foreach (var interceptor in interceptors.Reverse())
            {
                var next = chain;
                chain = (webSocket, closeStatus, statusDescription, cancellationToken) => interceptor(webSocket, closeStatus, statusDescription, next, cancellationToken);
            }

            return chain;
        }

        /// <summary>
        /// Builds a chain of WebSocket error interceptors
        /// </summary>
        public static WebSocketErrorHandler BuildWebSocketErrorChain(
            IEnumerable<WebSocketErrorInterceptor>? interceptors)
        {
            WebSocketErrorHandler terminal = (webSocket, exception, cancellationToken) => default(ValueTask);
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
            WebSocketMessageHandler terminal = (webSocket, messageType, messageStream, cancellationToken) => default(ValueTask);
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