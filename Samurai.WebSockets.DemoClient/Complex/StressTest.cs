using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Samurai.WebSockets;

namespace WebSockets.DemoClient.Complex
{
    class StressTest
    {
        private readonly int seed;
        private readonly Uri uri;
        private readonly int numItems;
        private readonly int minNumBytesPerMessage;
        private readonly int maxNumBytesPerMessage;
        WebSocket webSocket;
        CancellationToken cancellationToken;
        byte[][] expectedValues;
        private readonly IWebSocketClientFactory clientFactory;

        public StressTest(int seed, Uri uri, int numItems, int minNumBytesPerMessage, int maxNumBytesPerMessage)
        {
            this.seed = seed;
            this.uri = uri;
            this.numItems = numItems;
            this.minNumBytesPerMessage = minNumBytesPerMessage;
            this.maxNumBytesPerMessage = maxNumBytesPerMessage;
            this.clientFactory = new WebSocketClientFactory();
        }

        public async Task Run()
        {
            // NOTE: if the service is so busy that it cannot respond to a PING within the KeepAliveInterval interval the websocket connection will be closed
            // To run extreme tests it is best to set the KeepAliveInterval to TimeSpan.Zero to disable ping pong
            WebSocketClientOptions options = new WebSocketClientOptions() { NoDelay = true, KeepAliveInterval = TimeSpan.FromSeconds(2), SecWebSocketProtocol = "chatV2, chatV1" };
            using (this.webSocket = await this.clientFactory.ConnectAsync(this.uri, options))
            {
                var source = new CancellationTokenSource();
                this.cancellationToken = source.Token;

                Random rand = new Random(this.seed);
                this.expectedValues = new byte[50][];
                for (int i = 0; i < this.expectedValues.Length; i++)
                {
                    int numBytes = rand.Next(this.minNumBytesPerMessage, this.maxNumBytesPerMessage);
                    byte[] bytes = new byte[numBytes];
                    rand.NextBytes(bytes);
                    this.expectedValues[i] = bytes;
                }

                Task recTask = Task.Run(this.ReceiveLoop);
                byte[] sendBuffer = new byte[this.maxNumBytesPerMessage];
                for (int i = 0; i < this.numItems; i++)
                {
                    int index = i % this.expectedValues.Length;
                    byte[] bytes = this.expectedValues[index];
                    Buffer.BlockCopy(bytes, 0, sendBuffer, 0, bytes.Length);
                    ArraySegment<byte> buffer = new ArraySegment<byte>(sendBuffer, 0, bytes.Length);
                    await this.webSocket.SendAsync(buffer, WebSocketMessageType.Binary, true, source.Token);
                }

                await this.webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, source.Token);
                recTask.Wait();
            }
        }

        private static bool AreEqual(byte[] actual, byte[] expected, int countActual)
        {
            if (countActual != expected.Length)
            {
                return false;
            }

            for (int i = 0; i < countActual; i++)
            {
                if (actual[i] != expected[i])
                {
                    return false;
                }
            }

            return true;
        }

        private async Task ReceiveLoop()
        {
            // the recArray should be large enough to at least receive control frames like Ping and Close frames (with payload)
            const int MIN_BUFFER_SIZE = 510;
            int size = this.maxNumBytesPerMessage < MIN_BUFFER_SIZE ? MIN_BUFFER_SIZE : this.maxNumBytesPerMessage;
            var recArray = new byte[size];
            var recBuffer = new ArraySegment<byte>(recArray);

            int i = 0;
            while (true)
            {
                WebSocketReceiveResult result = await this.webSocket.ReceiveAsync(recBuffer, this.cancellationToken);

                if (!result.EndOfMessage)
                {
                    throw new Exception("Multi frame messages not supported");
                }

                if (result.MessageType == WebSocketMessageType.Close || this.cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (result.Count == 0)
                {
                    await this.webSocket.CloseOutputAsync(WebSocketCloseStatus.InvalidPayloadData, "Zero bytes in payload", this.cancellationToken);
                    return;
                }

                byte[] valueActual = recBuffer.Array;
                int index = i % this.expectedValues.Length;
                i++;
                byte[] valueExpected = this.expectedValues[index];

                if (!AreEqual(valueActual, valueExpected, result.Count))
                {
                    await this.webSocket.CloseOutputAsync(WebSocketCloseStatus.InvalidPayloadData, "Value actual does not equal value expected", this.cancellationToken);
                    throw new Exception($"Expected: {valueExpected.Length} bytes Actual: {result.Count} bytes. Contents different.");
                }
            }
        }
    }
}
