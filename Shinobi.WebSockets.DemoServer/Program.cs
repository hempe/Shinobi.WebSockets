using System;
using System.Reflection;
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
        private const string DEMO_TOKEN = "demo-token-12345";

        private static async Task Main(string[] args)
        {
            var loggerFactory = LoggerFactory.Create(builder => builder
                .SetMinimumLevel(LogLevel.Debug)
                .AddConsole());

            var logger = loggerFactory.CreateLogger<WebSocketServer>();

            logger.LogInformation("Starting WebSocket Demo Server...");
            logger.LogInformation("Features included:");
            logger.LogInformation("- Echo messages");
            logger.LogInformation("- Connection logging");
            logger.LogInformation("- SSL support with dev certificate");
            logger.LogInformation("- Connection limit (10 max)");

            try
            {
                var assembly = Assembly.GetExecutingAssembly();

                // Create the WebSocket server with multiple features
                var server = WebSocketServerBuilder.Create()
#if NET8_0_OR_GREATER
                    .UsePerMessageDeflate(x =>
                    {
                        x.ServerContextTakeover = ContextTakeoverMode.ForceDisabled;
                        x.ClientContextTakeover = ContextTakeoverMode.ForceDisabled;
                    })
#endif
                    .UsePort(8080)
                    .UseDevCertificate()         // Enable SSL with ASP.NET Core dev cert
                    .UseLogging(loggerFactory)   // Log connections/disconnections/errors
                    .UseCors("*")                // Allow all origins
                    .OnConnect(async (webSocket, next, cancellationToken) =>
                    {
                        // Authentication is now handled in OnHandshake - this only runs for authenticated connections
                        var authMethod = !string.IsNullOrEmpty(webSocket.SubProtocol) && webSocket.SubProtocol!.StartsWith("Authorization:")
                            ? "Authorization subprotocol"
                            : "Authorization header";

                        logger.LogInformation("New client connected: {ConnectionId} (auth: {AuthMethod})", webSocket.Context.Guid, authMethod);
                        await webSocket.SendTextAsync("Welcome to the WebSocket Demo Server!", cancellationToken);
                        await webSocket.SendTextAsync("Available commands:", cancellationToken);
                        await webSocket.SendTextAsync("- time: Get server time", cancellationToken);
                        await webSocket.SendTextAsync("- help: Show this help", cancellationToken);
                        await webSocket.SendTextAsync("- Any other text will be echoed back", cancellationToken);

                        await next(webSocket, cancellationToken);
                    })
                    .OnClose((webSocket, closeStatus, statusDescription, next, cancellationToken) =>
                    {
                        logger.LogInformation("Client disconnected: {ConnectionId}: {StatusDescription}", webSocket.Context.Guid, statusDescription);
                        return next(webSocket, closeStatus, statusDescription, cancellationToken);
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
                    .OnHandshake(async (context, next, cancellationToken) =>
                    {
                        // This is not a hardened web server, but for testing this seem fine:
                        if (!context.IsWebSocketRequest)
                        {
                            if (context.Path == "/")
                                return context.HttpRequest.CreateEmbeddedResourceResponse(assembly, "Shinobi.WebSockets.DemoServer.Client.html");

                            if (context.Path == "/favicon.ico")
                                return context.HttpRequest.CreateEmbeddedResourceResponse(assembly, "Shinobi.WebSockets.DemoServer.favicon.ico");
                        }

                        // Handle authentication during handshake
                        var isAuthenticated = false;
                        string? selectedProtocol = null;

                        // Check Authorization header (C# clients)
                        var authHeader = context.HttpRequest?.GetHeaderValue("Authorization");
                        if (!string.IsNullOrEmpty(authHeader) && authHeader!.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        {
                            var token = authHeader.Substring("Bearer ".Length);
                            if (ValidateToken(token))
                            {
                                isAuthenticated = true;
                                logger.LogInformation("Valid Authorization header provided: {ConnectionId}", context.Guid);
                            }
                        }

                        // Check Authorization:[Token] subprotocol (JS clients)
                        if (!isAuthenticated)
                        {
                            var requestedProtocols = context.WebSocketRequestedProtocols;
                            foreach (var protocol in requestedProtocols)
                            {
                                if (protocol.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase))
                                {
                                    var token = protocol.Substring("Authorization:".Length);
                                    if (ValidateToken(token))
                                    {
                                        selectedProtocol = protocol;
                                        isAuthenticated = true;
                                        logger.LogInformation("Valid Authorization subprotocol provided: {ConnectionId}", context.Guid);
                                        break;
                                    }
                                }
                            }
                        }

                        if (!isAuthenticated)
                        {
                            logger.LogWarning("Unauthorized handshake attempt: {ConnectionId}", context.Guid);
                            return HttpResponse.Create(401)
                                .AddHeader("Connection", "close")
                                .AddHeader("Content-Type", "text/plain")
                                .WithBody("Authentication required. Use Authorization header or 'Authorization:[token]' subprotocol");
                        }

                        logger.LogInformation("Path: {Path}", context.Path);
                        var response = await next(context, cancellationToken);

                        // Add the selected Authorization protocol to response
                        if (!string.IsNullOrEmpty(selectedProtocol))
                        {
                            response = response.AddHeader("Sec-WebSocket-Protocol", selectedProtocol!);
                        }

                        return response;
                    })
                    .Build();

                // Start the server
                await server.StartAsync();

                logger.LogInformation("WebSocket server started successfully!");
                logger.LogInformation("HTTPS WebSocket URL: wss://localhost:8080");
                logger.LogInformation("Test with the demo client, launch https://localhost:8080");

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

        private static bool ValidateToken(string token)
        {
            // Demo validation - in real apps, validate JWT, check database, etc.
            if (string.IsNullOrEmpty(token))
                return false;

            // For demo: accept the hardcoded token or validate JWT-like structure
            if (token == DEMO_TOKEN)
                return true;

            // Example: Basic JWT-like validation (check format)
            var parts = token.Split('.');
            if (parts.Length == 3)
            {
                // Could decode and validate JWT claims here
                // For demo, just check if it looks like a JWT
                return parts[0].Length > 0 && parts[1].Length > 0 && parts[2].Length > 0;
            }

            return false;
        }
    }
}
