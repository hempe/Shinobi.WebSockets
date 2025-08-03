using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;

using Samurai.WebSockets.Extensions;

using Microsoft.Extensions.Logging;


[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[MinIterationCount(5)]
[MaxIterationCount(15)]
public class WebSocketThroughputBenchmarks
{
    private WebSocket[] clients;
    private int port;
    private string serverUrl;
    private CancellationTokenSource serverCts;
    private Task<Action> serverTask;


    //[Params(1_000, 10_000)]
    public int MessageCount { get; set; } = 10_000;

    //[Params(16, 64)]
    [Params(1, 17)]
    public int MessageSizeKb { get; set; } = 16;

    //[Params(1000)]
    public int ClientCount { get; set; } = 1_00;

    //[Params("Ninja", "Samurai", "Samurai.PermessageDeflate")]
    //[Params("Ninja", "Samurai", "Native")]
    [Params("Ninja", "Samurai")]
    public string Server { get; set; } = "Samurai";

    //[Params("Ninja", "Samurai", "Native")]
    [Params("Ninja", "Samurai")]
    public string Client { get; set; } = "Samurai";

    private ArraySegment<byte> data;

    private bool permessageDeflate;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        this.permessageDeflate = this.Server.EndsWith("PermessageDeflate");

        var random = new Random(42069);
        var bytes = ArrayPool<byte>.Shared.Rent(this.MessageSizeKb * 1024);
        random.NextBytes(bytes);
        this.data = new ArraySegment<byte>(bytes, 0, this.MessageSizeKb * 1024);

        this.port = GetAvailablePort();
        this.serverUrl = $"ws://localhost:{this.port}/";
        this.serverCts = new CancellationTokenSource();
        this.clients = new WebSocket[this.ClientCount];


        var serverReady = new TaskCompletionSource<bool>();

        this.serverTask = Task.Run(async () =>
        {
            Action cleanup = null;
            try
            {
                var tasks = new List<Task>();

                if (this.Server == "Ninja")
                {
                    using var listener = new TcpListener(IPAddress.Loopback, this.port);
                    listener.Start();
                    for (var i = 0; i < this.ClientCount; i++)
                    {
                        var tcpClient = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                        var stream = tcpClient.GetStream();
                        using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

                        var server = new Ninja.WebSockets.WebSocketServerFactory();
                        var context = await server.ReadHttpHeaderFromStreamAsync(stream, connectCts.Token).ConfigureAwait(false);
                        if (context.IsWebSocketRequest)
                        {
                            var webSocket = await server.AcceptWebSocketAsync(context, connectCts.Token).ConfigureAwait(false);
                            tasks.Add(this.EchoLoopAsync(webSocket, this.serverCts.Token, [tcpClient, stream]));
                        }
                        else
                        {
                            stream.Dispose();
                            tcpClient.Dispose();
                        }
                    }
                    Console.WriteLine($"All {this.ClientCount} clients connected and ready for WebSocket communication.");
                    serverReady.SetResult(true);
                    listener.Stop();
                }
                else if (this.Server.StartsWith("Samurai"))
                {
                    using var listener = new TcpListener(IPAddress.Loopback, this.port);
                    listener.Start();
                    for (var i = 0; i < this.ClientCount; i++)
                    {
                        var tcpClient = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                        var stream = tcpClient.GetStream();
                        using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

                        var server = new Samurai.WebSockets.WebSocketServerFactory();
                        var options = new Samurai.WebSockets.WebSocketServerOptions { AllowPerMessageDeflate = true };
                        var context = await server.ReadHttpHeaderFromStreamAsync(stream, connectCts.Token).ConfigureAwait(false);
                        if (context.IsWebSocketRequest)
                        {
                            var webSocket = await server.AcceptWebSocketAsync(context, options, connectCts.Token).ConfigureAwait(false);
                            tasks.Add(this.EchoLoopAsync(webSocket, this.serverCts.Token, [tcpClient, stream]));
                        }
                        else
                        {
                            stream.Dispose();
                            tcpClient.Dispose();
                        }
                    }
                    Console.WriteLine($"All {this.ClientCount} clients connected and ready for WebSocket communication.");
                    serverReady.SetResult(true);
                    listener.Stop();
                }
                else if (this.Server == "Native")
                {
                    var listener = new HttpListener();
                    listener.Prefixes.Add($"http://localhost:{port}/");
                    listener.Start();
                    for (var i = 0; i < this.ClientCount; i++)
                    {
                        var context = await listener.GetContextAsync();
                        if (context.Request.IsWebSocketRequest)
                        {
                            var webSocketContext = await context.AcceptWebSocketAsync(null);
                            tasks.Add(this.EchoLoopAsync(webSocketContext.WebSocket, this.serverCts.Token, []));
                        }
                    }
                    Console.WriteLine($"All {this.ClientCount} clients connected and ready for WebSocket communication.");
                    serverReady.SetResult(true);
                    cleanup = () => listener.Stop();
                }
                else
                {
                    throw new NotSupportedException($"{this.Server} is not supported");
                }

                await Task.WhenAll(tasks).WaitAsync(this.serverCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {

                Console.WriteLine($"[{this.Server}] Error in WebSocket server setup: {serverReady.Task.IsCompleted} {this.serverCts.IsCancellationRequested} {ex.ToString()}");
                if (serverReady.Task.IsCompleted && this.serverCts.IsCancellationRequested)
                    return cleanup;

                serverReady.TrySetException(new Exception($"Error in WebSocket setup server: {ex.Message}", ex));
            }
            return cleanup;
        });

        if (this.Client == "Ninja")
        {
            var client = new Ninja.WebSockets.WebSocketClientFactory();
            for (var i = 0; i < this.ClientCount; i++)
            {
                this.clients[i] = await client.ConnectAsync(
                    new Uri(this.serverUrl),
                    new Ninja.WebSockets.WebSocketClientOptions
                    {
                        SecWebSocketExtensions = this.permessageDeflate ? "permessage-deflate" : null
                    },
                    this.serverCts.Token).ConfigureAwait(false);
            }
        }
        else if (this.Client.StartsWith("Samurai"))
        {
            var client = new Samurai.WebSockets.WebSocketClientFactory();
            for (var i = 0; i < this.ClientCount; i++)
            {
                this.clients[i] = await client.ConnectAsync(
                    new Uri(this.serverUrl),
                    new Samurai.WebSockets.WebSocketClientOptions
                    {
                        SecWebSocketExtensions = this.permessageDeflate ? "permessage-deflate" : null
                    },
                    this.serverCts.Token).ConfigureAwait(false);
            }
        }
        else if (this.Client.StartsWith("Native"))
        {
            for (var i = 0; i < this.ClientCount; i++)
            {
                var client = new ClientWebSocket();
                await client.ConnectAsync(new Uri(this.serverUrl), this.serverCts.Token).ConfigureAwait(false);
                this.clients[i] = client;
            }
        }
        else
        {
            throw new NotSupportedException($"{this.Server} is not supported");
        }

        // Wait for server WebSocket with timeout
        using var readyCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await serverReady.Task.WaitAsync(readyCts.Token).ConfigureAwait(false);
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        try
        {
            this.serverCts?.Cancel();

            using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            await Task.WhenAll(this.clients.Select(async client =>
            {
                if (client?.State == WebSocketState.Open)
                {
                    try
                    {
                        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", closeCts.Token).ConfigureAwait(false);
                    }
                    catch { }
                }
                client?.Dispose();
            }));

            try
            {
                var res = await (this.serverTask?.WaitAsync(TimeSpan.FromSeconds(3)) ?? Task.FromResult<Action>(null)).ConfigureAwait(false);
                res?.Invoke();
            }
            catch (TimeoutException)
            {
                // Server task didn't complete in time, that's ok during cleanup
            }
            catch (OperationCanceledException)
            {
                // Expected if server was cancelled
            }
        }
        catch (Exception ex)
        {
            // Log or handle cleanup exceptions if necessary
            Console.WriteLine($"Cleanup failed: {ex.Message}");
            throw;
        }
        finally
        {
            this.serverCts?.Dispose();
            this.clients = null;
            this.serverTask = null;
            ArrayPool<byte>.Shared.Return(this.data.Array);
        }
    }


    [Benchmark]
    public async Task RunThrouputBenchmarkAsync()
    {
        // Console.WriteLine("===== RunThrouputBenchmarkAsync start");
        await Task.WhenAll(this.clients.Select(client => this.RunBenchmarkAsync(client, this.MessageCount / this.clients.Length, CancellationToken.None)));
        // Console.WriteLine("===== RunThrouputBenchmarkAsync end");
    }

    private async Task RunBenchmarkAsync(WebSocket client, int messageCount, CancellationToken cancellationToken)
    {
        using var receiveBuffer = new Samurai.WebSockets.ArrayPoolStream();

        try
        {
            for (var i = 0; i < messageCount; i++)
            {
                receiveBuffer.SetLength(0);

                // Console.WriteLine("Client sending ");
                await client.SendAsync(this.data, WebSocketMessageType.Binary, true, cancellationToken);
                // Console.WriteLine("Client sent ");
                while (!cancellationToken.IsCancellationRequested)
                {
                    var result = await client.ReceiveAsync(receiveBuffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                        return;

                    var message = HandleWebSocketMessage(receiveBuffer, result, this.permessageDeflate);
                    if (message != null)
                    {
                        if (message.Count != this.data.Count)
                            throw new Exception($"[Server] Expected {this.permessageDeflate}: {this.data.Count} got {message.Count}");
                        break;
                    }
                }
            }
        }
        catch (Exception e)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            Console.WriteLine("We dont know in the benchmark loop: " + e.Message);
            throw;
        }
    }

    private static ArraySegment<byte> HandleWebSocketMessage(Samurai.WebSockets.ArrayPoolStream messageBuffer, WebSocketReceiveResult result, bool permessageDeflate)
    {
        try
        {
            // If this completes the message, process it
            if (result.EndOfMessage)
            {
                if (!permessageDeflate)
                    return messageBuffer.GetDataArraySegment();

                messageBuffer.Position = 0;
                using var deflateStream = new DeflateStream(messageBuffer, CompressionMode.Decompress, leaveOpen: true);
                using var decompressedStream = new Samurai.WebSockets.ArrayPoolStream();
                deflateStream.CopyTo(decompressedStream);
                return decompressedStream.GetDataArraySegment();

            }
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WHY ARE YOU LIKE THIS?????????????: {ex.Message}");
            throw;
        }
    }

    private async Task EchoLoopAsync(WebSocket webSocket, CancellationToken cancellationToken, IDisposable[] disposables)
    {
        using (webSocket)
        {
            using var receiveBuffer = new Samurai.WebSockets.ArrayPoolStream();

            try
            {

                while (webSocket.State == WebSocketState.Open)
                {
                    var result = await webSocket.ReceiveAsync(receiveBuffer, cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                        return;

                    var message = HandleWebSocketMessage(receiveBuffer, result, this.permessageDeflate);
                    if (message != null)
                    {
                        if (message.Count != this.data.Count)
                        {
                            throw new Exception($"[Server] Expected {this.permessageDeflate}: {this.data.Count} got {message.Count}");
                        }

                        await webSocket.SendAsync(message, WebSocketMessageType.Binary, result.EndOfMessage, cancellationToken);
                        receiveBuffer.SetLength(0);
                    }
                }

                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", cancellationToken);
            }
            catch (Exception e)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                Console.WriteLine("We dont know in the echo loop: " + e.Message);
                throw;
            }
            finally
            {
                foreach (var disposable in disposables)
                {
                    disposable.Dispose();
                }
            }
        }
    }

    private static byte[] GenerateRandomJsonMessage(Random rand, int targetSizeKb)
    {
        var sb = new StringBuilder();

        sb.Append("{");
        while (Encoding.UTF8.GetByteCount(sb.ToString()) < targetSizeKb * 1024)
        {
            string key = $"key_{rand.Next(0, 100)}";
            string value = $"value_{rand.Next(0, 1000)}";
            sb.AppendFormat("\"{0}\":\"{1}\",", key, value);
        }

        sb.Length--; // Remove last comma
        sb.Append("}");

        return Encoding.UTF8.GetBytes(sb.ToString());
    }
    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

}

