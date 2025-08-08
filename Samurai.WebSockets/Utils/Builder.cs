using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Samurai.WebSockets.Utils
{

    // 1 input
    public delegate ValueTask InvokeOn<TInput>(TInput input, CancellationToken cancellationToken);

    public delegate ValueTask On<TInput>(
        TInput input,
        CancellationToken cancellationToken,
        InvokeOn<TInput> next);


    // 2 inputs
    public delegate ValueTask InvokeOn<TInput1, TInput2>(
        TInput1 input1,
        TInput2 input2,
        CancellationToken cancellationToken);

    public delegate ValueTask On<TInput1, TInput2>(
        TInput1 input1,
        TInput2 input2,
        CancellationToken cancellationToken,
        InvokeOn<TInput1, TInput2> next);


    // 3 inputs
    public delegate ValueTask InvokeOn<TInput1, TInput2, TInput3>(
        TInput1 input1,
        TInput2 input2,
        TInput3 input3,
        CancellationToken cancellationToken);

    public delegate ValueTask On<TInput1, TInput2, TInput3>(
        TInput1 input1,
        TInput2 input2,
        TInput3 input3,
        CancellationToken cancellationToken,
        InvokeOn<TInput1, TInput2, TInput3> next);

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

        // 1 input
        public static InvokeOn<TInput> BuildOmChain<TInput>(

            IEnumerable<On<TInput>>? interceptors)
        {
            InvokeOn<TInput> terminal = (TInput _1, CancellationToken _2) => new ValueTask();
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


        // 2 inputs
        public static InvokeOn<TInput1, TInput2> BuildOmChain<TInput1, TInput2>(
            IEnumerable<On<TInput1, TInput2>>? interceptors)
        {
            InvokeOn<TInput1, TInput2> terminal = (TInput1 _1, TInput2 _2, CancellationToken _3) => new ValueTask();
            if (interceptors == null)
                return terminal;

            var chain = terminal;
            foreach (var interceptor in interceptors.Reverse())
            {
                var next = chain;
                chain = (input1, input2, cancellationToken) => interceptor(input1, input2, cancellationToken, next);
            }

            return chain;
        }


        // 3 inputs
        public static InvokeOn<TInput1, TInput2, TInput3> BuildOmChain<TInput1, TInput2, TInput3>(
            IEnumerable<On<TInput1, TInput2, TInput3>>? interceptors)
        {
            InvokeOn<TInput1, TInput2, TInput3> terminal = (TInput1 _1, TInput2 _2, TInput3 _3, CancellationToken _4) => new ValueTask();
            if (interceptors == null)
                return terminal;

            var chain = terminal;
            foreach (var interceptor in interceptors.Reverse())
            {
                var next = chain;
                chain = (input1, input2, input3, cancellationToken) => interceptor(input1, input2, input3, cancellationToken, next);
            }

            return chain;
        }

    }
}