using System;
using System.Net.Security;
using System.Net.WebSockets;

using Microsoft.Extensions.Logging;

namespace Shinobi.WebSockets.Internal
{
    internal static partial class Events
    {
        #region Connection Events (1000-1099)

        [LoggerMessage(
            EventId = 1001,
            Level = LogLevel.Information,
            Message = "Client {Guid} connecting to IP {IpAddress}:{Port}")]
        public static partial void ClientConnectingToIpAddress(
            this ILogger logger, Guid guid, string ipAddress, int port);

        [LoggerMessage(
            EventId = 1002,
            Level = LogLevel.Information,
            Message = "Client {Guid} connecting to host {Host}:{Port}")]
        public static partial void ClientConnectingToHost(
            this ILogger logger, Guid guid, string host, int port);

        [LoggerMessage(
            EventId = 1003,
            Level = LogLevel.Information,
            Message = "Client {Guid} attempting to secure SSL connection")]
        public static partial void AttemptingToSecureSslConnection(
            this ILogger logger, Guid guid);

        [LoggerMessage(
            EventId = 1004,
            Level = LogLevel.Information,
            Message = "Client {Guid} SSL connection secured")]
        public static partial void ConnectionSecured(
            this ILogger logger, Guid guid);

        [LoggerMessage(
            EventId = 1005,
            Level = LogLevel.Information,
            Message = "Client {Guid} SSL connection not secure")]
        public static partial void ConnectionNotSecure(
            this ILogger logger, Guid guid);

        [LoggerMessage(
            EventId = 1006,
            Level = LogLevel.Error,
            Message = "SSL certificate error: {Errors}")]
        public static partial void SslCertificateError(
            this ILogger logger, SslPolicyErrors errors);

        #endregion

        #region Handshake Events (1100-1199)

        [LoggerMessage(
            EventId = 1101,
            Level = LogLevel.Information,
            Message = "Client {Guid} sent handshake: {HttpHeader}")]
        public static partial void HandshakeSent(
            this ILogger logger, Guid guid, string httpHeader);

        [LoggerMessage(
            EventId = 1102,
            Level = LogLevel.Information,
            Message = "Client {Guid} reading HTTP response")]
        public static partial void ReadingHttpResponse(
            this ILogger logger, Guid guid);

        [LoggerMessage(
            EventId = 1103,
            Level = LogLevel.Error,
            Message = "Client {Guid} HTTP response error")]
        public static partial void ReadHttpResponseError(
            this ILogger logger, Guid guid, Exception exception);

        [LoggerMessage(
            EventId = 1104,
            Level = LogLevel.Error,
            Message = "Client {Guid} handshake failure: {Message}")]
        public static partial void HandshakeFailure(
            this ILogger logger, Guid guid, string message);

        [LoggerMessage(
            EventId = 1105,
            Level = LogLevel.Information,
            Message = "Client {Guid} handshake success")]
        public static partial void ClientHandshakeSuccess(
            this ILogger logger, Guid guid);

        [LoggerMessage(
            EventId = 1106,
            Level = LogLevel.Information,
            Message = "Server {Guid} handshake success")]
        public static partial void ServerHandshakeSuccess(
            this ILogger logger, Guid guid);

        [LoggerMessage(
            EventId = 1107,
            Level = LogLevel.Information,
            Message = "Server {Guid} started accepting WebSocket")]
        public static partial void AcceptWebSocketStarted(
            this ILogger logger, Guid guid);

        [LoggerMessage(
            EventId = 1108,
            Level = LogLevel.Information,
            Message = "Server {Guid} sending handshake response, StatusCode: {StatusCode}")]
        public static partial void SendingHandshakeResponse(
            this ILogger logger, Guid guid, int statusCode);

        [LoggerMessage(
            EventId = 1109,
            Level = LogLevel.Error,
            Message = "Client {Guid} WebSocket version not supported")]
        public static partial void WebSocketVersionNotSupported(
            this ILogger logger, Guid guid, Exception exception);

