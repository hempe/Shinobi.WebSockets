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
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Shinobi.WebSockets.Http;
using Shinobi.WebSockets.Internal;

[assembly: InternalsVisibleTo("Shinobi.WebSockets.UnitTests")]
[assembly: InternalsVisibleTo("Shinobi.WebSockets.Benchmark")]


namespace Shinobi.WebSockets
{

    /// <summary>
    /// Main implementation of the WebSocket abstract class
    /// </summary>
    public sealed class ShinobiWebSocket : WebSocket
    {
        private const int MAX_PING_PONG_PAYLOAD_LEN = 125;
        private static readonly byte[] EMPTY = new byte[0];
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1);
        private readonly Stopwatch stopwatch;
        private readonly bool includeExceptionInCloseResponse;
        private readonly bool isClient;
        private readonly CancellationTokenSource internalReadCts;

        private WebSocketState state;
        private bool isContinuationFrame;
        private WebSocketMessageType continuationFrameMessageType = WebSocketMessageType.Binary;
        private WebSocketReadCursor? readCursor;

#if NET8_0_OR_GREATER
        private readonly WebSocketDeflater? deflater;
        private readonly WebSocketInflater? inflater;
        private bool isCollectingCompressedMessage;
        private WebSocketMessageType collectedMessageType;
        private ArraySegment<byte>? pendingDecompressedData = null;
        private int pendingDataOffset;
#endif
        private WebSocketCloseStatus? closeStatus;
        private string? closeStatusDescription;
        private long pingSentTicks;

        public readonly WebSocketHttpContext Context;

        public readonly bool PermessageDeflate;
        public ShinobiWebSocket(
            WebSocketHttpContext context,
#if NET8_0_OR_GREATER
            WebSocketExtension? secWebSocketExtensions,
#endif
            TimeSpan keepAliveInterval,
            bool includeExceptionInCloseResponse,
            bool isClient,
            string? subProtocol)
        {
            this.Context = context;
            this.isClient = isClient;
            this.SubProtocol = subProtocol;
            this.internalReadCts = new CancellationTokenSource();
            this.state = WebSocketState.Open;
            this.stopwatch = Stopwatch.StartNew();

#if NET8_0_OR_GREATER
            this.PermessageDeflate = secWebSocketExtensions != null;
            if (this.PermessageDeflate)
            {
                var deflaterNoContextTakeover = isClient ? "client_no_context_takeover" : "server_no_context_takeover";
                var inflaterNoContextTakeover = isClient ? "server_no_context_takeover" : "client_no_context_takeover";
                this.deflater = new WebSocketDeflater(secWebSocketExtensions?.Parameters.ContainsKey(deflaterNoContextTakeover) == true);
                this.inflater = new WebSocketInflater(secWebSocketExtensions?.Parameters.ContainsKey(inflaterNoContextTakeover) == true);
                Events.Log?.UsePerMessageDeflate(context.Guid);
            }
            else
            {
                this.deflater = null;
                this.inflater = null;
                Events.Log?.NoMessageCompression(context.Guid);
            }
#else
            Events.Log?.NoMessageCompression(context.Guid);
#endif

            this.KeepAliveInterval = keepAliveInterval;
            this.includeExceptionInCloseResponse = includeExceptionInCloseResponse;
            if (keepAliveInterval.Ticks < 0)
            {
                throw new InvalidOperationException("KeepAliveInterval must be Zero or positive");
            }

            if (keepAliveInterval == TimeSpan.Zero)
            {
                Events.Log?.KeepAliveIntervalZero(context.Guid);
            }
            else
            {
                _ = Task.Run(() => this.PingForeverAsync(this.internalReadCts.Token), this.internalReadCts.Token);
            }
        }

        public override WebSocketCloseStatus? CloseStatus => this.closeStatus;

        public override string? CloseStatusDescription => this.closeStatusDescription;

        public override WebSocketState State { get { return this.state; } }

        public override string? SubProtocol { get; }

        public TimeSpan KeepAliveInterval { get; }

