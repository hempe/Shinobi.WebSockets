using System;
using System.Collections.Generic;

#if NET9_0_OR_GREATER
#else 
using Samurai.WebSockets.Extensions;
#endif

namespace Samurai.WebSockets
{
    /// <summary>
    /// Builder for HttpResponse objects
    /// </summary>
    public class HttpResponseBuilder
    {
        private readonly int statusCode;
        private readonly Dictionary<string, HashSet<string>> headers = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        internal HttpResponseBuilder(int statusCode)
        {
            this.statusCode = statusCode;
        }

        /// <summary>
        /// Add a header with a single value
        /// </summary>
        public HttpResponseBuilder AddHeader(string name, string value)
        {
            if (!this.headers.TryGetValue(name, out var values))
            {
                values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                this.headers[name] = values;
            }
            values.Add(value);
            return this;
        }

        /// <summary>
        /// Add headers from dictionary
        /// </summary>
        public HttpResponseBuilder AddHeaders(Dictionary<string, string>? headers)
        {
            if (headers is null)
                return this;

            foreach (var header in headers)
                this.AddHeader(header.Key, header.Value);

            return this;
        }

        /// <summary>
        /// Add a header with multiple values
        /// </summary>
        public HttpResponseBuilder AddHeader(string name, IEnumerable<string> values)
        {
            if (!this.headers.TryGetValue(name, out var headerValues))
            {
                headerValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                this.headers[name] = headerValues;
            }

            foreach (var value in values)
            {
                headerValues.Add(value);
            }
            return this;
        }

        /// <summary>
        /// Add a header conditionally
        /// </summary>
        public HttpResponseBuilder AddHeaderIf(bool condition, string name, string value)
        {
            if (condition)
                this.AddHeader(name, value);
            return this;
        }

        /// <summary>
        /// Add a header conditionally with a value factory
        /// </summary>
        public HttpResponseBuilder AddHeaderIf(bool condition, string name, Func<string> valueFactory)
        {
            if (condition)
                this.AddHeader(name, valueFactory());
            return this;
        }

        /// <summary>
        /// Add raw header string (for compatibility with existing code)
        /// </summary>
        /// <param name="rawHeaders">Raw header string in format "Header1: value1\r\nHeader2: value2\r\n"</param>
        public HttpResponseBuilder AddRawHeaders(string? rawHeaders)
        {
            if (string.IsNullOrEmpty(rawHeaders))
                return this;

            foreach (var line in rawHeaders!.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var colonIndex = line.IndexOf(':');
                if (colonIndex > 0)
                {
                    var headerName = line.Substring(0, colonIndex).Trim();
                    var headerValue = line.Substring(colonIndex + 1).Trim();
                    this.AddHeader(headerName, headerValue);
                }
            }
            return this;
        }

        /// <summary>
        /// Add headers from another HttpHeader
        /// </summary>
        public HttpResponseBuilder AddHeaders(HttpHeader other)
        {
            if (other.headers != null)
            {
                foreach (var header in other.headers)
                {
                    this.AddHeader(header.Key, header.Value);
                }
            }
            return this;
        }

        /// <summary>
        /// Build the final HttpResponse
        /// </summary>
        public HttpResponse Build() => new HttpResponse(this.statusCode, this.headers);

        /// <summary>
        /// Build and convert to HTTP response string
        /// </summary>
        public string ToHttpResponse(string reasonPhrase = "OK", string? body = null)
            => this.Build().ToHttpResponse(reasonPhrase, body);

        /// <summary>
        /// Implicit conversion to HttpResponse
        /// </summary>
        public static implicit operator HttpResponse(HttpResponseBuilder builder)
            => builder.Build();
    }
}
