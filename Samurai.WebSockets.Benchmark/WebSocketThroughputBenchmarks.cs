using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Jobs;
using Ninja.WebSockets;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class WebSocketThroughputBenchmarks
{
    private WebSocketServerFactory server;
    private TcpListener listener;
    private ClientWebSocket client;
    private int port;
    private string serverUrl;
    private CancellationTokenSource serverCts;
    private WebSocket serverWebSocket;
    private Task serverTask;

    [Params(50, 100, 250)] // Reduced counts to prevent hangs
    public int MessageCount { get; set; }

    [GlobalSetup]
    public async Task SetupAsync()
    {
        this.port = GetAvailablePort();
        this.serverUrl = $"ws://localhost:{this.port}/";
        this.serverCts = new CancellationTokenSource();
        this.server = new WebSocketServerFactory();

        var serverReady = new TaskCompletionSource<WebSocket>();

        this.listener = new TcpListener(IPAddress.Loopback, this.port);
        this.listener.Start();

        this.serverTask = Task.Run(async () =>
        {
            try
            {
                var tcpClient = await this.listener.AcceptTcpClientAsync();
                tcpClient.ReceiveTimeout = 10000;
                tcpClient.SendTimeout = 10000;

                var stream = tcpClient.GetStream();
                var context = await this.server.ReadHttpHeaderFromStreamAsync(stream);

                if (context.IsWebSocketRequest)
                {
                    var webSocket = await this.server.AcceptWebSocketAsync(context);
                    serverReady.SetResult(webSocket);

                    // Keep connection alive for benchmarking
                    var buffer = new byte[64 * 1024];
                    while (webSocket.State == WebSocketState.Open && !this.serverCts.Token.IsCancellationRequested)
                    {
                        try
                        {
                            using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(this.serverCts.Token);
                            receiveCts.CancelAfter(TimeSpan.FromSeconds(30));

                            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), receiveCts.Token);
                            if (result.MessageType == WebSocketMessageType.Close)
                                break;
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!serverReady.Task.IsCompleted)
                {
                    serverReady.SetException(ex);
                }
            }
        });

        // Connect client
        this.client = new ClientWebSocket();
        this.client.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

        using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await this.client.ConnectAsync(new Uri(this.serverUrl), connectCts.Token);

        // Wait for server WebSocket with timeout
        using var serverCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        this.serverWebSocket = await serverReady.Task.WaitAsync(serverCts.Token);

        await Task.Delay(100); // Ensure connection is stable
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        try
        {
            this.serverCts?.Cancel();

            if (this.client?.State == WebSocketState.Open)
            {
                try
                {
                    using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await this.client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", closeCts.Token);
                }
                catch { }
            }
            this.client?.Dispose();

            try
            {
                this.listener?.Stop();
            }
            catch { }

            if (this.serverTask != null)
            {
                try
                {
                    await this.serverTask.WaitAsync(TimeSpan.FromSeconds(3));
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

            await Task.Delay(100);
        }
        catch (Exception ex)
        {
            // Log or handle cleanup exceptions if necessary
            Console.WriteLine($"Cleanup failed: {ex.Message}");
        }
    }

    [Benchmark]
    public async Task ClientToServer_SmallMessagesAsync()
    {
        var message = Encoding.UTF8.GetBytes("test message");

        for (int i = 0; i < this.MessageCount; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await this.client.SendAsync(new ArraySegment<byte>(message), WebSocketMessageType.Text, true, cts.Token);
        }
    }

    [Benchmark]
    public async Task ClientToServer_MediumMessagesAsync()
    {
        var message = new byte[1024]; // 1KB
        Random.Shared.NextBytes(message);

        for (int i = 0; i < this.MessageCount; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await this.client.SendAsync(new ArraySegment<byte>(message), WebSocketMessageType.Binary, true, cts.Token);
        }
    }

    /*
    [Benchmark]
    public async Task ServerToClient_SmallMessagesAsync()
    {
        var message = Encoding.UTF8.GetBytes("server message");

        for (int i = 0; i < this.MessageCount; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await this.serverWebSocket.SendAsync(new ArraySegment<byte>(message), WebSocketMessageType.Text, true, cts.Token);
        }
    }

    [Benchmark]
    public async Task ServerToClient_MediumMessagesAsync()
    {
        var message = new byte[1024]; // 1KB
        Random.Shared.NextBytes(message);

        for (int i = 0; i < this.MessageCount; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await this.serverWebSocket.SendAsync(new ArraySegment<byte>(message), WebSocketMessageType.Binary, true, cts.Token);
        }
    }
    */

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

}

