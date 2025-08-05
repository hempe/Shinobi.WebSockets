using System;
using System.Collections.Generic;

#if NET9_0_OR_GREATER
#endif

namespace Samurai.WebSockets
{
    /// <summary>
    /// Builder class for constructing HTTP headers fluently
    /// </summary>
    public class HttpHeaderBuilder
    {
        private readonly int? statusCode;
        private readonly string? method;
        private readonly string? path;
        private readonly Dictionary<string, HashSet<string>> headers;

        internal HttpHeaderBuilder(int? statusCode, string? method, string? path)
        {
            this.statusCode = statusCode;
            this.method = method;
            this.path = path;
            this.headers = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Add a header with a single value
        /// </summary>
        public HttpHeaderBuilder AddHeader(string name, string value)
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
        /// Add a header with multiple values
        /// </summary>
        public HttpHeaderBuilder AddHeader(string name, IEnumerable<string> values)
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
        public HttpHeaderBuilder AddHeaderIf(bool condition, string name, string value)
        {
            if (condition)
                this.AddHeader(name, value);
            return this;
        }

        /// <summary>
        /// Add a header conditionally with a value factory
        /// </summary>
        public HttpHeaderBuilder AddHeaderIf(bool condition, string name, Func<string> valueFactory)
        {
            if (condition)
                this.AddHeader(name, valueFactory());
            return this;
        }

        /// <summary>
        /// Add raw header string (for compatibility with existing code)
        /// </summary>
        /// <param name="rawHeaders">Raw header string in format "Header1: value1\r\nHeader2: value2\r\n"</param>
        public HttpHeaderBuilder AddRawHeaders(string? rawHeaders)
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
        public HttpHeaderBuilder AddHeaders(HttpHeader other)
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
        /// Build the final HttpHeader
        /// </summary>
        public HttpHeader Build()
            => new HttpHeader(this.statusCode, this.method, this.path, this.headers);

        /// <summary>
        /// Build and convert to HTTP response string
        /// </summary>
        public string ToHttpResponse(string reasonPhrase = "OK")
            => this.Build().ToHttpResponse(reasonPhrase);

        /// <summary>
        /// Build and convert to HTTP request string
        /// </summary>
        public string ToHttpRequest(string httpVersion = "HTTP/1.1")
            => this.Build().ToHttpRequest(httpVersion);

        /// <summary>
        /// Implicit conversion to HttpHeader
        /// </summary>
        public static implicit operator HttpHeader(HttpHeaderBuilder builder)
            => new HttpHeader(builder.statusCode, builder.method, builder.path, builder.headers);
    }
}