        [LoggerMessage(
            EventId = 1110,
            Level = LogLevel.Error,
            Message = "Client {Guid} bad request")]
        public static partial void BadRequest(
            this ILogger logger, Guid guid, Exception exception);

        #endregion

        #region Compression Events (1200-1299)

        [LoggerMessage(
            EventId = 1201,
            Level = LogLevel.Debug,
            Message = "Client {Guid} using per-message deflate")]
        public static partial void UsePerMessageDeflate(
            this ILogger logger, Guid guid);

        [LoggerMessage(
            EventId = 1202,
            Level = LogLevel.Debug,
            Message = "Client {Guid} no message compression")]
        public static partial void NoMessageCompression(
            this ILogger logger, Guid guid);

        #endregion

        #region Keep-Alive Events (1300-1399)

        [LoggerMessage(
            EventId = 1301,
            Level = LogLevel.Debug,
            Message = "Client {Guid} keep-alive interval is zero")]
        public static partial void KeepAliveIntervalZero(
            this ILogger logger, Guid guid);

        [LoggerMessage(
            EventId = 1302,
            Level = LogLevel.Debug,
            Message = "PingPong manager started for {Guid} with interval {Seconds}s")]
        public static partial void PingPongStarted(
            this ILogger logger, Guid guid, int seconds);

        [LoggerMessage(
            EventId = 1303,
            Level = LogLevel.Debug,
            Message = "PingPong manager ended for {Guid}")]
        public static partial void PingPongEnded(
            this ILogger logger, Guid guid);

        [LoggerMessage(
            EventId = 1304,
            Level = LogLevel.Warning,
            Message = "Keep-alive interval expired for {Guid} after {Seconds}s")]
        public static partial void KeepAliveIntervalExpired(
            this ILogger logger, Guid guid, int seconds);

        [LoggerMessage(
            EventId = 1305,
            Level = LogLevel.Debug,
            Message = "Keep-alive connection timed out waiting for data")]
        public static partial void KeepAliveConnectionTimedOut(
            this ILogger logger);

        [LoggerMessage(
            EventId = 1306,
            Level = LogLevel.Debug,
            Message = "Evicted oldest keep-alive connection {ConnectionId}")]
        public static partial void EvictedOldestKeepAliveConnection(
            this ILogger logger, Guid connectionId);

        #endregion

        #region Frame Events (2000-2999)

        [LoggerMessage(
            EventId = 2001,
            Level = LogLevel.Trace,
            Message = "Sending frame for {Guid}. OpCode: {OpCode}, FIN: {IsFinBitSet}, Bytes: {NumBytes}, Compressed: {IsPayloadCompressed}")]
        public static partial void SendingFrame(
            this ILogger logger, Guid guid, WebSocketOpCode opCode, bool isFinBitSet, int numBytes, bool isPayloadCompressed);

        [LoggerMessage(
            EventId = 2002,
            Level = LogLevel.Trace,
            Message = "Received frame for {Guid}. OpCode: {OpCode}, FIN: {IsFinBitSet}, Bytes: {NumBytes}")]
        public static partial void ReceivedFrame(
            this ILogger logger, Guid guid, WebSocketOpCode opCode, bool isFinBitSet, int numBytes);

        #endregion

        #region Close Events (3000-3999)

        [LoggerMessage(
            EventId = 3001,
            Level = LogLevel.Warning,
            Message = "Close output auto-timeout for {Guid}. Status: {CloseStatus}, Description: {StatusDescription}")]
        public static partial void CloseOutputAutoTimeout(
            this ILogger logger, Guid guid, WebSocketCloseStatus closeStatus, string statusDescription, Exception exception);

        [LoggerMessage(
            EventId = 3002,
            Level = LogLevel.Error,
            Message = "Close output auto-timeout cancelled for {Guid} after {TimeoutSeconds}s. Status: {CloseStatus}, Description: {StatusDescription}")]
        public static partial void CloseOutputAutoTimeoutCancelled(
            this ILogger logger, Guid guid, int timeoutSeconds, WebSocketCloseStatus closeStatus, string statusDescription, Exception exception);

