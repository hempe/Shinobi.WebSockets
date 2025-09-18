using System;

namespace Shinobi.WebSockets.Internal
{
    /// <summary>
    /// Interface for calculating backoff delays for WebSocket reconnection attempts
    /// </summary>
    public interface IBackoffCalculator
    {
        /// <summary>
        /// Calculates the delay for a reconnection attempt using exponential backoff with jitter
        /// </summary>
        /// <param name="attemptNumber">The attempt number (0-based)</param>
        /// <param name="initialDelay">The initial delay for the first attempt</param>
        /// <param name="maxDelay">The maximum delay allowed</param>
        /// <param name="jitterPercent">The jitter percentage (0.0 to 1.0)</param>
        /// <param name="backoffMultiplier">The backoff multiplier for exponential growth</param>
        /// <returns>The calculated delay</returns>
        TimeSpan CalculateDelay(int attemptNumber, TimeSpan initialDelay, TimeSpan maxDelay, double jitterPercent = 0.1, double backoffMultiplier = 2.0);
        
        /// <summary>
        /// Calculates delay without jitter for predictable testing
        /// </summary>
        /// <param name="attemptNumber">The attempt number (0-based)</param>
        /// <param name="initialDelay">The initial delay for the first attempt</param>
        /// <param name="maxDelay">The maximum delay allowed</param>
        /// <param name="backoffMultiplier">The backoff multiplier for exponential growth</param>
        /// <returns>The calculated delay without jitter</returns>
        TimeSpan CalculateDelayWithoutJitter(int attemptNumber, TimeSpan initialDelay, TimeSpan maxDelay, double backoffMultiplier = 2.0);
    }

    /// <summary>
    /// Calculates backoff delays for WebSocket reconnection attempts
    /// </summary>
    public class BackoffCalculator : IBackoffCalculator
    {
        private readonly Random random;

        public BackoffCalculator()
        {
            this.random = new Random();
        }

        /// <summary>
        /// Calculates the delay for a reconnection attempt using exponential backoff with jitter
        /// </summary>
        /// <param name="attemptNumber">The attempt number (0-based)</param>
        /// <param name="initialDelay">The initial delay for the first attempt</param>
        /// <param name="maxDelay">The maximum delay allowed</param>
        /// <param name="jitterPercent">The jitter percentage (0.0 to 1.0)</param>
        /// <param name="backoffMultiplier">The backoff multiplier for exponential growth</param>
        /// <returns>The calculated delay</returns>
        public TimeSpan CalculateDelay(int attemptNumber, TimeSpan initialDelay, TimeSpan maxDelay, double jitterPercent = 0.1, double backoffMultiplier = 2.0)
        {
            if (attemptNumber < 0)
                throw new ArgumentOutOfRangeException(nameof(attemptNumber), "Attempt number must be non-negative");
            
            if (jitterPercent < 0.0 || jitterPercent > 1.0)
                throw new ArgumentOutOfRangeException(nameof(jitterPercent), "Jitter percent must be between 0.0 and 1.0");

            if (backoffMultiplier <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(backoffMultiplier), "Backoff multiplier must be positive");

            // Calculate exponential backoff: initialDelay * backoffMultiplier^attemptNumber
            // Guard against overflow by checking if the power calculation would exceed TimeSpan.MaxValue
            var multiplier = Math.Pow(backoffMultiplier, attemptNumber);
            var delayMs = initialDelay.TotalMilliseconds * multiplier;
            
            TimeSpan exponentialDelay;
            if (double.IsInfinity(multiplier) || double.IsNaN(delayMs) || delayMs > TimeSpan.MaxValue.TotalMilliseconds)
            {
                // If calculation would overflow, use maxDelay
                exponentialDelay = maxDelay;
            }
            else
            {
                exponentialDelay = TimeSpan.FromMilliseconds(delayMs);
                // Cap at max delay
                if (exponentialDelay > maxDelay)
                    exponentialDelay = maxDelay;
            }

            // Apply jitter: random value between (1 - jitterPercent) and (1 + jitterPercent)
            var jitterMultiplier = 1.0 + (this.random.NextDouble() * 2.0 - 1.0) * jitterPercent;
            var finalDelayMs = exponentialDelay.TotalMilliseconds * jitterMultiplier;

            // Ensure minimum of 0 and cap at maxDelay
            finalDelayMs = Math.Max(0, finalDelayMs);
            finalDelayMs = Math.Min(finalDelayMs, maxDelay.TotalMilliseconds);

            return TimeSpan.FromMilliseconds(finalDelayMs);
        }

        /// <summary>
        /// Calculates delay without jitter for predictable testing
        /// </summary>
        /// <param name="attemptNumber">The attempt number (0-based)</param>
        /// <param name="initialDelay">The initial delay for the first attempt</param>
        /// <param name="maxDelay">The maximum delay allowed</param>
        /// <param name="backoffMultiplier">The backoff multiplier for exponential growth</param>
        /// <returns>The calculated delay without jitter</returns>
        public TimeSpan CalculateDelayWithoutJitter(int attemptNumber, TimeSpan initialDelay, TimeSpan maxDelay, double backoffMultiplier = 2.0)
        {
            if (attemptNumber < 0)
                throw new ArgumentOutOfRangeException(nameof(attemptNumber), "Attempt number must be non-negative");

            if (backoffMultiplier <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(backoffMultiplier), "Backoff multiplier must be positive");

            // Calculate exponential backoff: initialDelay * backoffMultiplier^attemptNumber
            // Guard against overflow by checking if the power calculation would exceed TimeSpan.MaxValue
            var multiplier = Math.Pow(backoffMultiplier, attemptNumber);
            var delayMs = initialDelay.TotalMilliseconds * multiplier;
            
            if (double.IsInfinity(multiplier) || double.IsNaN(delayMs) || delayMs > TimeSpan.MaxValue.TotalMilliseconds)
            {
                // If calculation would overflow, return maxDelay
                return maxDelay;
            }
            
            var exponentialDelay = TimeSpan.FromMilliseconds(delayMs);

            // Cap at max delay
            return exponentialDelay > maxDelay ? maxDelay : exponentialDelay;
        }
    }
}