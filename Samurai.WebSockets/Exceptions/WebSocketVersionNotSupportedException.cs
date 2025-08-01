using System;

namespace Samurai.WebSockets.Exceptions
{
    /// <summary>
    /// Exception thrown when the WebSocket version is not supported by the server.
    /// This can occur if the client attempts to connect using a WebSocket version that the server
    /// does not recognize or support.
    /// </summary>
    [Serializable]
    public class WebSocketVersionNotSupportedException : Exception
    {
        public WebSocketVersionNotSupportedException()
        {
        }

        public WebSocketVersionNotSupportedException(string message)
        : base(message)
        {
        }

        public WebSocketVersionNotSupportedException(string message, Exception inner)
        : base(message, inner)
        {
        }
    }
}
