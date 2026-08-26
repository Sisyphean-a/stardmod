using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security;
using StardewModdingAPI;

namespace PortableLoadingOptimizer.Services;

internal sealed class BackgroundFilePrefetcher : IDisposable
{
    private readonly record struct FileCandidate(string Path, long Length, int Priority);

    private readonly string modsPath;
    private readonly string savesPath;
    private readonly ModConfig config;
    private readonly PlatformPolicy policy;
    private readonly ManualResetEventSlim pauseGate = new(initialState: true);
    private readonly ConcurrentQueue<WorkerMessage> messages = new();
    private readonly object sync = new();
    private CancellationTokenSource cancellation = new();
    private Task? worker;
    private volatile string state = "idle";
    private volatile string pauseReason = "";
    private long bytesRead;
    private int filesRead;
    private int filesPlanned;
    private long pauseVersion;

    internal BackgroundFilePrefetcher(string modsPath, string savesPath, ModConfig config, PlatformPolicy policy)
    {
        this.modsPath = modsPath;
        this.savesPath = savesPath;
        this.config = config;
        this.policy = policy;
    }

    internal void Start(bool restartIfCompleted = false)
    {
        if (!config.EnableBackgroundFilePrefetch)
        {
            state = "disabled";
            return;
        }

        lock (sync)
        {
            if (worker is { IsCompleted: false })
                return;
            if (worker is { IsCompletedSuccessfully: true } && !restartIfCompleted)
                return;

            cancellation.Dispose();
            cancellation = new CancellationTokenSource();
            pauseGate.Set();
            pauseReason = "";
            Interlocked.Exchange(ref bytesRead, 0);
            Interlocked.Exchange(ref filesRead, 0);
            Interlocked.Exchange(ref filesPlanned, 0);
            Interlocked.Exchange(ref pauseVersion, 0);
            state = config.PrefetchStartDelaySeconds > 0 ? "waiting" : "planning";
            CancellationToken token = cancellation.Token;
            worker = Task.Run(() => Run(token), token);
        }
    }

    internal void Pause(string reason)
    {
        Interlocked.Increment(ref pauseVersion);
        pauseReason = reason;
        pauseGate.Reset();
        if (state is "waiting" or "planning" or "reading")
            state = "paused";
    }

