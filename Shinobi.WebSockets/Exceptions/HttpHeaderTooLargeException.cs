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
        /// <summary>
        /// Gets the actual size of the HTTP header that exceeded the limit.
        /// </summary>
        public int? ActualSize { get; }

        /// <summary>
        /// Gets the maximum allowed size for HTTP headers.
        /// </summary>
        public int? MaxSize { get; }

        public HttpHeaderTooLargeException()
        {
        }

        public HttpHeaderTooLargeException(string message)
        : base(message)
        {
        }

        public HttpHeaderTooLargeException(int actualSize, int maxSize)
        : base($"HTTP header size {actualSize} exceeds maximum allowed size {maxSize}")
        {
            this.ActualSize = actualSize;
            this.MaxSize = maxSize;
        }

        public HttpHeaderTooLargeException(string message, Exception inner)
        : base(message, inner)
        {
        }
    }
}
