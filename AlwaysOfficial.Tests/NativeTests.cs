using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace AlwaysOfficial.Tests;

public class NativeTests
{
    private const string WorkerVariable = "ALWAYSOFFICIAL_TEST_WORKER";

    [Test]
    public async Task SuiteRunsIn32BitProcess()
    {
        await Assert.That(IntPtr.Size).IsEqualTo(4);
    }

    [Test]
    public Task StandaloneProxyLoadsAdjacentModAndForwards()
    {
        return RunIsolated();
    }

    [Test]
    public Task DistributedDllLoadsAndForwards()
    {
        return RunIsolated();
    }

    [Test]
    public Task MismatchedImageLeavesEveryCallUntouched()
    {
        return RunIsolated();
    }

    [Test]
    public Task NativeCallbacksPreserveOfficialRetryFlow()
    {
        return RunIsolated();
    }

    [Test]
    public Task HeldDrivingInputsSurviveOfficialRestart()
    {
        return RunIsolated();
    }

    [Test]
    public Task CountdownReleaseWithoutBufferedKeyUp()
    {
        return RunIsolated();
    }

    // AOT runtimes and installed hooks live for a process lifetime. A filtered TUnit child
    // gives each scenario a fresh DLL, mutex and static state, and contains native crashes.
    private static async Task RunIsolated([CallerMemberName] string scenario = "")
    {
        await Assert.That(IntPtr.Size).IsEqualTo(4);
        if (Environment.GetEnvironmentVariable(WorkerVariable) == scenario)
        {
            NativeFixture.Run(scenario);
            return;
        }

        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.Environment[WorkerVariable] = scenario;
        start.ArgumentList.Add("--treenode-filter");
        start.ArgumentList.Add($"/*/*/NativeTests/{scenario}");
        start.ArgumentList.Add("--results-directory");
        start.ArgumentList.Add(Path.Combine(FindRoot(), "TestResults", "Native", scenario));
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start x86 TUnit worker");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Native scenario {scenario} exceeded 45 seconds");
        }
        var output = await stdout + await stderr;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Native scenario {scenario} exited with {process.ExitCode}:\n{output}");
        }
        // A misspelled filter must fail in the child (MTP exit code 8 for zero tests).
        Console.WriteLine(output);
    }

    internal static string FindRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "global.json")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("Cannot find the project root");
    }
}