        /// <summary>
        /// Receive web socket result
        /// </summary>
        /// <param name="buffer">The buffer to copy data into</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>The web socket result details</returns>
        public async override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            return await this.ReceiveCoreAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask<WebSocketReceiveResult> ReceiveCoreAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            try
            {
#if NET8_0_OR_GREATER
                // First, check if we have pending decompressed data from a previous call
                if (this.pendingDecompressedData.HasValue && this.pendingDecompressedData.Value.Count > 0)
                {
                    return this.ReturnPendingData(this.pendingDecompressedData.Value, buffer);
                }
#endif
                // we may receive control frames so reading needs to happen in an infinite loop
                while (true)
                {
                    // allow this operation to be cancelled from inside OR outside this instance
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(this.internalReadCts.Token, cancellationToken);

                    WebSocketFrame frame;

                    try
                    {
                        if (this.readCursor.HasValue && this.readCursor.Value.NumBytesLeftToRead > 0)
                        {
                            this.readCursor = await WebSocketFrameReader.ReadFromCursorAsync(this.Context.Stream, buffer, this.readCursor.Value, linkedCts.Token).ConfigureAwait(false);
                            frame = this.readCursor.Value.WebSocketFrame;
                        }
                        else
                        {
                            this.readCursor = await WebSocketFrameReader.ReadAsync(this.Context.Stream, buffer, linkedCts.Token).ConfigureAwait(false);
                            frame = this.readCursor.Value.WebSocketFrame;
                            Events.Log?.ReceivedFrame(this.Context.Guid, frame.OpCode, frame.IsFinBitSet, frame.Count);
                        }
                    }
                    catch (InternalBufferOverflowException ex)
                    {
                        await this.CloseOutputAutoTimeoutAsync(WebSocketCloseStatus.MessageTooBig, "Frame too large to fit in buffer. Use message fragmentation", ex).ConfigureAwait(false);
                        throw;
                    }
                    catch (ArgumentOutOfRangeException ex)
                    {
                        await this.CloseOutputAutoTimeoutAsync(WebSocketCloseStatus.ProtocolError, "Payload length out of range", ex).ConfigureAwait(false);
                        throw;
                    }
                    catch (EndOfStreamException ex)
                    {
                        await this.CloseOutputAutoTimeoutAsync(WebSocketCloseStatus.InvalidPayloadData, "Unexpected end of stream encountered", ex).ConfigureAwait(false);
                        throw;
                    }
                    catch (OperationCanceledException ex)
                    {
                        await this.CloseOutputAutoTimeoutAsync(WebSocketCloseStatus.EndpointUnavailable, "Operation cancelled", ex).ConfigureAwait(false);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        await this.CloseOutputAutoTimeoutAsync(WebSocketCloseStatus.InternalServerError, "Error reading WebSocket frame", ex).ConfigureAwait(false);
                        throw;
                    }

                    var endOfMessage = frame.IsFinBitSet && this.readCursor.Value.NumBytesLeftToRead == 0;
                    var framePayload = new ArraySegment<byte>(buffer.Array!, buffer.Offset, this.readCursor.Value.NumBytesRead);

                    switch (frame.OpCode)
                    {
                        case WebSocketOpCode.ConnectionClose:
                            return await this.RespondToCloseFrameAsync(frame, linkedCts.Token).ConfigureAwait(false);
                        case WebSocketOpCode.Ping:
                            await this.SendPongAsync(framePayload, linkedCts.Token).ConfigureAwait(false);
                            break;
                        case WebSocketOpCode.Pong:
                            this.pingSentTicks = 0;
                            break;
                        case WebSocketOpCode.TextFrame:
                        case WebSocketOpCode.BinaryFrame:
                            return await this.HandleDataFrameAsync(frame, framePayload, endOfMessage, buffer, linkedCts.Token).ConfigureAwait(false);
                        case WebSocketOpCode.ContinuationFrame:
                            return await this.HandleContinuationFrameAsync(framePayload, endOfMessage, buffer, linkedCts.Token).ConfigureAwait(false);
                        default:
                            Exception ex = new NotSupportedException($"Unknown WebSocket opcode {frame.OpCode}");
                            await this.CloseOutputAutoTimeoutAsync(WebSocketCloseStatus.ProtocolError, ex.Message, ex).ConfigureAwait(false);
                            throw ex;
                    }
                }
            }
            catch (Exception catchAll)
            {
                if (this.state == WebSocketState.Open)
                    await this.CloseOutputAutoTimeoutAsync(WebSocketCloseStatus.InternalServerError, "Unexpected error reading from WebSocket", catchAll).ConfigureAwait(false);
                throw;
            }
        }

        private ValueTask<WebSocketReceiveResult> HandleDataFrameAsync(WebSocketFrame frame, ArraySegment<byte> framePayload, bool endOfMessage, ArraySegment<byte> userBuffer, CancellationToken cancellationToken)
        {
            var messageType = frame.OpCode == WebSocketOpCode.TextFrame ? WebSocketMessageType.Text : WebSocketMessageType.Binary;

#if NET8_0_OR_GREATER
            if (this.inflater != null)
            {
                if (!frame.IsFinBitSet)
                {
                    // Start of a fragmented compressed message - begin collecting
                    this.isCollectingCompressedMessage = true;
                    this.collectedMessageType = messageType;

                    // Feed the frame to the inflater (won't return data until endOfMessage)
                    this.inflater.Write(framePayload);

                    // Continue reading more frames
                    return this.ReceiveCoreAsync(userBuffer, cancellationToken);
                }
                else
                {
                    // Single compressed frame - decompress and return
                    this.inflater.Write(framePayload);
                    return ValueTask.FromResult(this.HandleDecompressedMessage(messageType, userBuffer));
                }
            }
            else
            {
                // Uncompressed message - handle normally
                if (!frame.IsFinBitSet)
                    this.continuationFrameMessageType = messageType;

                return ValueTask.FromResult(new WebSocketReceiveResult(this.readCursor!.Value.NumBytesRead, messageType, endOfMessage));
            }
#else
            // Uncompressed message - handle normally
            if (frame.IsFinBitSet)
                this.continuationFrameMessageType = messageType;

            return new ValueTask<WebSocketReceiveResult>(new WebSocketReceiveResult(this.readCursor!.Value.NumBytesRead, messageType, endOfMessage));
#endif
        }

        private ValueTask<WebSocketReceiveResult> HandleContinuationFrameAsync(ArraySegment<byte> framePayload, bool endOfMessage, ArraySegment<byte> userBuffer, CancellationToken cancellationToken)
        {
#if NET8_0_OR_GREATER
            if (this.isCollectingCompressedMessage && this.inflater != null)
            {
                // Part of a compressed message - feed to inflater
                this.inflater.Write(framePayload);

                if (endOfMessage)
                {
                    // End of compressed message - we now have the full decompressed data
                    this.isCollectingCompressedMessage = false;
                    return ValueTask.FromResult(this.HandleDecompressedMessage(this.collectedMessageType, userBuffer));
                }
                else
                {
                    // Continue collecting compressed fragments
                    return this.ReceiveCoreAsync(userBuffer, cancellationToken);
                }
            }
            else
            {
                // Normal uncompressed continuation frame
                return ValueTask.FromResult(new WebSocketReceiveResult(this.readCursor!.Value.NumBytesRead, this.continuationFrameMessageType, endOfMessage));
            }
#else
            return new ValueTask<WebSocketReceiveResult>(new WebSocketReceiveResult(this.readCursor!.Value.NumBytesRead, this.continuationFrameMessageType, endOfMessage));
#endif
        }
