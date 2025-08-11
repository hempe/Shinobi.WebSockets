using System.Collections.Generic;

#if NET9_0_OR_GREATER
using System.Buffers.Text;
using System.Runtime.InteropServices;
#else 
using Shinobi.WebSockets.Extensions;
#endif

namespace Shinobi.WebSockets.Http
{

    /// <summary>
    /// Builder for HttpRequest objects
    /// </summary>
    public static class HttpRequestBuilder
    {

        /// <summary>
        /// Add a header with a single value
        /// </summary>
        public static HttpRequest AddHeader(this HttpRequest @this, string name, string value)
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
        public static HttpRequest AddHeaders(this HttpRequest @this, Dictionary<string, string>? headers)
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
        public static HttpRequest AddHeader(this HttpRequest @this, string name, IEnumerable<string> values)
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
        public static HttpRequest AddHeaderIf(this HttpRequest @this, bool condition, string name, string value)
        {
            if (condition)
                @this.AddHeader(name, value);
            return @this;
        }

        /// <summary>
        /// Add a header conditionally with a value factory
        /// </summary>
        public static HttpRequest AddHeaderIf(this HttpRequest @this, bool condition, string name, Func<string> valueFactory)
        {
            if (condition)
                @this.AddHeader(name, valueFactory());
            return @this;
        }

        /// <summary>
        /// Add raw header string (for compatibility with existing code)
        /// </summary>
        /// <param name="rawHeaders">Raw header string in format "Header1: value1\r\nHeader2: value2\r\n"</param>
        public static HttpRequest AddRawHeaders(this HttpRequest @this, string? rawHeaders)
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
        public static HttpRequest AddHeaders(this HttpRequest @this, HttpHeader other)
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