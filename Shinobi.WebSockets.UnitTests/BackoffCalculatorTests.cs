using System;
using System.Collections.Generic;
using System.Linq;
using Shinobi.WebSockets.Internal;
using Xunit;

namespace Shinobi.WebSockets.UnitTests
{
    public class BackoffCalculatorTests
    {
        [Fact]
        public void CalculateDelayWithoutJitter_ShouldReturnCorrectExponentialBackoff()
        {
            // Arrange
            var calculator = new BackoffCalculator();
            var initialDelay = TimeSpan.FromMilliseconds(100);
            var maxDelay = TimeSpan.FromSeconds(30);

            // Act & Assert
            Assert.Equal(TimeSpan.FromMilliseconds(100), calculator.CalculateDelayWithoutJitter(0, initialDelay, maxDelay)); // 100 * 2^0 = 100
            Assert.Equal(TimeSpan.FromMilliseconds(200), calculator.CalculateDelayWithoutJitter(1, initialDelay, maxDelay)); // 100 * 2^1 = 200
            Assert.Equal(TimeSpan.FromMilliseconds(400), calculator.CalculateDelayWithoutJitter(2, initialDelay, maxDelay)); // 100 * 2^2 = 400
            Assert.Equal(TimeSpan.FromMilliseconds(800), calculator.CalculateDelayWithoutJitter(3, initialDelay, maxDelay)); // 100 * 2^3 = 800
            Assert.Equal(TimeSpan.FromMilliseconds(1600), calculator.CalculateDelayWithoutJitter(4, initialDelay, maxDelay)); // 100 * 2^4 = 1600
        }

        [Fact]
        public void CalculateDelayWithoutJitter_ShouldRespectMaxDelay()
        {
            // Arrange
            var calculator = new BackoffCalculator();
            var initialDelay = TimeSpan.FromMilliseconds(1000);
            var maxDelay = TimeSpan.FromMilliseconds(2500);

            // Act & Assert
            Assert.Equal(TimeSpan.FromMilliseconds(1000), calculator.CalculateDelayWithoutJitter(0, initialDelay, maxDelay)); // 1000 * 2^0 = 1000
            Assert.Equal(TimeSpan.FromMilliseconds(2000), calculator.CalculateDelayWithoutJitter(1, initialDelay, maxDelay)); // 1000 * 2^1 = 2000
            Assert.Equal(TimeSpan.FromMilliseconds(2500), calculator.CalculateDelayWithoutJitter(2, initialDelay, maxDelay)); // 1000 * 2^2 = 4000, capped at 2500
            Assert.Equal(TimeSpan.FromMilliseconds(2500), calculator.CalculateDelayWithoutJitter(3, initialDelay, maxDelay)); // Capped at max
            Assert.Equal(TimeSpan.FromMilliseconds(2500), calculator.CalculateDelayWithoutJitter(10, initialDelay, maxDelay)); // Capped at max
        }

        [Fact]
        public void CalculateDelayWithoutJitter_WithNegativeAttemptNumber_ShouldThrowArgumentOutOfRangeException()
        {
            // Arrange
            var calculator = new BackoffCalculator();
            var initialDelay = TimeSpan.FromMilliseconds(100);
            var maxDelay = TimeSpan.FromSeconds(30);

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => 
                calculator.CalculateDelayWithoutJitter(-1, initialDelay, maxDelay));
        }

        [Fact]
        public void CalculateDelay_ShouldReturnValueWithinJitterRange()
        {
            // Arrange
            var calculator = new BackoffCalculator();
            var initialDelay = TimeSpan.FromMilliseconds(1000);
            var maxDelay = TimeSpan.FromSeconds(30);
            var jitterPercent = 0.1; // 10% jitter

            // Act
            var delay = calculator.CalculateDelay(1, initialDelay, maxDelay, jitterPercent);

            // Assert
            var expectedBaseDelay = 2000; // 1000 * 2^1
            var minDelay = expectedBaseDelay * (1.0 - jitterPercent);
            var maxDelayWithJitter = expectedBaseDelay * (1.0 + jitterPercent);

            Assert.True(delay.TotalMilliseconds >= minDelay, 
                $"Delay {delay.TotalMilliseconds}ms should be >= {minDelay}ms");
            Assert.True(delay.TotalMilliseconds <= maxDelayWithJitter, 
                $"Delay {delay.TotalMilliseconds}ms should be <= {maxDelayWithJitter}ms");
        }

