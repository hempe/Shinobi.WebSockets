using System;
using System.IO;
using System.Reflection;

namespace Shinobi.WebSockets.Http
{
    public sealed class FileResponse
    {
        /// <summary>
        /// Creates an HTTP response from a file path with custom content type
        /// </summary>
        public static HttpResponse CreateFromFile(
            string filePath,
            string? contentType = null,
            HttpRequest? request = null)
        {
            if (request != null && !string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                return HttpResponse.Create(405)
                    .AddHeader("Allow", "GET")
                    .AddHeader("Connection", "close")
                    .AddHeader("Content-Type", "text/plain")
                    .WithBody($"Method {request.Method} not allowed. File serving requires GET method.");
            }

            if (!File.Exists(filePath))
                return HttpResponse.Create(404);

            var fileInfo = new FileInfo(filePath);
            var lastModified = fileInfo.LastWriteTimeUtc;

            if (request != null && request.GetHeaderValue("Cache-Control")?.Contains("no-cache") != true)
            {
                if (request.HasHeader("If-Modified-Since"))
                {
                    var ifModifiedSinceValue = request.GetHeaderValue("If-Modified-Since");
                    if (DateTime.TryParse(ifModifiedSinceValue, out var ifModifiedSince)
                       && lastModified <= ifModifiedSince.ToUniversalTime())
                    {
                        return HttpResponse.Create(304);
                    }
                }
            }

            var bytes = File.ReadAllBytes(filePath);
            var response = CreateFromBytes(bytes, contentType ?? GetContentType(filePath));

            response.AddHeader("Last-Modified", lastModified.ToString("R"));
            return response;
        }

        /// <summary>
        /// Creates an HTTP response from an embedded resource with custom content type
        /// </summary>
        public static HttpResponse CreateFromEmbeddedResource(
            Assembly assembly,
            string resourceName,
            string? contentType = null,
            HttpRequest? request = null)
        {
            if (request != null && !string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                return HttpResponse.Create(405)
                    .AddHeader("Allow", "GET")
                    .AddHeader("Connection", "close")
                    .AddHeader("Content-Type", "text/plain")
                    .WithBody($"Method {request.Method} not allowed. Resource serving requires GET method.");
            }

            var assemblyVersion = assembly.GetName().Version?.ToString() ?? "1.0.0.0";
            var etag = $"\"{assemblyVersion}\"";

            if (request != null && request.GetHeaderValue("Cache-Control")?.Contains("no-cache") != true
               && request.HasHeader("If-None-Match"))
            {
                var ifNoneMatchValue = request.GetHeaderValue("If-None-Match");
                if (ifNoneMatchValue == etag)
                {
                    return HttpResponse.Create(304).AddHeader("ETag", etag);
                }
            }

            var stream = assembly.GetManifestResourceStream(resourceName);
            var response = CreateFromStream(stream, contentType ?? GetContentTypeFromResourceName(resourceName));

            response.AddHeader("ETag", etag);
            return response;
        }

        /// <summary>
        /// Creates an HTTP response from a stream
        /// </summary>
        public static HttpResponse CreateFromStream(Stream? stream, string contentType)
        {
            if (stream is null)
                return HttpResponse.Create(404);

            return HttpResponse.Create(200)
                .AddHeader("Content-Type", contentType)
                .WithBody(stream);
        }

        /// <summary>
        /// Creates an HTTP response from a byte array
        /// </summary>
        public static HttpResponse CreateFromBytes(byte[]? bytes, string contentType)
        {
            if (bytes is null)
                return HttpResponse.Create(404);

            return HttpResponse.Create(200)
                .AddHeader("Content-Type", contentType)
                .WithBody(bytes);
        }


        /// <summary>
        /// Gets the MIME type based on file extension
        /// </summary>
        public static string GetContentType(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension switch
            {
                // Text files
                ".txt" => "text/plain",
                ".csv" => "text/csv",
                ".md" => "text/markdown",

                // Web files
                ".html" or ".htm" => "text/html",
                ".css" => "text/css",
                ".js" or ".mjs" => "application/javascript",
                ".json" => "application/json",
                ".xml" => "application/xml",
                ".svg" => "image/svg+xml",

                // Images
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".webp" => "image/webp",
                ".ico" => "image/x-icon",
                ".tiff" or ".tif" => "image/tiff",
                ".avif" => "image/avif",

                // Audio
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                ".ogg" => "audio/ogg",
                ".m4a" => "audio/mp4",
                ".aac" => "audio/aac",
                ".flac" => "audio/flac",
                ".opus" => "audio/opus",

                // Video
                ".mp4" => "video/mp4",
                ".webm" => "video/webm",
                ".avi" => "video/x-msvideo",
                ".mov" => "video/quicktime",
                ".mkv" => "video/x-matroska",
                ".ogv" => "video/ogg",

                // Documents
                ".pdf" => "application/pdf",
                ".doc" => "application/msword",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".xls" => "application/vnd.ms-excel",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".ppt" => "application/vnd.ms-powerpoint",
                ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",

                // Archives
                ".zip" => "application/zip",

                // Fonts
                ".ttf" => "font/ttf",
                ".otf" => "font/otf",
                ".woff" => "font/woff",
                ".woff2" => "font/woff2",
                ".eot" => "application/vnd.ms-fontobject",

                // Default fallback
                _ => "application/octet-stream"
            };
        }

        /// <summary>
        /// Gets content type from resource name (useful for embedded resources)
        /// </summary>
        private static string GetContentTypeFromResourceName(string resourceName)
        {
            // Extract the file extension from the resource name
            var lastDot = resourceName.LastIndexOf('.');
            if (lastDot >= 0)
            {
                var extension = resourceName.Substring(lastDot);
                return GetContentType("file" + extension);
            }

            return "application/octet-stream";
        }
    }
}
