using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Xunit;
using Xunit.Abstractions;
using Xunit.Runners;

public class Program
{
    public static async Task Main(string[] args)
    {
        if (args.Length == 1)
        {
            RunTestClass(args[0]);
            return;
        }

        var assemblyPath = typeof(Shinobi.WebSockets.UnitTests.TheInternetTests).Assembly.Location;

        using var runner = AssemblyRunner.WithoutAppDomain(assemblyPath);

        runner.OnDiscoveryComplete = info =>
            Console.WriteLine($"Discovered {info.TestCasesToRun} test(s)");

        runner.OnExecutionComplete = info =>
        {
            Console.WriteLine($"Tests completed: {info.TotalTests}, Failed: {info.TestsFailed}, Skipped: {info.TestsSkipped}");
        };

        var failures = new List<string>();
        runner.OnTestFailed = failure =>
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"❌ {failure.TestDisplayName}");
            Console.WriteLine(failure.ExceptionMessage);

            failures.Add($"x {failure.TestDisplayName}: {failure.ExceptionMessage}");
            Console.ResetColor();
        };

        runner.OnTestPassed = result =>
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✅ {result.TestDisplayName}");
            Console.ResetColor();
        };

        Console.WriteLine("Running tests...");
        runner.Start();

        // Wait until all tests complete
        while (runner.Status != AssemblyRunnerStatus.Idle)
            await Task.Delay(100);

        if (failures.Any())
            throw new Exception(string.Join(Environment.NewLine, failures));
    }

    private static void RunTestClass(string name)
    {
        var assembly = typeof(Shinobi.WebSockets.UnitTests.TheInternetTests).Assembly;
        string assemblyPath = assembly.Location;
        foreach (var test in assembly.GetTypes().Where(x => x.Name.Contains(name)))
        {
            string hardcodedTestClass = test.FullName;

            using var controller = new XunitFrontController(AppDomainSupport.Denied, assemblyPath);
            using var discoverySink = new TestDiscoverySink();

            // 1. Discover tests
            controller.Find(includeSourceInformation: false, discoverySink, TestFrameworkOptions.ForDiscovery());
            discoverySink.Finished.WaitOne();

            // 2. Filter to only tests from the one class
            var matchingTests = discoverySink.TestCases
                .Where(tc => tc.TestMethod.TestClass.Class.Name == hardcodedTestClass)
                .ToList();

            if (matchingTests.Count == 0)
            {
                Console.WriteLine($"No tests found in: {hardcodedTestClass}");
                return;
            }

            // 3. Run only filtered tests
            using var executionSink = new TestExecutionSink();
            controller.RunTests(matchingTests, executionSink, TestFrameworkOptions.ForExecution());
            executionSink.Finished.WaitOne();

            // 4. Report results
            foreach (var result in executionSink.TestResults)
            {
                switch (result)
                {
                    case ITestPassed passed:
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"✅ {passed.Test.DisplayName}");
                        Console.ResetColor();
                        break;
                    case ITestFailed failed:
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"❌ {failed.Test.DisplayName}");
                        Console.WriteLine(string.Join("\n", failed.Messages));
                        Console.ResetColor();
                        break;
                    case ITestSkipped skipped:
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"⚠️ {skipped.Test.DisplayName} (Skipped)");
                        Console.ResetColor();
                        break;
                }
            }

            Console.WriteLine($"Finished running {executionSink.TestResults.Count} test(s) from {hardcodedTestClass}");
        }
    }
}

internal sealed class TestExecutionSink : TestMessageSink, IDisposable
{
    public List<ITestResultMessage> TestResults { get; } = new List<ITestResultMessage>();
    public ManualResetEvent Finished { get; } = new ManualResetEvent(false);

    public TestExecutionSink()
    {
        this.Execution.TestPassedEvent += msg => this.TestResults.Add(msg.Message);
        this.Execution.TestFailedEvent += msg => this.TestResults.Add(msg.Message);
        this.Execution.TestSkippedEvent += msg => this.TestResults.Add(msg.Message);
        this.Execution.TestAssemblyFinishedEvent += _ => this.Finished.Set();
    }

    public override void Dispose()
    {
        this.Finished.Dispose();
        base.Dispose();
    }
}
