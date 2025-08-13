using System;

namespace Shinobi.WebSockets.Internal
{
    /// <summary>
    /// Calculates backoff delays for WebSocket reconnection attempts
    /// </summary>
    public class BackoffCalculator
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
        /// <returns>The calculated delay</returns>
        public TimeSpan CalculateDelay(int attemptNumber, TimeSpan initialDelay, TimeSpan maxDelay, double jitterPercent = 0.1)
        {
            if (attemptNumber < 0)
                throw new ArgumentOutOfRangeException(nameof(attemptNumber), "Attempt number must be non-negative");
            
            if (jitterPercent < 0.0 || jitterPercent > 1.0)
                throw new ArgumentOutOfRangeException(nameof(jitterPercent), "Jitter percent must be between 0.0 and 1.0");

            // Calculate exponential backoff: initialDelay * 2^attemptNumber
            var exponentialDelay = TimeSpan.FromMilliseconds(
                initialDelay.TotalMilliseconds * Math.Pow(2, attemptNumber));

            // Cap at max delay
            if (exponentialDelay > maxDelay)
                exponentialDelay = maxDelay;

            // Apply jitter: random value between (1 - jitterPercent) and (1 + jitterPercent)
            var jitterMultiplier = 1.0 + (this.random.NextDouble() * 2.0 - 1.0) * jitterPercent;
            var finalDelayMs = exponentialDelay.TotalMilliseconds * jitterMultiplier;

            // Ensure minimum of 0
            finalDelayMs = Math.Max(0, finalDelayMs);

            return TimeSpan.FromMilliseconds(finalDelayMs);
        }

        /// <summary>
        /// Calculates delay without jitter for predictable testing
        /// </summary>
        /// <param name="attemptNumber">The attempt number (0-based)</param>
        /// <param name="initialDelay">The initial delay for the first attempt</param>
        /// <param name="maxDelay">The maximum delay allowed</param>
        /// <returns>The calculated delay without jitter</returns>
        public TimeSpan CalculateDelayWithoutJitter(int attemptNumber, TimeSpan initialDelay, TimeSpan maxDelay)
        {
            if (attemptNumber < 0)
                throw new ArgumentOutOfRangeException(nameof(attemptNumber), "Attempt number must be non-negative");

            // Calculate exponential backoff: initialDelay * 2^attemptNumber
            var exponentialDelay = TimeSpan.FromMilliseconds(
                initialDelay.TotalMilliseconds * Math.Pow(2, attemptNumber));

            // Cap at max delay
            return exponentialDelay > maxDelay ? maxDelay : exponentialDelay;
        }
    }
}