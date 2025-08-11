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
using System.Collections.Generic;

#if NET9_0_OR_GREATER
#else 
using Shinobi.WebSockets.Extensions;
#endif

namespace Shinobi.WebSockets.Http
{
    /// <summary>
    /// Builder for HttpResponse objects
    /// </summary>
    public static class HttpResponseBuilder
    {

        /// <summary>
        /// Sets the body content for the HTTP response.
        /// </summary>
        /// <param name="body">The body content to be included in the HTTP response.</param>
        /// <returns>
        /// Returns the current instance of <see cref="HttpResponse"/> to allow method chaining.
        /// </returns>
        public static HttpResponse WithBody(this HttpResponse @this, string? body)
        {
            @this.body = body;
            return @this;
        }

        /// <summary>
        /// Add a header with a single value
        /// </summary>
        public static HttpResponse AddHeader(this HttpResponse @this, string name, string value)
        {
            if (!@this.headers.TryGetValue(name, out var values))
            {
                values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                @this.headers[name] = values;
            }
            values.Add(value);
            return @this;
        }

        /// <summary>
        /// Add headers from dictionary
        /// </summary>
        public static HttpResponse AddHeaders(this HttpResponse @this, Dictionary<string, string>? headers)
        {
            if (headers is null)
                return @this;

            foreach (var header in headers)
                @this.AddHeader(header.Key, header.Value);

            return @this;
        }

        /// <summary>
        /// Add a header with multiple values
        /// </summary>
        public static HttpResponse AddHeader(this HttpResponse @this, string name, IEnumerable<string> values)
        {
            if (!@this.headers.TryGetValue(name, out var headerValues))
            {
                headerValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                @this.headers[name] = headerValues;
            }

            foreach (var value in values)
            {
                headerValues.Add(value);
            }
            return @this;
        }

        /// <summary>
        /// Add a header conditionally
        /// </summary>
        public static HttpResponse AddHeaderIf(this HttpResponse @this, bool condition, string name, string value)
        {
            if (condition)
                @this.AddHeader(name, value);
            return @this;
        }

        /// <summary>
        /// Add a header conditionally with a value factory
        /// </summary>
        public static HttpResponse AddHeaderIf(this HttpResponse @this, bool condition, string name, Func<string> valueFactory)
        {
            if (condition)
                @this.AddHeader(name, valueFactory());
            return @this;
        }

        /// <summary>
        /// Add raw header string (for compatibility with existing code)
        /// </summary>
        /// <param name="rawHeaders">Raw header string in format "Header1: value1\r\nHeader2: value2\r\n"</param>
        public static HttpResponse AddRawHeaders(this HttpResponse @this, string? rawHeaders)
        {
            if (string.IsNullOrEmpty(rawHeaders))
                return @this;

            foreach (var line in rawHeaders!.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var colonIndex = line.IndexOf(':');
                if (colonIndex > 0)
                {
                    var headerName = line.Substring(0, colonIndex).Trim();
                    var headerValue = line.Substring(colonIndex + 1).Trim();
                    @this.AddHeader(headerName, headerValue);
                }
            }
            return @this;
        }

        /// <summary>
        /// Add headers from another HttpHeader
        /// </summary>
        public static HttpResponse AddHeaders(this HttpResponse @this, HttpHeader other)
        {
            if (other.headers != null)
            {
                foreach (var header in other.headers)
                {
                    @this.AddHeader(header.Key, header.Value);
                }
            }
            return @this;
        }
    }
}
