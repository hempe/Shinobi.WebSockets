/**
 * Base58 encoding/decoding utilities
 * Uses Bitcoin alphabet: 123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz
 */
class Base58 {
    static alphabet = '123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz';
    
    static encode(str) {
        const bytes = new TextEncoder().encode(str);
        let num = 0n;
        
        // Convert bytes to big integer
        for (let i = 0; i < bytes.length; i++) {
            num = num * 256n + BigInt(bytes[i]);
        }
        
        // Convert to base58
        let result = '';
        while (num > 0n) {
            const remainder = num % 58n;
            result = this.alphabet[Number(remainder)] + result;
            num = num / 58n;
        }
        
        // Add leading zeros
        for (let i = 0; i < bytes.length && bytes[i] === 0; i++) {
            result = '1' + result;
        }
        
        return result || '1';
    }
    
    static decode(str) {
        let num = 0n;
        
        // Convert from base58
        for (let i = 0; i < str.length; i++) {
            const char = str[i];
            const charIndex = this.alphabet.indexOf(char);
            if (charIndex === -1) {
                throw new Error(`Invalid base58 character: ${char}`);
            }
            num = num * 58n + BigInt(charIndex);
        }
        
        // Convert to bytes
        const bytes = [];
        while (num > 0n) {
            bytes.unshift(Number(num % 256n));
            num = num / 256n;
        }
        
        // Add leading zeros
        for (let i = 0; i < str.length && str[i] === '1'; i++) {
            bytes.unshift(0);
        }
        
        return new TextDecoder().decode(new Uint8Array(bytes));
    }
}

/**
 * ShinobiWebSocket - Enhanced WebSocket client with auto-reconnect and header support
 * 
 * Provides same API as native WebSocket with additional features:
 * - Auto-reconnect with exponential backoff
 * - HTTP headers via |h|base58_name|base58_value subprotocols
 * - Additional events (onreconnecting, onreconnected)
 * - Drop-in replacement for native WebSocket
 */
class ShinobiWebSocket {
    /**
     * Creates a new ShinobiWebSocket instance
     * @param {string} url - WebSocket URL
     * @param {string[]|object} protocols - Subprotocols array or options object  
     * @param {object} options - Additional options (when protocols is array)
     */
    constructor(url, protocols = [], options = {}) {
        // Handle overloaded constructor: (url, options) or (url, protocols, options)
        if (typeof protocols === 'object' && !Array.isArray(protocols)) {
            options = protocols;
            protocols = options.protocols || [];
        }

        // Configuration
        const headers = options.headers || {};
        const subprotocols = Array.isArray(protocols) ? protocols : [];
        
        // Convert headers to |h|base58_name|base58_value subprotocols
        const finalProtocols = [...subprotocols];
        if (Object.keys(headers).length > 0) {
            // Add the base |h| transport protocol first
            finalProtocols.push("|h|");
            // Then add each header as |h|name|value
            for (const [name, value] of Object.entries(headers)) {
                const encodedName = Base58.encode(name);   // Base58 encode header name
                const encodedValue = Base58.encode(value); // Base58 encode header value
                finalProtocols.push(`|h|${encodedName}|${encodedValue}`);
            }
        }

        // Create internal WebSocket
        this._ws = new WebSocket(url, finalProtocols);

        // Shinobi-specific configuration
        this.autoReconnect = options.autoReconnect !== false;
        this.reconnectDelay = options.reconnectDelay || 1000;
        this.maxReconnectDelay = options.maxReconnectDelay || 10000;
        this.maxReconnectAttempts = options.maxReconnectAttempts || Infinity;
        this.backoffMultiplier = options.backoffMultiplier || 2.0;
        this.jitter = options.jitter || 0.1;
        this.urlSelector = options.urlSelector || null;

        // WebSocket API properties
        this.url = this._ws.url;
        this.protocol = this._ws.protocol;
        this.extensions = this._ws.extensions;
        this.binaryType = this._ws.binaryType;

        // Shinobi-specific events
        this.onreconnecting = null;  // Called before reconnect attempt
        this.onreconnected = null;   // Called after successful reconnection

        // Internal state
        this._originalUrl = url;
        this._originalProtocols = subprotocols;
        this._originalHeaders = headers;
        this._reconnectAttempts = 0;
        this._reconnectTimeout = null;
        this._isDestroyed = false;
        this._isFirstConnection = true;

        // Event handlers
        this._onopen = null;
        this._onmessage = null;
        this._onclose = null;
        this._onerror = null;

        this._setupWebSocketEvents();
    }

