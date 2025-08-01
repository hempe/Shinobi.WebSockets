using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Samurai.WebSockets;

namespace WebSockets.DemoServer
{
    internal class Program
    {
        private static ILogger logger;
        private static ILoggerFactory loggerFactory;
        private static IWebSocketServerFactory webSocketServerFactory;

        private static async Task Main(string[] _)
        {
            loggerFactory = LoggerFactory.Create(builder => builder
                    .SetMinimumLevel(LogLevel.Trace)
                    .AddConsole());

            logger = loggerFactory.CreateLogger<Program>();

            Samurai.WebSockets.Internal.Events.Log
                = new Samurai.WebSockets.Internal.Events(loggerFactory.CreateLogger<Samurai.WebSockets.Internal.Events>());

            webSocketServerFactory = new WebSocketServerFactory();
            await StartWebServerAsync();
            logger.LogInformation("Server stopped. Press any key to exit.");
            Console.ReadKey();
        }

        private static async Task StartWebServerAsync()
        {
            try
            {
                int port = 27416;
                IList<string> supportedSubProtocols = new string[] { "chatV1", "chatV2", "chatV3" };
                using (WebServer server = new WebServer(webSocketServerFactory, loggerFactory, supportedSubProtocols))
                {
                    await server.ListenAsync(port);
                    logger.LogInformation($"Listening on port {port}");
                    logger.LogInformation("Press any key to quit");
                    Console.ReadKey();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex.ToString());
                Console.ReadKey();
            }
        }
    }
}
