using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;


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
    private Task serverTask;


    [Params(1_000, 10_000)]
    public int MessageCount { get; set; }

    //[Params(16, 64)]
    [Params(1, 16, 32)]
    public int MessageSizeKb { get; set; }

    [Params(100, 1000)]
    public int ClientCount { get; set; }

    [Params("Ninja", "Samurai")]
    public string Implementation { get; set; }

    private byte[] data;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        var random = new Random(42069);
        this.data = ArrayPool<byte>.Shared.Rent(this.MessageSizeKb * 1024);
        random.NextBytes(this.data);

        this.port = GetAvailablePort();
        this.serverUrl = $"ws://localhost:{this.port}/";
        this.serverCts = new CancellationTokenSource();
        this.clients = new WebSocket[this.ClientCount];


        var serverReady = new TaskCompletionSource<bool>();

        this.serverTask = Task.Run(async () =>
        {
            try
            {
                using var listener = new TcpListener(IPAddress.Loopback, this.port);
                listener.Start();
                var tasks = new List<Task>();
                for (var i = 0; i < this.ClientCount; i++)
                {
                    var tcpClient = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    var stream = tcpClient.GetStream();
                    using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

                    if (this.Implementation == "Ninja")
                    {
                        var server = new Ninja.WebSockets.WebSocketServerFactory();
                        var context = await server.ReadHttpHeaderFromStreamAsync(stream, connectCts.Token).ConfigureAwait(false);
                        if (context.IsWebSocketRequest)
                        {
                            var webSocket = await server.AcceptWebSocketAsync(context, connectCts.Token).ConfigureAwait(false);
                            tasks.Add(EchoLoopAsync(webSocket, this.serverCts.Token, [tcpClient, stream]));
                        }
                        else
                        {
                            stream.Dispose();
                            tcpClient.Dispose();
                        }
                    }
                    else if (this.Implementation == "Samurai")
                    {
                        var server = new Samurai.WebSockets.WebSocketServerFactory();
                        var context = await server.ReadHttpHeaderFromStreamAsync(stream, connectCts.Token).ConfigureAwait(false);
                        if (context.IsWebSocketRequest)
                        {
                            var webSocket = await server.AcceptWebSocketAsync(context, connectCts.Token).ConfigureAwait(false);
                            tasks.Add(EchoLoopAsync(webSocket, this.serverCts.Token, [tcpClient, stream]));
                        }
                        else
                        {
                            stream.Dispose();
                            tcpClient.Dispose();
                        }
                    }
                    else
                    {
                        throw new NotSupportedException($"{this.Implementation} is not supported");
                    }
                }

                Console.WriteLine($"All {this.ClientCount} clients connected and ready for WebSocket communication.");
                serverReady.SetResult(true);
                listener.Stop();
                await Task.WhenAll(tasks).WaitAsync(this.serverCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in WebSocket server setup: {ex.Message}");
                serverReady.TrySetException(new Exception($"Error in WebSocket setup server: {ex.Message}", ex));
            }
        });
        if (this.Implementation == "Ninja")
        {
            var client = new Ninja.WebSockets.WebSocketClientFactory();
            for (var i = 0; i < this.ClientCount; i++)
            {
                this.clients[i] = await client.ConnectAsync(
                    new Uri(this.serverUrl),
                    this.serverCts.Token).ConfigureAwait(false);
            }
        }
        else if (this.Implementation == "Samurai")
        {
            var client = new Samurai.WebSockets.WebSocketClientFactory();
            for (var i = 0; i < this.ClientCount; i++)
            {
                this.clients[i] = await client.ConnectAsync(
                    new Uri(this.serverUrl),
                    this.serverCts.Token).ConfigureAwait(false);
            }
        }
        else
        {
            throw new NotSupportedException($"{this.Implementation} is not supported");
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

            foreach (var client in this.clients)
            {
                if (client?.State == WebSocketState.Open)
                {
                    try
                    {
                        using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", closeCts.Token).ConfigureAwait(false);
                    }
                    catch { }
                }
                client?.Dispose();
            }

            try
            {
                await (this.serverTask?.WaitAsync(TimeSpan.FromSeconds(3)) ?? Task.CompletedTask).ConfigureAwait(false);

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
        }
        finally
        {
            this.serverCts?.Dispose();
            this.clients = null;
            this.serverTask = null;
            ArrayPool<byte>.Shared.Return(this.data);
        }
    }


    [Benchmark]
    public async Task RunThrouputBenchmarkAsync()
    {
        await Task.WhenAll(this.clients.Select(client => this.RunBenchmarkAsync(client, this.MessageCount / this.clients.Length, CancellationToken.None)));
    }

    private async Task RunBenchmarkAsync(WebSocket client, int messageCount, CancellationToken cancellationToken)
    {
        var receiveBuffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            for (var i = 0; i < messageCount; i++)
            {
                await client.SendAsync(this.data, WebSocketMessageType.Binary, true, cancellationToken);
                var received = 0;

                while (!cancellationToken.IsCancellationRequested)
                {
                    var result = await client.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), cancellationToken);
                    received += result.Count;
                    if (result.EndOfMessage)
                        break;
                }

                if (received != this.data.Length)
                    throw new Exception($"Expected {this.data.Length} got {received}");

                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(receiveBuffer);
        }
    }

    private static async Task EchoLoopAsync(WebSocket webSocket, CancellationToken cancellationToken, IDisposable[] disposables)
    {
        using (webSocket)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
            try
            {
                while (webSocket.State == WebSocketState.Open)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                        break;

                    var msg = new ArraySegment<byte>(buffer, 0, result.Count);
                    await webSocket.SendAsync(msg, WebSocketMessageType.Binary, result.EndOfMessage, cancellationToken);
                }

                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", cancellationToken);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
                foreach (var disposable in disposables)
                {
                    disposable.Dispose();
                }
            }
        }
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

