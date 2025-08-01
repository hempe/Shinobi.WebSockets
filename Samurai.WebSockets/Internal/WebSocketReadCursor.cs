namespace Samurai.WebSockets.Internal
{
    internal class WebSocketReadCursor
    {
        public WebSocketFrame WebSocketFrame { get; }

        // Number of bytes read in the last read operation
        public int NumBytesRead { get; }

        // Number of bytes remaining to read before we are done reading the entire frame
        public int NumBytesLeftToRead { get; }

        public WebSocketReadCursor(WebSocketFrame frame, int numBytesRead, int numBytesLeftToRead)
        {
            this.WebSocketFrame = frame;
            this.NumBytesRead = numBytesRead;
            this.NumBytesLeftToRead = numBytesLeftToRead;
        }
    }
}
