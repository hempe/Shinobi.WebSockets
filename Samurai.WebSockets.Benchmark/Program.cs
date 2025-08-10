
using System.Threading.Tasks;

using BenchmarkDotNet.Running;

public class Program
{
    public static async Task Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "single")
        {
            var b = new WebSocketThroughputBenchmarks();
            await b.SetupAsync();
            await b.RunThrouputBenchmarkAsync();
            await b.CleanupAsync();
        }
        else
        {
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        }
    }
}
