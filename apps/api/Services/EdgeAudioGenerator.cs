using System.Diagnostics;

namespace ChatGPTVoiceCompanion.Services;

internal static class EdgeAudioGenerator
{
    public static async Task GenerateAsync(string text, string voice, int rate, string path, CancellationToken ct)
    {
        var input = Path.ChangeExtension(path, ".txt");
        await File.WriteAllTextAsync(input, text, ct);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var start = new ProcessStartInfo("edge-tts")
            {
                RedirectStandardError = true, RedirectStandardOutput = true,
                UseShellExecute = false, CreateNoWindow = true
            };
            foreach (var argument in new[] { "--voice", voice, "--rate", $"{(rate >= 0 ? "+" : "")}{rate}%",
                "--file", input, "--write-media", path, "--no-progress" })
                start.ArgumentList.Add(argument);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start edge-tts.");
            using var registration = timeout.Token.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            });
            var stderrTask = process.StandardError.ReadToEndAsync();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                await process.WaitForExitAsync(CancellationToken.None);
                await Task.WhenAll(stderrTask, stdoutTask);
                throw new TimeoutException("Edge did not generate audio within 45 seconds. Try again or select Windows Local in Voice settings.");
            }
            catch (OperationCanceledException)
            {
                await process.WaitForExitAsync(CancellationToken.None);
                await Task.WhenAll(stderrTask, stdoutTask);
                throw;
            }
            var stderr = await stderrTask;
            await stdoutTask;
            ct.ThrowIfCancellationRequested();
            if (process.ExitCode == 0 && File.Exists(path) && new FileInfo(path).Length > 0) return;
            if (stderr.Contains("NoAudioReceived", StringComparison.Ordinal) && attempt < 3)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt), ct);
                continue;
            }
            throw new InvalidOperationException(stderr.Contains("NoAudioReceived", StringComparison.Ordinal)
                ? "Edge returned no audio after 3 attempts. Try another voice or use Windows Local."
                : $"Edge audio generation failed (exit {process.ExitCode}). {stderr}".Trim());
        }
    }
}
