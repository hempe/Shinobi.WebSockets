using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Shinobi.WebSockets.Extensions;
using Shinobi.WebSockets.Internal;

using Xunit;

namespace Shinobi.WebSockets.UnitTests
{
    public class WebSocketExtensionsTests
    {
        private class TestWebSocket : WebSocket
        {
            public override WebSocketState State => WebSocketState.Open;
            public override WebSocketCloseStatus? CloseStatus => null;
            public override string? CloseStatusDescription => null;
            public override string? SubProtocol => null;

            public ArraySegment<byte>? LastSentBuffer { get; private set; }
            public WebSocketMessageType? LastMessageType { get; private set; }
            public bool? LastEndOfMessage { get; private set; }
            public CancellationToken? LastCancellationToken { get; private set; }

            public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
            {
                this.LastSentBuffer = buffer;
                this.LastMessageType = messageType;
                this.LastEndOfMessage = endOfMessage;
                this.LastCancellationToken = cancellationToken;
                return Task.CompletedTask;
            }

            public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
            {
                // For testing ReceiveAsync, we'll simulate receiving some data
                var testData = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }; // "Hello"
                testData.CopyTo(buffer.Array!, buffer.Offset);
                var result = new WebSocketReceiveResult(testData.Length, WebSocketMessageType.Binary, true);
                return Task.FromResult(result);
            }

            public override void Abort() { }
            public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
            public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
            public override void Dispose() { }
        }

        [Fact]
        public async Task SendTextAsync_WithValidMessage_ShouldSendUtf8EncodedTextAsync()
        {
            // Arrange
            var webSocket = new TestWebSocket();
            var message = "Hello WebSocket!";
            var expectedBytes = Encoding.UTF8.GetBytes(message);
            var cancellationToken = new CancellationToken();

            // Act
            await webSocket.SendTextAsync(message, cancellationToken);

            // Assert
            Assert.NotNull(webSocket.LastSentBuffer);
            Assert.Equal(expectedBytes, webSocket.LastSentBuffer.Value.ToArray());
            Assert.Equal(WebSocketMessageType.Text, webSocket.LastMessageType);
            Assert.True(webSocket.LastEndOfMessage);
            Assert.Equal(cancellationToken, webSocket.LastCancellationToken);
        }

        [Fact]
        public async Task SendTextAsync_WithUnicodeMessage_ShouldEncodeCorrectlyAsync()
        {
            // Arrange
            var webSocket = new TestWebSocket();
            var unicodeMessage = "Hello 🌟 WebSocket! 中文测试";
            var expectedBytes = Encoding.UTF8.GetBytes(unicodeMessage);

            // Act
            await webSocket.SendTextAsync(unicodeMessage);

            // Assert
            Assert.NotNull(webSocket.LastSentBuffer);
            Assert.Equal(expectedBytes, webSocket.LastSentBuffer.Value.ToArray());

            // Verify the encoded bytes can be decoded back to the original message
            var decodedMessage = Encoding.UTF8.GetString(webSocket.LastSentBuffer.Value.ToArray());
            Assert.Equal(unicodeMessage, decodedMessage);
        }

        [Fact]
        public async Task SendTextAsync_WithNullWebSocket_ShouldThrowArgumentNullExceptionAsync()
        {
            // Arrange
            WebSocket? nullWebSocket = null;
            var message = "test";

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => nullWebSocket.SendTextAsync(message).AsTask());

