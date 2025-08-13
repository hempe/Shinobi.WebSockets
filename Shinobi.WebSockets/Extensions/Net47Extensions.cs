#if !NET8_0_OR_GREATER
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Shinobi.WebSockets.Extensions
{
    public static class Net47Extensions
    {
        public static bool Contains(this string source, string value, StringComparison comparisonType)
        {
            return source?.IndexOf(value, comparisonType) >= 0;
        }

        public static async Task WaitAsync(this Task task, CancellationToken cancellationToken)
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                var delayTask = Task.Delay(Timeout.Infinite, cts.Token);
                var completedTask = await Task.WhenAny(task, delayTask).ConfigureAwait(false);

                if (completedTask == delayTask)
                    cancellationToken.ThrowIfCancellationRequested();

                cts.Cancel(); // cancel the delay to avoid leaks
                await task.ConfigureAwait(false);
            }
        }

        public static async Task<T> WaitAsync<T>(this Task<T> task, CancellationToken cancellationToken)
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                var delayTask = Task.Delay(Timeout.Infinite, cts.Token);
                var completedTask = await Task.WhenAny(task, delayTask).ConfigureAwait(false);

                if (completedTask == delayTask)
                    cancellationToken.ThrowIfCancellationRequested();

                cts.Cancel();
                return await task.ConfigureAwait(false);
            }
        }

        public static async Task WaitAsync(this Task task, TimeSpan timeout)
        {
            var timeoutTask = Task.Delay(timeout);
            var completedTask = await Task.WhenAny(task, timeoutTask).ConfigureAwait(false);

            if (completedTask == timeoutTask)
                throw new TimeoutException("The operation has timed out.");

            await task.ConfigureAwait(false);
        }

        public static async Task<T> WaitAsync<T>(this Task<T> task, TimeSpan timeout)
        {
            var timeoutTask = Task.Delay(timeout);
            var completedTask = await Task.WhenAny(task, timeoutTask).ConfigureAwait(false);

            if (completedTask == timeoutTask)
                throw new TimeoutException("The operation has timed out.");

            return await task.ConfigureAwait(false);
        }
    }
}
#endif