using System;
using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

// Extension methods for easier usage
namespace Shinobi.WebSockets.Extensions
{
    public static class WebSocketBuilderExtensions
    {
        // Cache for certificates to avoid repeated store access
        private static readonly ConcurrentDictionary<string, CachedCertificate> certificateCache = new ConcurrentDictionary<string, CachedCertificate>();
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> loadingSemaphores = new ConcurrentDictionary<string, SemaphoreSlim>();
        private static readonly TimeSpan DefaultCacheExpiration = TimeSpan.FromMinutes(5);

        private class CachedCertificate
        {
            public X509Certificate2 Certificate { get; set; } = null!;
            public DateTime CachedAt { get; set; }
            public TimeSpan CacheExpiration { get; set; }

            public bool IsExpired => DateTime.UtcNow - this.CachedAt > this.CacheExpiration;
        }

        /// <summary>
        /// Uses the ASP.NET Core development certificate for SSL/TLS
        /// </summary>
        /// <param name="builder">The WebSocketBuilder instance</param>
        /// <returns>The WebSocketBuilder for method chaining</returns>
        public static WebSocketBuilder UseDevCertificate(this WebSocketBuilder builder)
        {
            var certificate = GetAspNetCoreDevelopmentCertificate();
            if (certificate is null)
                throw new InvalidOperationException("ASP.NET Core development certificate not found. Please ensure the development certificate is installed. Run 'dotnet dev-certs https --trust' to install and trust the development certificate.");

            return builder.UseSsl(certificate);
        }

        /// <summary>
        /// Uses a certificate from the specified store and subject name with caching
        /// </summary>
        /// <param name="builder">The WebSocketBuilder instance</param>
        /// <param name="storeName">The certificate store name</param>
        /// <param name="storeLocation">The certificate store location</param>
        /// <param name="subjectName">The certificate subject name to search for</param>
        /// <param name="cacheExpiration">How long to cache the certificate (default: 5 minutes)</param>
        /// <returns>The WebSocketBuilder for method chaining</returns>
        public static WebSocketBuilder UseCertificate(
            this WebSocketBuilder builder,
            StoreName storeName,
            StoreLocation storeLocation,
            string subjectName,
            TimeSpan? cacheExpiration = null)
        {
            var expiration = cacheExpiration ?? DefaultCacheExpiration;

            // Load and validate certificate immediately to fail fast
            var certificate = GetCachedCertificate(storeName, storeLocation, X509FindType.FindBySubjectName, subjectName, expiration);

            // Create interceptor that uses cached certificate lookup
            builder.UseSsl(async (_client, _next, _cancellationToken) =>
                await GetCachedCertificateAsync(storeName, storeLocation, X509FindType.FindBySubjectName, subjectName, expiration));

            return builder;
        }

        /// <summary>
        /// Uses a certificate from the specified store and thumbprint with caching
        /// </summary>
        /// <param name="builder">The WebSocketBuilder instance</param>
        /// <param name="storeName">The certificate store name</param>
        /// <param name="storeLocation">The certificate store location</param>
        /// <param name="thumbprint">The certificate thumbprint</param>
        /// <param name="cacheExpiration">How long to cache the certificate (default: 5 minutes)</param>
        /// <returns>The WebSocketBuilder for method chaining</returns>
        public static WebSocketBuilder UseCertificateByThumbprint(
            this WebSocketBuilder builder,
            StoreName storeName,
            StoreLocation storeLocation,
            string thumbprint,
            TimeSpan? cacheExpiration = null)
        {
            var expiration = cacheExpiration ?? DefaultCacheExpiration;

            // Load and validate certificate immediately to fail fast
            var certificate = GetCachedCertificate(storeName, storeLocation, X509FindType.FindByThumbprint, thumbprint, expiration);

            // Create interceptor that uses cached certificate lookup
            builder.UseSsl(async (_client, _next, _cancellationToken) =>
                await GetCachedCertificateAsync(storeName, storeLocation, X509FindType.FindByThumbprint, thumbprint, expiration));

            return builder;
        }

