# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
# Build entire solution
dotnet build Shinobi.WebSockets.sln

# Build specific project
dotnet build Shinobi.WebSockets/Shinobi.WebSockets.csproj

# Build for specific target framework
dotnet build -f netstandard2.0
dotnet build -f net9

# Build in Release mode
dotnet build -c Release

# Build signed release (for publishing)
dotnet build -c ReleaseSigned
```

## Test Commands

```bash
# Run all unit tests
dotnet test Shinobi.WebSockets.UnitTests/Shinobi.WebSockets.UnitTests.csproj --framework net9.0

# Run tests with verbosity
dotnet test -v normal --framework net9.0

# Run specific test class
dotnet test --filter "FullyQualifiedName~HttpHeaderParserTests" --framework net9.0

# Run tests on Linux runner (alternative)
dotnet run --project Shinobi.WebSockets.UnitTests.RunnerLinux/Shinobi.WebSockets.UnitTests.RunnerLinux.csproj
```

## Benchmarking

```bash
# Run performance benchmarks
dotnet run --project Shinobi.WebSockets.Benchmark/Shinobi.WebSockets.Benchmark.csproj -c Release

# Results are saved to BenchmarkDotNet.Artifacts/results/
```

## Demo Applications

```bash
# Run demo server (serves WebSocket endpoint and test client)
dotnet run --project Shinobi.WebSockets.DemoServer/Shinobi.WebSockets.DemoServer.csproj

# Run demo client (connects to server)
dotnet run --project c/Shinobi.WebSockets.DemoClient.csproj
```

## Code Architecture

### Core Library Structure
- **Shinobi.WebSockets/** - Main library implementing .NET Standard 2.0 WebSocket abstract class
- **ShinobiWebSocket** - Primary WebSocket implementation extending System.Net.WebSockets.WebSocket
- **ShinobiServer** - High-level server for accepting WebSocket connections with TLS support
- **WebSocketClientFactory** - Factory for creating client WebSocket connections

### Key Components
- **Internal/WebSocketFrame*** - Low-level frame handling (reader, writer, common operations)
- **Internal/WebSocketDeflater/Inflater** - Per-message deflate compression support
- **Extensions/** - Extension methods for HTTP headers, WebSocket operations
- **HttpRequest/Response builders** - HTTP handshake handling
- **WebSocketHttpContext** - Context object for WebSocket upgrade requests

### Multi-Target Support
The library targets both .NET Standard 2.0 and .NET 9, with compatibility back to .NET Framework 4.7.2. Uses conditional compilation for signed releases via RELEASESIGNED define.

### Testing Structure
- **Shinobi.WebSockets.UnitTests/** - xUnit test suite with comprehensive WebSocket protocol tests
- **TheInternet.cs/TheInternetTests.cs** - Integration tests against real WebSocket endpoints
- **MockNetworkStream.cs** - Test infrastructure for network stream mocking

### Performance Focus
Extensive benchmarking infrastructure using BenchmarkDotNet to measure throughput and performance across different scenarios. Results are automatically generated in multiple formats (HTML, CSV, Markdown).

## Project Dependencies

The main library has minimal dependencies:
- System.Buffers (for high-performance buffer management)
- Microsoft.Extensions.Logging.Abstractions (for logging)
- Uses ArrayPool&lt;byte&gt; extensively for memory efficiency