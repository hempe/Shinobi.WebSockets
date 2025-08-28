// ---------------------------------------------------------------------
// Copyright 2018 David Haig
// Copyright 2025 Hansueli Burri
//
// Permission is hereby granted, free of charge, to any person obtaining a copy 
// of this software and associated documentation files (the "Software"), to deal 
// in the Software without restriction, including without limitation the rights 
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell 
// copies of the Software, and to permit persons to whom the Software is 
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR 
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, 
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN 
// THE SOFTWARE.
// ---------------------------------------------------------------------


using System;
using System.Net.Security;
using System.Net.WebSockets;

using Microsoft.Extensions.Logging;

namespace Shinobi.WebSockets.Internal
{
    internal static partial class Events
    {
        // Connection Events (1000-1999)
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

        // Handshake Events (1100-1199)
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

        // Compression Events (1200-1299)
        [LoggerMessage(
            EventId = 1201,
            Level = LogLevel.Information,
            Message = "Client {Guid} using per-message deflate")]
        public static partial void UsePerMessageDeflate(
            this ILogger logger, Guid guid);

        [LoggerMessage(
            EventId = 1202,
            Level = LogLevel.Information,
            Message = "Client {Guid} no message compression")]
        public static partial void NoMessageCompression(
            this ILogger logger, Guid guid);

        // Keep-Alive Events (1300-1399)
        [LoggerMessage(
            EventId = 1301,
            Level = LogLevel.Information,
            Message = "Client {Guid} keep-alive interval is zero")]
        public static partial void KeepAliveIntervalZero(
            this ILogger logger, Guid guid);

        [LoggerMessage(
            EventId = 1302,
            Level = LogLevel.Information,
            Message = "PingPong manager started for {Guid} with interval {Seconds}s")]
        public static partial void PingPongStarted(
            this ILogger logger, Guid guid, int seconds);

        [LoggerMessage(
            EventId = 1303,
            Level = LogLevel.Information,
            Message = "PingPong manager ended for {Guid}")]
        public static partial void PingPongEnded(
            this ILogger logger, Guid guid);

        [LoggerMessage(
            EventId = 1304,
            Level = LogLevel.Warning,
            Message = "Keep-alive interval expired for {Guid} after {Seconds}s")]
        public static partial void KeepAliveIntervalExpired(
            this ILogger logger, Guid guid, int seconds);

        // Frame Events (2000-2999)
        [LoggerMessage(
            EventId = 2001,
            Level = LogLevel.Trace,
            Message = "Sending frame for {Guid}. OpCode: {OpCode}, FIN: {Fin}, Bytes: {Bytes}, Compressed: {Compressed}")]
        public static partial void SendingFrame(
            this ILogger logger, Guid guid, WebSocketOpCode opCode, bool fin, int bytes, bool compressed);

        [LoggerMessage(
            EventId = 2002,
            Level = LogLevel.Trace,
            Message = "Received frame for {Guid}. OpCode: {OpCode}, FIN: {Fin}, Bytes: {Bytes}")]
        public static partial void ReceivedFrame(
            this ILogger logger, Guid guid, WebSocketOpCode opCode, bool fin, int bytes);

        // Close Events (3000-3999)
        [LoggerMessage(
            EventId = 3001,
            Level = LogLevel.Warning,
            Message = "Close output auto-timeout for {Guid}. Status: {Status}, Description: {Desc}")]
        public static partial void CloseOutputAutoTimeout(
            this ILogger logger, Guid guid, WebSocketCloseStatus status, string desc, Exception exception);

        [LoggerMessage(
            EventId = 3002,
            Level = LogLevel.Error,
            Message = "Close output auto-timeout cancelled for {Guid} after {Seconds}s. Status: {Status}, Description: {Desc}")]
        public static partial void CloseOutputAutoTimeoutCancelled(
            this ILogger logger, Guid guid, int seconds, WebSocketCloseStatus status, string desc, Exception exception);

        [LoggerMessage(
            EventId = 3003,
            Level = LogLevel.Error,
            Message = "Close output auto-timeout error for {Guid}. Status: {Status}, Desc: {Desc}")]
        public static partial void CloseOutputAutoTimeoutError(
            this ILogger logger, Guid guid, Exception closeException, WebSocketCloseStatus status, string desc, Exception exception);

        [LoggerMessage(
            EventId = 3004,
            Level = LogLevel.Information,
            Message = "Close output with no handshake for {Guid}. Status: {Status}, Desc: {Desc}")]
        public static partial void CloseOutputNoHandshake(
            this ILogger logger, Guid guid, WebSocketCloseStatus? status, string? desc);

        [LoggerMessage(
            EventId = 3005,
            Level = LogLevel.Information,
            Message = "Close handshake started for {Guid}. Status: {Status}, Desc: {Desc}")]
        public static partial void CloseHandshakeStarted(
            this ILogger logger, Guid guid, WebSocketCloseStatus? status, string? desc);

        [LoggerMessage(
            EventId = 3006,
            Level = LogLevel.Information,
            Message = "Close handshake respond for {Guid}. Status: {Status}, Desc: {Desc}")]
        public static partial void CloseHandshakeRespond(
            this ILogger logger, Guid guid, WebSocketCloseStatus? status, string? desc);

        [LoggerMessage(
            EventId = 3007,
            Level = LogLevel.Information,
            Message = "Close handshake complete for {Guid}")]
        public static partial void CloseHandshakeComplete(
            this ILogger logger, Guid guid);

        [LoggerMessage(
            EventId = 3008,
            Level = LogLevel.Warning,
            Message = "Close handshake timed out after {TimeoutSeconds} seconds for {Guid}")]
        public static partial void CloseHandshakeTimedOut(
            this ILogger logger, Guid guid, int timeoutSeconds);

        [LoggerMessage(
            EventId = 3009,
            Level = LogLevel.Warning,
            Message = "Close frame received in unexpected state for {Guid}. State: {State}, Status: {Status}, Desc: {Desc}")]
        public static partial void CloseFrameReceivedInUnexpectedState(
            this ILogger logger, Guid guid, WebSocketState state, WebSocketCloseStatus? status, string? desc);

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

        // Disposal Events (9000-9999)
        [LoggerMessage(
            EventId = 9001,
            Level = LogLevel.Information,
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
            Message = "WebSocket dispose error for {Guid}. State: {State}, Exception: {Ex}")]
        public static partial void WebSocketDisposeError(
            this ILogger logger, Guid guid, WebSocketState state, Exception ex);
    }
}
