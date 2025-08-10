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
            var inflater = new WebSocketInflater();
            var deflater = new WebSocketDeflater();
            var message = "Hello World";
            deflater.Write(new ArraySegment<byte>(Encoding.UTF8.GetBytes(message)));
            inflater.Write(deflater.Read());

            using var df = inflater.Read();
            df.Position = 0;
            using var reader = new StreamReader(df, Encoding.UTF8);
            var result = reader.ReadToEnd();
            Assert.Equal(message, result);
        }

        [Fact]
        public void DeflateHugeMessageReadAllTest()
        {
            var inflater = new WebSocketInflater();
            var deflater = new WebSocketDeflater();
            this.DeflateHugeMessageReadAll(inflater, deflater);
        }

        [Fact]
        public void DeflateMultipleHugeMessageReadAll()
        {
            var inflater = new WebSocketInflater();
            var deflater = new WebSocketDeflater();
            for (var i = 0; i < 10; i++)
                this.DeflateHugeMessageReadAll(inflater, deflater);
        }

        private void DeflateHugeMessageReadAll(WebSocketInflater inflater, WebSocketDeflater deflater)
        {
            var message = string.Join(string.Empty, Enumerable.Range(0, 32 * 1024).Select(_ => 'A'));
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
                deflater.Write(new ArraySegment<byte>(chunk));
            }

            inflater.Write(deflater.Read());

            using var df = inflater.Read();
            df.Position = 0;
            using var reader = new StreamReader(df, Encoding.UTF8);
            var result = reader.ReadToEnd();
            Assert.Equal(message, result);
        }
    }
}