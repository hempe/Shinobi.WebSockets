#if NET8_0_OR_GREATER

using System;
using System.IO;
using System.Linq;
using System.Text;

using Shinobi.WebSockets.Internal;

using Xunit;

namespace Shinobi.WebSockets.UnitTests
{

    public class PerMessageDeflateHandlerTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void DeflateTest(bool noContextTakeover)
        {
            using var inflater = new WebSocketInflater(noContextTakeover);
            using var deflater = new WebSocketDeflater(noContextTakeover);
            var message = "Hello World";
            deflater.Write(new ArraySegment<byte>(Encoding.UTF8.GetBytes(message)));
            using var deflated = deflater.Read();
            inflater.Write(deflated.GetDataArraySegment());

            using var df = inflater.Read();
            df.Position = 0;
            using var reader = new StreamReader(df, Encoding.UTF8);
            var result = reader.ReadToEnd();
            Assert.Equal(message, result);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void DeflateHugeMessageReadAllTest(bool noContextTakeover)
        {
            using var inflater = new WebSocketInflater(noContextTakeover);
            using var deflater = new WebSocketDeflater(noContextTakeover);
            this.DeflateHugeMessageReadAll(inflater, deflater);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void DeflateMultipleHugeMessageReadAll(bool noContextTakeover)
        {
            using var inflater = new WebSocketInflater(noContextTakeover);
            using var deflater = new WebSocketDeflater(noContextTakeover);
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

            using var deflated = deflater.Read();
            inflater.Write(deflated.GetDataArraySegment());

            using var df = inflater.Read();
            df.Position = 0;
            using var reader = new StreamReader(df, Encoding.UTF8);
            var result = reader.ReadToEnd();
            Assert.Equal(message, result);
        }
    }

}

#endif
