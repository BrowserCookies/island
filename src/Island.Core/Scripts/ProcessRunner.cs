using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Island.Core.Scripts;

public sealed record ProcessSpec(
    string FileName,
    string Arguments,
    string? WorkingDirectory = null,
    bool ShowWindow = false,
    IReadOnlyDictionary<string, string>? Environment = null);

public enum ProcessOutcome { Completed, TimedOut, Cancelled, FailedToStart }

public sealed record ProcessResult(ProcessOutcome Outcome, int ExitCode, string Stdout, string Stderr, TimeSpan Duration, string? StartError = null)
{
    public bool Succeeded => Outcome == ProcessOutcome.Completed && ExitCode == 0;
}

/// <summary>Runs a process asynchronously, capturing output, with timeout and cancellation (kills the process tree).</summary>
public static class ProcessRunner
{
    /// <summary>Output beyond this many characters per stream is dropped (with a marker).</summary>
    public const int MaxCapturedChars = 1_000_000;

    public static async Task<ProcessResult> RunAsync(ProcessSpec spec, TimeSpan? timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = spec.FileName,
            Arguments = spec.Arguments ?? "",
            UseShellExecute = false,
            CreateNoWindow = !spec.ShowWindow,
            // With a visible console the user watches the output there, so it isn't captured.
            RedirectStandardOutput = !spec.ShowWindow,
            RedirectStandardError = !spec.ShowWindow,
            RedirectStandardInput = false,
        };
        if (!spec.ShowWindow)
        {
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
        }
        if (!string.IsNullOrWhiteSpace(spec.WorkingDirectory)) psi.WorkingDirectory = spec.WorkingDirectory;
        if (spec.Environment is not null)
            foreach (var kv in spec.Environment) psi.Environment[kv.Key] = kv.Value;

        var stdout = new BoundedBuilder(MaxCapturedChars);
        var stderr = new BoundedBuilder(MaxCapturedChars);
        var sw = Stopwatch.StartNew();

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdoutDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stderrDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!spec.ShowWindow)
        {
            proc.OutputDataReceived += (_, e) => { if (e.Data is null) stdoutDone.TrySetResult(); else stdout.AppendLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data is null) stderrDone.TrySetResult(); else stderr.AppendLine(e.Data); };
        }
        else
        {
            stdoutDone.TrySetResult();
            stderrDone.TrySetResult();
        }

        try
        {
            if (!proc.Start())
                return new ProcessResult(ProcessOutcome.FailedToStart, -1, "", "", sw.Elapsed, "Process did not start.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or System.IO.IOException)
        {
            return new ProcessResult(ProcessOutcome.FailedToStart, -1, "", "", sw.Elapsed,
                $"Could not start \"{spec.FileName}\": {ex.Message}");
        }

        if (!spec.ShowWindow)
        {
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
        }

        using var timeoutCts = timeout is { } t && t > TimeSpan.Zero ? new CancellationTokenSource(t) : new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        var outcome = ProcessOutcome.Completed;
        try
        {
            await proc.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            outcome = ct.IsCancellationRequested ? ProcessOutcome.Cancelled : ProcessOutcome.TimedOut;
            try { proc.Kill(entireProcessTree: true); }
            catch (Exception) { /* already gone */ }
            try { await proc.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch (Exception) { }
        }

        // Let the async readers drain.
        await Task.WhenAny(Task.WhenAll(stdoutDone.Task, stderrDone.Task), Task.Delay(2000)).ConfigureAwait(false);
        sw.Stop();

        int code;
        try { code = proc.HasExited ? proc.ExitCode : -1; }
        catch (InvalidOperationException) { code = -1; }

        return new ProcessResult(outcome, code, stdout.ToString(), stderr.ToString(), sw.Elapsed);
    }

    private sealed class BoundedBuilder
    {
        private readonly StringBuilder _sb = new();
        private readonly int _max;
        private bool _truncated;
        public BoundedBuilder(int max) => _max = max;

        public void AppendLine(string s)
        {
            lock (_sb)
            {
                if (_truncated) return;
                if (_sb.Length + s.Length + 1 > _max)
                {
                    _sb.Append("\n[output truncated]\n");
                    _truncated = true;
                    return;
                }
                _sb.Append(s).Append('\n');
            }
        }

        public override string ToString()
        {
            lock (_sb) return _sb.ToString();
        }
    }
}
