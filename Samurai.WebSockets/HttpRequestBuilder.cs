using System;
using System.Collections.Generic;

#if NET9_0_OR_GREATER
using System.Buffers.Text;
using System.Runtime.InteropServices;
#else 
using Samurai.WebSockets.Extensions;
#endif

namespace Samurai.WebSockets
{

    /// <summary>
    /// Builder for HttpRequest objects
    /// </summary>
    public class HttpRequestBuilder
    {
        private readonly string method;
        private readonly string path;
        private readonly Dictionary<string, HashSet<string>> headers = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        internal HttpRequestBuilder(string method, string path)
        {
            this.method = method;
            this.path = path;
        }

        /// <summary>
        /// Add a header with a single value
        /// </summary>
        public HttpRequestBuilder AddHeader(string name, string value)
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
        public HttpRequestBuilder AddHeaders(Dictionary<string, string>? headers)
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
        public HttpRequestBuilder AddHeader(string name, IEnumerable<string> values)
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
        public HttpRequestBuilder AddHeaderIf(bool condition, string name, string value)
        {
            if (condition)
                this.AddHeader(name, value);
            return this;
        }

        /// <summary>
        /// Add a header conditionally with a value factory
        /// </summary>
        public HttpRequestBuilder AddHeaderIf(bool condition, string name, Func<string> valueFactory)
        {
            if (condition)
                this.AddHeader(name, valueFactory());
            return this;
        }

        /// <summary>
        /// Add raw header string (for compatibility with existing code)
        /// </summary>
        /// <param name="rawHeaders">Raw header string in format "Header1: value1\r\nHeader2: value2\r\n"</param>
        public HttpRequestBuilder AddRawHeaders(string? rawHeaders)
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
        public HttpRequestBuilder AddHeaders(HttpHeader other)
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
        /// Build the final HttpRequest
        /// </summary>
        public HttpRequest Build() => new HttpRequest(this.method, this.path, this.headers);

        /// <summary>
        /// Build and convert to HTTP request string
        /// </summary>
        public string ToHttpRequest(string httpVersion = "HTTP/1.1")
            => this.Build().ToHttpRequest(httpVersion);

        /// <summary>
        /// Implicit conversion to HttpRequest
        /// </summary>
        public static implicit operator HttpRequest(HttpRequestBuilder builder)
            => builder.Build();
    }
}