        [LoggerMessage(
            EventId = 3003,
            Level = LogLevel.Error,
            Message = "Close output auto-timeout error for {Guid}. Status: {CloseStatus}, Description: {StatusDescription}")]
        public static partial void CloseOutputAutoTimeoutError(
            this ILogger logger, Guid guid, WebSocketCloseStatus closeStatus, string statusDescription, Exception exception);

        [LoggerMessage(
            EventId = 3004,
            Level = LogLevel.Information,
            Message = "Close output with no handshake for {Guid}. Status: {CloseStatus}, Description: {StatusDescription}")]
        public static partial void CloseOutputNoHandshake(
            this ILogger logger, Guid guid, WebSocketCloseStatus? closeStatus, string? statusDescription);

        [LoggerMessage(
            EventId = 3005,
            Level = LogLevel.Information,
            Message = "Close handshake started for {Guid}. Status: {CloseStatus}, Description: {StatusDescription}")]
        public static partial void CloseHandshakeStarted(
            this ILogger logger, Guid guid, WebSocketCloseStatus? closeStatus, string? statusDescription);

        [LoggerMessage(
            EventId = 3006,
            Level = LogLevel.Information,
            Message = "Close handshake respond for {Guid}. Status: {CloseStatus}, Description: {StatusDescription}")]
        public static partial void CloseHandshakeRespond(
            this ILogger logger, Guid guid, WebSocketCloseStatus? closeStatus, string? statusDescription);

        [LoggerMessage(
            EventId = 3007,
            Level = LogLevel.Information,
            Message = "Close handshake complete for {Guid}")]
        public static partial void CloseHandshakeComplete(
            this ILogger logger, Guid guid);

        [LoggerMessage(
            EventId = 3008,
            Level = LogLevel.Warning,
            Message = "Close handshake timed out for {Guid} after {TimeoutSeconds}s")]
        public static partial void CloseHandshakeTimedOut(
            this ILogger logger, Guid guid, int timeoutSeconds);

        [LoggerMessage(
            EventId = 3009,
            Level = LogLevel.Warning,
            Message = "Close frame received in unexpected state for {Guid}. State: {State}, Status: {CloseStatus}, Description: {StatusDescription}")]
        public static partial void CloseFrameReceivedInUnexpectedState(
            this ILogger logger, Guid guid, WebSocketState state, WebSocketCloseStatus? closeStatus, string? statusDescription);

        [LoggerMessage(
            EventId = 3010,
            Level = LogLevel.Warning,
            Message = "Invalid state before close for {Guid}. State: {State}")]
        public static partial void InvalidStateBeforeClose(
            this ILogger logger, Guid guid, WebSocketState state);

        [LoggerMessage(
            EventId = 3011,
            Level = LogLevel.Warning,
            Message = "Invalid state before close output for {Guid}. State: {State}")]
        public static partial void InvalidStateBeforeCloseOutput(
            this ILogger logger, Guid guid, WebSocketState state);

        #endregion

        #region Client Connection Events (4000-4999)

        [LoggerMessage(
            EventId = 4001,
            Level = LogLevel.Information,
            Message = "WebSocket reconnection cancelled")]
        public static partial void ReconnectionCancelled(
            this ILogger logger);

        [LoggerMessage(
            EventId = 4002,
            Level = LogLevel.Warning,
            Message = "Connection error (attempt {AttemptNumber})")]
        public static partial void ConnectionError(
            this ILogger logger, int attemptNumber, Exception exception);

        [LoggerMessage(
            EventId = 4003,
            Level = LogLevel.Error,
            Message = "WebSocket connection failed permanently - reconnection disabled")]
        public static partial void ConnectionFailedPermanently(
            this ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = 4004,
            Level = LogLevel.Information,
            Message = "Successfully reconnected to {Uri} after {AttemptNumber} attempts")]
        public static partial void ReconnectedSuccessfully(
            this ILogger logger, Uri uri, int attemptNumber);

        [LoggerMessage(
            EventId = 4005,
            Level = LogLevel.Information,
            Message = "WebSocket connection closed after {Duration}ms - treating as connection failure (not considered stable)")]
        public static partial void ConnectionClosedUnstable(
            this ILogger logger, double duration);

