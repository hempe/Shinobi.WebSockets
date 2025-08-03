// ---------------------------------------------------------------------
// Copyright 2018 David Haig
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
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#if RELEASESIGNED
[assembly: InternalsVisibleTo("Samurai.WebSockets.UnitTests, PublicKey=0024000004800000940000000602000000240000525341310004000001000100b1707056f4761b7846ed503642fcde97fc350c939f78026211304a56ba51e094c9cefde77fadce5b83c0a621c17f032c37c520b6d9ab2da8291a21472175d9caad55bf67bab4bffb46a96f864ea441cf695edc854296e02a44062245a4e09ccd9a77ef6146ecf941ce1d9da078add54bc2d4008decdac2fa2b388e17794ee6a6")]
#else
[assembly: InternalsVisibleTo("Samurai.WebSockets.UnitTests")]
#endif


namespace Samurai.WebSockets.Internal
{

    /// <summary>
    /// Main implementation of the WebSocket abstract class
    /// </summary>
    public sealed class SamuraiWebSocket : WebSocket
    {
        private const int MAX_PING_PONG_PAYLOAD_LEN = 125;
        private static readonly byte[] EMPTY = new byte[0];
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1);
        private readonly Stopwatch stopwatch;
        private readonly Guid guid;
        private readonly Stream stream;
        private readonly bool includeExceptionInCloseResponse;
        private readonly bool isClient;
        private readonly CancellationTokenSource internalReadCts;

        private WebSocketState state;
        private bool isContinuationFrame;
        private WebSocketMessageType continuationFrameMessageType = WebSocketMessageType.Binary;
        private WebSocketReadCursor? readCursor;
        private readonly PerMessageDeflateHandler? perMessageDeflateHandler;
        private WebSocketCloseStatus? closeStatus;
        private string? closeStatusDescription;
        private long pingSentTicks;

