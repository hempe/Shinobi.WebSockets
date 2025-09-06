using System;
using System.Diagnostics.CodeAnalysis;

namespace Shinobi.WebSockets.Exceptions
{
    /// <summary>
    /// Exception thrown when an invalid HTTP response code is received during the WebSocket handshake.
    /// This can occur if the server responds with a non-101 status code, indicating that the WebSocket upgrade request was not successful.
    /// </summary>
    [Serializable]
    [ExcludeFromCodeCoverage]
    public class InvalidHttpResponseCodeException : Exception
    {
        /// <summary>
        /// Gets the HTTP response code that was received.
        /// </summary>
        public int? ResponseCode { get; }

        public InvalidHttpResponseCodeException(int? responseCode)
        : base($"Invalid status code: {responseCode}")
        {
            this.ResponseCode = responseCode;
        }

        public InvalidHttpResponseCodeException()
        {
        }

        public InvalidHttpResponseCodeException(string message) : base(message)
        {
        }

        public InvalidHttpResponseCodeException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
