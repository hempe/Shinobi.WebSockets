using System.Collections.Generic;

namespace Shinobi.WebSockets.Internal
{
    public class WebSocketExtension
    {
        public string Name { get; set; } = string.Empty;
        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();
    }

}