    internal void Resume(int afterSeconds = 0)
    {
        Task? current;
        CancellationToken token;
        lock (sync)
        {
            current = worker;
            token = cancellation.Token;
        }

        if (current is null || current.IsCompleted)
        {
            Start();
            return;
        }

        pauseReason = "";
        long expectedPauseVersion = Interlocked.Read(ref pauseVersion);
        if (afterSeconds <= 0)
        {
            pauseGate.Set();
            state = "reading";
            return;
        }

        state = "waiting";
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(afterSeconds), token).ConfigureAwait(false);
                if (!token.IsCancellationRequested
                    && Interlocked.Read(ref pauseVersion) == expectedPauseVersion)
                {
                    pauseGate.Set();
                    state = "reading";
                }
            }
            catch (OperationCanceledException)
            {
                // 线程正在停止；最终状态由主工作任务统一收口。
            }
        }, token);
    }

    internal void Stop(string reason)
    {
        lock (sync)
        {
            cancellation.Cancel();
            pauseGate.Set();
            state = $"stopping:{reason}";
        }
    }

    internal string GetStatus()
    {
        double megabytes = Interlocked.Read(ref bytesRead) / 1024d / 1024d;
        string reason = string.IsNullOrEmpty(pauseReason) ? "" : $", reason={pauseReason}";
        return $"[PREFETCH] state={state}, files={Volatile.Read(ref filesRead)}/{Volatile.Read(ref filesPlanned)}, read={megabytes:F1}MB, limit={policy.PrefetchMaximumMegabytes}MB, rate={policy.PrefetchMegabytesPerSecond}MB/s{reason}";
    }

    internal bool TryDequeueMessage(out WorkerMessage message) => messages.TryDequeue(out message);

    public void Dispose()
    {
        Task? current;
        lock (sync)
        {
            cancellation.Cancel();
            pauseGate.Set();
            current = worker;
            state = "stopping:dispose";
        }

        if (current is { IsCompleted: false })
        {
            try
            {
                current.Wait(TimeSpan.FromSeconds(1));
            }
            catch (AggregateException)
            {
                // 可选文件错误由工作线程自己的保护路径处理。
            }
        }

        lock (sync)
        {
            cancellation.Dispose();
            pauseGate.Dispose();
        }
    }

    private void Run(CancellationToken token)
    {
        try
        {
            if (config.PrefetchStartDelaySeconds > 0)
            {
                state = "waiting";
                WaitWithPause(TimeSpan.FromSeconds(config.PrefetchStartDelaySeconds), token);
            }

            pauseGate.Wait(token);
            state = "planning";
            List<FileCandidate> plan = BuildPlan(token);
            Volatile.Write(ref filesPlanned, plan.Count);
            state = "reading";

            byte[] buffer = ArrayPool<byte>.Shared.Rent(256 * 1024);
            try
            {
                foreach (FileCandidate candidate in plan)
                {
                    token.ThrowIfCancellationRequested();
                    pauseGate.Wait(token);
                    state = "reading";
                    if (ReadFile(candidate.Path, buffer, token))
                        Interlocked.Increment(ref filesRead);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            state = "complete";
            messages.Enqueue(new WorkerMessage(GetStatus(), LogLevel.Info));
        }
        catch (OperationCanceledException)
        {
            state = "cancelled";
        }
        catch (Exception ex)
        {
            state = "failed";
            messages.Enqueue(new WorkerMessage($"[PREFETCH] 后台预读已停止，原生加载不受影响：{ex.GetBaseException().Message}", LogLevel.Warn));
        }
    }

    private bool ReadFile(string path, byte[] buffer, CancellationToken token)
    {
        try
        {
            using FileStream stream = OpenSequentialRead(path, buffer.Length);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                pauseGate.Wait(token);
                Stopwatch clock = Stopwatch.StartNew();
                int count = stream.Read(buffer, 0, buffer.Length);
                if (count <= 0)
                    return true;

                Interlocked.Add(ref bytesRead, count);
                Throttle(count, clock, token);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            // 失败：被锁定或被沙盒拒绝的文件不是必需项，游戏继续走原生读取路径。
            return false;
        }
    }

    private static FileStream OpenSequentialRead(string path, int bufferSize)
    {
        try
        {
            return new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize,
                FileOptions.SequentialScan);
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException)
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize, FileOptions.SequentialScan);
        }
    }

    private List<FileCandidate> BuildPlan(CancellationToken token)
    {
        StringComparer pathComparer = policy.IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        HashSet<string> extensions = new(
            config.PrefetchExtensions.Select(NormalizeExtension).Where(extension => extension.Length > 1),
            StringComparer.OrdinalIgnoreCase);
        HashSet<string> excludedDirectories = new(config.ExcludedDirectoryNames, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, FileCandidate> candidates = new(pathComparer);

        if (config.PrefetchRecentSaveFiles && config.RecentSaveCount > 0 && Directory.Exists(savesPath))
        {
            foreach (DirectoryInfo directory in SafeEnumerateDirectories(savesPath)
                         .OrderByDescending(item => item.LastWriteTimeUtc)
                         .Take(config.RecentSaveCount))
            {
                token.ThrowIfCancellationRequested();
                pauseGate.Wait(token);
                foreach (FileInfo file in SafeEnumerateFiles(directory.FullName))
                {
                    if (file.Length > 0 && (file.Extension.Equals(".xml", StringComparison.OrdinalIgnoreCase) || file.Extension.Length == 0))
                        candidates[file.FullName] = new FileCandidate(file.FullName, file.Length, 0);
                }
            }
        }

        foreach (FileInfo file in EnumerateModFiles(excludedDirectories, token))
        {
            if (!extensions.Contains(file.Extension) || file.Length <= 0)
                continue;

            int priority = file.Extension.ToLowerInvariant() switch
            {
                ".tmx" or ".tbin" => 10,
                ".json" => 20,
                ".png" => 30,
                _ => 40
            };
            candidates[file.FullName] = new FileCandidate(file.FullName, file.Length, priority);
        }

        long limit = policy.PrefetchMaximumMegabytes * 1024L * 1024L;
        long selectedBytes = 0;
        List<FileCandidate> selected = new();
        foreach (FileCandidate candidate in candidates.Values
                     .Where(candidate => candidate.Length <= limit)
                     .OrderBy(candidate => candidate.Priority)
                     .ThenBy(candidate => candidate.Length)
                     .ThenBy(candidate => candidate.Path, pathComparer))
        {
            if (selectedBytes + candidate.Length > limit)
                continue;

            selected.Add(candidate);
            selectedBytes += candidate.Length;
        }

        return selected;
    }

    private IEnumerable<FileInfo> EnumerateModFiles(HashSet<string> excludedDirectories, CancellationToken token)
    {
        if (!Directory.Exists(modsPath))
            yield break;

        Stack<DirectoryInfo> pending = new();
        pending.Push(new DirectoryInfo(modsPath));
        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            pauseGate.Wait(token);
            DirectoryInfo directory = pending.Pop();
            foreach (FileInfo file in SafeEnumerateFiles(directory.FullName))
                yield return file;
            foreach (DirectoryInfo child in SafeEnumerateDirectories(directory.FullName))
            {
                if (!excludedDirectories.Contains(child.Name)
                    && (child.Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    pending.Push(child);
                }
            }
        }
    }

    private void Throttle(int bytes, Stopwatch clock, CancellationToken token)
    {
        double remainingSeconds = bytes / (policy.PrefetchMegabytesPerSecond * 1024d * 1024d) - clock.Elapsed.TotalSeconds;
        if (remainingSeconds <= 0)
            return;

        int milliseconds = (int)Math.Min(250, Math.Ceiling(remainingSeconds * 1000));
        if (token.WaitHandle.WaitOne(milliseconds))
            token.ThrowIfCancellationRequested();
    }

    private void WaitWithPause(TimeSpan delay, CancellationToken token)
    {
        TimeSpan remaining = delay;
        while (remaining > TimeSpan.Zero)
        {
            token.ThrowIfCancellationRequested();
            pauseGate.Wait(token);
            int milliseconds = remaining > TimeSpan.FromMilliseconds(250)
                ? 250
                : Math.Max(1, (int)remaining.TotalMilliseconds);
            Stopwatch slice = Stopwatch.StartNew();
            if (token.WaitHandle.WaitOne(milliseconds))
                token.ThrowIfCancellationRequested();
            remaining -= slice.Elapsed;
        }
    }

    private static FileInfo[] SafeEnumerateFiles(string path)
    {
        try
        {
            return new DirectoryInfo(path).EnumerateFiles().ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return Array.Empty<FileInfo>();
        }
    }

    private static DirectoryInfo[] SafeEnumerateDirectories(string path)
    {
        try
        {
            return new DirectoryInfo(path).EnumerateDirectories().ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return Array.Empty<DirectoryInfo>();
        }
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return string.Empty;
        return extension.StartsWith(".", StringComparison.Ordinal) ? extension : $".{extension}";
    }
}
