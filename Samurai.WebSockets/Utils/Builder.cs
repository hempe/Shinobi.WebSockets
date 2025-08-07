using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Samurai.WebSockets.Utils
{

    public delegate ValueTask<TResult> Invoke<TInput, TResult>(TInput input, CancellationToken cancellationToken);
    public delegate ValueTask<TResult> Next<TInput, TResult>(TInput input, CancellationToken cancellationToken, Invoke<TInput, TResult> next);

    public static class Builder
    {
        public static Invoke<TInput, TResult> BuildInterceptorChain<TInput, TResult>(
            Invoke<TInput, TResult> terminal,
            IEnumerable<Next<TInput, TResult>>? interceptors)
        {
            if (interceptors == null)
                return terminal;

            var chain = terminal;
            foreach (var interceptor in interceptors.Reverse())
            {
                var next = chain;
                chain = (input, cancellationToken) => interceptor(input, cancellationToken, next);
            }

            return chain;
        }
    }
}