        [LoggerMessage(
            EventId = 4006,
            Level = LogLevel.Information,
            Message = "WebSocket connection closed normally after {Duration}ms - resetting backoff (considered stable)")]
        public static partial void ConnectionClosedStable(
            this ILogger logger, double duration);

        [LoggerMessage(
            EventId = 4007,
            Level = LogLevel.Debug,
            Message = "WebSocket connection state changed from {PreviousState} to {NewState}")]
        public static partial void ConnectionStateChanged(
            this ILogger logger, WebSocketConnectionState previousState, WebSocketConnectionState newState);

        [LoggerMessage(
            EventId = 4008,
            Level = LogLevel.Debug,
            Message = "Auto-reconnect is disabled, staying disconnected")]
        public static partial void AutoReconnectDisabled(
            this ILogger logger);

        [LoggerMessage(
            EventId = 4009,
            Level = LogLevel.Debug,
            Message = "Starting auto-reconnect sequence (attempt {AttemptNumber})")]
        public static partial void StartingAutoReconnect(
            this ILogger logger, int attemptNumber);

        [LoggerMessage(
            EventId = 4010,
            Level = LogLevel.Debug,
            Message = "Calling OnReconnecting handler for attempt {AttemptNumber}")]
        public static partial void CallingOnReconnectingHandler(
            this ILogger logger, int attemptNumber);

        [LoggerMessage(
            EventId = 4011,
            Level = LogLevel.Debug,
            Message = "OnReconnecting handler changed URI from {OldUri} to {NewUri}")]
        public static partial void ReconnectingUriChanged(
            this ILogger logger, Uri oldUri, Uri newUri);

        [LoggerMessage(
            EventId = 4012,
            Level = LogLevel.Debug,
            Message = "Reconnecting to {Uri} in {Delay}ms (attempt {AttemptNumber})")]
        public static partial void ReconnectingWithDelay(
            this ILogger logger, Uri uri, int delay, int attemptNumber);

        #endregion

        #region Server Events (5000-5999)

        [LoggerMessage(
            EventId = 5001,
            Level = LogLevel.Error,
            Message = "Error listening on port {Port}. Make sure IIS or another application is not running and consuming your port")]
        public static partial void ListeningError(
            this ILogger logger, int port, Exception exception);

        [LoggerMessage(
            EventId = 5002,
            Level = LogLevel.Warning,
            Message = "Server listener disposed while processing clients")]
        public static partial void ListenerDisposed(
            this ILogger logger);

