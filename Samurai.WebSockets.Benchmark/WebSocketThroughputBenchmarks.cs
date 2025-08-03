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
    private Task serverTask;


    //[Params(1_000, 10_000)]
    public int MessageCount { get; set; } = 4;

    //[Params(16, 64)]
    //[Params(1, 16)]
    public int MessageSizeKb { get; set; } = 16;

    //[Params(1000)]
    public int ClientCount { get; set; } = 2;

    // [Params("Ninja", "Samurai", "Samurai.PermessageDeflate")]
    public string Implementation { get; set; } = "Samurai.PermessageDeflate";

    private ArraySegment<byte> data;

    private bool permessageDeflate;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        this.permessageDeflate = this.Implementation.EndsWith("PermessageDeflate");

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
                            tasks.Add(this.EchoLoopAsync(webSocket, this.serverCts.Token, [tcpClient, stream]));
                        }
                        else
                        {
                            stream.Dispose();
                            tcpClient.Dispose();
                        }
                    }
                    else if (this.Implementation.StartsWith("Samurai"))
                    {
                        var server = new Samurai.WebSockets.WebSocketServerFactory();
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
                    new Ninja.WebSockets.WebSocketClientOptions
                    {
                        SecWebSocketExtensions = this.permessageDeflate ? "permessage-deflate" : null
                    },
                    this.serverCts.Token).ConfigureAwait(false);
            }
        }
        else if (this.Implementation.StartsWith("Samurai"))
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
        using var temp = new Samurai.WebSockets.ArrayPoolStream();
        for (var i = 0; i < messageCount; i++)
        {
            // Console.WriteLine("Client sending ");
            await client.SendAsync(this.data, WebSocketMessageType.Binary, true, cancellationToken);
            // Console.WriteLine("Client sent ");
            using var ms = new Samurai.WebSockets.ArrayPoolStream();
            using var bufferStream = new MemoryStream();
            var buffer = new byte[16 * 1024];
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                    return;

                var message = HandleWebSocketMessage(bufferStream, buffer, result, this.permessageDeflate);
                if (message != null)
                {
                    if (message.Length != this.data.Count)
                        throw new Exception($"[Server] Expected {this.permessageDeflate}: {this.data.Count} got {ms.Position}");
                    break;
                }
            }
        }
    }

    private static byte[] HandleWebSocketMessage(MemoryStream messageBuffer, byte[] buffer, WebSocketReceiveResult result, bool permessageDeflate)
    {
        try
        {
            // Append this chunk to our buffer
            messageBuffer.Write(buffer, 0, result.Count);

            // If this completes the message, process it
            if (result.EndOfMessage)
            {
                messageBuffer.Position = 0;
                using (var deflateStream = new DeflateStream(messageBuffer, CompressionMode.Decompress, leaveOpen: true))
                using (var decompressedStream = new MemoryStream())
                {
                    deflateStream.CopyTo(decompressedStream);
                    var arr = decompressedStream.ToArray();
                    messageBuffer.SetLength(0);
                    return arr;
                }
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

            var buffer = new byte[16 * 1024];
            try
            {
                using var ms = new Samurai.WebSockets.ArrayPoolStream();
                var bufferStream = new MemoryStream();

                while (webSocket.State == WebSocketState.Open)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                        return;

                    if (result.MessageType != WebSocketMessageType.Binary)
                    {
                        Console.WriteLine("What the hell" + result.MessageType);
                        throw new Exception("What the fuck");
                    }

                    var message = HandleWebSocketMessage(bufferStream, buffer, result, this.permessageDeflate);
                    if (message != null)
                    {
                        if (message.Length != this.data.Count)
                        {
                            Console.WriteLine("What the hell is it still wrong?");

                            throw new Exception($"[Server] Expected {this.permessageDeflate}: {this.data.Count} got {ms.Position}");
                        }

                        await webSocket.SendAsync(message, WebSocketMessageType.Binary, result.EndOfMessage, cancellationToken);
                    }
                }

                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", cancellationToken);
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