        /// <summary>
        /// Gets a cached certificate synchronously (for startup/configuration), refreshing if expired. Uses semaphore to prevent concurrent loads.
        /// </summary>
        private static X509Certificate2 GetCachedCertificate(
            StoreName storeName,
            StoreLocation storeLocation,
            X509FindType findType,
            string findValue,
            TimeSpan cacheExpiration)
        {
            var cacheKey = $"{storeLocation}_{storeName}_{findType}_{findValue}";

            // Check if we have a valid cached certificate
            if (certificateCache.TryGetValue(cacheKey, out var cached) && !cached.IsExpired)
                return cached.Certificate;

            // Get or create a semaphore for this cache key to prevent concurrent loads
            var semaphore = loadingSemaphores.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));

            semaphore.Wait();
            try
            {
                return LoadAndCacheCertificate(cacheKey, storeName, storeLocation, findType, findValue, cacheExpiration);
            }
            finally
            {
                semaphore.Release();
            }
        }

        /// <summary>
        /// Gets a cached certificate asynchronously (for runtime pipeline), refreshing if expired. Uses semaphore to prevent concurrent loads.
        /// </summary>
        private static async ValueTask<X509Certificate2?> GetCachedCertificateAsync(
            StoreName storeName,
            StoreLocation storeLocation,
            X509FindType findType,
            string findValue,
            TimeSpan cacheExpiration)
        {
            var cacheKey = $"{storeLocation}_{storeName}_{findType}_{findValue}";

            // Check if we have a valid cached certificate
            if (certificateCache.TryGetValue(cacheKey, out var cached) && !cached.IsExpired)
                return cached.Certificate;

            // Get or create a semaphore for this cache key to prevent concurrent loads
            var semaphore = loadingSemaphores.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));

            await semaphore.WaitAsync();
            try
            {
                return LoadAndCacheCertificate(cacheKey, storeName, storeLocation, findType, findValue, cacheExpiration);
            }
            finally
            {
                semaphore.Release();
            }
        }

        /// <summary>
        /// Loads and caches a certificate. Called within semaphore protection.
        /// </summary>
        private static X509Certificate2 LoadAndCacheCertificate(
            string cacheKey,
            StoreName storeName,
            StoreLocation storeLocation,
            X509FindType findType,
            string findValue,
            TimeSpan cacheExpiration)
        {
            // Double-check after acquiring the semaphore
            if (certificateCache.TryGetValue(cacheKey, out var cached) && !cached.IsExpired)
                return cached.Certificate;

            // Load certificate from store - this will throw if not found or invalid
            var certificate = LoadCertificateFromStore(storeName, storeLocation, findType, findValue);

            // Cache the result
            certificateCache.AddOrUpdate(cacheKey,
                new CachedCertificate
                {
                    Certificate = certificate,
                    CachedAt = DateTime.UtcNow,
                    CacheExpiration = cacheExpiration
                },
                (_key, _existing) => new CachedCertificate
                {
                    Certificate = certificate,
                    CachedAt = DateTime.UtcNow,
                    CacheExpiration = cacheExpiration
                });

            return certificate;
        }

        /// <summary>
        /// Loads a certificate from the specified store using the given find criteria
        /// </summary>
        private static X509Certificate2 LoadCertificateFromStore(StoreName storeName, StoreLocation storeLocation, X509FindType findType, string findValue)
        {
            using var store = new X509Store(storeName, storeLocation);
            store.Open(OpenFlags.ReadOnly);

            var certificates = store.Certificates.Find(findType, findValue, false);

            // Filter for valid certificates and take the first one
            foreach (var cert in certificates)
            {
                if (cert.HasPrivateKey && cert.NotAfter > DateTime.Now)
                    return new X509Certificate2(cert);
            }

            throw new InvalidOperationException($"Certificate with {findType} '{findValue}' not found in store '{storeName}' at location '{storeLocation}', or no valid certificate found with private key that hasn't expired.");
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

            // Alternative approach: try the LocalMachine store as well
            using var machineStore = new X509Store(StoreName.My, StoreLocation.LocalMachine);
            machineStore.Open(OpenFlags.ReadOnly);

            var machineCertificates = machineStore.Certificates.Find(
                X509FindType.FindBySubjectName,
                "localhost",
                false);

            foreach (X509Certificate2 cert in machineCertificates)
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

            return null;
        }

        /// <summary>
        /// Clears the certificate cache (useful for testing or manual cache invalidation)
        /// </summary>
        public static void ClearCertificateCache()
        {
            certificateCache.Clear();

            // Clean up semaphores
            foreach (var semaphore in loadingSemaphores.Values)
            {
                semaphore.Dispose();
            }
            loadingSemaphores.Clear();
        }
    }
}