    // WebSocket API constants
    static get CONNECTING() { return WebSocket.CONNECTING; }
    static get OPEN() { return WebSocket.OPEN; }
    static get CLOSING() { return WebSocket.CLOSING; }
    static get CLOSED() { return WebSocket.CLOSED; }

    // WebSocket API properties  
    get readyState() { return this._ws.readyState; }
    get bufferedAmount() { return this._ws.bufferedAmount; }

    // WebSocket API event handlers
    get onopen() { return this._onopen; }
    set onopen(handler) { this._onopen = handler; }

    get onmessage() { return this._onmessage; }
    set onmessage(handler) { this._onmessage = handler; }

    get onclose() { return this._onclose; }
    set onclose(handler) { this._onclose = handler; }

    get onerror() { return this._onerror; }
    set onerror(handler) { this._onerror = handler; }

    // WebSocket API methods
    send(data) {
        return this._ws.send(data);
    }

    close(code = 1000, reason = '') {
        // Disable auto-reconnect on manual close
        this.autoReconnect = false;
        
        if (this._reconnectTimeout) {
            clearTimeout(this._reconnectTimeout);
            this._reconnectTimeout = null;
        }

        this._ws.close(code, reason);
    }

    /**
     * Sets up WebSocket event forwarding and auto-reconnect logic
     */
    _setupWebSocketEvents() {
        this._ws.onopen = (event) => {
            this._reconnectAttempts = 0;
            
            // Update mirrored properties
            this.url = this._ws.url;
            this.protocol = this._ws.protocol;
            this.extensions = this._ws.extensions;
            
            if (!this._isFirstConnection) {
                console.log('Reconnected to WebSocket server');
                this.onreconnected?.(event);
            } else {
                console.log('Connected to WebSocket server');
                this._isFirstConnection = false;
            }
            
            this._onopen?.(event);
        };

        this._ws.onmessage = (event) => {
            this._onmessage?.(event);
        };

        this._ws.onclose = (event) => {
            this._onclose?.(event);

            if (this.autoReconnect && !this._isDestroyed && !this._isCleanClose(event.code)) {
                this._scheduleReconnect();
            }
        };

        this._ws.onerror = (event) => {
            this._onerror?.(event);
        };
    }

    /**
     * Determines if close was intentional (don't reconnect)
     */
    _isCleanClose(code) {
        // 1000 = Normal Closure, 1001 = Going Away (user navigated away)
        return code === 1000 || code === 1001;
    }

    /**
     * Schedules a reconnection attempt with exponential backoff and jitter
     */
    _scheduleReconnect() {
        if (this._reconnectAttempts >= this.maxReconnectAttempts || this._isDestroyed) {
            console.log('Max reconnection attempts reached or client destroyed');
            return;
        }

        // Calculate exponential backoff delay
        const baseDelay = Math.min(
            this.reconnectDelay * Math.pow(this.backoffMultiplier, this._reconnectAttempts),
            this.maxReconnectDelay
        );

        // Apply jitter to prevent thundering herd
        const jitterAmount = baseDelay * this.jitter;
        const delay = baseDelay + (Math.random() * 2 - 1) * jitterAmount;
        const finalDelay = Math.max(0, Math.round(delay));

        this._reconnectAttempts++;

        console.log(`Reconnecting in ${finalDelay}ms (attempt ${this._reconnectAttempts})`);

        this.onreconnecting?.({ 
            attempt: this._reconnectAttempts, 
            delay: finalDelay,
            maxAttempts: this.maxReconnectAttempts 
        });

        this._reconnectTimeout = setTimeout(() => {
            console.log('Attempting to reconnect...');
            this._reconnect();
        }, finalDelay);
    }

    /**
     * Creates a new WebSocket connection (internal reconnection)
     */
    _reconnect() {
        if (this._isDestroyed) return;

        try {
            // Determine URL to connect to (use URL selector if configured)
            let connectUrl = this._originalUrl;
            if (this.urlSelector) {
                connectUrl = this.urlSelector(this._originalUrl, this._reconnectAttempts);
            }

            // Convert headers to |h|base58_name|base58_value subprotocols
            const protocols = [...this._originalProtocols];
            if (Object.keys(this._originalHeaders).length > 0) {
                // Add the base |h| transport protocol first
                protocols.push("|h|");
                // Then add each header as |h|name|value
                for (const [name, value] of Object.entries(this._originalHeaders)) {
                    const encodedName = Base58.encode(name);   // Base58 encode header name
                    const encodedValue = Base58.encode(value); // Base58 encode header value
                    protocols.push(`|h|${encodedName}|${encodedValue}`);
                }
            }

            // Close existing socket
            this._ws.onopen = null;
            this._ws.onmessage = null;
            this._ws.onclose = null;
            this._ws.onerror = null;

            // Create new WebSocket connection
            this._ws = new WebSocket(connectUrl, protocols);
            
            // Re-setup event handlers
            this._setupWebSocketEvents();
            
            // Update mirrored properties
            this.url = this._ws.url;
            this.protocol = this._ws.protocol;
            this.extensions = this._ws.extensions;

        } catch (error) {
            console.error('Failed to reconnect WebSocket:', error);
            this._onerror?.(error);
            
            if (this.autoReconnect && !this._isDestroyed) {
                this._scheduleReconnect();
            }
        }
    }


