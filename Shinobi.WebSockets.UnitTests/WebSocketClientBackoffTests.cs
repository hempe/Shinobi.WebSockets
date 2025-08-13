using System;
using System.Reflection;
using Shinobi.WebSockets.Builders;
using Shinobi.WebSockets.Internal;
using Xunit;

namespace Shinobi.WebSockets.UnitTests
{
    public class WebSocketClientBackoffTests
    {
        [Fact]
        public void WebSocketClient_ShouldUseBackoffCalculatorForDelayCalculation()
        {
            // Arrange
            using var client = WebSocketClientBuilder.Create()
                .UseAutoReconnect(options =>
                {
                    options.InitialDelay = TimeSpan.FromMilliseconds(100);
                    options.MaxDelay = TimeSpan.FromSeconds(5);
                    options.Jitter = 0.0; // No jitter for predictable results
                })
                .Build();

            // Act - Use reflection to access the private CalculateReconnectDelay method
            var clientType = client.GetType();
            var calculateDelayMethod = clientType.GetMethod("CalculateReconnectDelay", 
                BindingFlags.NonPublic | BindingFlags.Instance);
            
            Assert.NotNull(calculateDelayMethod);

            // Test different attempt numbers
            var delay1 = (TimeSpan)calculateDelayMethod!.Invoke(client, new object[] { 1 })!;
            var delay2 = (TimeSpan)calculateDelayMethod.Invoke(client, new object[] { 2 })!;
            var delay3 = (TimeSpan)calculateDelayMethod.Invoke(client, new object[] { 3 })!;

            // Assert - Should follow exponential backoff pattern: 100ms, 200ms, 400ms
            Assert.Equal(100, delay1.TotalMilliseconds);
            Assert.Equal(200, delay2.TotalMilliseconds); 
            Assert.Equal(400, delay3.TotalMilliseconds);
        }

        [Fact]
        public void WebSocketClient_ShouldRespectMaxDelayConfiguration()
        {
            // Arrange
            using var client = WebSocketClientBuilder.Create()
                .UseAutoReconnect(options =>
                {
                    options.InitialDelay = TimeSpan.FromMilliseconds(1000);
                    options.MaxDelay = TimeSpan.FromMilliseconds(1500); // Cap at 1.5 seconds
                    options.Jitter = 0.0; // No jitter for predictable results
                })
                .Build();

            // Act - Use reflection to access the private CalculateReconnectDelay method
            var clientType = client.GetType();
            var calculateDelayMethod = clientType.GetMethod("CalculateReconnectDelay", 
                BindingFlags.NonPublic | BindingFlags.Instance);
            
            Assert.NotNull(calculateDelayMethod);

            var delay1 = (TimeSpan)calculateDelayMethod!.Invoke(client, new object[] { 1 })!;
            var delay2 = (TimeSpan)calculateDelayMethod.Invoke(client, new object[] { 2 })!;
            var delay3 = (TimeSpan)calculateDelayMethod.Invoke(client, new object[] { 3 })!;

            // Assert - Should be: 1000ms, 1500ms (capped), 1500ms (capped)
            Assert.Equal(1000, delay1.TotalMilliseconds);
            Assert.Equal(1500, delay2.TotalMilliseconds); // 2000ms capped to 1500ms
            Assert.Equal(1500, delay3.TotalMilliseconds); // 4000ms capped to 1500ms
        }

        [Fact]
        public void WebSocketClient_WithJitter_ShouldProduceVariableDelays()
        {
            // Arrange
            using var client = WebSocketClientBuilder.Create()
                .UseAutoReconnect(options =>
                {
                    options.InitialDelay = TimeSpan.FromMilliseconds(1000);
                    options.MaxDelay = TimeSpan.FromSeconds(10);
                    options.Jitter = 0.2; // 20% jitter
                })
                .Build();

            // Act - Use reflection to access the private CalculateReconnectDelay method
            var clientType = client.GetType();
            var calculateDelayMethod = clientType.GetMethod("CalculateReconnectDelay", 
                BindingFlags.NonPublic | BindingFlags.Instance);
            
            Assert.NotNull(calculateDelayMethod);

            // Generate multiple delays for the same attempt to see jitter variation
            var delays = new double[10];
            for (int i = 0; i < delays.Length; i++)
            {
                var delay = (TimeSpan)calculateDelayMethod!.Invoke(client, new object[] { 2 })!;
                delays[i] = delay.TotalMilliseconds;
            }

            // Assert - With 20% jitter on a 2000ms base delay, expect range ~1600ms to ~2400ms
            var expectedBase = 2000.0; // 1000 * 2^1
            var minExpected = expectedBase * 0.8; // 1600ms
            var maxExpected = expectedBase * 1.2; // 2400ms

            foreach (var delay in delays)
            {
                Assert.True(delay >= minExpected, $"Delay {delay}ms should be >= {minExpected}ms");
                Assert.True(delay <= maxExpected, $"Delay {delay}ms should be <= {maxExpected}ms");
            }

            // Verify we actually get some variation (not all delays are exactly the same)
            bool hasVariation = false;
            for (int i = 1; i < delays.Length; i++)
            {
                if (Math.Abs(delays[i] - delays[0]) > 1.0) // Allow for small floating point differences
                {
                    hasVariation = true;
                    break;
                }
            }
            
            Assert.True(hasVariation, "Expected some variation in delays due to jitter");
        }
    }
}