using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Samurai.WebSockets.Internal;

// Extension methods for easier usage
namespace Samurai.WebSockets.Extensions
{
    public static class WebSocketBuilderExtensions
    {

        /// <summary>
        /// Uses the ASP.NET Core development certificate for SSL/TLS
        /// </summary>
        /// <param name="builder">The WebSocketBuilder instance</param>
        /// <returns>The WebSocketBuilder for method chaining</returns>
        public static WebSocketBuilder UseDevCertificate(this WebSocketBuilder builder)
        {
            var certificate = GetAspNetCoreDevelopmentCertificate();
            if (certificate == null)
            {
                throw new InvalidOperationException("ASP.NET Core development certificate not found. Please ensure the development certificate is installed. Run 'dotnet dev-certs https --trust' to install and trust the development certificate.");
            }

            return builder.UseSsl(certificate);
        }

        /// <summary>
        /// Attempts to find and load the ASP.NET Core development certificate
        /// </summary>
        /// <returns>The development certificate if found, null otherwise</returns>
        private static X509Certificate2? GetAspNetCoreDevelopmentCertificate()
        {
            // Try to find the ASP.NET Core development certificate
            using (var store = new X509Store(StoreName.My, StoreLocation.CurrentUser))
            {
                store.Open(OpenFlags.ReadOnly);

                // Look for certificates with the ASP.NET Core HTTPS development certificate subject
                var certificates = store.Certificates.Find(
                    X509FindType.FindBySubjectName,
                    "localhost",
                    false);

                foreach (X509Certificate2 cert in certificates)
                {
                    // Check if this is likely the ASP.NET Core dev certificate
                    if (cert.Subject.Contains("CN=localhost") &&
                        cert.HasPrivateKey &&
                        cert.NotAfter > DateTime.Now)
                    {
                        // Additional check for the ASP.NET Core development certificate OID
                        foreach (var extension in cert.Extensions)
                        {
                            if (extension.Oid!.Value == "1.3.6.1.4.1.311.84.1.1") // ASP.NET Core HTTPS development certificate OID
                            {
                                return new X509Certificate2(cert);
                            }
                        }

                        // Fallback: if we find a localhost certificate with private key, use it
                        if (cert.FriendlyName.Contains("ASP.NET Core") ||
                            cert.Issuer.Contains("localhost"))
                        {
                            return new X509Certificate2(cert);
                        }
                    }
                }
            }

            // Alternative approach: try the Personal store location as well
            using (var store = new X509Store(StoreName.My, StoreLocation.LocalMachine))
            {
                try
                {
                    store.Open(OpenFlags.ReadOnly);

                    var certificates = store.Certificates.Find(
                        X509FindType.FindBySubjectName,
                        "localhost",
                        false);

                    foreach (X509Certificate2 cert in certificates)
                    {
                        if (cert.Subject.Contains("CN=localhost") &&
                            cert.HasPrivateKey &&
                            cert.NotAfter > DateTime.Now &&
                            (cert.FriendlyName.Contains("ASP.NET Core") ||
                             cert.Issuer.Contains("localhost")))
                        {
                            return new X509Certificate2(cert);
                        }
                    }
                }
                catch
                {
                    // Ignore if we can't access the LocalMachine store
                }
            }

            return null;
        }
    }
}
