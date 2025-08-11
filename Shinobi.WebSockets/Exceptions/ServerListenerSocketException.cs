using System;

namespace Shinobi.WebSockets.Exceptions
{
    /// <summary>
    /// Exception thrown when there is an issue with the server listener socket.
    /// This can occur if the server fails to start listening for incoming WebSocket connections,
    /// or if there is a problem with the underlying socket operations.
    /// </summary>
    [Serializable]
    public class ServerListenerSocketException : Exception
    {
        public ServerListenerSocketException()
        {
        }

        public ServerListenerSocketException(string message)
        : base(message)
        {
        }

        public ServerListenerSocketException(string message, Exception inner)
        : base(message, inner)
        {
        }
    }
}
