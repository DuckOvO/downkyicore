using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;

namespace DownKyi.CentralTestRunner;

internal static class BuildProcessRunner
{
    internal static async Task<int> BuildProjectAsync(
        string projectPath,
        string configuration,
        bool noRestore,
        CancellationToken cancellationToken)
    {
        var startInfo = CreateBuildStartInfo(projectPath, configuration, noRestore);
        var cleanupResourceDirectory = OperatingSystem.IsWindows()
            ? Path.GetDirectoryName(Path.GetFullPath(projectPath))
            : null;

        return await RunAsync(
            startInfo,
            cancellationToken,
            cleanupResourceDirectory: cleanupResourceDirectory).ConfigureAwait(false);
    }

    internal static ProcessStartInfo CreateBuildStartInfo(
        string projectPath,
        string configuration,
        bool noRestore)
    {
        var startInfo = new ProcessStartInfo("dotnet") { UseShellExecute = false };
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(configuration);
        startInfo.ArgumentList.Add("--disable-build-servers");
        startInfo.ArgumentList.Add("-nodeReuse:false");
        startInfo.ArgumentList.Add("-p:UseSharedCompilation=false");
        if (noRestore)
        {
            startInfo.ArgumentList.Add("--no-restore");
        }

        return startInfo;
    }

    internal static async Task<int> RunAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken,
        TimeSpan? cleanupTimeout = null,
        string? cleanupResourceDirectory = null,
        Func<int, TimeSpan, Task<FinalProcessSnapshot>>? captureSnapshotAsync = null)
    {
        var cleanupWindow = cleanupTimeout ?? TimeSpan.FromSeconds(5);
        using var scope = await OwnedProcessScope.StartAsync(startInfo, cleanupWindow)
            .ConfigureAwait(false);
        var process = scope.Host;
        var outputTask = ForwardOutputAsync(process.StandardOutput, Console.Out);
        var errorTask = ForwardOutputAsync(process.StandardError, Console.Error);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CleanupAfterCancellationAsync(
                scope,
                cleanupWindow,
                captureSnapshotAsync,
                cleanupResourceDirectory,
                outputTask,
                errorTask).ConfigureAwait(false);
            throw;
        }

        var exitCode = process.ExitCode;
        await ReleaseScopeAsync(
            scope,
            new CleanupDeadline(cleanupWindow),
            cleanupResourceDirectory,
            outputTask,
            errorTask).ConfigureAwait(false);
        return exitCode;
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A diagnostic failure must be retained while mandatory process termination still runs.")]
    internal static async Task CleanupAfterCancellationAsync(
        OwnedProcessScope scope,
        TimeSpan cleanupWindow,
        Func<int, TimeSpan, Task<FinalProcessSnapshot>>? captureSnapshotAsync = null,
        string? cleanupResourceDirectory = null)
    {
        await CleanupAfterCancellationAsync(
            scope,
            cleanupWindow,
            captureSnapshotAsync,
            cleanupResourceDirectory,
            outputTask: null,
            errorTask: null).ConfigureAwait(false);
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A diagnostic failure must be retained while mandatory process termination still runs.")]
    private static async Task CleanupAfterCancellationAsync(
        OwnedProcessScope scope,
        TimeSpan cleanupWindow,
        Func<int, TimeSpan, Task<FinalProcessSnapshot>>? captureSnapshotAsync,
        string? cleanupResourceDirectory,
        Task? outputTask,
        Task? errorTask)
    {
        var deadline = new CleanupDeadline(cleanupWindow);
        var captureSnapshot = captureSnapshotAsync ?? ProcessTreeSnapshot.CaptureAsync;
        ExceptionDispatchInfo? snapshotFailure = null;
        try
        {
            await Task.Run(() => captureSnapshot(scope.RootPid, deadline.SnapshotWindow))
                .WaitAsync(deadline.SnapshotWindow).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            snapshotFailure = ExceptionDispatchInfo.Capture(exception);
        }

        try
        {
            await ReleaseScopeAsync(
                scope,
                deadline,
                cleanupResourceDirectory,
                outputTask,
                errorTask).ConfigureAwait(false);
        }
        catch (Exception cleanupFailure) when (snapshotFailure is not null)
        {
            throw new AggregateException(
                $"The process snapshot failed: {snapshotFailure.SourceException.Message} " +
                $"Mandatory process cleanup also failed: {cleanupFailure.Message}",
                snapshotFailure.SourceException,
                cleanupFailure);
        }

        snapshotFailure?.Throw();
    }

    private static async Task ReleaseScopeAsync(
        OwnedProcessScope scope,
        CleanupDeadline deadline,
        string? cleanupResourceDirectory,
        Task? outputTask,
        Task? errorTask)
    {
        await scope.TerminateAsync(deadline).ConfigureAwait(false);
        if (outputTask is not null && errorTask is not null)
        {
            await Task.WhenAll(outputTask, errorTask)
                .WaitAsync(deadline.Remaining).ConfigureAwait(false);
        }

        if (OperatingSystem.IsWindows() && cleanupResourceDirectory is not null)
        {
            await WindowsDirectoryResourceRundown.WaitForDeleteAccessAsync(
                cleanupResourceDirectory,
                deadline.Remaining).ConfigureAwait(false);
        }
    }

    private static async Task ForwardOutputAsync(StreamReader source, TextWriter destination)
    {
        var buffer = new char[4096];
        while (true)
        {
            var count = await source.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
            if (count == 0)
            {
                return;
            }

            await destination.WriteAsync(buffer.AsMemory(0, count)).ConfigureAwait(false);
        }
    }

    internal static void KillOwnedProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (
            (exception is InvalidOperationException or System.ComponentModel.Win32Exception) &&
            process.HasExited)
        {
            // The build exited between the liveness check and the kill request.
        }
    }

    internal static async Task WaitForRootExitAsync(Process process, TimeSpan cleanupTimeout)
    {
        await process.WaitForExitAsync()
            .WaitAsync(cleanupTimeout)
            .ConfigureAwait(false);
    }

    internal static async Task WaitForOwnedProcessesToExitAsync(
        IReadOnlyList<ObservedProcess> ownedProcesses,
        TimeSpan cleanupTimeout,
        Func<Process, DateTimeOffset>? readStartTimeUtc = null)
    {
        using var timeout = new CancellationTokenSource(cleanupTimeout);
        var waits = ownedProcesses.Select(
            observedProcess => WaitForObservedProcessExitAsync(
                observedProcess,
                readStartTimeUtc ?? ReadStartTimeUtc,
                cancellationToken: timeout.Token));
        try
        {
            await Task.WhenAll(waits).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                "Owned process cleanup exceeded the cleanup timeout.",
                exception);
        }
    }

    internal static async Task WaitForObservedProcessExitAsync(
        ObservedProcess observedProcess,
        Func<Process, DateTimeOffset>? readStartTimeUtc = null,
        Func<Process, bool>? readHasExited = null,
        Func<int, bool>? isProcessPresent = null,
        CancellationToken cancellationToken = default)
    {
        await ObservedProcessIdentity.WaitUntilGoneAsync(
            observedProcess,
            cancellationToken,
            process => ObservedProcessIdentity.Observe(
                process, readStartTimeUtc, readHasExited, isProcessPresent)).ConfigureAwait(false);
    }

    private static DateTimeOffset ReadStartTimeUtc(Process process)
    {
        return process.StartTime.ToUniversalTime();
    }
}
