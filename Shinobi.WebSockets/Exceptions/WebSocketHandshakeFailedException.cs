using System;

namespace Shinobi.WebSockets.Exceptions
{
    /// <summary>
    /// Exception thrown when the WebSocket handshake fails.
    /// This can occur if the server does not respond correctly to the WebSocket handshake request,
    /// or if the handshake response does not meet the expected criteria.
    /// </summary>
    [Serializable]
    public class WebSocketHandshakeFailedException : Exception
    {
        public WebSocketHandshakeFailedException()
        {
        }

        public WebSocketHandshakeFailedException(string message)
        : base(message)
        {
        }

        public WebSocketHandshakeFailedException(string message, Exception inner)
        : base(message, inner)
        {
        }
    }
}
