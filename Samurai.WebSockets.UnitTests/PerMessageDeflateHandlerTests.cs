using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

using Microsoft.VisualBasic;

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
            var buffer = new byte[1024];
            var ms = new ArrayPoolStream();
            sut.Write(Encoding.UTF8.GetBytes(message), System.Net.WebSockets.WebSocketMessageType.Text, WebSocketOpCode.TextFrame);
            foreach (var frame in sut.GetFames(buffer))
            {
                ms.Write(buffer, 0, frame.Count);
            }
            ms.Position = 0;
            using var df = new DeflateStream(ms, CompressionMode.Decompress);
            using var reader = new StreamReader(df, Encoding.UTF8);
            var result = reader.ReadToEnd();
            Assert.Equal(message, result);
        }

        [Fact]
        public void DeflateHugeMessageTest()
        {
            var sut = new PerMessageDeflateHandler();
            var message = string.Join(string.Empty, Enumerable.Range(0, 32 * 1024).Select(_ => 'A'));
            var buffer = new byte[1024];
            var ms = new ArrayPoolStream();
            sut.Write(Encoding.UTF8.GetBytes(message), System.Net.WebSockets.WebSocketMessageType.Text, WebSocketOpCode.TextFrame);
            foreach (var frame in sut.GetFames(buffer))
            {
                ms.Write(buffer, 0, frame.Count);
            }
            ms.Position = 0;
            using var df = new DeflateStream(ms, CompressionMode.Decompress);
            using var reader = new StreamReader(df, Encoding.UTF8);
            var result = reader.ReadToEnd();
            Assert.Equal(message, result);
        }
    }
}