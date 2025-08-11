using System;

namespace Shinobi.WebSockets.Exceptions
{
    /// <summary>
    /// Exception thrown when a WebSocket buffer overflows.
    /// This can occur if the data being sent or received exceeds the allocated buffer size,
    /// leading to potential data loss or corruption.
    /// </summary>
    [Serializable]
    public class WebSocketBufferOverflowException : Exception
    {
        public WebSocketBufferOverflowException()
        {
        }

        public WebSocketBufferOverflowException(string message)
        : base(message)
        {
        }

        public WebSocketBufferOverflowException(string message, Exception inner)
        : base(message, inner)
        {
        }
    }
}
