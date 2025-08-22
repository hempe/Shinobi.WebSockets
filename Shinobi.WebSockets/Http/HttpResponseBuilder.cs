using System;
using System.Collections.Generic;
using System.IO;
using System.Text;


#if NET8_0_OR_GREATER
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
            @this.body = body is null ? null : new MemoryStream(Encoding.UTF8.GetBytes(body));
            return @this;
        }

        /// <summary>
        /// Sets the body content for the HTTP response from a byte array.
        /// </summary>
        /// <param name="this">The HTTP response instance.</param>
        /// <param name="body">The byte array containing the body content. If null, no body will be set.</param>
        /// <returns>
        /// Returns the current instance of <see cref="HttpResponse"/> to allow method chaining.
        /// </returns>
        public static HttpResponse WithBody(this HttpResponse @this, byte[]? body)
        {
            @this.body = body is null ? null : new MemoryStream(body);
            return @this;
        }

        /// <summary>
        /// Sets the body content for the HTTP response from a stream.
        /// </summary>
        /// <param name="this">The HTTP response instance.</param>
        /// <param name="body">The stream containing the body content. The stream will be used directly and should be positioned at the start of the content to be sent.</param>
        /// <returns>
        /// Returns the current instance of <see cref="HttpResponse"/> to allow method chaining.
        /// </returns>
        /// <remarks>
        /// The provided stream will be disposed when the HTTP response is written. Ensure the stream is readable and positioned correctly before passing it to this method.
        /// </remarks>
        public static HttpResponse WithBody(this HttpResponse @this, Stream? body)
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
