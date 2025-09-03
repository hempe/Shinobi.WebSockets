<p align="center">
  <img src="icon.png" width="96" height="96" alt="Shinobi.WebSockets Logo">
</p>

# Shinobi.WebSockets

## Background

This project originated from using **Ninja WebSocket** for local communication scenarios rather than server deployments. For server-side websocket communication, I recommend using **ASP.NET Core WebSockets** or **SignalR** instead.

The typical use case is a small Windows service that local user interfaces or lightweight tools connect to.

While working with Ninja WebSocket, I found a bug, fixed it, and took the opportunity to update and refactor the project. The goal was to simplify its usage, add better logging capabilities, and improve expandability and performance.

## Introduction

A modern, flexible, and extensible WebSocket server library for .NET, built on top of `System.Net.WebSockets` and designed for high-performance, feature-rich test or local applications.

Shinobi.WebSockets lets you build WebSocket servers with a fluent builder API, optional compression, CORS, SSL/TLS, authentication, and custom event hooks.

> **Origin:** This library is a **complete refactor and extension** of [Ninja.WebSockets](https://github.com/ninjasource/Ninja.WebSockets) by David Haig, updated with modern APIs (`ValueTask`, `CancellationToken`, `Shared Buffers`), improved extensibility, and new features.

## Features

- Fully asynchronous WebSocket server implementation
- Fluent `WebSocketServerBuilder` configuration API
- Per-message Deflate compression (RFC 7692)
- SSL/TLS support (including ASP.NET Core dev certs)
- CORS support
- Authentication hooks
- HTTP keep-alive support with timeout and connection limits
- Connection, message, and error event interceptors
- Built-in logging integration with `Microsoft.Extensions.Logging`
- `CancellationToken` and `ValueTask` support

---

## Quick Start – Server Example

```csharp
using Microsoft.Extensions.Logging;
using Shinobi.WebSockets;
using Shinobi.WebSockets.Builders;
using Shinobi.WebSockets.Extensions;

var loggerFactory = LoggerFactory.Create(builder => builder
    .SetMinimumLevel(LogLevel.Information)
    .AddConsole());

var server = WebSocketServerBuilder.Create()
    .UsePort(8080)
    .UseDevCertificate()
    .UseLogging(loggerFactory)
    .UsePerMessageDeflate(x =>
    {
        x.ServerContextTakeover = ContextTakeoverMode.ForceDisabled;
        x.ClientContextTakeover = ContextTakeoverMode.ForceDisabled;
    })
    .OnConnect(async (webSocket, next, token) =>
    {
        var logger = loggerFactory.CreateLogger("Server");
        logger.LogInformation("Client connected: {Id}", webSocket.Context.Guid);
        await webSocket.SendTextAsync("Welcome to Shinobi.WebSockets!", token);
        await next(webSocket, token);
    })
    .OnTextMessage((webSocket, message, token) =>
    {
        return webSocket.SendTextAsync($"ECHO: {message}", token);
    })
    .Build();

await server.StartAsync();

Console.WriteLine("Server running on wss://localhost:8080");
Console.ReadLine();
await server.StopAsync();
```

## Installation

Install the package via NuGet Package Manager:

```bash
dotnet add package Shinobi.WebSockets
```

Or via the Package Manager Console in Visual Studio:

```powershell
Install-Package Shinobi.WebSockets
```

You can also add it directly to your project file:

```xml
<PackageReference Include="Shinobi.WebSockets" Version="1.0.0" />
```

Note: Replace "1.0.0" with the latest version number available on NuGet.

---

## Authentication

Shinobi.WebSockets supports two authentication approaches to work with both C# and JavaScript clients:

### Authorization Headers (C# Clients)

C# clients can use standard HTTP Authorization headers:

```csharp
var client = WebSocketClientBuilder.Create()
    .AddHeader("Authorization", "Bearer your-token-here")
    .Build();
```

### Authorization Subprotocols (JavaScript Clients)

JavaScript clients cannot set custom headers, so use the `Authorization:[Token]` subprotocol pattern:

```javascript
// JavaScript WebSocket with auth subprotocol
const ws = new WebSocket('wss://localhost:8080', ['Authorization:your-jwt-token-here']);
```

### Server-Side Authentication

Configure the server to validate tokens from both headers and subprotocols:

```csharp
var server = WebSocketServerBuilder.Create()
    .OnHandshake(async (context, next, cancellationToken) =>
    {
        var isAuthenticated = false;
        string? selectedProtocol = null;
        
        // Check Authorization header (C# clients)
        var authHeader = context.HttpRequest?.GetHeaderValue("Authorization");
        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
        {
            var token = authHeader.Substring("Bearer ".Length);
            if (await ValidateTokenAsync(token))
            {
                isAuthenticated = true;
            }
        }
        
        // Check Authorization:[Token] subprotocol (JS clients)
        if (!isAuthenticated)
        {
            foreach (var protocol in context.WebSocketRequestedProtocols)
            {
                if (protocol.StartsWith("Authorization:"))
                {
                    var token = protocol.Substring("Authorization:".Length);
                    if (await ValidateTokenAsync(token))
                    {
                        selectedProtocol = protocol;
                        isAuthenticated = true;
                        break;
                    }
                }
            }
        }
        
        if (!isAuthenticated)
        {
            return HttpResponse.Create(401)
                .AddHeader("Connection", "close")
                .WithBody("Authentication required");
        }
        
        var response = await next(context, cancellationToken);
        
        // Echo back the selected Authorization subprotocol
        if (!string.IsNullOrEmpty(selectedProtocol))
        {
            response = response.AddHeader("Sec-WebSocket-Protocol", selectedProtocol);
        }
        
        return response;
    })
    .Build();

// Example token validation
private static async Task<bool> ValidateTokenAsync(string token)
{
    // Validate JWT, check database, verify claims, etc.
    // For demo: accept hardcoded token or JWT format
    if (token == "demo-token-12345") return true;
    
    // Example JWT validation
    var parts = token.Split('.');
    if (parts.Length == 3)
    {
        // Could decode and validate JWT claims, expiration, signature
        return true; // Simplified for demo
    }
    
    return false;
}
```

---

## HTTP Keep-Alive Configuration

Shinobi.WebSockets supports HTTP keep-alive connections for better performance when clients make multiple HTTP requests before upgrading to WebSocket. You can configure timeout and connection limits to prevent resource exhaustion:

```csharp
var server = WebSocketServerBuilder.Create()
    .UseKeepAliveTimeout(TimeSpan.FromSeconds(30))  // Close idle connections after 30s
    .UseMaxKeepAliveConnections(100)                // Limit to 100 concurrent keep-alive connections
    .OnHandshake(async (context, next, cancellationToken) =>
    {
        // Your handshake logic
        return await next(context, cancellationToken);
    })
    .Build();
```

### Keep-Alive Security Features

- **Timeout Protection**: Idle keep-alive connections are automatically closed after the configured timeout
- **Connection Limits**: When the limit is reached, the oldest idle connection is evicted (LRU)
- **WebSocket Transition**: Once a connection upgrades to WebSocket, it's no longer counted as keep-alive
- **Resource Management**: Proper cleanup ensures connection counters remain accurate

### Configuration Options

- `KeepAliveTimeout` (default: 30 seconds) - Time before idle connections are closed
- `MaxKeepAliveConnections` (default: 1000) - Maximum concurrent keep-alive connections
- Set `KeepAliveTimeout` to `TimeSpan.Zero` to disable timeout
- Set `MaxKeepAliveConnections` to `0` for unlimited connections

---

## WebSocket Client Usage

> **Note:** WebSocketClientFactory has been removed. All connection logic is now integrated directly into WebSocketClient/WebSocketClientBuilder.

### Basic Usage

```csharp
using var client = WebSocketClientBuilder.Create()
    .OnTextMessage((ws, message, ct) =>
    {
        Console.WriteLine($"Received: {message}");
        return default(ValueTask);
    })
    .Build();

// Start connection
await client.StartAsync(new Uri("ws://localhost:8080"), cancellationToken);

// Send messages using the new API
await client.SendTextAsync("Hello WebSocket!", cancellationToken);
await client.SendBinaryAsync(new byte[] { 1, 2, 3, 4, 5 }, cancellationToken);

// Check connection state
Console.WriteLine($"Connection state: {client.ConnectionState}");

// Stop connection
await client.StopAsync();
```

### Auto-Reconnect with Default Settings

```csharp
using var client = WebSocketClientBuilder.Create()
    .UseReliableConnection() // Enables auto-reconnect with sensible defaults
    .OnTextMessage((message, ct) => // Simplified handler
    {
        Console.WriteLine($"Received: {message}");
        return default(ValueTask);
    })
    .Build();

await client.StartAsync(uri);
// Client will automatically reconnect if connection is lost
```

### Custom Auto-Reconnect Configuration

```csharp
using var client = WebSocketClientBuilder.Create()
    .UseAutoReconnect(options =>
    {
        options.InitialDelay = TimeSpan.FromSeconds(2);
        options.MaxDelay = TimeSpan.FromMinutes(1);
        options.BackoffMultiplier = 2.0;
        options.Jitter = 0.1; // Add 10% random jitter
    })
    .Build();
```

### URL Failover During Reconnection

```csharp
var fallbackUrls = new[]
{
    new Uri("ws://backup1.example.com/websocket"),
    new Uri("ws://backup2.example.com/websocket"),
    new Uri("ws://backup3.example.com/websocket")
};

using var client = WebSocketClientBuilder.Create()
    .EnableAutoReconnect()
    .UseFallbackUrls(fallbackUrls)
    .OnReconnecting(async (currentUri, attemptNumber, ct) =>
    {
        Console.WriteLine($"Reconnecting to {currentUri} (attempt {attemptNumber})");
        // You can modify the URI here for custom logic
        return currentUri;
    })
    .Build();
```

### Connection State Monitoring

```csharp
using var client = WebSocketClientBuilder.Create()
    .UseReliableConnection()
    .Build();

client.ConnectionStateChanged += (client, e) =>
{
    Console.WriteLine($"Connection state changed: {e.PreviousState} -> {e.NewState}");
    if (e.Exception != null)
    {
        Console.WriteLine($"Error: {e.Exception.Message}");
    }
};

client.Reconnecting += (client, e) =>
{
    Console.WriteLine($"Reconnecting to {e.CurrentUri} in {e.Delay} (attempt {e.AttemptNumber})");
};

// Comprehensive logging is automatically included when you configure a logger
using var loggerFactory = LoggerFactory.Create(builder => builder
    .SetMinimumLevel(LogLevel.Information) // Set to Debug for more detailed logs
    .AddConsole());

using var client = WebSocketClientBuilder.Create()
    .UseLogging(loggerFactory) // This enables all reconnect logging
    .UseReliableConnection()
    .Build();
```

### Advanced Client Configuration

```csharp
using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

using var client = WebSocketClientBuilder.Create()
    .UseKeepAlive(TimeSpan.FromSeconds(30))
    .UseNoDelay(true)
    .UsePerMessageDeflate()
    .AddHeader("Authorization", "Bearer your-token")
    .UseLogging(loggerFactory)
    .UseExponentialBackoff(
        initialDelay: TimeSpan.FromSeconds(1),
        maxDelay: TimeSpan.FromSeconds(30),
        multiplier: 2.0,
        jitter: 0.1)
    .OnConnect(async (ws, next, ct) =>
    {
        Console.WriteLine("Connected!");
        await next(ws, ct);
    })
    .OnClose(async (ws, next, ct) =>
    {
        Console.WriteLine("Connection closed");
        await next(ws, ct);
    })
    .OnError(async (ws, ex, next, ct) =>
    {
        Console.WriteLine($"Error: {ex.Message}");
        await next(ws, ex, ct);
    })
    .OnTextMessage((ws, message, ct) =>
    {
        Console.WriteLine($"Text: {message}");
        return default(ValueTask);
    })
    .OnBinaryMessage((ws, data, ct) =>
    {
        Console.WriteLine($"Binary: {data.Length} bytes");
        return default(ValueTask);
    })
    .Build();
```

### Connection States

The `ConnectionState` property provides real-time connection status:

- `Disconnected` - Not connected
- `Connecting` - Establishing connection
- `Connected` - Successfully connected
- `Reconnecting` - Attempting to reconnect
- `Disconnecting` - Shutting down connection
- `Failed` - Connection failed (after max attempts if configured)

### Reconnect Logging

When you configure logging with `UseLogging()`, the WebSocketClient automatically logs comprehensive information about reconnection attempts:

#### Information Level Logs

- **Connection closed**: "WebSocket connection closed, checking reconnect options"
- **Auto-reconnect disabled**: "Auto-reconnect is disabled, staying disconnected"
- **Starting reconnect**: "Starting auto-reconnect sequence (attempt N)"
- **Reconnect attempt**: "Reconnecting to ws://example.com in 2000ms (attempt 3)"
- **Successful reconnect**: "Successfully reconnected to ws://example.com after 3 attempts"
- **Reconnection cancelled**: "WebSocket reconnection cancelled"

#### Warning Level Logs

- **Max attempts exceeded**: "Maximum reconnect attempts (5) exceeded"

#### Error Level Logs

- **Connection errors**: "Connection error (attempt 2)" with full exception details

#### Debug Level Logs

- **State changes**: "WebSocket connection state changed from Connected to Reconnecting"
- **OnReconnecting handler**: "Calling OnReconnecting handler for attempt 2"
- **URI changes**: "OnReconnecting handler changed URI from ws://primary.com to ws://backup.com"

#### Example Log Output

```
info: WebSocket connection closed, checking reconnect options
info: Starting auto-reconnect sequence (attempt 1)
info: Reconnecting to ws://localhost:8080 in 1000ms (attempt 1)
dbug: WebSocket connection state changed from Reconnecting to Connecting
info: Successfully reconnected to ws://localhost:8080 after 1 attempts
```

---

## Migration from WebSocketClientFactory

### Before (Old Factory Pattern)

```csharp
var factory = new WebSocketClientFactory();
var webSocket = await factory.ConnectAsync(uri, options);
await webSocket.SendAsync(buffer, WebSocketMessageType.Text, true, ct);
webSocket.Dispose();
```

### After (New Builder Pattern)

```csharp
using var client = WebSocketClientBuilder.Create().Build();
await client.StartAsync(uri);
await client.SendTextAsync(message, ct);
await client.StopAsync();
// Disposal is handled by using statement
```

### Key API Changes

#### Before (WebSocketClientFactory Pattern)

```csharp
var factory = new WebSocketClientFactory();
var webSocket = await factory.ConnectAsync(uri, options);
var buffer = Encoding.UTF8.GetBytes("Hello");
await webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, ct);
webSocket.Dispose();
```

#### After (New WebSocketClient Pattern)

```csharp
var client = WebSocketClientBuilder.Create().Build();
await client.StartAsync(uri);
await client.SendTextAsync("Hello", ct);
// client.ConnectionState property available
// Auto-reconnect support built-in
await client.StopAsync(); // or dispose client
```

---

## Demo Applications

### Demo Server

A full server example is included in the Shinobi.WebSockets.DemoServer project:

- Echoes text messages
- Supports time and help commands
- Logs connections and disconnections
- Supports SSL/TLS with dev certificate
- Demonstrates per-message deflate compression

Run it with:

```bash
dotnet run --project Shinobi.WebSockets.DemoServer
```

### Demo Client

A comprehensive console-based client demo is included in the Shinobi.WebSockets.DemoClient project that showcases all WebSocket client features:

- Interactive command-line interface
- Connection management (connect/disconnect)
- Text and binary message sending
- Built-in stress testing capabilities
- Auto-reconnect demonstration
- Real-time connection state monitoring
- Connection statistics and timing

Run it with:

```bash
dotnet run --project Shinobi.WebSockets.DemoClient
```

#### Demo Client Commands

Once running, the demo client supports these interactive commands:

```
connect [url]     - Connect to WebSocket server (default: wss://localhost:8080)
disconnect        - Disconnect from server
send <message>    - Send a text message
binary <message>  - Send a binary message
ping              - Send ping command
time              - Send time command
serverhelp        - Send help command to server
stress [count]    - Run stress test (default: 1000 messages)
stopstress        - Stop running stress test
reconnect         - Enable auto-reconnect features
stats             - Show connection statistics
status            - Show connection status
clear             - Clear the console
help/?            - Show command help
quit/exit         - Exit the application
```

#### Running Both Demo Applications

1. Start the server in one terminal:

   ```bash
   dotnet run --project Shinobi.WebSockets.DemoServer
   ```

2. Start the client in another terminal:

   ```bash
   dotnet run --project Shinobi.WebSockets.DemoClient
   ```

3. In the client, type `connect` to connect to the demo server, then try various commands like:
   - `send Hello World!`
   - `time`
   - `stress 100`
   - `reconnect`

## License

This project is licensed under the MIT License – see the [LICENCE.md](https://github.com/hempe/Shinobi.WebSockets/blob/master/LICENCE.md) file for details.

## Credits

- Original work: [Ninja.WebSockets](https://github.com/ninjasource/Ninja.WebSockets) by David Haig
- Refactor, new features, and maintenance: Hempe
