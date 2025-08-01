using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace WebSockets.DemoClient.Complex
{
    internal class TestRunner
    {
        private readonly Uri uri;
        private readonly int numThreads;
        private readonly int numItemsPerThread;
        private readonly int minNumBytesPerMessage;
        private readonly int maxNumBytesPerMessage;

        public TestRunner(Uri uri, int numThreads, int numItemsPerThread, int minNumBytesPerMessage, int maxNumBytesPerMessage)
        {
            this.uri = uri;
            this.numThreads = numThreads;
            this.numItemsPerThread = numItemsPerThread;
            this.minNumBytesPerMessage = minNumBytesPerMessage;
            this.maxNumBytesPerMessage = maxNumBytesPerMessage;
        }

        public void Run()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            Parallel.For(0, this.numThreads, this.Run);
            Console.WriteLine($"Completed in {stopwatch.Elapsed.TotalMilliseconds:#,##0.00} ms");
        }

        public void Run(int index, ParallelLoopState state)
        {
            StressTest test = new StressTest(index, this.uri, this.numItemsPerThread, this.minNumBytesPerMessage, this.maxNumBytesPerMessage);
            test.Run().Wait();
        }
    }
}
