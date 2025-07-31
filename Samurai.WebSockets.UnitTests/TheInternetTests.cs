using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Samurai.WebSockets.UnitTests
{
    public class TheInternetTests
    {
        public TheInternetTests()
        {
            using var loggerFactory = LoggerFactory.Create(builder => builder
                    .SetMinimumLevel(LogLevel.Warning)
                    .AddConsole());
            Internal.Events.Log = new Internal.Events(loggerFactory.CreateLogger<Internal.Events>());
        }

        [Fact]
        public async Task ClientToServerTest()
        {
            Console.WriteLine("ClientToServerTest");
            using var theInternet = new TheInternet();
            var expected = "hello world";
            var buffer = Encoding.UTF8.GetBytes(expected);
            var readBuffer = new byte[256];

            await theInternet.ClientNetworkStream.WriteAsync(buffer, 0, buffer.Length);
            int count = await theInternet.ServerNetworkStream.ReadAsync(readBuffer, 0, readBuffer.Length);

            var actual = Encoding.UTF8.GetString(readBuffer, 0, count);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public async Task ServerToClientTest()
        {
            Console.WriteLine("ServerToClientTest");
            using var theInternet = new TheInternet();
            var expected = "hello world";
            var buffer = Encoding.UTF8.GetBytes(expected);
            var readBuffer = new byte[256];

            await theInternet.ServerNetworkStream.WriteAsync(buffer, 0, buffer.Length);
            int count = await theInternet.ClientNetworkStream.ReadAsync(readBuffer, 0, readBuffer.Length);

            var actual = Encoding.UTF8.GetString(readBuffer, 0, count);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public async Task ClientToServerToClientTest()
        {
            Console.WriteLine("ClientToServerToClientTest");
            using var theInternet = new TheInternet();
            var expected = "hello world";
            var buffer = Encoding.UTF8.GetBytes(expected);
            var readBuffer = new byte[256];

            await theInternet.ClientNetworkStream.WriteAsync(buffer, 0, buffer.Length);
            int count = await theInternet.ServerNetworkStream.ReadAsync(readBuffer, 0, readBuffer.Length);
            await theInternet.ServerNetworkStream.WriteAsync(readBuffer, 0, count);
            count = await theInternet.ClientNetworkStream.ReadAsync(readBuffer, 0, readBuffer.Length);

            var actual = Encoding.UTF8.GetString(readBuffer, 0, count);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void MultiTaskEchoTest()
        {
            Console.WriteLine("MultiTaskEchoTest");
            // This test sends the following 5 messages to the server:
            // "hello world 0"
            // "hello world 1"
            // "hello world 2"
            // "hello world 3"
            // "hello world 4"
            // And expects the following response
            // "Server : hello world 0"
            // "Server : hello world 1"
            // "Server : hello world 2"
            // "Server : hello world 3"
            // "Server : hello world 4"

            using var theInternet = new TheInternet();
            var expected = "hello world";
            const int NumMessagesToSend = 5;
            var source = new CancellationTokenSource();

            var clientSend = Task.Run(async () =>
            {
                for (int i = 0; i < NumMessagesToSend; i++)
                {
                    var buffer = Encoding.UTF8.GetBytes($"{expected} {i}");
                    await theInternet.ClientNetworkStream.WriteAsync(buffer, 0, buffer.Length, source.Token);
                }
            });

            Task<string[]> clientReceive = Task.Run(async () =>
            {
                var replies = new List<string>();
                var buffer = new byte[256];
                int count;
                while ((count = await theInternet.ClientNetworkStream.ReadAsync(buffer, 0, buffer.Length, source.Token)) > 0)
                {
                    var reply = Encoding.UTF8.GetString(buffer, 0, count);
                    replies.Add(reply);
                    if (replies.Count >= NumMessagesToSend)
                    {
                        source.Cancel();
                        break;
                    }
                }

                return replies.ToArray();
            });

            var serverTask = Task.Run(async () =>
            {
                var buffer = new byte[256];
                while (!source.Token.IsCancellationRequested)
                {
                    int count = await theInternet.ServerNetworkStream.ReadAsync(buffer, 0, buffer.Length, source.Token);
                    var message = Encoding.UTF8.GetString(buffer, 0, count);
                    message = "Server: " + message;
                    var sendBuffer = Encoding.UTF8.GetBytes(message);
                    await theInternet.ServerNetworkStream.WriteAsync(sendBuffer, 0, sendBuffer.Length, source.Token);
                }
            });

            Task.WaitAll(clientReceive, clientSend);

            var results = clientReceive.Result;
            Assert.Equal(NumMessagesToSend, results.Length);
            for (var i = 0; i < NumMessagesToSend; i++)
            {
                Assert.Equal($"Server: {expected} {i}", results[i]);
            }
        }
    }
}
