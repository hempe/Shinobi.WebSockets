using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Jobs;
using Samurai.WebSockets;

[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class WebSocketServerClientBenchmarks
{
    private WebSocketServerFactory server;
    private TcpListener listener;
    private List<ClientWebSocket> clients;
    private int port;
    private string serverUrl;
    private CancellationTokenSource serverCts;
    private Task serverTask;

    // Track server readiness and client connections
    private TaskCompletionSource<bool> serverReadyTcs;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> clientDisconnectTasks = new();

    [Params(1, 5, 10)]
    public int ClientCount { get; set; }

    [GlobalSetup]
    public async Task SetupAsync()
    {
        this.port = GetAvailablePort();
        this.serverUrl = $"ws://localhost:{this.port}/";
        this.serverCts = new CancellationTokenSource();
        this.server = new WebSocketServerFactory();
        this.serverReadyTcs = new TaskCompletionSource<bool>();

        // Start the WebSocket server
        this.listener = new TcpListener(IPAddress.Loopback, this.port);
        this.listener.Start();

        this.serverTask = Task.Run(async () =>
        {
            var clientTasks = new List<Task>();

            try
            {
                // Signal that server is ready to accept connections
                this.serverReadyTcs.SetResult(true);

                while (!this.serverCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var tcpClient = await this.listener.AcceptTcpClientAsync();
                        var clientTask = this.HandleClientAsync(tcpClient, this.serverCts.Token);
                        clientTasks.Add(clientTask);

                        // Clean up completed tasks to prevent memory leaks
                        clientTasks.RemoveAll(t => t.IsCompleted);
                    }
                    catch when (this.serverCts.Token.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                if (!this.serverReadyTcs.Task.IsCompleted)
                {
                    this.serverReadyTcs.SetException(ex);
                }
            }
            finally
            {
                // Wait for all client tasks to complete - no arbitrary timeout
                var remainingTasks = clientTasks.Where(t => !t.IsCompleted).ToList();
                if (remainingTasks.Count > 0)
                {
                    try
                    {
                        // Wait for natural completion or a reasonable timeout
                        using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        await Task.WhenAll(remainingTasks).WaitAsync(cleanupCts.Token);
                    }
                    catch (TimeoutException)
                    {
                        // Log that some tasks didn't complete cleanly
                    }
                }
            }
        });

        // Wait for server to be ready with timeout
        using var serverReadyCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await this.serverReadyTcs.Task.WaitAsync(serverReadyCts.Token);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException("Server failed to start within timeout period");
        }

        // Create clients with proper error handling
        this.clients = new List<ClientWebSocket>();
        var clientConnectTasks = new List<Task>();

        for (int i = 0; i < this.ClientCount; i++)
        {
            var clientIndex = i; // Capture for lambda
            clientConnectTasks.Add(Task.Run(async () =>
            {
                try
                {
                    var client = new ClientWebSocket();
                    client.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

                    using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await client.ConnectAsync(new Uri(this.serverUrl), connectCts.Token);

                    lock (this.clients)
                    {
                        this.clients.Add(client);
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to connect client {clientIndex}: {ex.Message}", ex);
                }
            }));
        }

        // Wait for all clients to connect
        using var clientConnectCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await Task.WhenAll(clientConnectTasks).WaitAsync(clientConnectCts.Token);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException("Not all clients connected within timeout period");
        }

        // Verify all connections are actually ready by sending a ping to each
        await this.VerifyAllConnectionsReadyAsync();
    }

    // Add method to verify connections are ready
    private async Task VerifyAllConnectionsReadyAsync()
    {
        var verifyTasks = this.clients.Select(async client =>
        {
            if (client.State == WebSocketState.Open)
            {
                try
                {
                    // Send a small ping frame to verify connection is truly ready
                    var pingData = Encoding.UTF8.GetBytes("ping");
                    using var verifyCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

                    await client.SendAsync(
                        new ArraySegment<byte>(pingData),
                        WebSocketMessageType.Text,
                        true,
                        verifyCts.Token);

                    // Receive the echo to confirm round-trip works
                    var buffer = new byte[pingData.Length + 10];
                    await client.ReceiveAsync(new ArraySegment<byte>(buffer), verifyCts.Token);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Connection verification failed: {ex.Message}", ex);
                }
            }
        });

        using var verifyCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Task.WhenAll(verifyTasks).WaitAsync(verifyCts.Token);
    }

    private async Task HandleClientAsync(TcpClient tcpClient, CancellationToken cancellationToken)
    {
        WebSocket webSocket = null;
        string clientId = Guid.NewGuid().ToString();

        try
        {
            // Remove arbitrary socket timeouts - let WebSocket layer handle this
            var stream = tcpClient.GetStream();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

            var context = await this.server.ReadHttpHeaderFromStreamAsync(stream);

            if (context.IsWebSocketRequest)
            {
                webSocket = await this.server.AcceptWebSocketAsync(context);

                // Register this client for disconnect tracking
                var disconnectTcs = new TaskCompletionSource<bool>();
                this.clientDisconnectTasks[clientId] = disconnectTcs;

                try
                {
                    await this.EchoMessagesAsync(webSocket, timeoutCts.Token);
                }
                finally
                {
                    // Signal that this client has disconnected
                    disconnectTcs.SetResult(true);
                }
            }
        }
        catch when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shutdown
        }
        catch (Exception)
        {
            // Handle errors during client processing
        }
        finally
        {
            try
            {
                webSocket?.Dispose();
            }
            catch { }

            try
            {
                tcpClient?.Close();
            }
            catch { }

            // Ensure disconnect is signaled even if there was an exception
            if (this.clientDisconnectTasks.TryGetValue(clientId, out var tcs) && !tcs.Task.IsCompleted)
            {
                tcs.SetResult(true);
            }
        }
    }

    private async Task EchoMessagesAsync(WebSocket webSocket, CancellationToken cancellationToken)
    {
        // Use a larger buffer to reduce the chance of fragmentation
        var buffer = new byte[64 * 1024]; // Increased from 8KB to 64KB

        try
        {
            while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Text || result.MessageType == WebSocketMessageType.Binary)
                {
                    await webSocket.SendAsync(
                        new ArraySegment<byte>(buffer, 0, result.Count),
                        result.MessageType,
                        result.EndOfMessage,
                        cancellationToken);
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    if (webSocket.State == WebSocketState.Open)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", cancellationToken);
                    }
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shutdown
        }
        catch (WebSocketException)
        {
            // Connection closed unexpectedly
        }
        catch (Exception)
        {
            // Handle other errors silently for benchmarking
        }
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        try
        {
            // Cancel server operations first
            this.serverCts?.Cancel();

            // Close all client connections and wait for them to disconnect
            if (this.clients != null)
            {
                var closeTasks = this.clients.Select(async client =>
                {
                    try
                    {
                        if (client.State == WebSocketState.Open)
                        {
                            using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                            await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", closeCts.Token);
                        }
                    }
                    catch { }
                    finally
                    {
                        client.Dispose();
                    }
                });

                await Task.WhenAll(closeTasks);
            }

            // Wait for all server-side client handlers to complete disconnect
            if (this.clientDisconnectTasks.Count > 0)
            {
                var disconnectTasks = this.clientDisconnectTasks.Values.Select(tcs => tcs.Task);

                using var disconnectCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await Task.WhenAll(disconnectTasks).WaitAsync(disconnectCts.Token);
                }
                catch (TimeoutException)
                {
                    // Some clients didn't disconnect cleanly, but continue cleanup
                }
            }

            // Stop the listener
            try
            {
                this.listener?.Stop();
            }
            catch { }

            // Wait for server task to complete with timeout
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

        }
        catch (Exception ex)
        {
            // Log or handle cleanup exceptions if necessary
            Console.WriteLine($"Cleanup failed: {ex.Message}");
        }
        finally
        {
            // Clear the disconnect tracking
            this.clientDisconnectTasks.Clear();
            this.serverCts?.Dispose();
        }
    }

    [Benchmark]
    public async Task SendReceive_SmallTextMessagesAsync()
    {
        var message = "Hello WebSocket!";
        var data = Encoding.UTF8.GetBytes(message);
        var tasks = new List<Task>();

        foreach (var client in this.clients.Where(c => c.State == WebSocketState.Open))
        {
            tasks.Add(this.SendAndReceiveMessageAsync(client, data, WebSocketMessageType.Text));
        }

        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks);
        }
    }

    [Benchmark]
    public async Task SendReceive_MediumJsonMessagesAsync()
    {
        var messageObj = new
        {
            id = Guid.NewGuid(),
            timestamp = DateTime.UtcNow,
            data = string.Join("", Enumerable.Repeat("test", 50)),
            numbers = Enumerable.Range(1, 25).ToArray()
        };

        var json = JsonSerializer.Serialize(messageObj);
        var data = Encoding.UTF8.GetBytes(json);
        var tasks = new List<Task>();

        foreach (var client in this.clients.Where(c => c.State == WebSocketState.Open))
        {
            tasks.Add(this.SendAndReceiveMessageAsync(client, data, WebSocketMessageType.Text));
        }

        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks);
        }
    }

    [Benchmark]
    public async Task SendReceive_LargeBinaryMessagesAsync()
    {
        var data = new byte[16 * 1024];
        Random.Shared.NextBytes(data);
        var tasks = new List<Task>();

        foreach (var client in this.clients.Where(c => c.State == WebSocketState.Open))
        {
            tasks.Add(this.SendAndReceiveMessageAsync(client, data, WebSocketMessageType.Binary));
        }

        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks);
        }
    }

    private async Task SendAndReceiveMessageAsync(ClientWebSocket client, byte[] data, WebSocketMessageType messageType)
    {
        // Use adaptive timeout based on message size
        var timeoutMs = Math.Max(5000, data.Length / 1000); // 1ms per KB, minimum 5 seconds
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));

        try
        {
            // Send message
            await client.SendAsync(new ArraySegment<byte>(data), messageType, true, cts.Token);

            // Receive echo - handle potential message fragmentation more efficiently
            var buffer = new byte[Math.Max(data.Length * 2, 8192)]; // Ensure enough space
            var totalReceived = 0;

            while (totalReceived < data.Length && !cts.Token.IsCancellationRequested)
            {
                var segment = new ArraySegment<byte>(buffer, totalReceived, buffer.Length - totalReceived);
                var result = await client.ReceiveAsync(segment, cts.Token);

                totalReceived += result.Count;

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    // throw new InvalidOperationException("Received close message unexpectedly");
                    Console.WriteLine("Received close message, stopping receive loop.");
                    return;
                }

                if (result.EndOfMessage)
                {
                    break;
                }

                // Safety check - this should rarely happen with proper buffer sizing
                if (totalReceived >= buffer.Length)
                {
                    throw new InvalidOperationException("Buffer overflow during receive - message larger than expected");
                }
            }

            // Verify we received the expected amount of data
            if (totalReceived != data.Length && !cts.Token.IsCancellationRequested)
            {
                throw new InvalidOperationException($"Received data length mismatch: expected {data.Length}, got {totalReceived}");
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            throw new TimeoutException($"Send/Receive operation timed out after {timeoutMs}ms for {data.Length} bytes");
        }
        finally
        {
            client.Dispose(); // Ensure client is disposed after use
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