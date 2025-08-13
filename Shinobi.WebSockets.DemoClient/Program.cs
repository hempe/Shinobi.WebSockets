using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Shinobi.WebSockets;
using Shinobi.WebSockets.Builders;

namespace Shinobi.WebSockets.DemoClient
{
    /// <summary>
    /// Console-based WebSocket client demo that showcases Shinobi.WebSockets client features
    /// </summary>
    internal class Program
    {
        private static WebSocketClient? client;
        private static ILogger<Program>? logger;
        private static int messagesSent;
        private static int messagesReceived;
        private static DateTime? connectTime;
        private static Timer? connectionTimer;
        private static readonly CancellationTokenSource appCts = new CancellationTokenSource();

        // Stress test variables
        private static bool stressTestRunning;
        private static int stressTestCount;
        private static int stressTestTotal = 1000;
        private static DateTime stressTestStartTime;
        private static long totalLatency;
        private static bool awaitingStressResponse;
        private static DateTime stressTestSendTime;

        static async Task Main(string[] args)
        {
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                appCts.Cancel();
            };

            // Setup logging
            using var loggerFactory = LoggerFactory.Create(builder => builder
                .SetMinimumLevel(LogLevel.Information)
                .AddConsole());

            logger = loggerFactory.CreateLogger<Program>();

            // Show banner
            ShowBanner();
            ShowHelp();

            // Main loop
            await RunInteractiveLoopAsync();

            // Cleanup
            await DisconnectClientAsync();
            Console.WriteLine("\nGoodbye!");
        }

        private static void ShowBanner()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(@"
 ███████╗██╗  ██╗██╗███╗   ██╗ ██████╗ ██████╗ ██╗
 ██╔════╝██║  ██║██║████╗  ██║██╔═══██╗██╔══██╗██║
 ███████╗███████║██║██╔██╗ ██║██║   ██║██████╔╝██║
 ╚════██║██╔══██║██║██║╚██╗██║██║   ██║██╔══██╗██║
 ███████║██║  ██║██║██║ ╚████║╚██████╔╝██████╔╝██║
 ╚══════╝╚═╝  ╚═╝╚═╝╚═╝  ╚═══╝ ╚═════╝ ╚═════╝ ╚═╝");
            Console.WriteLine("              WebSocket Demo Client");
            Console.ResetColor();
            Console.WriteLine();
        }

        private static void ShowHelp()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Commands:");
            Console.ResetColor();

            Console.WriteLine("  connect [url]     - Connect to WebSocket server (default: wss://localhost:8080)");
            Console.WriteLine("  disconnect        - Disconnect from server");
            Console.WriteLine("  send <message>    - Send a text message");
            Console.WriteLine("  binary <message>  - Send a binary message");
            Console.WriteLine("  ping              - Send ping command");
            Console.WriteLine("  time              - Send time command");
            Console.WriteLine("  serverhelp        - Send help command to server");
            Console.WriteLine("  stress [count]    - Run stress test (default: 1000 messages)");
            Console.WriteLine("  stopstress        - Stop running stress test");
            Console.WriteLine("  reconnect         - Enable auto-reconnect features");
            Console.WriteLine("  stats             - Show connection statistics");
            Console.WriteLine("  status            - Show connection status");
            Console.WriteLine("  clear             - Clear the console");
            Console.WriteLine("  quit/exit         - Exit the application");
            Console.WriteLine();
        }

        private static async Task RunInteractiveLoopAsync()
        {
            while (!appCts.Token.IsCancellationRequested)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("> ");
                Console.ResetColor();

                var input = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(input)) continue;

#if NET472
                var parts = input!.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
#else
                var parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
