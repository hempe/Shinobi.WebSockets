# WebSocket Testing Roadmap

This document outlines additional testing opportunities to improve code coverage for the Shinobi.WebSockets library.

## 🎯 Current Status
- **Total Tests**: ~165+ (after adding exception and options tests)
- **Estimated Coverage**: ~70%
- **Status**: ✅ WebSocketClient ecosystem well-tested, ❌ WebSocketServer needs work

## 🍎 Low Hanging Fruit (Easy Wins)

### ✅ COMPLETED
- [x] **Exception Classes Tests** (`WebSocketExceptionTests.cs`) - 12 tests
- [x] **WebSocketServerOptions Tests** (`WebSocketServerOptionsTests.cs`) - 11 tests

### 🟡 EASY (30-60 minutes each)

#### **1. Enum/Constant Classes**
```csharp
// File: WebSocketEnumTests.cs
- MessageType enum (Text, Binary, Close, etc.)
- WebSocketOpCode enum (if exists)
- Any other enums/constants
```

#### **2. Extension Method Tests**
```csharp
// File: WebSocketExtensionTests.cs  
- WebSocketExtensions.cs methods
- WebSocketHttpContextExtensions.cs methods
- HttpHeaderExtensions.cs methods (if not covered)
```

#### **3. Builder Pattern Tests**
```csharp
// File: WebSocketServerBuilderTests.cs
- WebSocketServerBuilder fluent API
- WebSocketServerBuilderExtensions methods
- Configuration method chaining
```

## 🟡 Medium Effort (2-4 hours each)

### **4. Core WebSocket Frame Processing**
```csharp
// File: WebSocketFrameTests.cs
- WebSocketFrameReader parsing logic
- WebSocketFrameWriter creation logic  
- WebSocketFrameCommon utilities
- Frame validation and error cases
```

### **5. HTTP Processing Tests**
```csharp
// File: HttpRequestResponseTests.cs
- HttpRequest parsing and creation
- HttpResponse parsing and creation
- HttpHeader manipulation beyond current tests
```

### **6. WebSocket Context Tests**
```csharp
// File: WebSocketContextTests.cs
- WebSocketHttpContext creation and properties
- Context state management
- Context extension methods
```

## 🔴 High Effort (1-2 days each)

### **7. WebSocketServer Core Tests** ⭐ HIGH PRIORITY
```csharp
// File: WebSocketServerTests.cs
- Server startup/shutdown
- Port binding and configuration
- Connection acceptance (with mocks)
- Basic request routing
- Error handling scenarios
```

### **8. ShinobiWebSocket Core Tests** ⭐ HIGH PRIORITY  
```csharp
// File: ShinobiWebSocketTests.cs
- WebSocket state machine
- Send/receive message handling
- Connection lifecycle management
- Error handling and cleanup
```

### **9. Compression Component Tests**
```csharp
// File: WebSocketCompressionTests.cs
- WebSocketDeflater compression logic
- WebSocketInflater decompression logic  
- Per-message deflate scenarios
- Error handling for corrupt data
```

## 🚀 Integration & Performance Tests

### **10. End-to-End Server Tests**
```csharp
// File: WebSocketServerIntegrationTests.cs
- Full server lifecycle tests
- Client-server communication
- Multiple concurrent connections
- Real network scenarios (when needed)
```

### **11. Performance & Load Tests**
```csharp
// File: WebSocketPerformanceTests.cs
- Memory usage under load
- Connection handling capacity
- Message throughput testing
- Resource cleanup verification
```

## 📊 Coverage Goals

| Component | Current Coverage | Target Coverage | Priority |
|-----------|------------------|-----------------|----------|
| WebSocketClient* | ~85% | 90% | ✅ Done |
| WebSocketServer* | ~15% | 70% | 🔴 High |
| Core Engine | ~40% | 75% | 🟡 Medium |
| HTTP Processing | ~90% | 95% | 🟢 Low |
| Exceptions | ~0% | 95% | ✅ Done |
| Extensions | ~20% | 80% | 🟡 Medium |

## 🎯 Recommended Next Steps

1. **Immediate (this week)**: Add the "Easy" tests above (~2-3 hours total)
2. **Short-term (next sprint)**: Focus on WebSocketServer core tests 
3. **Medium-term**: Add frame processing and context tests
4. **Long-term**: Integration and performance test suite

## 📝 Notes

- Tests marked with ⭐ provide highest value for effort invested
- Focus on unit tests over integration tests for maintainability
- Consider using mocks for complex dependencies (network, file system)
- Prioritize edge cases and error scenarios for robustness