using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

#if NET8_0_OR_GREATER
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

        /// <summary>
        /// Sets the body content for the HTTP request from a string.
        /// Automatically sets Content-Type to text/plain and Content-Length.
        /// </summary>
        /// <param name="this">The HTTP request instance.</param>
        /// <param name="body">The string content to be included as the request body.</param>
        /// <param name="contentType">Content type (defaults to text/plain; charset=utf-8)</param>
        /// <returns>Returns the current instance of <see cref="HttpRequest"/> to allow method chaining.</returns>
        public static HttpRequest WithBody(this HttpRequest @this, string? body, string contentType = "text/plain; charset=utf-8")
        {
            if (body == null)
            {
                return @this;
            }

            var bodyBytes = Encoding.UTF8.GetBytes(body);
            return @this.WithBody(bodyBytes, contentType);
        }

        /// <summary>
        /// Sets the body content for the HTTP request from a byte array.
        /// Automatically sets Content-Length header.
        /// </summary>
        /// <param name="this">The HTTP request instance.</param>
        /// <param name="body">The byte array containing the body content.</param>
        /// <param name="contentType">Content type (defaults to application/octet-stream)</param>
        /// <returns>Returns the current instance of <see cref="HttpRequest"/> to allow method chaining.</returns>
        public static HttpRequest WithBody(this HttpRequest @this, byte[]? body, string contentType = "application/octet-stream")
        {
            if (body == null || body.Length == 0)
            {
                return @this;
            }

            var bodyStream = new MemoryStream(body, false);
            @this.Body = bodyStream;
            
            // Remove existing headers and set new ones
            @this.headers.Remove("Content-Type");
            @this.headers.Remove("Content-Length");
            @this.AddHeader("Content-Type", contentType);
            @this.AddHeader("Content-Length", body.Length.ToString());
            
            return @this;
        }

        /// <summary>
        /// Sets the body content for the HTTP request from a stream.
        /// Note: If the stream has a known length, Content-Length will be set automatically.
        /// </summary>
        /// <param name="this">The HTTP request instance.</param>
        /// <param name="body">The stream containing the body content.</param>
        /// <param name="contentType">Content type (defaults to application/octet-stream)</param>
        /// <returns>Returns the current instance of <see cref="HttpRequest"/> to allow method chaining.</returns>
        public static HttpRequest WithBody(this HttpRequest @this, Stream? body, string contentType = "application/octet-stream")
        {
            if (body == null)
            {
                return @this;
            }

            @this.Body = body;
            
            // Remove existing headers and set new ones
            @this.headers.Remove("Content-Type");
            @this.headers.Remove("Content-Length");
            @this.AddHeader("Content-Type", contentType);
            
            // Set Content-Length if the stream has a known length
            if (body.CanSeek)
            {
                @this.AddHeader("Content-Length", body.Length.ToString());
            }
            
            return @this;
        }

        /// <summary>
        /// Sets JSON body content for the HTTP request.
        /// Automatically sets Content-Type to application/json and Content-Length.
        /// </summary>
        /// <param name="this">The HTTP request instance.</param>
        /// <param name="json">The JSON string to be included as the request body.</param>
        /// <returns>Returns the current instance of <see cref="HttpRequest"/> to allow method chaining.</returns>
        public static HttpRequest WithJsonBody(this HttpRequest @this, string json)
        {
            return @this.WithBody(json, "application/json; charset=utf-8");
        }

        /// <summary>
        /// Sets form data body content for the HTTP request.
        /// Automatically sets Content-Type to application/x-www-form-urlencoded and Content-Length.
        /// </summary>
        /// <param name="this">The HTTP request instance.</param>
        /// <param name="formData">The form data string to be included as the request body.</param>
        /// <returns>Returns the current instance of <see cref="HttpRequest"/> to allow method chaining.</returns>
        public static HttpRequest WithFormBody(this HttpRequest @this, string formData)
        {
            return @this.WithBody(formData, "application/x-www-form-urlencoded; charset=utf-8");
        }
    }
}