    /**
     * Permanently destroys the WebSocket client (Shinobi-specific)
     */
    destroy() {
        this._isDestroyed = true;
        this.close();
    }

    /**
     * Reconnects the WebSocket (Shinobi-specific)
     */
    reconnect() {
        if (this._isDestroyed) return;
        
        this.autoReconnect = true;
        this._reconnectAttempts = 0;
        
        if (this._reconnectTimeout) {
            clearTimeout(this._reconnectTimeout);
            this._reconnectTimeout = null;
        }
        
        this._ws.close(1000, 'Manual reconnect');
    }

    /**
     * Creates a ShinobiWebSocket builder for fluent configuration
     * @param {string} url - WebSocket URL
     * @returns {ShinobiWebSocketBuilder} Builder instance
     */
    static create(url) {
        return new ShinobiWebSocketBuilder(url);
    }
}

/**
 * Builder class for fluent ShinobiWebSocket configuration
 */
class ShinobiWebSocketBuilder {
    constructor(url) {
        this._url = url;
        this._protocols = [];
        this._headers = {};
        this._options = {};
        this._eventHandlers = {};
    }

    /**
     * Adds HTTP headers to be sent via subprotocols
     * @param {string} name - Header name
     * @param {string} value - Header value  
     * @returns {ShinobiWebSocketBuilder} Builder instance
     */
    addHeader(name, value) {
        this._headers[name] = value;
        return this;
    }

    /**
     * Adds multiple headers at once
     * @param {object} headers - Object with header name/value pairs
     * @returns {ShinobiWebSocketBuilder} Builder instance
     */
    addHeaders(headers) {
        Object.assign(this._headers, headers);
        return this;
    }

    /**
     * Adds a subprotocol
     * @param {string} protocol - Subprotocol name
     * @returns {ShinobiWebSocketBuilder} Builder instance
     */
    addProtocol(protocol) {
        this._protocols.push(protocol);
        return this;
    }

    /**
     * Sets multiple subprotocols
     * @param {string[]} protocols - Array of subprotocol names
     * @returns {ShinobiWebSocketBuilder} Builder instance  
     */
    useProtocols(protocols) {
        this._protocols = [...protocols];
        return this;
    }

    /**
     * Enables auto-reconnect
     * @param {boolean} enabled - Enable auto-reconnect (default: true)
     * @returns {ShinobiWebSocketBuilder} Builder instance
     */
    useAutoReconnect(enabled = true) {
        this._options.autoReconnect = enabled;
        return this;
    }

    /**
     * Sets reconnect timing configuration
     * @param {number} delay - Initial delay in ms (default: 1000)
     * @param {number} maxDelay - Maximum delay in ms (default: 10000)
     * @param {number} maxAttempts - Maximum attempts (default: Infinity)
     * @returns {ShinobiWebSocketBuilder} Builder instance
     */
    useReconnectConfig(delay = 1000, maxDelay = 10000, maxAttempts = Infinity) {
        this._options.reconnectDelay = delay;
        this._options.maxReconnectDelay = maxDelay;
        this._options.maxReconnectAttempts = maxAttempts;
        return this;
    }

    /**
     * Sets the onopen event handler
     * @param {function} handler - Event handler
     * @returns {ShinobiWebSocketBuilder} Builder instance
     */
    onOpen(handler) {
        this._eventHandlers.onopen = handler;
        return this;
    }

    /**
     * Sets the onmessage event handler
     * @param {function} handler - Event handler
     * @returns {ShinobiWebSocketBuilder} Builder instance
     */
    onMessage(handler) {
        this._eventHandlers.onmessage = handler;
        return this;
    }

    /**
     * Sets the onclose event handler
     * @param {function} handler - Event handler
     * @returns {ShinobiWebSocketBuilder} Builder instance
     */
    onClose(handler) {
        this._eventHandlers.onclose = handler;
        return this;
    }

