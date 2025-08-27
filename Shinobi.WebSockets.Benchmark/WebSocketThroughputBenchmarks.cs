using System;
using System.Buffers;
using System.Collections.Generic;
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

using Microsoft.Extensions.Logging;

using Shinobi.WebSockets;
using Shinobi.WebSockets.Builders;
using Shinobi.WebSockets.Extensions;
using Shinobi.WebSockets.Http;
using Shinobi.WebSockets.Internal;


[SimpleJob(RuntimeMoniker.Net90)]
//[SimpleJob(RuntimeMoniker.Net472)]
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

    public int MessageCount { get; set; } = 1_000;

    public int MessageSizeKb { get; set; } = 4;

    public int ClientCount { get; set; } = 10;

    [Params("Ninja", "Shinobi", "Native")]
    public string Server { get; set; }

    [Params("Ninja", "Shinobi", "Native")]
    public string Client { get; set; }

    private ArraySegment<byte> data;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder
                .SetMinimumLevel(LogLevel.Trace)
                .AddConsole());

        var random = new Random(42069);
        var bytes = ArrayPool<byte>.Shared.Rent(this.MessageSizeKb * 1024);
        random.NextBytes(bytes);
        this.data = new ArraySegment<byte>(bytes, 0, this.MessageSizeKb * 1024);

        this.port = GetAvailablePort();
        var ssl = this.Server.Contains("SSL");
        this.serverUrl = $"{(ssl ? "wss" : "ws")}://localhost:{this.port}/";
        this.serverCts = new CancellationTokenSource();
        this.clients = new WebSocket[this.ClientCount];


        var serverReady = new TaskCompletionSource<bool>();
        var serverStarted = new TaskCompletionSource<bool>();

        this.serverTask = Task.Run(async () =>
        {
            Action cleanup = null;
            try
            {
                var tasks = new List<Task>();

                if (this.Server == "Ninja")
                {
                    var listener = new TcpListener(IPAddress.Loopback, this.port);
                    listener.Start();
                    serverStarted.TrySetResult(true);
                    for (var i = 0; i < this.ClientCount; i++)
                    {
                        var tcpClient = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                        var stream = tcpClient.GetStream();
                        using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

                        var httpRequest = await HttpRequest.ReadAsync(stream, connectCts.Token);
                        if (httpRequest == null)
                            continue;
                        var context = new WebSocketHttpContext(tcpClient, httpRequest, stream, Guid.NewGuid());

                        if (context.IsWebSocketRequest)
                        {
                            var webSocket = await context.AcceptWebSocketAsync(new WebSocketServerOptions(), connectCts.Token).ConfigureAwait(false);
                            tasks.Add(this.EchoLoopAsync(webSocket, this.serverCts.Token, new IDisposable[] { tcpClient, stream }));
                        }
                        else
                        {
                            stream.Dispose();
                            tcpClient.Dispose();
                        }
                    }
                    Console.WriteLine($"All {this.ClientCount} clients connected and ready for WebSocket communication.");
                    serverReady.TrySetResult(true);
                    listener.Stop();
                }
                else if (this.Server == "Native")
                {
                    var listener = new HttpListener();
                    listener.Prefixes.Add($"{(ssl ? "https" : "http")}://localhost:{this.port}/");
                    listener.Start();
                    serverStarted.TrySetResult(true);
                    for (var i = 0; i < this.ClientCount; i++)
                    {
                        var context = await listener.GetContextAsync();
                        if (context.Request.IsWebSocketRequest)
                        {
                            var webSocketContext = await context.AcceptWebSocketAsync(null);
                            tasks.Add(this.EchoLoopAsync(webSocketContext.WebSocket, this.serverCts.Token, new IDisposable[0]));
                        }
                    }
                    Console.WriteLine($"All {this.ClientCount} clients connected and ready for WebSocket communication.");
                    serverReady.TrySetResult(true);
                    cleanup = () => listener.Stop();
                }
                else if (this.Server == "Shinobi")
                {
                    var clients = 0;
                    var server = WebSocketServerBuilder.Create()
                        .UsePort((ushort)this.port)
                        .OnConnect((client, next, cancellationToken) =>
                        {
                            Interlocked.Increment(ref clients);
                            if (clients == this.ClientCount)
                                serverReady.TrySetResult(true);

                            return next(client, cancellationToken);
                        })
                        .OnMessage(async (client, type, stream, next, cancellationToken) =>
                        {
                            if (type == Shinobi.WebSockets.MessageType.Binary && stream is ArrayPoolStream aps)
                            {
                                aps.Position = aps.Length;
                                var seg = aps.GetDataArraySegment();
                                await client.SendAsync(seg, WebSocketMessageType.Binary, true, cancellationToken);
                            }
                        }).Build();

                    await server.StartAsync();
                    serverStarted.TrySetResult(true);
                    cleanup = () =>
                    {
                        server.StopAsync().GetAwaiter().GetResult();
                        server.Dispose();
                    };
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

        await serverStarted.Task;

        if (this.Client == "Ninja")
        {
            var client = new Ninja.WebSockets.WebSocketClientFactory();

            this.clients = await Task.WhenAll(Enumerable.Range(0, this.ClientCount).Select(async _ =>
            {
                return await client.ConnectAsync(
                    new Uri(this.serverUrl),
                    new Ninja.WebSockets.WebSocketClientOptions(),
                    this.serverCts.Token).ConfigureAwait(false);
            }));

        }
        else if (this.Client.StartsWith("Shinobi"))
        {
            this.clients = await Task.WhenAll(Enumerable.Range(0, this.ClientCount).Select(async _ =>
            {
                var client = Shinobi.WebSockets.Builders.WebSocketClientBuilder.Create().Build();
                await client.StartAsync(new Uri(this.serverUrl), this.serverCts.Token);
                return client.webSocket!;
            }));
        }
        else if (this.Client.StartsWith("Native"))
        {
            this.clients = await Task.WhenAll(Enumerable.Range(0, this.ClientCount).Select(async _ =>
            {
                var client = new ClientWebSocket();
                await client.ConnectAsync(new Uri(this.serverUrl), this.serverCts.Token).ConfigureAwait(false);
                return client;
            }));
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
            catch
            {
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
        using var receiveBuffer = new ArrayPoolStream();

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

                    var message = HandleWebSocketMessage(receiveBuffer, result);
                    if (message.HasValue)
                    {
                        if (message.Value.Count != this.data.Count)
                            throw new Exception($"[Server] Expected: {this.data.Count} got {message.Value.Count}");
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

    private static ArraySegment<byte>? HandleWebSocketMessage(ArrayPoolStream messageBuffer, WebSocketReceiveResult result)
    {
        // If this completes the message, process it
        return result.EndOfMessage
            ? messageBuffer.GetDataArraySegment()
            : (ArraySegment<byte>?)null;
    }

    private async Task EchoLoopAsync(WebSocket webSocket, CancellationToken cancellationToken, IDisposable[] disposables)
    {
        using (webSocket)
        {
            using var receiveBuffer = new ArrayPoolStream();

            try
            {

                while (webSocket.State == WebSocketState.Open)
                {
                    var result = await webSocket.ReceiveAsync(receiveBuffer, cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                        return;

                    var message = HandleWebSocketMessage(receiveBuffer, result);
                    if (message.HasValue)
                    {
                        if (message.Value.Count != this.data.Count)
                        {
                            throw new Exception($"[Server] Expected: {this.data.Count} got {message.Value.Count}");
                        }

                        await webSocket.SendAsync(message.Value, WebSocketMessageType.Binary, result.EndOfMessage, cancellationToken);
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
            var key = $"key_{rand.Next(0, 100)}";
            var value = $"value_{rand.Next(0, 1000)}";
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

