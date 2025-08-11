using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Shinobi.WebSockets;
using Shinobi.WebSockets.Builders;
using Shinobi.WebSockets.Extensions;
using Shinobi.WebSockets.Http;

namespace WebSockets.DemoServer
{
    internal static class Program
    {
        private static async Task Main(string[] args)
        {
            var loggerFactory = LoggerFactory.Create(builder => builder
                .SetMinimumLevel(LogLevel.Warning)
                .AddConsole());

            var logger = loggerFactory.CreateLogger<ShinobiServer>();

            logger.LogInformation("Starting WebSocket Demo Server...");
            logger.LogInformation("Features included:");
            logger.LogInformation("- Echo messages");
            logger.LogInformation("- Connection logging");
            logger.LogInformation("- SSL support with dev certificate");
            logger.LogInformation("- Connection limit (10 max)");

            try
            {
                // Create the WebSocket server with multiple features
                var server = WebSocketBuilder.Create()
                    .UsePerMessageDeflate(x =>
                    {
                        x.ServerContextTakeover = ContextTakeoverMode.ForceDisabled;
                        x.ClientContextTakeover = ContextTakeoverMode.ForceDisabled;
                    })

                    .UsePort(8080)
                    .UseDevCertificate()         // Enable SSL with ASP.NET Core dev cert
                    .UseLogging(loggerFactory)   // Log connections/disconnections/errors
                    .UseCors("*")                // Allow all origins
                    .OnConnect(async (webSocket, next, cancellationToken) =>
                    {
                        logger.LogInformation("New client connected: {ConnectionId}", webSocket.Context.Guid);
                        await webSocket.SendTextAsync("Welcome to the WebSocket Demo Server!", cancellationToken);
                        await webSocket.SendTextAsync("Welcome to the WebSocket Demo Server!", cancellationToken);
                        await webSocket.SendTextAsync("Available commands:", cancellationToken);
                        await webSocket.SendTextAsync("- time: Get server time", cancellationToken);
                        await webSocket.SendTextAsync("- help: Show this help", cancellationToken);
                        await webSocket.SendTextAsync("- Any other text will be echoed back", cancellationToken);

                        await next(webSocket, cancellationToken);
                    })
                    .OnClose((webSocket, next, cancellationToken) =>
                    {
                        logger.LogInformation("Client disconnected: {ConnectionId}", webSocket.Context.Guid);
                        return next(webSocket, cancellationToken);
                    })
                    .OnTextMessage(async (webSocket, message, cancellationToken) =>
                    {
                        logger.LogInformation("Received from {ConnectionId}: {Message}", webSocket.Context.Guid, message);

                        // Handle special commands
                        switch (message.Trim().ToLower())
                        {
                            case "time":
                                await webSocket.SendTextAsync($"Server time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}", cancellationToken);
                                break;
                            case "help":
                                await webSocket.SendTextAsync("Available commands:", cancellationToken);
                                await webSocket.SendTextAsync("- time: Get server time", cancellationToken);
                                await webSocket.SendTextAsync("- help: Show this help", cancellationToken);
                                await webSocket.SendTextAsync("- Any other text will be echoed back", cancellationToken);
                                break;
                            default:
                                await webSocket.SendTextAsync($"ECHO: {message}", cancellationToken);
                                break;
                        }
                    })
                    .OnBinaryMessage((webSocket, bytes, cancellationToken) =>
                        webSocket.SendBinaryAsync(bytes, cancellationToken)
                    )
                    .OnHandshake((context, next, cancellationToken) =>
                    {
                        // This is not a harded web server, but for testing this seem fine:
                        if (!context.IsWebSocketRequest && context.Path == "/")
                        {
                            var assembly = Assembly.GetExecutingAssembly();
                            var resourceName = "Shinobi.WebSockets.DemoServer.Client.html";
                            using var stream = assembly.GetManifestResourceStream(resourceName) ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
                            using var reader = new StreamReader(stream);
                            var htmlContent = reader.ReadToEnd();

                            var response = HttpResponse.Create(200)
                                .AddHeader("Content-Type", "text/html; charset=utf-8")
                                .WithBody(htmlContent);

                            return new ValueTask<HttpResponse>(response);
                        }

                        logger.LogInformation("Path: {Path}", context.Path);
                        return next(context, cancellationToken);
                    })
                    .Build();

                // Start the server
                await server.StartAsync();

                logger.LogInformation("WebSocket server started successfully!");
                logger.LogInformation("HTTPS WebSocket URL: wss://localhost:8080");
                logger.LogInformation("Test with the demo client, lauche https://localhost:8080");

                Console.WriteLine("\nPress [ENTER] key to stop the server...");

                // Keep the server running until a key is pressed
                Console.ReadLine();

                logger.LogInformation("Shutting down server...");
                await server.StopAsync();
                logger.LogInformation("Server stopped.");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("development certificate"))
            {
                logger.LogError("SSL Certificate Error: {ErrorMessage}", ex.Message);
                logger.LogInformation("To fix this, run the following command: dotnet dev-certs https --trust");
                logger.LogInformation("Alternatively, you can run without SSL by removing the .UseDevCertificate() call.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error starting server: {ErrorMessage}", ex.Message);
            }
            finally
            {
                loggerFactory.Dispose();
            }
        }
    }
}