    /**
     * Sets the onerror event handler
     * @param {function} handler - Event handler
     * @returns {ShinobiWebSocketBuilder} Builder instance
     */
    onError(handler) {
        this._eventHandlers.onerror = handler;
        return this;
    }

    /**
     * Sets the onreconnecting event handler (Shinobi-specific)
     * @param {function} handler - Event handler
     * @returns {ShinobiWebSocketBuilder} Builder instance
     */
    onReconnecting(handler) {
        this._eventHandlers.onreconnecting = handler;
        return this;
    }

    /**
     * Sets the onreconnected event handler (Shinobi-specific)
     * @param {function} handler - Event handler
     * @returns {ShinobiWebSocketBuilder} Builder instance
     */
    onReconnected(handler) {
        this._eventHandlers.onreconnected = handler;
        return this;
    }

    /**
     * Enables auto-reconnect with default settings (convenience method)
     * @returns {ShinobiWebSocketBuilder} Builder instance
     */
    enableAutoReconnect() {
        return this.useAutoReconnect(true);
    }

    /**
     * Configures exponential backoff with custom settings  
     * @param {number} initialDelay - Initial delay in ms (default: 1000)
     * @param {number} maxDelay - Maximum delay in ms (default: 30000)
     * @param {number} multiplier - Backoff multiplier (default: 2.0) 
     * @param {number} jitter - Jitter factor 0-1 (default: 0.1)
     * @returns {ShinobiWebSocketBuilder} Builder instance
     */
    useExponentialBackoff(initialDelay = 1000, maxDelay = 30000, multiplier = 2.0, jitter = 0.1) {
        return this.useReconnectConfig(initialDelay, maxDelay, Infinity)
            .useJitter(jitter)
            .useBackoffMultiplier(multiplier);
    }

    /**
     * Sets the backoff multiplier for reconnection delays
     * @param {number} multiplier - Multiplier for exponential backoff (default: 2.0)
     * @returns {ShinobiWebSocketBuilder} Builder instance
     */
    useBackoffMultiplier(multiplier = 2.0) {
        this._options.backoffMultiplier = multiplier;
        return this;
    }

    /**
     * Sets jitter factor to randomize reconnection delays
     * @param {number} jitter - Jitter factor between 0-1 (default: 0.1)
     * @returns {ShinobiWebSocketBuilder} Builder instance
     */
    useJitter(jitter = 0.1) {
        this._options.jitter = Math.max(0, Math.min(1, jitter));
        return this;
    }

    /**
     * Convenience method for reliable connection with sensible defaults
     * @returns {ShinobiWebSocketBuilder} Builder instance
     */
    useReliableConnection() {
        return this.enableAutoReconnect()
            .useExponentialBackoff(1000, 30000, 2.0, 0.1);
    }

    /**
     * Adds URL selector function for customizing reconnection URLs
     * @param {function} selector - Function(currentUrl, attemptNumber) => newUrl
     * @returns {ShinobiWebSocketBuilder} Builder instance
     */
    useUrlSelector(selector) {
        this._options.urlSelector = selector;
        return this;
    }

    /**
     * Configures fallback URLs for high availability
     * @param {...string} fallbackUrls - Array of fallback URLs to try
     * @returns {ShinobiWebSocketBuilder} Builder instance
     */
    useFallbackUrls(...fallbackUrls) {
        if (!fallbackUrls.length) {
            throw new Error('At least one fallback URL must be provided');
        }
        
        return this.useUrlSelector((currentUrl, attemptNumber) => {
            // Use original URL for first attempt, then cycle through fallbacks
            if (attemptNumber <= 1) return currentUrl;
            
            const fallbackIndex = (attemptNumber - 2) % fallbackUrls.length;
            return fallbackUrls[fallbackIndex];
        });
    }

    /**
     * Builds and connects the ShinobiWebSocket
     * @returns {ShinobiWebSocket} Configured ShinobiWebSocket instance
     */
    build() {
        const options = {
            ...this._options,
            headers: this._headers,
            protocols: this._protocols
        };

        const ws = new ShinobiWebSocket(this._url, options);

        // Apply event handlers
        for (const [event, handler] of Object.entries(this._eventHandlers)) {
            ws[event] = handler;
        }

        return ws;
    }
}

// Export for use in Node.js or browser modules
if (typeof module !== 'undefined' && module.exports) {
    module.exports = ShinobiWebSocket;
} else if (typeof window !== 'undefined') {
    window.ShinobiWebSocket = ShinobiWebSocket;
}