            Assert.Equal("webSocket", exception.ParamName);
        }

        [Fact]
        public async Task SendTextAsync_WithNullMessage_ShouldThrowArgumentNullExceptionAsync()
        {
            // Arrange
            var webSocket = new TestWebSocket();
            string? nullMessage = null;

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => webSocket.SendTextAsync(nullMessage!).AsTask());

            Assert.Equal("message", exception.ParamName);
        }

        [Fact]
        public async Task SendTextAsync_WithEmptyMessage_ShouldSendEmptyArrayAsync()
        {
            // Arrange
            var webSocket = new TestWebSocket();
            var emptyMessage = "";

            // Act
            await webSocket.SendTextAsync(emptyMessage);

            // Assert
            Assert.NotNull(webSocket.LastSentBuffer);
            Assert.Empty(webSocket.LastSentBuffer.Value.ToArray());
            Assert.Equal(WebSocketMessageType.Text, webSocket.LastMessageType);
            Assert.True(webSocket.LastEndOfMessage);
        }

        [Fact]
        public async Task SendBinaryAsync_WithValidData_ShouldSendBinaryMessageAsync()
        {
            // Arrange
            var webSocket = new TestWebSocket();
            var binaryData = new byte[] { 0x01, 0x02, 0x03, 0xFF, 0xAB };
            var cancellationToken = new CancellationToken();

            // Act
            await webSocket.SendBinaryAsync(binaryData, cancellationToken);

            // Assert
            Assert.NotNull(webSocket.LastSentBuffer);
            Assert.Equal(binaryData, webSocket.LastSentBuffer.Value.ToArray());
            Assert.Equal(WebSocketMessageType.Binary, webSocket.LastMessageType);
            Assert.True(webSocket.LastEndOfMessage);
            Assert.Equal(cancellationToken, webSocket.LastCancellationToken);
        }

        [Fact]
        public async Task SendBinaryAsync_WithNullWebSocket_ShouldThrowArgumentNullExceptionAsync()
        {
            // Arrange
            WebSocket? nullWebSocket = null;
            var data = new byte[] { 0x01, 0x02 };

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => nullWebSocket.SendBinaryAsync(data).AsTask());

            Assert.Equal("webSocket", exception.ParamName);
        }

        [Fact]
        public async Task SendBinaryAsync_WithNullData_ShouldThrowArgumentNullExceptionAsync()
        {
            // Arrange
            var webSocket = new TestWebSocket();
            byte[]? nullData = null;

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => webSocket.SendBinaryAsync(nullData!).AsTask());

            Assert.Equal("data", exception.ParamName);
        }

        [Fact]
        public async Task SendBinaryAsync_WithEmptyData_ShouldSendEmptyArrayAsync()
        {
            // Arrange
            var webSocket = new TestWebSocket();
            var emptyData = new byte[0];

            // Act
            await webSocket.SendBinaryAsync(emptyData);

            // Assert
            Assert.NotNull(webSocket.LastSentBuffer);
            Assert.Empty(webSocket.LastSentBuffer.Value.ToArray());
            Assert.Equal(WebSocketMessageType.Binary, webSocket.LastMessageType);
            Assert.True(webSocket.LastEndOfMessage);
        }

        [Fact]
        public async Task ReceiveAsync_WithValidWebSocket_ShouldReceiveDataIntoArrayPoolStreamAsync()
        {
            // Arrange
            var webSocket = new TestWebSocket();
            using var arrayPoolStream = new ArrayPoolStream();
            var cancellationToken = new CancellationToken();

            // Act
            var result = await webSocket.ReceiveAsync(arrayPoolStream, cancellationToken);

            // Assert - The TestWebSocket simulates receiving "Hello" (5 bytes)
            Assert.Equal(5, result.Count);
            Assert.Equal(WebSocketMessageType.Binary, result.MessageType);
            Assert.True(result.EndOfMessage);
            Assert.Equal(5, arrayPoolStream.Length);
            Assert.Equal(5, arrayPoolStream.Position);

            // Verify the data was written to the stream
            arrayPoolStream.Position = 0;
            var readBuffer = new byte[5];
#if NET8_0_OR_GREATER
            await arrayPoolStream.ReadExactlyAsync(readBuffer, 0, readBuffer.Length);
#else
            await arrayPoolStream.ReadAsync(readBuffer, 0, readBuffer.Length);
#endif


            var expectedData = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }; // "Hello"
            Assert.Equal(expectedData, readBuffer);
        }
    }
}