using System;

namespace Shinobi.WebSockets.Exceptions
{
    /// <summary>
    /// Exception thrown when an HTTP header exceeds the maximum allowed size.
    /// This can occur if the client sends a request with headers that are too large,
    /// leading to potential issues in processing the request.
    /// The server may reject the request or throw this exception to indicate the problem.
    /// </summary>
    [Serializable]
    public class HttpHeaderTooLargeException : Exception
    {
        public HttpHeaderTooLargeException()
        {
        }

        public HttpHeaderTooLargeException(string message)
        : base(message)
        {
        }

        public HttpHeaderTooLargeException(string message, Exception inner)
        : base(message, inner)
        {
        }
    }
}
