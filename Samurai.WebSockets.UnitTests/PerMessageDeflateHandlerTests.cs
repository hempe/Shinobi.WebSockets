using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

using Samurai.WebSockets.Internal;

using Xunit;

namespace Samurai.WebSockets.UnitTests
{

    public class PerMessageDeflateHandlerTests
    {
        [Fact]
        public void DeflateTest()
        {
            var sut = new PerMessageDeflateHandler();
            var message = "Hello World";
            var ms = new ArrayPoolStream();
            var frame = sut.Write(Encoding.UTF8.GetBytes(message), System.Net.WebSockets.WebSocketMessageType.Text);
            ms.Write(frame.Array!, frame.Offset, frame.Count);
            ms.Position = 0;
            using var df = new DeflateStream(ms, CompressionMode.Decompress);
            using var reader = new StreamReader(df, Encoding.UTF8);
            var result = reader.ReadToEnd();
            Assert.Equal(message, result);
        }

        [Fact]
        public void DeflateHugeMessageReadAllTest()
        {
            var sut = new PerMessageDeflateHandler();
            this.DeflateHugeMessageReadAll(sut);
        }

        [Fact]
        public void DeflateMultipleHugeMessageReadAll()
        {
            var sut = new PerMessageDeflateHandler();
            for (var i = 0; i < 10; i++)
                this.DeflateHugeMessageReadAll(sut);
        }

        private void DeflateHugeMessageReadAll(PerMessageDeflateHandler sut)
        {
            var message = string.Join(string.Empty, Enumerable.Range(0, 32 * 1024).Select(_ => 'A'));
            var ms = new ArrayPoolStream();
            var bytes = Encoding.UTF8.GetBytes(message);
            var chunkSize = (int)Math.Ceiling((double)bytes.Length / 4);
            var chunks = bytes
                .Select((b, i) => new { Byte = b, Index = i })
                .GroupBy(x => x.Index / chunkSize)
                .Select(g => g.Select(x => x.Byte).ToArray())
                .ToArray();

            var ct = 0;
            foreach (var chunk in chunks)
            {
                ct++;
                var frame = sut.Write(chunk, System.Net.WebSockets.WebSocketMessageType.Text);
                ms.Write(frame.Array!, frame.Offset, frame.Count);
            }

            sut.Reset();
            Console.WriteLine("ReadAll transfer size was: " + ms.Position);
            ms.Position = 0;
            using var df = new DeflateStream(ms, CompressionMode.Decompress);
            using var reader = new StreamReader(df, Encoding.UTF8);
            var result = reader.ReadToEnd();
            Assert.Equal(message, result);
        }
    }
}