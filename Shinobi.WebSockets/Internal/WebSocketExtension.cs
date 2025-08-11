using System.Collections.Generic;

namespace Shinobi.WebSockets.Internal
{
    /// <summary>
    /// Represents a WebSocket extension with its name and parameters.
    /// WebSocket extensions allow additional functionality to be negotiated during the handshake.
    /// </summary>
    public class WebSocketExtension
    {
        /// <summary>
        /// Gets or sets the name of the WebSocket extension
        /// </summary>
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the parameters for the WebSocket extension
        /// </summary>
        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();
    }

}
