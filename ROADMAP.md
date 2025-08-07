# 🧠 WebSocket Server Refactor — TODO List

This document tracks implementation steps for building a flexible, interceptor-based WebSocket server in C#, inspired by the refactoring of Ninja.WebSockets.

---

## ✅ Phase 1: Low-Level Core Infrastructure

> Build foundational TCP, TLS, and HTTP parsing layers.

- [ ] Accept TCP connections via `TcpListener`
- [ ] Add optional TLS support (via `SslStream`)
- [ ] Implement `OnCertificateSelection` delegate
- [ ] Parse HTTP request from stream (`HttpRequest` abstraction)
- [ ] Validate WebSocket upgrade per RFC 6455
- [ ] Send `101 Switching Protocols` response
- [ ] Upgrade connection to `WebSocket`

---

## ✅ Phase 2: Interceptor Model & Builder

> Define extensibility hooks for all major lifecycle phases.

- [ ] Define delegate types:
  - `OnConnectionAcceptedDelegate`
  - `OnHandshakeDelegate`
  - `OnWebSocketAcceptedDelegate`
  - `OnMessageReceivedDelegate`
  - `OnMessageSendingDelegate`
  - `OnConnectionClosedDelegate`
  - `OnClientCertificateValidationDelegate`
  - `OnErrorDelegate`
- [ ] Implement `WebSocketServerBuilder`
  - Register multiple interceptors per type
  - Build middleware chains (`next` pattern)
- [ ] Create `WebSocketConnection` abstraction
  - Expose `WebSocket`, `TcpClient`, connection ID
  - Include `Metadata` dictionary
- [ ] Implement interceptor pipeline chaining
  - Support short-circuiting and error catching

---

## ✅ Phase 3: Connection & Lifecycle Management

> Manage connected clients and lifecycle events.

- [ ] Implement `IConnectionManager` interface
  - Add, remove, enumerate connections
- [ ] Provide default `ConnectionManager`
- [ ] Call `OnWebSocketAccepted` after successful upgrade
- [ ] Call `OnConnectionClosed` on graceful or abrupt disconnect
- [ ] Expose connection metadata (e.g., tags, userId)

---

## ✅ Phase 4: Message Handling

> Support receiving and sending messages via interceptors.

- [ ] Implement `OnMessageReceived` pipeline
  - Pass text/binary messages to interceptors
  - Allow message mutation or cancellation
- [ ] Implement `OnMessageSending` pipeline
- [ ] Add `SendAsync` and `BroadcastAsync` helpers
  - Integrate with connection manager
  - Respect `OnMessageSending`

---

## ✅ Phase 5: Error Handling

> Centralize error handling across lifecycle phases.

- [ ] Implement `OnError` delegate
- [ ] Catch and route all internal exceptions through `OnError`
- [ ] Add error context (e.g., phase: "handshake", "receive", etc.)

---

## ✅ Phase 6: Optional Features

> Extra capabilities to add later if needed.

- [ ] Support `Sec-WebSocket-Protocol` negotiation
- [ ] Add idle timeout / ping-pong support
- [ ] Add metrics (connections, messages/sec, etc.)
- [ ] Support Redis pub/sub or external broadcaster
  - Optional for multi-node scale-out

---

## ✅ Example Usage Target

```csharp
var builder = new WebSocketServerBuilder();

builder
    .OnHandshake((req, res, next) => { ... })
    .OnWebSocketAccepted(conn => { ... })
    .OnMessageReceived((conn, msg, next) => { ... });

var server = builder.Build();
await server.StartAsync(port: 8080);
```
