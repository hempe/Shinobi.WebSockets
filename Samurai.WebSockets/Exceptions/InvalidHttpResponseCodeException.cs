using System;

namespace Samurai.WebSockets.Exceptions
{
    /// <summary>
    /// Exception thrown when an invalid HTTP response code is received during the WebSocket handshake.
    /// This can occur if the server responds with a non-101 status code, indicating that the WebSocket upgrade request was not successful.
    /// The exception includes details about the response code, response header, and any additional response details that may help diagnose the issue.
    /// </summary>
    [Serializable]
    public class InvalidHttpResponseCodeException : Exception
    {
        /// <summary>
        /// Gets the HTTP response code that was received.
        /// </summary>
        public string? ResponseCode { get; }

        /// <summary>
        /// Gets the additional details about the HTTP response, if any.
        /// </summary>
        public string? ResponseHeader { get; }

        /// <summary>
        /// Gets the details of the response, which may include error messages or other information related to the response code.
        /// </summary>
        public string? ResponseDetails { get; }

        public InvalidHttpResponseCodeException()
        {
        }

        public InvalidHttpResponseCodeException(string message)
        : base(message)
        {
        }

        public InvalidHttpResponseCodeException(string? responseCode, string? responseDetails, string? responseHeader)
        : base(responseCode)
        {
            this.ResponseCode = responseCode;
            this.ResponseDetails = responseDetails;
            this.ResponseHeader = responseHeader;
        }

        public InvalidHttpResponseCodeException(string message, Exception inner)
        : base(message, inner)
        {
        }
    }
}
