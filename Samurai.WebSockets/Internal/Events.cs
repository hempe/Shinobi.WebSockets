using System;
using System.Net.Security;
using System.Net.WebSockets;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Samurai.WebSockets.Internal
{
    public sealed class Events
    {
        private readonly ILogger<Events> logger;
        public static Events Log { get; set; } = new Events(NullLogger<Events>.Instance);


        public Events(ILogger<Events> logger)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        internal void ClientConnectingToIpAddress(Guid guid, string ipAddress, int port)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("Client {Guid} connecting to IP {IpAddress}:{Port}", guid, ipAddress, port);
        }

        internal void ClientConnectingToHost(Guid guid, string host, int port)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("Client {Guid} connecting to host {Host}:{Port}", guid, host, port);
        }

        internal void AttemtingToSecureSslConnection(Guid guid)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("Client {Guid} attempting to secure SSL connection", guid);
        }

        internal void ConnectionSecured(Guid guid)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("Client {Guid} SSL connection secured", guid);
        }

        internal void ConnectionNotSecure(Guid guid)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("Client {Guid} SSL connection not secure", guid);
        }

        internal void SslCertificateError(SslPolicyErrors sslPolicyErrors)
        {
            if (this.logger.IsEnabled(LogLevel.Error))
                this.logger.LogError("SSL certificate error: {Errors}", sslPolicyErrors);
        }

        internal void HandshakeSent(Guid guid, string httpHeader)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("Client {Guid} sent handshake: {HttpHeader}", guid, httpHeader);
        }

        internal void ReadingHttpResponse(Guid guid)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("Client {Guid} reading HTTP response", guid);
        }

        internal void ReadHttpResponseError(Guid guid, Exception exception)
        {
            if (this.logger.IsEnabled(LogLevel.Error))
                this.logger.LogError(exception, "Client {Guid} HTTP response error.", guid);
        }

        internal void HandshakeFailure(Guid guid, string message)
        {
            if (this.logger.IsEnabled(LogLevel.Error))
                this.logger.LogError("Client {Guid} handshake failure: {Message}", guid, message);
        }

        internal void ClientHandshakeSuccess(Guid guid)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("Client {Guid} handshake success", guid);
        }

        internal void ServerHandshakeSuccess(Guid guid)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("Server {Guid} handshake success", guid);
        }

        internal void AcceptWebSocketStarted(Guid guid)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("Server {Guid} started accepting WebSocket", guid);
        }

        internal void SendingHandshakeResponse(Guid guid, string response)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("Server {Guid} sending handshake response: {Response}", guid, response);
        }

        internal void WebSocketVersionNotSupported(Guid guid, Exception exception)
        {
            if (this.logger.IsEnabled(LogLevel.Error))
                this.logger.LogError(exception, "Client {Guid} WebSocket version not supported", guid);
        }

        internal void BadRequest(Guid guid, Exception exception)
        {
            if (this.logger.IsEnabled(LogLevel.Error))
                this.logger.LogError(exception, "Client {Guid} bad request", guid);
        }

        internal void UsePerMessageDeflate(Guid guid)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("Client {Guid} using per-message deflate", guid);
        }

        internal void NoMessageCompression(Guid guid)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("Client {Guid} no message compression", guid);
        }

        internal void KeepAliveIntervalZero(Guid guid)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("Client {Guid} keep-alive interval is zero", guid);
        }

        internal void PingPongStarted(Guid guid, int keepAliveIntervalSeconds)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("PingPong manager started for {Guid} with interval {Seconds}s", guid, keepAliveIntervalSeconds);
        }

        internal void PingPongEnded(Guid guid)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("PingPong manager ended for {Guid}", guid);
        }

        internal void KeepAliveIntervalExpired(Guid guid, int keepAliveIntervalSeconds)
        {
            if (this.logger.IsEnabled(LogLevel.Warning))
                this.logger.LogWarning("Keep-alive interval expired for {Guid} after {Seconds}s", guid, keepAliveIntervalSeconds);
        }

        internal void CloseOutputAutoTimeout(Guid guid, WebSocketCloseStatus closeStatus, string statusDescription, Exception exception)
        {
            if (this.logger.IsEnabled(LogLevel.Warning))
                this.logger.LogWarning(
                    exception,
                    "Close output auto-timeout for {Guid}. Status: {Status}, Description: {Desc}",
                    guid,
                    closeStatus,
                    statusDescription);
        }

        internal void CloseOutputAutoTimeoutCancelled(Guid guid, int timeoutSeconds, WebSocketCloseStatus closeStatus, string statusDescription, Exception exception)
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

        internal void CloseOutputAutoTimeoutError(Guid guid, Exception closeException, WebSocketCloseStatus closeStatus, string statusDescription, Exception exception)
        {
            if (this.logger.IsEnabled(LogLevel.Error))
                this.logger.LogError(
                    new AggregateException(closeException, exception),
                    "Close output auto-timeout error for {Guid}. Status: {Status}, Desc: {Desc}",
                    guid,
                    closeStatus,
                    statusDescription);
        }

        internal void SendingFrame(Guid guid, WebSocketOpCode opCode, bool isFinBitSet, int numBytes, bool isPayloadCompressed)
        {
            if (this.logger.IsEnabled(LogLevel.Trace))
                this.logger.LogTrace(
                    "Sending frame for {Guid}. OpCode: {OpCode}, FIN: {Fin}, Bytes: {Bytes}, Compressed: {Compressed}",
                    guid,
                    opCode,
                    isFinBitSet,
                    numBytes,
                    isPayloadCompressed);
        }

        internal void ReceivedFrame(Guid guid, WebSocketOpCode opCode, bool isFinBitSet, int numBytes)
        {
            if (this.logger.IsEnabled(LogLevel.Trace))
                this.logger.LogTrace(
                    "Received frame for {Guid}. OpCode: {OpCode}, FIN: {Fin}, Bytes: {Bytes}",
                    guid,
                    opCode,
                    isFinBitSet,
                    numBytes);
        }

        internal void CloseOutputNoHandshake(Guid guid, WebSocketCloseStatus? closeStatus, string? statusDescription)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation(
                    "Close output with no handshake for {Guid}. Status: {Status}, Desc: {Desc}",
                    guid,
                    closeStatus,
                    statusDescription);
        }

        internal void CloseHandshakeStarted(Guid guid, WebSocketCloseStatus? closeStatus, string? statusDescription)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation(
                    "Close handshake started for {Guid}. Status: {Status}, Desc: {Desc}",
                    guid,
                    closeStatus,
                    statusDescription);
        }

        internal void CloseHandshakeRespond(Guid guid, WebSocketCloseStatus? closeStatus, string? statusDescription)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation(
                    "Close handshake respond for {Guid}. Status: {Status}, Desc: {Desc}",
                    guid,
                    closeStatus,
                    statusDescription);
        }

        internal void CloseHandshakeComplete(Guid guid)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("Close handshake complete for {Guid}", guid);
        }

        internal void CloseFrameReceivedInUnexpectedState(Guid guid, WebSocketState state, WebSocketCloseStatus? closeStatus, string? statusDescription)
        {
            if (this.logger.IsEnabled(LogLevel.Warning))
                this.logger.LogWarning(
                    "Close frame received in unexpected state for {Guid}. State: {State}, Status: {Status}, Desc: {Desc}",
                    guid,
                    state,
                    closeStatus,
                    statusDescription);
        }

        internal void WebSocketDispose(Guid guid, WebSocketState state)
        {
            if (this.logger.IsEnabled(LogLevel.Information))
                this.logger.LogInformation("WebSocket disposed for {Guid}. State: {State}", guid, state);
        }

        internal void WebSocketDisposeCloseTimeout(Guid guid, WebSocketState state)
        {
            if (this.logger.IsEnabled(LogLevel.Warning))
                this.logger.LogWarning("WebSocket dispose due to close timeout for {Guid}. State: {State}", guid, state);
        }

        internal void WebSocketDisposeError(Guid guid, WebSocketState state, string exception)
        {
            if (this.logger.IsEnabled(LogLevel.Error))
                this.logger.LogError("WebSocket dispose error for {Guid}. State: {State}, Exception: {Ex}", guid, state, exception);
        }

        internal void InvalidStateBeforeClose(Guid guid, WebSocketState state)
        {
            if (this.logger.IsEnabled(LogLevel.Warning))
                this.logger.LogWarning("Invalid state before close for {Guid}. State: {State}", guid, state);
        }

        internal void InvalidStateBeforeCloseOutput(Guid guid, WebSocketState state)
        {
            if (this.logger.IsEnabled(LogLevel.Warning))
                this.logger.LogWarning("Invalid state before close output for {Guid}. State: {State}", guid, state);
        }
    }
}