        [Fact]
        public void CalculateDelay_WithZeroJitter_ShouldReturnExactExponentialBackoff()
        {
            // Arrange
            var calculator = new BackoffCalculator();
            var initialDelay = TimeSpan.FromMilliseconds(100);
            var maxDelay = TimeSpan.FromSeconds(30);

            // Act
            var delay = calculator.CalculateDelay(2, initialDelay, maxDelay, 0.0);

            // Assert
            Assert.Equal(400, delay.TotalMilliseconds); // 100 * 2^2 = 400, no jitter
        }

        [Fact]
        public void CalculateDelay_WithInvalidJitterPercent_ShouldThrowArgumentOutOfRangeException()
        {
            // Arrange
            var calculator = new BackoffCalculator();
            var initialDelay = TimeSpan.FromMilliseconds(100);
            var maxDelay = TimeSpan.FromSeconds(30);

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => 
                calculator.CalculateDelay(1, initialDelay, maxDelay, -0.1));
            Assert.Throws<ArgumentOutOfRangeException>(() => 
                calculator.CalculateDelay(1, initialDelay, maxDelay, 1.1));
        }

        [Fact]
        public void CalculateDelay_WithNegativeAttemptNumber_ShouldThrowArgumentOutOfRangeException()
        {
            // Arrange
            var calculator = new BackoffCalculator();
            var initialDelay = TimeSpan.FromMilliseconds(100);
            var maxDelay = TimeSpan.FromSeconds(30);

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => 
                calculator.CalculateDelay(-1, initialDelay, maxDelay));
        }

        [Fact]
        public void CalculateDelay_MultipleCallsWithSameInputs_ShouldReturnDifferentValuesWithJitter()
        {
            // Arrange
            var calculator = new BackoffCalculator();
            var initialDelay = TimeSpan.FromMilliseconds(1000);
            var maxDelay = TimeSpan.FromSeconds(30);
            var jitterPercent = 0.2; // 20% jitter for more variation

            // Act
            var delays = new double[10];
            for (int i = 0; i < delays.Length; i++)
            {
                delays[i] = calculator.CalculateDelay(1, initialDelay, maxDelay, jitterPercent).TotalMilliseconds;
            }

            // Assert - with 20% jitter, we should see some variation
            // Check that not all delays are exactly the same
            bool hasVariation = false;
            for (int i = 1; i < delays.Length; i++)
            {
                if (Math.Abs(delays[i] - delays[0]) > 1.0) // Allow for small floating point differences
                {
                    hasVariation = true;
                    break;
                }
            }
            
            Assert.True(hasVariation, "Expected some variation in delays due to jitter, but all were the same");
        }

        [Fact]
        public void CalculateDelay_ShouldNeverReturnNegativeValue()
        {
            // Arrange
            var calculator = new BackoffCalculator();
            var initialDelay = TimeSpan.FromMilliseconds(1);
            var maxDelay = TimeSpan.FromSeconds(1);

            // Act & Assert - even with maximum jitter, should never be negative
            for (int attempt = 0; attempt < 5; attempt++)
            {
                for (int i = 0; i < 100; i++) // Test multiple times due to randomness
                {
                    var delay = calculator.CalculateDelay(attempt, initialDelay, maxDelay, 1.0); // 100% jitter
                    Assert.True(delay.TotalMilliseconds >= 0, 
                        $"Delay should never be negative, got {delay.TotalMilliseconds}ms for attempt {attempt}");
                }
            }
        }

        [Fact]
        public void CalculateDelay_WithNoExponentialGrowthAndHighJitter_ShouldCreateVariation()
        {
            // Arrange - Test the specific scenario from the failing integration test
            var calculator = new BackoffCalculator();
            var initialDelay = TimeSpan.FromMilliseconds(200);
            var maxDelay = TimeSpan.FromSeconds(30);
            var jitterPercent = 0.5; // 50% jitter

            // Act - Simulate multiple reconnect attempts with no exponential growth (multiplier = 1.0)
            // Since BackoffCalculator uses exponential backoff (multiplier = 2), we test attempt 0 multiple times
            var delays = new double[20];
            for (int i = 0; i < delays.Length; i++)
            {
                delays[i] = calculator.CalculateDelay(0, initialDelay, maxDelay, jitterPercent).TotalMilliseconds;
            }

            // Assert
            // With 50% jitter on 200ms base: range should be 100ms to 300ms
            foreach (var delay in delays)
            {
                Assert.InRange(delay, 100.0, 300.0);
            }

            // Should have variation - not all delays should be the same
            bool hasVariation = false;
            var firstDelay = delays[0];
            foreach (var delay in delays)
            {
                if (Math.Abs(delay - firstDelay) > 10.0) // Allow for some tolerance
                {
                    hasVariation = true;
                    break;
                }
            }

            Assert.True(hasVariation, "50% jitter should create noticeable variation in delays");

            // Test the standard deviation to ensure meaningful variation
            var mean = delays.Sum() / delays.Length;
            var variance = delays.Sum(d => Math.Pow(d - mean, 2)) / delays.Length;
            var stdDev = Math.Sqrt(variance);

            // With 50% jitter, we should see a reasonable standard deviation
            // The range is 100-300ms (200ms spread), so std dev should be meaningful
            Assert.True(stdDev > 20.0, $"Standard deviation {stdDev:F2} should indicate meaningful variation with 50% jitter");
        }

        [Fact]
        public void CalculateDelay_MatchingFailingTestConditions_ShouldProduceVariation()
        {
            // Arrange - Exactly matching the failing test conditions
            var calculator = new BackoffCalculator();
            var initialDelay = TimeSpan.FromMilliseconds(200);
            var jitterPercent = 0.5; // 50% jitter
            
            // The failing test uses BackoffMultiplier = 1.0, but our BackoffCalculator 
            // always uses exponential backoff. To simulate the same delay repeatedly,
            // we always use attempt 0
            
            var delays = new List<double>();
            
            // Generate 10 delays as the failing test might
            for (int i = 0; i < 10; i++)
            {
                var delay = calculator.CalculateDelay(0, initialDelay, TimeSpan.FromSeconds(30), jitterPercent);
                delays.Add(delay.TotalMilliseconds);
            }

            // Assert - check if variation exists with same criteria as failing test
            var firstDelay = delays[0];
            var hasVariation = false;

            foreach (var delay in delays)
            {
                if (Math.Abs(delay - firstDelay) > 20) // Same tolerance as failing test
                {
                    hasVariation = true;
                    break;
                }
            }

            Assert.True(hasVariation, "Jitter should create variation in delays (matching failing test criteria)");

            // Verify range - should be 100ms to 300ms (200ms ± 50%)
            foreach (var delay in delays)
            {
                Assert.InRange(delay, 80, 400); // Same range as failing test with tolerance
            }
        }

        [Fact]
        public void CalculateDelay_WithHighAttemptNumber_ShouldNotOverflow()
        {
            // Arrange - Test the exact scenario from production (attempt 42)
            var calculator = new BackoffCalculator();
            var initialDelay = TimeSpan.FromMilliseconds(1000);
            var maxDelay = TimeSpan.FromMinutes(5);
            var attemptNumber = 42; // This is where the overflow occurred in production

            // Act & Assert - Should not throw OverflowException
            var delay = calculator.CalculateDelay(attemptNumber, initialDelay, maxDelay);
            
            // Should return maxDelay since exponential backoff would be astronomical
            Assert.True(delay <= maxDelay);
            Assert.True(delay.TotalMilliseconds > 0);
        }

        [Fact]
        public void CalculateDelayWithoutJitter_WithHighAttemptNumber_ShouldNotOverflow()
        {
            // Arrange - Test the exact scenario from production (attempt 42)
            var calculator = new BackoffCalculator();
            var initialDelay = TimeSpan.FromMilliseconds(1000);
            var maxDelay = TimeSpan.FromMinutes(5);
            var attemptNumber = 42; // This is where the overflow occurred in production

            // Act & Assert - Should not throw OverflowException
            var delay = calculator.CalculateDelayWithoutJitter(attemptNumber, initialDelay, maxDelay);
            
            // Should return maxDelay since exponential backoff would be astronomical
            Assert.Equal(maxDelay, delay);
        }

        [Fact]
        public void CalculateDelay_WithExtremelyHighAttemptNumber_ShouldNotOverflow()
        {
            // Arrange - Test even higher attempt numbers to ensure robustness
            var calculator = new BackoffCalculator();
            var initialDelay = TimeSpan.FromMilliseconds(100);
            var maxDelay = TimeSpan.FromSeconds(30);
            
            // Act & Assert - Test various high attempt numbers
            var testAttempts = new[] { 50, 100, 1000, int.MaxValue };
            
            foreach (var attemptNumber in testAttempts)
            {
                var delay = calculator.CalculateDelay(attemptNumber, initialDelay, maxDelay);
                Assert.True(delay <= maxDelay, $"Delay should not exceed maxDelay for attempt {attemptNumber}");
                Assert.True(delay.TotalMilliseconds >= 0, $"Delay should be non-negative for attempt {attemptNumber}");
                
                var delayWithoutJitter = calculator.CalculateDelayWithoutJitter(attemptNumber, initialDelay, maxDelay);
                Assert.Equal(maxDelay, delayWithoutJitter);
            }
        }
    }
}