#endif
                var command = parts[0].ToLower();
                var args = parts.Length > 1 ? parts[1] : string.Empty;

                try
                {
                    await ProcessCommandAsync(command, args);
                }
                catch (Exception ex)
                {
                    WriteError($"Command failed: {ex.Message}");
                }
            }
        }

        private static async Task ProcessCommandAsync(string command, string args)
        {
            switch (command)
            {
                case "connect":
                    var url = !string.IsNullOrEmpty(args) ? args : "wss://localhost:8080";
                    await ConnectToServerAsync(url);
                    break;

                case "disconnect":
                    await DisconnectClientAsync();
                    break;

                case "send":
                    if (string.IsNullOrEmpty(args))
                    {
                        WriteError("Usage: send <message>");
                        return;
                    }
                    await SendTextMessageAsync(args);
                    break;

                case "binary":
                    if (string.IsNullOrEmpty(args))
                    {
                        WriteError("Usage: binary <message>");
                        return;
                    }
                    await SendBinaryMessageAsync(args);
                    break;

                case "ping":
                    await SendTextMessageAsync("ping");
                    break;

                case "time":
                    await SendTextMessageAsync("time");
                    break;

                case "serverhelp":
                    await SendTextMessageAsync("help");
                    break;

                case "stress":
                    var count = 1000;
                    if (!string.IsNullOrEmpty(args) && int.TryParse(args, out var parsedCount))
                        count = parsedCount;
                    await StartStressTestAsync(count);
                    break;

                case "stopstress":
                    StopStressTest();
                    break;

                case "reconnect":
                    await DemonstrateReconnectAsync();
                    break;

                case "stats":
                    ShowStats();
                    break;

                case "status":
                    ShowConnectionStatus();
                    break;

                case "clear":
                    Console.Clear();
                    ShowBanner();
                    break;

                case "help":
                case "?":
                    ShowHelp();
                    break;

                case "quit":
                case "exit":
                    appCts.Cancel();
                    break;

                default:
                    WriteError($"Unknown command: {command}. Type 'help' for available commands.");
                    break;
            }
        }

        private static async Task ConnectToServerAsync(string url)
        {
            if (client?.ConnectionState == WebSocketConnectionState.Connected)
            {
                WriteInfo("Already connected! Use 'disconnect' first.");
                return;
            }

            try
            {
                using var loggerFactory = LoggerFactory.Create(builder => builder
                    .SetMinimumLevel(LogLevel.Information)
                    .AddConsole());

                WriteInfo($"Connecting to {url}...");

                client = WebSocketClientBuilder.Create()
                    .UseLogging(loggerFactory)
                    .OnConnect(async (ws, next, ct) =>
                    {
                        WriteSuccess("✓ Connected to server!");
                        connectTime = DateTime.Now;
                        StartConnectionTimer();
                        await next(ws, ct);
                    })
                    .OnClose(async (ws, statusDescription, next, ct) =>
                    {
                        WriteWarning("✗ Connection closed: " + statusDescription);
                        StopConnectionTimer();
                        StopStressTest();
                        await next(ws, statusDescription, ct);
                    })
                    .OnError(async (ws, ex, next, ct) =>
                    {
                        WriteError($"✗ WebSocket error: {ex.Message}");
                        await next(ws, ex, ct);
                    })
                    .OnTextMessage((ws, message, ct) =>
                    {
                        messagesReceived++;

                        if (stressTestRunning && awaitingStressResponse && message.StartsWith("STRESS_"))
                        {
                            HandleStressTestResponseAsync();
                            return default(ValueTask);
                        }

                        WriteReceived($"▼ {message}");
                        return default(ValueTask);
                    })
                    .OnBinaryMessage((ws, data, ct) =>
                    {
                        messagesReceived++;
                        var message = Encoding.UTF8.GetString(data);

                        if (stressTestRunning && awaitingStressResponse && message.StartsWith("STRESS_"))
                        {
                            HandleStressTestResponseAsync();
                            return default(ValueTask);
                        }

                        WriteReceived($"▼ [BINARY] {message}");
                        return default(ValueTask);
                    })
                    .Build();

                client.ConnectionStateChanged += OnConnectionStateChanged;
                client.Reconnecting += OnReconnecting;

                await client.StartAsync(new Uri(url), appCts.Token);
            }
            catch (Exception ex)
            {
                WriteError($"Failed to connect: {ex.Message}");
                client = null;
            }
        }

        private static void OnConnectionStateChanged(WebSocketClient sender, WebSocketConnectionStateChangedEventArgs e)
        {
            var color = e.NewState switch
            {
                WebSocketConnectionState.Connected => ConsoleColor.Green,
                WebSocketConnectionState.Connecting => ConsoleColor.Yellow,
                WebSocketConnectionState.Reconnecting => ConsoleColor.Magenta,
                WebSocketConnectionState.Disconnecting => ConsoleColor.Yellow,
                WebSocketConnectionState.Failed => ConsoleColor.Red,
                _ => ConsoleColor.Gray
            };

            WriteColored($"[STATE] {e.PreviousState} → {e.NewState}", color);

            if (e.Exception != null)
            {
                WriteError($"[ERROR] {e.Exception.Message}");
            }
        }

        private static void OnReconnecting(WebSocketClient sender, WebSocketReconnectingEventArgs e)
        {
            WriteColored($"[RECONNECT] Attempting reconnection to {e.CurrentUri} in {e.Delay.TotalMilliseconds}ms (attempt {e.AttemptNumber})", ConsoleColor.Magenta);
        }

        private static async Task DisconnectClientAsync()
        {
            if (client != null)
            {
                WriteInfo("Disconnecting...");
                StopStressTest();
                await client.StopAsync();
                client.Dispose();
                client = null;
                StopConnectionTimer();
                WriteInfo("Disconnected.");
            }
            else
            {
                WriteInfo("Not connected.");
            }
        }

        private static async Task SendTextMessageAsync(string message)
        {
            if (client?.ConnectionState != WebSocketConnectionState.Connected)
            {
                WriteError("Not connected to server. Use 'connect' first.");
                return;
            }

            try
            {
                await client.SendTextAsync(message, appCts.Token);
                messagesSent++;
                WriteSent($"▲ {message}");
            }
            catch (Exception ex)
            {
                WriteError($"Failed to send message: {ex.Message}");
            }
        }

        private static async Task SendBinaryMessageAsync(string message)
        {
            if (client?.ConnectionState != WebSocketConnectionState.Connected)
            {
                WriteError("Not connected to server. Use 'connect' first.");
                return;
            }

            try
            {
                var data = Encoding.UTF8.GetBytes(message);
                await client.SendBinaryAsync(data, appCts.Token);
                messagesSent++;
                WriteSent($"▲ [BINARY] {message}");
            }
            catch (Exception ex)
            {
                WriteError($"Failed to send binary message: {ex.Message}");
            }
        }

        private static async Task StartStressTestAsync(int count)
        {
            if (client?.ConnectionState != WebSocketConnectionState.Connected)
            {
                WriteError("Not connected to server. Use 'connect' first.");
                return;
            }

            if (stressTestRunning)
            {
                WriteError("Stress test already running. Use 'stopstress' to stop it first.");
                return;
            }

            stressTestTotal = count;
            stressTestCount = 0;
            stressTestRunning = true;
            stressTestStartTime = DateTime.Now;
            totalLatency = 0;
            awaitingStressResponse = false;

            WriteInfo($"Starting stress test: {count} round-trip messages");

            // Start the stress test chain
            await SendStressTestMessageAsync();
        }

        private static async Task SendStressTestMessageAsync()
        {
            if (!stressTestRunning || stressTestCount >= stressTestTotal)
            {
                CompleteStressTest();
                return;
            }

            if (client?.ConnectionState != WebSocketConnectionState.Connected)
            {
                WriteError("Stress test stopped - connection lost");
                StopStressTest();
                return;
            }

            try
            {
                var message = $"STRESS_{stressTestCount + 1}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                var data = Encoding.UTF8.GetBytes(message);

                await client.SendBinaryAsync(data, appCts.Token);
                awaitingStressResponse = true;
                stressTestSendTime = DateTime.Now;

                // Show progress every 100 messages
                if (stressTestCount % 100 == 0)
                {
                    var elapsed = DateTime.Now - stressTestStartTime;
                    var rate = elapsed.TotalSeconds > 0 ? stressTestCount / elapsed.TotalSeconds : 0;
                    WriteInfo($"Stress test progress: {stressTestCount}/{stressTestTotal} ({rate:F0} req/sec)");
                }
            }
            catch (Exception ex)
            {
                WriteError($"Stress test error: {ex.Message}");
                StopStressTest();
            }
        }

        private static async void HandleStressTestResponseAsync()
        {
            if (!awaitingStressResponse) return;

            // Calculate latency
            if (stressTestSendTime != default)
            {
                var latency = (long)(DateTime.Now - stressTestSendTime).TotalMilliseconds;
                totalLatency += latency;
            }

            awaitingStressResponse = false;
            stressTestCount++;

            // Continue the chain
            await SendStressTestMessageAsync();
        }

        private static void CompleteStressTest()
        {
            var duration = DateTime.Now - stressTestStartTime;
            var rate = duration.TotalSeconds > 0 ? stressTestCount / duration.TotalSeconds : 0;
            var avgLatency = stressTestCount > 0 ? totalLatency / stressTestCount : 0;

            WriteSuccess($"✓ Stress test completed: {stressTestCount} round-trips in {duration.TotalMilliseconds:F0}ms");
            WriteSuccess($"  Rate: {rate:F1} req/sec, Average latency: {avgLatency}ms");

            StopStressTest();
        }

        private static void StopStressTest()
        {
            stressTestRunning = false;
            awaitingStressResponse = false;
            if (stressTestRunning)
            {
                WriteInfo("Stress test stopped.");
            }
        }

        private static async Task DemonstrateReconnectAsync()
        {
            WriteInfo("Demonstrating auto-reconnect features...");

            if (client != null)
            {
                WriteInfo("Disconnecting current client...");
                await DisconnectClientAsync();
            }

            try
            {
                using var loggerFactory = LoggerFactory.Create(builder => builder
                    .SetMinimumLevel(LogLevel.Debug)
                    .AddConsole());

                WriteInfo("Creating client with auto-reconnect enabled...");

                client = WebSocketClientBuilder.Create()
                    .UseLogging(loggerFactory)
                    .UseReliableConnection() // This enables auto-reconnect
                    .OnConnect(async (ws, next, ct) =>
                    {
                        WriteSuccess("✓ Connected with auto-reconnect!");
                        connectTime = DateTime.Now;
                        StartConnectionTimer();
                        await next(ws, ct);
                    })
                    .OnClose(async (ws, statusDescription, next, ct) =>
                    {
                        WriteWarning("✗ Connection closed - auto-reconnect will attempt to reconnect");
                        await next(ws, statusDescription, ct);
                    })
                    .OnTextMessage((ws, message, ct) =>
                    {
                        messagesReceived++;
                        WriteReceived($"▼ {message}");
                        return default(ValueTask);
                    })
                    .Build();

                client.ConnectionStateChanged += OnConnectionStateChanged;
                client.Reconnecting += OnReconnecting;

                await client.StartAsync(new Uri("wss://localhost:8080"), appCts.Token);

                WriteInfo("✓ Client configured with auto-reconnect. Try stopping/starting the server to see reconnection in action!");
            }
            catch (Exception ex)
            {
                WriteError($"Failed to setup reconnect demo: {ex.Message}");
                client = null;
            }
        }

        private static void ShowStats()
        {
            Console.WriteLine();
            WriteColored("=== Connection Statistics ===", ConsoleColor.Cyan);
            Console.WriteLine($"Messages Sent:     {messagesSent}");
            Console.WriteLine($"Messages Received: {messagesReceived}");

            if (connectTime.HasValue)
            {
                var duration = DateTime.Now - connectTime.Value;
                Console.WriteLine($"Connected Time:    {FormatDuration(duration)}");
            }
            else
            {
                Console.WriteLine($"Connected Time:    Not connected");
            }

            Console.WriteLine();
        }

        private static void ShowConnectionStatus()
        {
            var status = client?.ConnectionState ?? WebSocketConnectionState.Disconnected;
            var color = status switch
            {
                WebSocketConnectionState.Connected => ConsoleColor.Green,
                WebSocketConnectionState.Connecting => ConsoleColor.Yellow,
                WebSocketConnectionState.Reconnecting => ConsoleColor.Magenta,
                WebSocketConnectionState.Disconnecting => ConsoleColor.Yellow,
                WebSocketConnectionState.Failed => ConsoleColor.Red,
                _ => ConsoleColor.Gray
            };

            WriteColored($"Connection Status: {status}", color);
        }

        private static void StartConnectionTimer()
        {
            connectionTimer = new Timer(UpdateConnectionTime, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        private static void StopConnectionTimer()
        {
            connectionTimer?.Dispose();
            connectionTimer = null;
            connectTime = null;
        }

        private static void UpdateConnectionTime(object? state)
        {
            // This just maintains the connection time for stats display
            // The actual display is shown when stats are requested
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalHours >= 1)
                return $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s";
            if (duration.TotalMinutes >= 1)
                return $"{duration.Minutes}m {duration.Seconds}s";
            return $"{duration.Seconds}s";
        }

        private static void WriteSuccess(string message)
        {
            WriteColored(message, ConsoleColor.Green);
        }

        private static void WriteInfo(string message)
        {
            WriteColored(message, ConsoleColor.Cyan);
        }

        private static void WriteWarning(string message)
        {
            WriteColored(message, ConsoleColor.Yellow);
        }

        private static void WriteError(string message)
        {
            WriteColored(message, ConsoleColor.Red);
        }

        private static void WriteSent(string message)
        {
            WriteColored(message, ConsoleColor.Blue);
        }

        private static void WriteReceived(string message)
        {
            WriteColored(message, ConsoleColor.Green);
        }

        private static void WriteColored(string message, ConsoleColor color)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write($"[{timestamp}] ");
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ResetColor();
        }
    }
}