#if NET8_0_OR_GREATER
        private WebSocketReceiveResult HandleDecompressedMessage(WebSocketMessageType messageType, ArraySegment<byte> userBuffer)
        {
            var decompressed = this.inflater!.Read();
            var decompressedData = decompressed.GetDataArraySegment();

            if (decompressed.Length <= userBuffer.Count)
            {
                Array.Copy(decompressedData.Array!, decompressedData.Offset, userBuffer.Array!, userBuffer.Offset, decompressedData.Count);
                return new WebSocketReceiveResult(decompressedData.Count, messageType, true);
            }
            else
            {
                // Decompressed data is larger than user buffer - need to return it in chunks
                var bytesToCopy = userBuffer.Count;

                Array.Copy(decompressedData.Array!, decompressedData.Offset, userBuffer.Array!, userBuffer.Offset, bytesToCopy);

                // Store the remaining data for subsequent calls
                var remainingBytes = decompressedData.Count - bytesToCopy;
                if (this.pendingDecompressedData.HasValue)
                    Shared.Return(this.pendingDecompressedData.Value);

                this.pendingDecompressedData = new ArraySegment<byte>(decompressedData.Array!, decompressedData.Offset + bytesToCopy, remainingBytes);
                this.pendingDataOffset = 0;

                return new WebSocketReceiveResult(bytesToCopy, messageType, false); // Not end of message yet
            }
        }

        private WebSocketReceiveResult ReturnPendingData(ArraySegment<byte> pendingDecompressedData, ArraySegment<byte> userBuffer)
        {
            var availableBytes = pendingDecompressedData.Count - this.pendingDataOffset;
            var bytesToCopy = Math.Min(availableBytes, userBuffer.Count);

            Array.Copy(
                pendingDecompressedData.Array!,
                pendingDecompressedData.Offset + this.pendingDataOffset,
                userBuffer.Array!,
                userBuffer.Offset,
                bytesToCopy);

            this.pendingDataOffset += bytesToCopy;

            var isEndOfMessage = this.pendingDataOffset >= pendingDecompressedData.Count;
            if (isEndOfMessage)
            {
                // All pending data has been returned
                Shared.Return(pendingDecompressedData);
                this.pendingDecompressedData = null;
                this.pendingDataOffset = 0;
            }

            return new WebSocketReceiveResult(bytesToCopy, this.collectedMessageType, isEndOfMessage);
        }