        public SamuraiWebSocket(
            Guid guid,
            Stream stream,
            TimeSpan keepAliveInterval,
            bool permessageDeflate,
            bool includeExceptionInCloseResponse,
            bool isClient,
            string? subProtocol)
        {
            this.guid = guid;
            this.stream = stream;
            this.isClient = isClient;
            this.SubProtocol = subProtocol;
            this.internalReadCts = new CancellationTokenSource();
            this.state = WebSocketState.Open;
            this.stopwatch = Stopwatch.StartNew();

            if (permessageDeflate)
            {
                this.perMessageDeflateHandler = new PerMessageDeflateHandler();
                Events.Log.UsePerMessageDeflate(guid);
            }
            else
            {
                throw new Exception("It should be on");
                this.perMessageDeflateHandler = null;
                Events.Log.NoMessageCompression(guid);
            }

            this.KeepAliveInterval = keepAliveInterval;
            this.includeExceptionInCloseResponse = includeExceptionInCloseResponse;
            if (keepAliveInterval.Ticks < 0)
            {
                throw new InvalidOperationException("KeepAliveInterval must be Zero or positive");
            }

            if (keepAliveInterval == TimeSpan.Zero)
            {
                Events.Log.KeepAliveIntervalZero(guid);
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
            try
            {
                // we may receive control frames so reading needs to happen in an infinite loop
                while (true)
                {
                    // allow this operation to be cancelled from iniside OR outside this instance
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(this.internalReadCts.Token, cancellationToken);

                    WebSocketFrame frame;

                    try
                    {
                        if (this.readCursor.HasValue && this.readCursor.Value.NumBytesLeftToRead > 0)
                        {
                            // If the buffer used to read the frame was too small to fit the whole frame then we need to "remember" this frame
                            // and return what we have. Subsequent calls to the read function will simply continue reading off the stream without 
                            // decoding the first few bytes as a websocket header.
                            this.readCursor = await WebSocketFrameReader.ReadFromCursorAsync(this.stream, buffer, this.readCursor.Value, linkedCts.Token).ConfigureAwait(false);
                            frame = this.readCursor.Value.WebSocketFrame;
                        }
                        else
                        {
                            this.readCursor = await WebSocketFrameReader.ReadAsync(this.stream, buffer, linkedCts.Token).ConfigureAwait(false);
                            frame = this.readCursor.Value.WebSocketFrame;
                            Events.Log.ReceivedFrame(this.guid, frame.OpCode, frame.IsFinBitSet, frame.Count);
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
                    switch (frame.OpCode)
                    {
                        case WebSocketOpCode.ConnectionClose:
                            return await this.RespondToCloseFrameAsync(frame, linkedCts.Token).ConfigureAwait(false);
                        case WebSocketOpCode.Ping:
                            ArraySegment<byte> pingPayload = new ArraySegment<byte>(buffer.Array, buffer.Offset, this.readCursor.Value.NumBytesRead);
                            await this.SendPongAsync(pingPayload, linkedCts.Token).ConfigureAwait(false);
                            break;
                        case WebSocketOpCode.Pong:
                            this.pingSentTicks = 0;
                            break;
                        case WebSocketOpCode.TextFrame:
                            if (!frame.IsFinBitSet)
                            {
                                // continuation frames will follow, record the message type Text
                                this.continuationFrameMessageType = WebSocketMessageType.Text;
                            }
                            return new WebSocketReceiveResult(this.readCursor.Value.NumBytesRead, WebSocketMessageType.Text, endOfMessage);
                        case WebSocketOpCode.BinaryFrame:
                            if (!frame.IsFinBitSet)
                            {
                                // continuation frames will follow, record the message type Binary
                                this.continuationFrameMessageType = WebSocketMessageType.Binary;
                            }
                            return new WebSocketReceiveResult(this.readCursor.Value.NumBytesRead, WebSocketMessageType.Binary, endOfMessage);
                        case WebSocketOpCode.ContinuationFrame:
                            return new WebSocketReceiveResult(this.readCursor.Value.NumBytesRead, this.continuationFrameMessageType, endOfMessage);
                        default:
                            Exception ex = new NotSupportedException($"Unknown WebSocket opcode {frame.OpCode}");
                            await this.CloseOutputAutoTimeoutAsync(WebSocketCloseStatus.ProtocolError, ex.Message, ex).ConfigureAwait(false);
                            throw ex;
                    }
                }
            }
            catch (Exception catchAll)
            {
                // Most exceptions will be caught closer to their source to send an appropriate close message (and set the WebSocketState)
                // However, if an unhandled exception is encountered and a close message not sent then send one here
                if (this.state == WebSocketState.Open)
                    await this.CloseOutputAutoTimeoutAsync(WebSocketCloseStatus.InternalServerError, "Unexpected error reading from WebSocket", catchAll).ConfigureAwait(false);

                throw;
            }
        }

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
                if (this.perMessageDeflateHandler != null && (opCode == WebSocketOpCode.BinaryFrame || opCode == WebSocketOpCode.TextFrame))
                {
                    this.perMessageDeflateHandler.Write(buffer, messageType, opCode);
                    Events.Log.BufferDeflateFrame(this.guid, opCode, buffer.Count);
                    var cunkBuffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
                    try
                    {
                        foreach (var frame in this.perMessageDeflateHandler.GetFames(cunkBuffer))
                        {
                            using var stream = new ArrayPoolStream();
                            WebSocketFrameWriter.Write(frame.OpCode, new ArraySegment<byte>(cunkBuffer, 0, frame.Count), stream, frame.LastFrame && endOfMessage, this.isClient);
                            Events.Log.SendingFrame(this.guid, frame.OpCode, frame.LastFrame, frame.Count, true);
                            await this.WriteStreamToNetworkAsync(stream, cancellationToken).ConfigureAwait(false);
                            this.isContinuationFrame = !frame.LastFrame;
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(cunkBuffer);
                    }

                    if (endOfMessage)
                        this.perMessageDeflateHandler.Reset();
                }
                else
                {
                    var msOpCode = this.isContinuationFrame ? WebSocketOpCode.ContinuationFrame : opCode;
                    using var stream = new ArrayPoolStream();
                    WebSocketFrameWriter.Write(msOpCode, buffer, stream, endOfMessage, this.isClient);
                    Events.Log.SendingFrame(this.guid, msOpCode, endOfMessage, buffer.Count, false);
                    await this.WriteStreamToNetworkAsync(stream, cancellationToken).ConfigureAwait(false);
                }
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
                Events.Log.InvalidStateBeforeClose(this.guid, this.state);
                return;
            }

            using var stream = new ArrayPoolStream();
            (var buffer, var doReturn) = BuildClosePayload(closeStatus, statusDescription);
            try
            {
                WebSocketFrameWriter.Write(WebSocketOpCode.ConnectionClose, buffer, stream, true, this.isClient);
                Events.Log.CloseHandshakeStarted(this.guid, closeStatus, statusDescription);
                Events.Log.SendingFrame(this.guid, WebSocketOpCode.ConnectionClose, true, buffer.Count, true);
            }
            finally
            {
                if (doReturn)
                    ArrayPool<byte>.Shared.Return(buffer.Array);
            }
            await this.WriteStreamToNetworkAsync(stream, cancellationToken).ConfigureAwait(false);
            this.state = WebSocketState.CloseSent;
        }

        /// <summary>
        /// Fire and forget close
        /// </summary>
        public async override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
        {
            if (this.state == WebSocketState.Open)
            {
                this.state = WebSocketState.Closed; // set this before we write to the network because the write may fail

                using var stream = new ArrayPoolStream();
                (var buffer, var doReturn) = BuildClosePayload(closeStatus, statusDescription);
                try
                {
                    WebSocketFrameWriter.Write(WebSocketOpCode.ConnectionClose, buffer, stream, true, this.isClient);
                    Events.Log.CloseOutputNoHandshake(this.guid, closeStatus, statusDescription);
                    Events.Log.SendingFrame(this.guid, WebSocketOpCode.ConnectionClose, true, buffer.Count, true);
                }
                finally
                {
                    if (doReturn)
                        ArrayPool<byte>.Shared.Return(buffer.Array);
                }

                await this.WriteStreamToNetworkAsync(stream, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                Events.Log.InvalidStateBeforeCloseOutput(this.guid, this.state);
            }

            // cancel pending reads
            this.internalReadCts.Cancel();
        }

        /// <summary>
        /// Dispose will send a close frame if the connection is still open
        /// </summary>
        public override void Dispose()
        {
            Events.Log.WebSocketDispose(this.guid, this.state);

            try
            {
                if (this.state == WebSocketState.Open)
                {
                    CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try
                    {
                        // TODO: I Don't like that we don't await this.
                        // Now we can't dispose the semaphor and internalReadCts.
                        this.CloseOutputAsync(WebSocketCloseStatus.EndpointUnavailable, "Service is Disposed", cts.Token).Wait();
                    }
                    catch (OperationCanceledException)
                    {
                        // log don't throw
                        Events.Log.WebSocketDisposeCloseTimeout(this.guid, this.state);
                    }
                }

                // cancel pending reads - usually does nothing
                this.internalReadCts.Cancel();
                this.stream.Close();
                this.perMessageDeflateHandler?.Dispose();
            }
            catch (Exception ex)
            {
                // log dont throw
                Events.Log.WebSocketDisposeError(this.guid, this.state, ex.ToString());
            }
        }

        private async ValueTask PingForeverAsync(CancellationToken cancellationToken)
        {
            Events.Log.PingPongStarted(this.guid, (int)this.KeepAliveInterval.TotalSeconds);

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(this.KeepAliveInterval, cancellationToken).ConfigureAwait(false);

                    if (this.State != WebSocketState.Open)
                        return;

                    if (this.pingSentTicks != 0)
                    {
                        Events.Log.KeepAliveIntervalExpired(this.guid, (int)this.KeepAliveInterval.TotalSeconds);
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
                Events.Log.PingPongEnded(this.guid);
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
                Events.Log.SendingFrame(this.guid, WebSocketOpCode.Ping, true, payload.Count, false);
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
            var payload = ArrayPool<byte>.Shared.Rent(size);
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
                    Events.Log.SendingFrame(this.guid, WebSocketOpCode.Pong, true, payload.Count, false);
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
                Events.Log.CloseHandshakeComplete(this.guid);
            }
            else if (this.state == WebSocketState.Open)
            {
                // do not echo the close payload back to the client, there is no requirement for it in the spec. 
                // However, the same CloseStatus as recieved should be sent back.
                var closePayload = new ArraySegment<byte>(EMPTY, 0, 0);
                this.state = WebSocketState.CloseReceived;
                Events.Log.CloseHandshakeRespond(this.guid, frame.CloseStatus, frame.CloseStatusDescription);

                using (var stream = new ArrayPoolStream())
                {
                    WebSocketFrameWriter.Write(WebSocketOpCode.ConnectionClose, closePayload, stream, true, this.isClient);
                    Events.Log.SendingFrame(this.guid, WebSocketOpCode.ConnectionClose, true, closePayload.Count, false);
                    await this.WriteStreamToNetworkAsync(stream, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                Events.Log.CloseFrameReceivedInUnexpectedState(this.guid, this.state, frame.CloseStatus, frame.CloseStatusDescription);
            }

            return new WebSocketReceiveResult(frame.Count, WebSocketMessageType.Close, frame.IsFinBitSet, frame.CloseStatus, frame.CloseStatusDescription);
        }


        /// <summary>
        /// Puts data on the wire
        /// </summary>
        /// <param name="stream">The stream to read data from</param>
        private async ValueTask WriteStreamToNetworkAsync(ArrayPoolStream stream, CancellationToken cancellationToken)
        {
            var buffer = stream.GetArraySegmentBuffer();
            await this.semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await this.stream.WriteAsync(buffer.Array, 0, buffer.Count, cancellationToken).ConfigureAwait(false);
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
            Events.Log.CloseOutputAutoTimeout(this.guid, closeStatus, statusDescription, ex);

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
                Events.Log.CloseOutputAutoTimeoutCancelled(this.guid, (int)timeSpan.TotalSeconds, closeStatus, statusDescription, ex);
            }
            catch (Exception closeException)
            {
                // do not throw an exception because that will mask the original exception
                Events.Log.CloseOutputAutoTimeoutError(this.guid, closeException, closeStatus, statusDescription, ex);
            }
        }
    }
}