        [LoggerMessage(
            EventId = 5003,
            Level = LogLevel.Error,
            Message = "Unexpected error processing TCP client")]
        public static partial void TcpClientProcessingError(
            this ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = 5004,
            Level = LogLevel.Debug,
            Message = "Server HTTP header contains no WebSocket upgrade request")]
        public static partial void NoWebSocketUpgradeRequest(
            this ILogger logger);

        [LoggerMessage(
            EventId = 5005,
            Level = LogLevel.Debug,
            Message = "Server opening port {Port}")]
        public static partial void ServerOpeningPort(
            this ILogger logger, int port);

        [LoggerMessage(
            EventId = 5006,
            Level = LogLevel.Information,
            Message = "Server started listening on port {Port}")]
        public static partial void ServerStartedListening(
            this ILogger logger, int port);

        [LoggerMessage(
            EventId = 5007,
            Level = LogLevel.Debug,
            Message = "Server attempting to secure connection")]
        public static partial void AttemptingToSecureConnection(
            this ILogger logger);

        [LoggerMessage(
            EventId = 5008,
            Level = LogLevel.Debug,
            Message = "Server connection successfully secured")]
        public static partial void ConnectionSuccessfullySecured(
            this ILogger logger);

        [LoggerMessage(
            EventId = 5009,
            Level = LogLevel.Error,
            Message = "Failed to upgrade stream to ssl stream")]
        public static partial void FailedToUpgradeToSslStream(
            this ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = 5010,
            Level = LogLevel.Error,
            Message = "Failed to upgrade stream to ssl stream, inner exception: {Message}")]
        public static partial void FailedToUpgradeToSslStreamInner(
            this ILogger logger, string message, Exception exception);

        [LoggerMessage(
            EventId = 5011,
            Level = LogLevel.Error,
            Message = "Failure at the TCP connection")]
        public static partial void TcpConnectionFailure(
            this ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = 5012,
            Level = LogLevel.Debug,
            Message = "Server: Connection closed")]
        public static partial void ServerConnectionClosed(
            this ILogger logger);

        [LoggerMessage(
            EventId = 5013,
            Level = LogLevel.Warning,
            Message = "{Method} failed")]
        public static partial void ServerMethodFailed(
            this ILogger logger, string method, Exception exception);

        [LoggerMessage(
            EventId = 5014,
            Level = LogLevel.Debug,
            Message = "Drain clients succeeded")]
        public static partial void DrainClientsSucceeded(
            this ILogger logger);

        [LoggerMessage(
            EventId = 5015,
            Level = LogLevel.Debug,
            Message = "Drain clients failed")]
        public static partial void DrainClientsFailed(
            this ILogger logger, Exception exception);

        #endregion

        #region Builder Events (8000-8999)

        [LoggerMessage(
            EventId = 8001,
            Level = LogLevel.Debug,
            Message = "Server: Connection opened")]
        public static partial void ServerConnectionOpened(
            this ILogger logger);

        [LoggerMessage(
            EventId = 8002,
            Level = LogLevel.Information,
            Message = "WebSocket connected: {ConnectionId}")]
        public static partial void WebSocketConnected(
            this ILogger logger, Guid connectionId);

        [LoggerMessage(
            EventId = 8003,
            Level = LogLevel.Information,
            Message = "WebSocket disconnected: {ConnectionId}, CloseStatus: {CloseStatus}, StatusDescription: {StatusDescription}")]
        public static partial void WebSocketDisconnected(
            this ILogger logger, Guid connectionId, WebSocketCloseStatus? closeStatus, string? statusDescription);

        [LoggerMessage(
            EventId = 8004,
            Level = LogLevel.Error,
            Message = "WebSocket error for connection: {ConnectionId}")]
        public static partial void WebSocketError(
            this ILogger logger, Guid connectionId, Exception exception);

        [LoggerMessage(
            EventId = 8005,
            Level = LogLevel.Information,
            Message = "WebSocket client connected: {ConnectionId}")]
        public static partial void WebSocketClientConnected(
            this ILogger logger, Guid connectionId);

        [LoggerMessage(
            EventId = 8006,
            Level = LogLevel.Information,
            Message = "WebSocket client disconnected: {ConnectionId}, CloseStatus: {CloseStatus}, StatusDescription: {StatusDescription}")]
        public static partial void WebSocketClientDisconnected(
            this ILogger logger, Guid connectionId, WebSocketCloseStatus? closeStatus, string? statusDescription);

        [LoggerMessage(
            EventId = 8007,
            Level = LogLevel.Error,
            Message = "WebSocket client error for connection: {ConnectionId}")]
        public static partial void WebSocketClientError(
            this ILogger logger, Guid connectionId, Exception exception);

        #endregion

        #region Disposal Events (9000-9999)

        [LoggerMessage(
            EventId = 9001,
            Level = LogLevel.Debug,
            Message = "WebSocket disposed for {Guid}. State: {State}")]
        public static partial void WebSocketDispose(
            this ILogger logger, Guid guid, WebSocketState state);

        [LoggerMessage(
            EventId = 9002,
            Level = LogLevel.Warning,
            Message = "WebSocket dispose due to close timeout for {Guid}. State: {State}")]
        public static partial void WebSocketDisposeCloseTimeout(
            this ILogger logger, Guid guid, WebSocketState state);

        [LoggerMessage(
            EventId = 9003,
            Level = LogLevel.Error,
            Message = "WebSocket dispose error for {Guid}. State: {State}")]
        public static partial void WebSocketDisposeError(
            this ILogger logger, Guid guid, WebSocketState state, Exception ex);

        #endregion
    }
}
