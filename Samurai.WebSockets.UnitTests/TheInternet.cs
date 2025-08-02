using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;

namespace Samurai.WebSockets.UnitTests
{
    internal class TheInternet : IDisposable
    {
        private readonly Channel<byte[]> server;
        private readonly Channel<byte[]> client;

        public MockNetworkStream ClientNetworkStream { get; }
        public MockNetworkStream ServerNetworkStream { get; }

        public TheInternet()
        {
            this.server = Channel.CreateUnbounded<byte[]>();
            this.client = Channel.CreateUnbounded<byte[]>();

            // Client writes to clientToServer, reads from serverToClient
            this.ClientNetworkStream = new MockNetworkStream("Client", this.server.Writer, this.client.Reader);
            this.ServerNetworkStream = new MockNetworkStream("Server", this.client.Writer, this.server.Reader);
        }

        public void Dispose()
        {
            this.server.Writer.Complete();
            this.client.Writer.Complete();
        }
    }
}
