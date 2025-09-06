# 🚀 Shinobi.WebSockets — Future Roadmap

This document tracks potential future enhancements for Shinobi.WebSockets. The core WebSocket server and client functionality is already complete.

---

## 🔄 Potential Future Enhancements

### Developer Experience

- [ ] More UnitTests (Current coverage: 60.3% lines, 62% branches)

  **High Priority - Low Coverage Components:** ✅ **COMPLETED**

  - [x] **Exception throwing scenarios** - Missing integration tests for:
    - [x] **InternalBufferOverflowException** - Test oversized frame scenarios (WebSocketFrameReader.cs) ✅
    - [x] HttpHeaderTooLargeException, WebSocketHandshakeFailedException, SecWebSocketKeyMissingException, WebSocketVersionNotSupportedException, InvalidHttpResponseCodeException (well tested) ✅
  - [x] **WebSocketFrameCommon** (70% line, 68.7% branch coverage) - Frame validation edge cases and malformed data handling ✅ (Note: ToggleMask32Bit path untestable on 64-bit systems)
  - [x] **Internal.Events** (37.1% line, 16.2% branch coverage) - Logging events with reasonable coverage ✅ (Most uncovered events are error conditions/edge cases that require fault injection or integration testing)

  **Medium Priority - Moderate Coverage:**

  - [x] **HttpRequestBuilder** (95% line, 82.1% branch coverage) - Header validation, malformed request handling ✅
  - [x] **HttpResponse** (95.3% line, 87.8% branch coverage) - Response construction edge cases ✅
  - [x] **HttpHeaderExtensions** (100% line, 100% branch coverage) - Header parsing edge cases ✅
  - [x] **BinaryReaderWriterExtensions** (100% line, 100% branch coverage) - Endian conversion edge cases, buffer underrun scenarios ✅

  **Integration & Scenarios:**

  - [ ] **Authentication flow integration** - End-to-end auth scenarios with various failure modes
  - [ ] **Error handling paths** - Network failures, protocol violations, resource exhaustion
  - [ ] **Multi-threading scenarios** - Concurrent read/write operations and thread safety
  - [ ] **Frame handling edge cases** - Malformed frames, oversized payloads, fragmentation limits
  - [ ] **Compression scenarios** - Deflate/inflate error conditions and memory pressure

- [ ] More comprehensive benchmarking suite
- [ ] Performance comparison tools vs other WebSocket libraries

---

## 💡 Community Requested Features

_Add features requested by users here_
