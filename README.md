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
- Fluent `WebSocketBuilder` configuration API
- Per-message Deflate compression (RFC 7692)
- SSL/TLS support (including ASP.NET Core dev certs)
- CORS support
- Authentication hooks
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

var server = WebSocketBuilder.Create()
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

(NuGet publishing instructions go here if you publish it)

## Demo Server

A full example is included in the WebSockets.DemoServer project:

- Echoes text messages
- Supports time and help commands
- Logs connections and disconnections
- Supports SSL/TLS with dev certificate
- Demonstrates per-message deflate compression

Run it with:

```bash
dotnet run --project WebSockets.DemoServer
```

## License

This project is licensed under the MIT License – see the [LICENSE.md](LICENSE.md) file for details.

## Credits

- Original work: [Ninja.WebSockets](https://github.com/ninjasource/Ninja.WebSockets) by David Haig
- Refactor, new features, and maintenance: Hempe
