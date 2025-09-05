# 🚀 Shinobi.WebSockets — Future Roadmap

This document tracks potential future enhancements for Shinobi.WebSockets. The core WebSocket server and client functionality is already complete.

---

## 🔄 Potential Future Enhancements

### Developer Experience

- [ ] More UnitTests (Current coverage: 65.2% lines, 59.3% branches)

  **High Priority - Low Coverage Components:**

  - [ ] **Exception throwing scenarios** - Missing integration tests for:
    - ✅ **HttpHeaderTooLargeException** - Test actual large header scenario (HttpHeader.cs:145) 
    - [ ] **WebSocketHandshakeFailedException** - Test handshake failure scenarios (WebSocketClient.cs:483,511)
    - [ ] **InternalBufferOverflowException** - Test oversized frame scenarios (WebSocketFrameReader.cs)
    - ✅ SecWebSocketKeyMissingException, WebSocketVersionNotSupportedException, InvalidHttpResponseCodeException (well tested)
  - [ ] **WebSocketServerBuilderExtensions** (51.2% coverage) - Certificate loading, SSL configuration edge cases
  - [ ] **WebSocketFrameCommon** (70% coverage) - Frame validation edge cases and malformed data handling
  - [ ] **Internal.Events** (40.8% coverage) - Logging event scenarios and parameter validation

  **Medium Priority - Moderate Coverage:**

  - [ ] **HttpRequestBuilder** (76.6% coverage) - Header validation, malformed request handling
  - [ ] **HttpResponse** (68.5% coverage) - Response construction edge cases
  - [ ] **HttpHeaderExtensions** (77.1% coverage) - Header parsing edge cases
  - [ ] **BinaryReaderWriterExtensions** (77.2% coverage) - Endian conversion edge cases

  **Integration & Scenarios:**

  - [ ] **Authentication flow integration** - End-to-end auth scenarios with various failure modes
  - [ ] **Error handling paths** - Network failures, protocol violations, resource exhaustion
  - [ ] **Multi-threading scenarios** - Concurrent read/write operations and thread safety
  - [ ] **Frame handling edge cases** - Malformed frames, oversized payloads, fragmentation limits
  - [ ] **Compression scenarios** - Deflate/inflate error conditions and memory pressure

- [ ] **Code cleanup** - Remove unused exception constructors (parameterless constructors and some Exception inner overloads)
- [ ] More comprehensive benchmarking suite
- [ ] Performance comparison tools vs other WebSocket libraries

---

## 💡 Community Requested Features

_Add features requested by users here_