#endif
        /// <summary>
        /// Send data to the web socket
        /// </summary>
        /// <param name="buffer">the buffer containing data to send</param>
        /// <param name="messageType">The message type. Can be Text or Binary</param>
        /// <param name="endOfMessage">True if this message is a standalone message (this is the norm)
        /// If it is a multi-part message then false (and true for the last message)</param>
        /// <param name="cancellationToken">the cancellation token</param>
        public async override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            try
            {
                var opCode = this.GetOppCode(messageType);
                var msOpCode = this.isContinuationFrame ? WebSocketOpCode.ContinuationFrame : opCode;
#if NET8_0_OR_GREATER
                if (this.deflater != null && (messageType == WebSocketMessageType.Binary || messageType == WebSocketMessageType.Text))
                {
                    this.deflater.Write(buffer);
                    if (endOfMessage)
                    {
                        using var deflated = this.deflater.Read();
                        var frame = deflated.GetDataArraySegment();

                        using var stream = new ArrayPoolStream();
                        WebSocketFrameWriter.Write(opCode, frame, stream, endOfMessage, this.isClient, true, !this.isContinuationFrame);
                        var arr = stream.GetDataArraySegment();
                        Events.Log?.SendingFrame(this.Context.Guid, opCode, endOfMessage, frame.Count, true);
                        await this.WriteStreamToNetworkAsync(stream, cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {

                    using var stream = new ArrayPoolStream();
                    WebSocketFrameWriter.Write(msOpCode, buffer, stream, endOfMessage, this.isClient, false, !this.isContinuationFrame);
                    Events.Log?.SendingFrame(this.Context.Guid, msOpCode, endOfMessage, buffer.Count, false);
                    await this.WriteStreamToNetworkAsync(stream, cancellationToken).ConfigureAwait(false);
                }
#else
                using var stream = new ArrayPoolStream();
                WebSocketFrameWriter.Write(msOpCode, buffer, stream, endOfMessage, this.isClient, false, !this.isContinuationFrame);
                Events.Log?.SendingFrame(this.Context.Guid, msOpCode, endOfMessage, buffer.Count, false);
                await this.WriteStreamToNetworkAsync(stream, cancellationToken).ConfigureAwait(false);
#endif
                this.isContinuationFrame = !endOfMessage;
            }
            catch (Exception e)
            {
                await this.CloseAsync(WebSocketCloseStatus.InternalServerError, e.Message, cancellationToken);
                throw;
            }

        }

        /// <summary>
        /// Aborts the WebSocket without sending a Close frame
        /// </summary>
        public override void Abort()
        {
            this.state = WebSocketState.Aborted;
            this.internalReadCts.Cancel();
        }

        /// <summary>
        /// Polite close (use the close handshake)
        /// </summary>
        public async override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {

            if (this.state != WebSocketState.Open)
            {
                Events.Log?.InvalidStateBeforeClose(this.Context.Guid, this.state);
                return;
            }

            using var stream = new ArrayPoolStream();
            (var buffer, var doReturn) = BuildClosePayload(closeStatus, statusDescription);
            try
            {
                WebSocketFrameWriter.Write(WebSocketOpCode.ConnectionClose, buffer, stream, true, this.isClient);
                Events.Log?.CloseHandshakeStarted(this.Context.Guid, closeStatus, statusDescription);
                Events.Log?.SendingFrame(this.Context.Guid, WebSocketOpCode.ConnectionClose, true, buffer.Count, true);
            }
            finally
            {
                if (doReturn)
                    Shared.Return(buffer);
            }
            await this.WriteStreamToNetworkAsync(stream, cancellationToken).ConfigureAwait(false);
            this.state = WebSocketState.CloseSent;

            // For server-side connections, wait for client to respond to close handshake with timeout
            // If client doesn't respond within reasonable time, force cleanup to handle misbehaving clients
            if (!this.isClient)
            {
                var closeHandshakeTimeout = TimeSpan.FromMilliseconds(100);
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                try
                {
                    // Wait for the client to close its side or timeout
                    while (this.state == WebSocketState.CloseSent && !combinedCts.Token.IsCancellationRequested)
                    {
                        await Task.Delay(10, combinedCts.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Client didn't respond to close handshake within timeout
                    Events.Log?.CloseHandshakeTimedOut(this.Context.Guid, (int)closeHandshakeTimeout.TotalMilliseconds);
                }

                // Force cleanup of misbehaving clients by disposing the underlying connection
                if (this.state != WebSocketState.Closed)
                {
                    this.state = WebSocketState.Closed;
                    this.Dispose();
                }
            }
        }

        /// <summary>
        /// Fire and forget close
        /// </summary>
        public async override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            using (this)
            {
                if (this.state == WebSocketState.Open)
                {
                    this.state = WebSocketState.Closed; // set this before we write to the network because the write may fail

                    using var stream = new ArrayPoolStream();
                    (var buffer, var doReturn) = BuildClosePayload(closeStatus, statusDescription);
                    try
                    {
                        WebSocketFrameWriter.Write(WebSocketOpCode.ConnectionClose, buffer, stream, true, this.isClient);
                        Events.Log?.CloseOutputNoHandshake(this.Context.Guid, closeStatus, statusDescription);
                        Events.Log?.SendingFrame(this.Context.Guid, WebSocketOpCode.ConnectionClose, true, buffer.Count, true);
                    }
                    finally
                    {
                        if (doReturn)
                            Shared.Return(buffer);
                    }

                    await this.WriteStreamToNetworkAsync(stream, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    Events.Log?.InvalidStateBeforeCloseOutput(this.Context.Guid, this.state);
                }

                // cancel pending reads
                this.internalReadCts.Cancel();
            }
        }

        /// <summary>
        /// Dispose will send a close frame if the connection is still open
        /// </summary>
        public override void Dispose()
        {
            Events.Log?.WebSocketDispose(this.Context.Guid, this.state);

            try
            {
                if (this.state == WebSocketState.Open)
                {
                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try
                    {
                        // TODO: I Don't like that we don't await this.
                        // Now we can't dispose the semaphor and internalReadCts.
                        this.CloseOutputAsync(WebSocketCloseStatus.EndpointUnavailable, "Service is Disposed", cts.Token).Wait();
                    }
                    catch (OperationCanceledException)
                    {
                        // log don't throw
                        Events.Log?.WebSocketDisposeCloseTimeout(this.Context.Guid, this.state);
                    }
                }

                // cancel pending reads - usually does nothing
                this.internalReadCts.Cancel();
                this.Context.Stream.Close();

#if NET8_0_OR_GREATER
                this.inflater?.Dispose();
                this.deflater?.Dispose();
#endif
                this.Context.TcpClient?.Dispose();
            }
            catch (Exception ex)
            {
                // log dont throw
                Events.Log?.WebSocketDisposeError(this.Context.Guid, this.state, ex);
            }
        }

        private async ValueTask PingForeverAsync(CancellationToken cancellationToken)
        {
            Events.Log?.PingPongStarted(this.Context.Guid, (int)this.KeepAliveInterval.TotalSeconds);

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(this.KeepAliveInterval, cancellationToken).ConfigureAwait(false);

                    if (this.State != WebSocketState.Open)
                        return;

                    if (this.pingSentTicks != 0)
                    {
                        Events.Log?.KeepAliveIntervalExpired(this.Context.Guid, (int)this.KeepAliveInterval.TotalSeconds);
                        await this.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "No Pong message received in response to a Ping after KeepAliveInterval {this.KeepAliveInterval}",
                            cancellationToken)
                        .ConfigureAwait(false);
                        return;
                    }

                    if (!cancellationToken.IsCancellationRequested)
                    {
                        this.pingSentTicks = this.stopwatch.Elapsed.Ticks;
                        await this.SendPingAsync(
                            new ArraySegment<byte>(BitConverter.GetBytes(this.pingSentTicks)),
                            cancellationToken)
                        .ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // normal, do nothing
            }
            finally
            {
                Events.Log?.PingPongEnded(this.Context.Guid);
            }
        }


        private async ValueTask SendPingAsync(ArraySegment<byte> payload, CancellationToken cancellationToken)
        {
            if (payload.Count > MAX_PING_PONG_PAYLOAD_LEN)
                throw new InvalidOperationException($"Cannot send Ping: Max ping message size {MAX_PING_PONG_PAYLOAD_LEN} exceeded: {payload.Count}");

            if (this.state == WebSocketState.Open)
            {
                using var stream = new ArrayPoolStream();
                WebSocketFrameWriter.Write(WebSocketOpCode.Ping, payload, stream, true, this.isClient);
                Events.Log?.SendingFrame(this.Context.Guid, WebSocketOpCode.Ping, true, payload.Count, false);
                await this.WriteStreamToNetworkAsync(stream, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// As per the spec, write the close status followed by the close reason
        /// </summary>
        /// <param name="closeStatus">The close status</param>
        /// <param name="statusDescription">Optional extra close details</param>
        /// <returns>The payload to sent in the close frame</returns>
        private static (ArraySegment<byte>, bool Return) BuildClosePayload(WebSocketCloseStatus closeStatus, string? statusDescription)
        {
            var statusBuffer = BitConverter.GetBytes((ushort)closeStatus);
            Array.Reverse(statusBuffer); // network byte order (big endian)

            if (statusDescription is null)
                return (new ArraySegment<byte>(statusBuffer), false);

            var descBuffer = Encoding.UTF8.GetBytes(statusDescription);
            var size = statusBuffer.Length + descBuffer.Length;
            var payload = Shared.Rent(size);
            Buffer.BlockCopy(statusBuffer, 0, payload, 0, statusBuffer.Length);
            Buffer.BlockCopy(descBuffer, 0, payload, statusBuffer.Length, descBuffer.Length);
            return (new ArraySegment<byte>(payload, 0, size), true);
        }

        /// <summary>
        /// NOTE: pong payload must be 125 bytes or less
        /// Pong should contain the same payload as the ping
        /// </summary>
        /// <param name="payload">The payload of the ping.</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns></returns>
        private async ValueTask SendPongAsync(ArraySegment<byte> payload, CancellationToken cancellationToken)
        {
            // as per websocket spec
            if (payload.Count > MAX_PING_PONG_PAYLOAD_LEN)
            {
                var ex = new InvalidOperationException($"Max ping message size {MAX_PING_PONG_PAYLOAD_LEN} exceeded: {payload.Count}");
                await this.CloseOutputAutoTimeoutAsync(WebSocketCloseStatus.ProtocolError, ex.Message, ex).ConfigureAwait(false);
                throw ex;
            }

            try
            {
                if (this.state == WebSocketState.Open)
                {
                    using var stream = new ArrayPoolStream();
                    WebSocketFrameWriter.Write(WebSocketOpCode.Pong, payload, stream, true, this.isClient);
                    Events.Log?.SendingFrame(this.Context.Guid, WebSocketOpCode.Pong, true, payload.Count, false);
                    await this.WriteStreamToNetworkAsync(stream, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                await this.CloseOutputAutoTimeoutAsync(WebSocketCloseStatus.EndpointUnavailable, "Unable to send Pong response", ex).ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Called when a Close frame is received
        /// Send a response close frame if applicable
        /// </summary>
        private async ValueTask<WebSocketReceiveResult> RespondToCloseFrameAsync(WebSocketFrame frame, CancellationToken cancellationToken)
        {
            this.closeStatus = frame.CloseStatus;
            this.closeStatusDescription = frame.CloseStatusDescription;

            if (this.state == WebSocketState.CloseSent)
            {
                // this is a response to close handshake initiated by this instance
                this.state = WebSocketState.Closed;
                Events.Log?.CloseHandshakeComplete(this.Context.Guid);
            }
            else if (this.state == WebSocketState.Open)
            {
                // do not echo the close payload back to the client, there is no requirement for it in the spec. 
                // However, the same CloseStatus as recieved should be sent back.
                var closePayload = new ArraySegment<byte>(EMPTY, 0, 0);
                this.state = WebSocketState.CloseReceived;
                Events.Log?.CloseHandshakeRespond(this.Context.Guid, frame.CloseStatus, frame.CloseStatusDescription);

                using (var stream = new ArrayPoolStream())
                {
                    WebSocketFrameWriter.Write(WebSocketOpCode.ConnectionClose, closePayload, stream, true, this.isClient);
                    Events.Log?.SendingFrame(this.Context.Guid, WebSocketOpCode.ConnectionClose, true, closePayload.Count, false);
                    await this.WriteStreamToNetworkAsync(stream, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                Events.Log?.CloseFrameReceivedInUnexpectedState(this.Context.Guid, this.state, frame.CloseStatus, frame.CloseStatusDescription);
            }

            return new WebSocketReceiveResult(frame.Count, WebSocketMessageType.Close, frame.IsFinBitSet, frame.CloseStatus, frame.CloseStatusDescription);
        }


        /// <summary>
        /// Puts data on the wire
        /// </summary>
        /// <param name="stream">The stream to read data from</param>
        private async ValueTask WriteStreamToNetworkAsync(ArrayPoolStream stream, CancellationToken cancellationToken)
        {
            var buffer = stream.GetDataArraySegment();
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(this.internalReadCts.Token, cancellationToken);
            await this.semaphore.WaitAsync(linkedCts.Token).ConfigureAwait(false);
            try
            {
                await this.Context.Stream.WriteAsync(buffer.Array!, buffer.Offset, buffer.Count, linkedCts.Token).ConfigureAwait(false);
            }
            finally
            {
                this.semaphore.Release();
            }
        }

        /// <summary>
        /// Turns a spec websocket frame opcode into a WebSocketMessageType
        /// </summary>
        private WebSocketOpCode GetOppCode(WebSocketMessageType messageType)
        {
            switch (messageType)
            {
                case WebSocketMessageType.Binary:
                    return WebSocketOpCode.BinaryFrame;
                case WebSocketMessageType.Text:
                    return WebSocketOpCode.TextFrame;
                case WebSocketMessageType.Close:
                    throw new NotSupportedException("Cannot use Send function to send a close frame. Use Close function.");
                default:
                    throw new NotSupportedException($"MessageType {messageType} not supported");
            }
        }

        /// <summary>
        /// Automatic WebSocket close in response to some invalid data from the remote websocket host
        /// </summary>
        /// <param name="closeStatus">The close status to use</param>
        /// <param name="statusDescription">A description of why we are closing</param>
        /// <param name="ex">The exception (for logging)</param>
        private async ValueTask CloseOutputAutoTimeoutAsync(WebSocketCloseStatus closeStatus, string statusDescription, Exception ex)
        {
            var timeSpan = TimeSpan.FromSeconds(5);
            Events.Log?.CloseOutputAutoTimeout(this.Context.Guid, closeStatus, statusDescription, ex);

            try
            {
                // we may not want to send sensitive information to the client / server
                if (this.includeExceptionInCloseResponse)
                {
                    statusDescription = statusDescription + "\r\n\r\n" + ex.ToString();
                }

                using var autoCancel = new CancellationTokenSource(timeSpan);
                await this.CloseOutputAsync(closeStatus, statusDescription, autoCancel.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // do not throw an exception because that will mask the original exception
                Events.Log?.CloseOutputAutoTimeoutCancelled(this.Context.Guid, (int)timeSpan.TotalSeconds, closeStatus, statusDescription, ex);
            }
            catch (Exception closeException)
            {
                // do not throw an exception because that will mask the original exception
                Events.Log?.CloseOutputAutoTimeoutError(this.Context.Guid, closeException, closeStatus, statusDescription, ex);
            }
        }
    }
}
