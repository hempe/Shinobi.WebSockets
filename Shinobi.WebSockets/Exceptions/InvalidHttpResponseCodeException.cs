// ---------------------------------------------------------------------
// Copyright 2018 David Haig
//
// Permission is hereby granted, free of charge, to any person obtaining a copy 
// of this software and associated documentation files (the "Software"), to deal 
// in the Software without restriction, including without limitation the rights 
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell 
// copies of the Software, and to permit persons to whom the Software is 
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR 
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, 
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN 
// THE SOFTWARE.
// ---------------------------------------------------------------------

using System;

namespace Shinobi.WebSockets.Exceptions
{
    /// <summary>
    /// Exception thrown when an invalid HTTP response code is received during the WebSocket handshake.
    /// This can occur if the server responds with a non-101 status code, indicating that the WebSocket upgrade request was not successful.
    /// </summary>
    [Serializable]
    public class InvalidHttpResponseCodeException : Exception
    {
        /// <summary>
        /// Gets the HTTP response code that was received.
        /// </summary>
        public int? ResponseCode { get; }

        public InvalidHttpResponseCodeException()
        {
        }

        public InvalidHttpResponseCodeException(string message)
        : base(message)
        {
        }

        public InvalidHttpResponseCodeException(int? responseCode)
        : base($"Invalid status code: {responseCode}")
        {
            this.ResponseCode = responseCode;
        }

        public InvalidHttpResponseCodeException(string message, Exception inner)
        : base(message, inner)
        {
        }
    }
}
