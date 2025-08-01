using System;
using System.IO;
using System.Threading;

namespace Samurai.WebSockets.UnitTests
{
    internal class TheInternet : IDisposable
    {
        private readonly MemoryStream clientStream;
        private readonly MemoryStream serverStream;
        private readonly ManualResetEventSlim clientReadSlim;
        private readonly ManualResetEventSlim serverReadSlim;
        private readonly ManualResetEventSlim clientWriteSlim;
        private readonly ManualResetEventSlim serverWriteSlim;

        public MockNetworkStream? ClientNetworkStream { get; private set; }
        public MockNetworkStream? ServerNetworkStream { get; private set; }

        public TheInternet()
        {
            this.clientStream = new MemoryStream();
            this.serverStream = new MemoryStream();

            this.clientReadSlim = new ManualResetEventSlim(false);
            this.serverReadSlim = new ManualResetEventSlim(false);
            this.clientWriteSlim = new ManualResetEventSlim(true);
            this.serverWriteSlim = new ManualResetEventSlim(true);

            this.ClientNetworkStream = new MockNetworkStream("Client", this.clientStream, this.serverStream, this.clientReadSlim, this.serverReadSlim, this.clientWriteSlim, this.serverWriteSlim);
            this.ServerNetworkStream = new MockNetworkStream("Server", this.serverStream, this.clientStream, this.serverReadSlim, this.clientReadSlim, this.serverWriteSlim, this.clientWriteSlim);
        }

        public void Dispose()
        {
            this.clientStream?.Dispose();
            this.serverStream?.Dispose();
            this.clientReadSlim?.Dispose();
            this.serverReadSlim?.Dispose();
            this.clientWriteSlim?.Dispose();
            this.serverWriteSlim?.Dispose();

            this.ClientNetworkStream = null;
            this.ServerNetworkStream = null;
        }
    }
}
