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
        private static async Task Main(string[] _)
        {
            var loggerFactory = LoggerFactory.Create(builder => builder
                    .SetMinimumLevel(LogLevel.Trace)
                    .AddConsole());

            var logger = loggerFactory.CreateLogger<Program>();

            Samurai.WebSockets.Internal.Events.Log
                = new Samurai.WebSockets.Internal.Events(loggerFactory.CreateLogger<Samurai.WebSockets.Internal.Events>());

            ushort port = 27416;
            var server = new SamuraiServer(loggerFactory.CreateLogger<SamuraiServer>(), port);
            await server.StartAsync();
            // var webSocketServerFactory = new WebSocketServerFactory();
            //await StartWebServerAsync(port, logger, loggerFactory, webSocketServerFactory).ConfigureAwait(false);
            logger.LogInformation("Server stopped. Press ENTER to exit.");
            Console.ReadLine();
            await server.StopAsync();
        }

        private static async Task StartWebServerAsync(
            ushort port,
            ILogger logger,
            ILoggerFactory loggerFactory,
            IWebSocketServerFactory webSocketServerFactory)
        {
            try
            {
                IList<string> supportedSubProtocols = ["chatV1", "chatV2", "chatV3"];
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
