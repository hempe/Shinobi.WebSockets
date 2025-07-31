using System;
using System.Net.Security;
using System.Net.WebSockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Samurai.WebSockets.Internal
{
    internal sealed class Events
    {
        public static Events Log { get; set; } = new Events(NullLogger<Events>.Instance);

        private readonly ILogger<Events> logger;

        public Events(ILogger<Events> logger)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void ClientConnectingToIpAddress(Guid guid, string ipAddress, int port)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("Client {Guid} connecting to IP {IpAddress}:{Port}", guid, ipAddress, port);
        }

        public void ClientConnectingToHost(Guid guid, string host, int port)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("Client {Guid} connecting to host {Host}:{Port}", guid, host, port);
        }

        public void AttemtingToSecureSslConnection(Guid guid)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("Client {Guid} attempting to secure SSL connection", guid);
        }

        public void ConnectionSecured(Guid guid)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("Client {Guid} SSL connection secured", guid);
        }

        public void ConnectionNotSecure(Guid guid)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("Client {Guid} SSL connection not secure", guid);
        }

        public void SslCertificateError(SslPolicyErrors sslPolicyErrors)
        {
            if (this.logger.IsEnabled(LogLevel.Error))
                this.logger.LogError("SSL certificate error: {Errors}", sslPolicyErrors);
        }

        public void HandshakeSent(Guid guid, string httpHeader)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("Client {Guid} sent handshake: {HttpHeader}", guid, httpHeader);
        }

        public void ReadingHttpResponse(Guid guid)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("Client {Guid} reading HTTP response", guid);
        }

        public void ReadHttpResponseError(Guid guid, Exception exception)
        {
            if (this.logger.IsEnabled(LogLevel.Error))
                this.logger.LogError(exception, "Client {Guid} HTTP response error.", guid);
        }

        public void HandshakeFailure(Guid guid, string message)
        {
            if (this.logger.IsEnabled(LogLevel.Error))
                this.logger.LogError("Client {Guid} handshake failure: {Message}", guid, message);
        }

        public void ClientHandshakeSuccess(Guid guid)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("Client {Guid} handshake success", guid);
        }

        public void ServerHandshakeSuccess(Guid guid)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("Server {Guid} handshake success", guid);
        }

        public void AcceptWebSocketStarted(Guid guid)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("Server {Guid} started accepting WebSocket", guid);
        }

        public void SendingHandshakeResponse(Guid guid, string response)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("Server {Guid} sending handshake response: {Response}", guid, response);
        }

        public void WebSocketVersionNotSupported(Guid guid, Exception exception)
        {
            if (this.logger.IsEnabled(LogLevel.Error))
                this.logger.LogError(exception, "Client {Guid} WebSocket version not supported: {Exception}", guid);
        }

        public void BadRequest(Guid guid, Exception exception)
        {
            if (this.logger.IsEnabled(LogLevel.Error))
                this.logger.LogError(exception, "Client {Guid} bad request: {Exception}", guid);
        }

        public void UsePerMessageDeflate(Guid guid)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("Client {Guid} using per-message deflate", guid);
        }

        public void NoMessageCompression(Guid guid)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("Client {Guid} no message compression", guid);
        }

        public void KeepAliveIntervalZero(Guid guid)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("Client {Guid} keep-alive interval is zero", guid);
        }

        public void PingPongStarted(Guid guid, int keepAliveIntervalSeconds)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("PingPong manager started for {Guid} with interval {Seconds}s", guid, keepAliveIntervalSeconds);
        }

        public void PingPongEnded(Guid guid)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("PingPong manager ended for {Guid}", guid);
        }

        public void KeepAliveIntervalExpired(Guid guid, int keepAliveIntervalSeconds)
        {
            if (this.logger.IsEnabled(LogLevel.Warning))
                this.logger.LogWarning("Keep-alive interval expired for {Guid} after {Seconds}s", guid, keepAliveIntervalSeconds);
        }

        public void CloseOutputAutoTimeout(Guid guid, WebSocketCloseStatus closeStatus, string statusDescription, Exception exception)
        {
            if (this.logger.IsEnabled(LogLevel.Warning))
                this.logger.LogWarning(
                    exception,
                    "Close output auto-timeout for {Guid}. Status: {Status}, Description: {Desc}",
                    guid,
                    closeStatus,
                    statusDescription);
        }

        public void CloseOutputAutoTimeoutCancelled(Guid guid, int timeoutSeconds, WebSocketCloseStatus closeStatus, string statusDescription, Exception exception)
        {
            if (this.logger.IsEnabled(LogLevel.Error))
                this.logger.LogError(
                    exception,
                    "Close output auto-timeout cancelled for {Guid} after {Seconds}s. Status: {Status}, Description: {Desc}",
                    guid,
                    timeoutSeconds,
                    closeStatus,
                    statusDescription);

        }

        public void CloseOutputAutoTimeoutError(Guid guid, Exception closeException, WebSocketCloseStatus closeStatus, string statusDescription, Exception exception)
        {
            if (this.logger.IsEnabled(LogLevel.Error))
                this.logger.LogError(
                    new AggregateException(closeException, exception),
                    "Close output auto-timeout error for {Guid}. Status: {Status}, Desc: {Desc}",
                    guid,
                    closeStatus,
                    statusDescription);
        }

        public void TryGetBufferNotSupported(Guid guid, string streamType)
        {
            if (this.logger.IsEnabled(LogLevel.Warning))
                this.logger.LogWarning("TryGetBuffer not supported for {Guid}. Stream type: {Type}", guid, streamType);
        }

        public void SendingFrame(Guid guid, WebSocketOpCode opCode, bool isFinBitSet, int numBytes, bool isPayloadCompressed)
        {
            if (this.logger.IsEnabled(LogLevel.Trace))
                this.logger.LogTrace("Sending frame for {Guid}. OpCode: {OpCode}, FIN: {Fin}, Bytes: {Bytes}, Compressed: {Compressed}",
                    guid, opCode, isFinBitSet, numBytes, isPayloadCompressed);
        }

        public void ReceivedFrame(Guid guid, WebSocketOpCode opCode, bool isFinBitSet, int numBytes)
        {
            if (this.logger.IsEnabled(LogLevel.Trace))
                this.logger.LogTrace("Received frame for {Guid}. OpCode: {OpCode}, FIN: {Fin}, Bytes: {Bytes}", guid, opCode, isFinBitSet, numBytes);
        }

        public void CloseOutputNoHandshake(Guid guid, WebSocketCloseStatus? closeStatus, string statusDescription)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("Close output with no handshake for {Guid}. Status: {Status}, Desc: {Desc}", guid, closeStatus, statusDescription);
        }

        public void CloseHandshakeStarted(Guid guid, WebSocketCloseStatus? closeStatus, string statusDescription)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("Close handshake started for {Guid}. Status: {Status}, Desc: {Desc}", guid, closeStatus, statusDescription);
        }

        public void CloseHandshakeRespond(Guid guid, WebSocketCloseStatus? closeStatus, string statusDescription)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("Close handshake respond for {Guid}. Status: {Status}, Desc: {Desc}", guid, closeStatus, statusDescription);
        }

        public void CloseHandshakeComplete(Guid guid)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("Close handshake complete for {Guid}", guid);
        }

        public void CloseFrameReceivedInUnexpectedState(Guid guid, WebSocketState state, WebSocketCloseStatus? closeStatus, string statusDescription)
        {
            if (this.logger.IsEnabled(LogLevel.Warning))
                this.logger.LogWarning("Close frame received in unexpected state for {Guid}. State: {State}, Status: {Status}, Desc: {Desc}", guid, state, closeStatus, statusDescription);
        }

        public void WebSocketDispose(Guid guid, WebSocketState state)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("WebSocket disposed for {Guid}. State: {State}", guid, state);
        }

        public void WebSocketDisposeCloseTimeout(Guid guid, WebSocketState state)
        {
            if (this.logger.IsEnabled(LogLevel.Warning))
                this.logger.LogWarning("WebSocket dispose due to close timeout for {Guid}. State: {State}", guid, state);
        }

        public void WebSocketDisposeError(Guid guid, WebSocketState state, string exception)
        {
            if (this.logger.IsEnabled(LogLevel.Error))
                this.logger.LogError("WebSocket dispose error for {Guid}. State: {State}, Exception: {Ex}", guid, state, exception);
        }

        public void InvalidStateBeforeClose(Guid guid, WebSocketState state)
        {
            if (this.logger.IsEnabled(LogLevel.Warning))
                this.logger.LogWarning("Invalid state before close for {Guid}. State: {State}", guid, state);
        }

        public void InvalidStateBeforeCloseOutput(Guid guid, WebSocketState state)
        {
            if (this.logger.IsEnabled(LogLevel.Warning))
                this.logger.LogWarning("Invalid state before close output for {Guid}. State: {State}", guid, state);
        }
    }
}
