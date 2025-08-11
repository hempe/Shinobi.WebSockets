using System;

namespace Shinobi.WebSockets.Exceptions
{
    /// <summary>
    /// Exception thrown when the Sec-WebSocket-Key header is missing in the WebSocket handshake request.
    /// This header is required for establishing a WebSocket connection, and its absence indicates
    /// a failure in the handshake process.
    /// </summary>
    [Serializable]
    public class SecWebSocketKeyMissingException : Exception
    {
        public SecWebSocketKeyMissingException()
        {
        }

        public SecWebSocketKeyMissingException(string message)
        : base(message)
        {
        }

        public SecWebSocketKeyMissingException(string message, Exception inner)
        : base(message, inner)
        {
        }
    }
}
