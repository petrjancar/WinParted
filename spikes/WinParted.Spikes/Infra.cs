using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Management.Infrastructure;
using Microsoft.Management.Infrastructure.Generic;
using Microsoft.Management.Infrastructure.Options;

namespace WinParted.Spikes;

/// <summary>Writes every line to the console and to a log file in spikes/output.</summary>
internal static class Log
{
    private static StreamWriter? _writer;

    public static string? FilePath { get; private set; }

    public static void Start(string name)
    {
        var dir = Path.Combine(RepoRoot(), "spikes", "output");
        Directory.CreateDirectory(dir);
        FilePath = Path.Combine(dir, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        _writer = new StreamWriter(FilePath) { AutoFlush = true };
        Line($"WinParted spike '{name}' · {Environment.OSVersion} · elevated={IsElevated()} · {DateTime.Now:O}");
    }

    public static void Stop()
    {
        _writer?.Dispose();
        _writer = null;
    }

    public static void Line(string text)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {text}";
        Console.WriteLine(line);
        _writer?.WriteLine(line);
    }

    public static void Section(string title)
    {
        Line("");
        Line($"===== {title} =====");
    }

    public static void Instance(CimInstance instance, string? title = null)
    {
        Line($"--- {title ?? instance.CimSystemProperties.ClassName}");
        foreach (var p in instance.CimInstanceProperties)
        {
            Line($"    {p.Name} = {Format(p.Value)}");
        }
    }

    public static string Format(object? value) => value switch
    {
        null => "<null>",
        char c => c == '\0' ? "<none>" : c.ToString(),
        string s => $"\"{s}\"",
        CimInstance i => "{" + string.Join(", ", i.CimInstanceProperties.Select(p => $"{p.Name}={Format(p.Value)}")) + "}",
        System.Collections.IEnumerable e => "[" + string.Join(", ", e.Cast<object?>().Select(Format)) + "]",
        _ => value.ToString() ?? "",
    };

    public static bool IsElevated() =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "WinParted.sln")))
            {
                return dir.FullName;
            }
        }

        return Directory.GetCurrentDirectory();
    }
}

/// <summary>Thin, logging wrapper around SMP via CIM.</summary>
internal sealed class Smp : IDisposable
{
    public const string Ns = @"root\Microsoft\Windows\Storage";

    public CimSession Session { get; } = CimSession.Create(null);

    public List<CimInstance> Query(string wql, string ns = Ns)
    {
        try
        {
            return [.. Session.QueryInstances(ns, "WQL", wql)];
        }
        catch (CimException ex)
        {
            LogCimException($"QUERY {wql}", ex);
            return [];
        }
    }

    public static object? Prop(CimInstance i, string name) => i.CimInstanceProperties[name]?.Value;

    public static CimMethodParametersCollection Params(params (string Name, object Value, CimType Type)[] args)
    {
        var p = new CimMethodParametersCollection();
        foreach (var (name, value, type) in args)
        {
            p.Add(CimMethodParameter.Create(name, value, type, CimFlags.In));
        }

        return p;
    }

    /// <summary>Calls an SMP method synchronously and logs the return value and all out parameters.</summary>
    public CimMethodResult? Call(CimInstance target, string method, params (string Name, object Value, CimType Type)[] args)
    {
        var label = $"{target.CimSystemProperties.ClassName}.{method}({string.Join(", ", args.Select(a => $"{a.Name}={Log.Format(a.Value)}"))})";
        var sw = Stopwatch.StartNew();
        try
        {
            var result = Session.InvokeMethod(Ns, target, method, Params(args));
            Log.Line($"CALL {label} -> ReturnValue={Log.Format(result.ReturnValue?.Value)} ({sw.ElapsedMilliseconds} ms)");
            foreach (var p in result.OutParameters)
            {
                Log.Line($"    out {p.Name} = {Log.Format(p.Value)}");
            }

            return result;
        }
        catch (CimException ex)
        {
            LogCimException($"CALL {label} ({sw.ElapsedMilliseconds} ms)", ex);
            return null;
        }
    }

    public static uint? ReturnValue(CimMethodResult? r) => r?.ReturnValue?.Value is object v ? Convert.ToUInt32(v) : null;

    public static void LogCimException(string label, CimException ex)
    {
        Log.Line($"{label} -> CimException NativeErrorCode={ex.NativeErrorCode} StatusCode={ex.StatusCode} MessageId={ex.MessageId} Message={ex.Message}");
        if (ex.ErrorData is { } data)
        {
            Log.Instance(data, "ErrorData");
        }
    }

    /// <summary>
    /// Calls a method asynchronously with result streaming and progress callbacks enabled, logging
    /// every callback with a timestamp. Returns the final method result, if any.
    /// </summary>
    public CimMethodResult? CallWithProgress(CimInstance target, string method, params (string Name, object Value, CimType Type)[] args)
    {
        var label = $"{target.CimSystemProperties.ClassName}.{method}({string.Join(", ", args.Select(a => $"{a.Name}={Log.Format(a.Value)}"))})";
        var options = new CimOperationOptions
        {
            EnableMethodResultStreaming = true,
            WriteProgress = (activity, currentOperation, statusDescription, percentageCompleted, secondsRemaining) =>
                Log.Line($"    PROGRESS activity=\"{activity}\" op=\"{currentOperation}\" status=\"{statusDescription}\" percent={percentageCompleted} secondsRemaining={secondsRemaining}"),
            WriteMessage = (channel, message) => Log.Line($"    MESSAGE channel={channel} \"{message}\""),
        };

        Log.Line($"ASYNC {label} (streaming + progress callbacks)");
        var sw = Stopwatch.StartNew();
        var observer = new MethodObserver();
        CimAsyncMultipleResults<CimMethodResultBase> results = Session.InvokeMethodAsync(Ns, target, method, Params(args), options);
        using (results.Subscribe(observer))
        {
            observer.Done.Wait();
        }

        Log.Line($"ASYNC {label} finished after {sw.ElapsedMilliseconds} ms; streamed items={observer.StreamedCount}");
        if (observer.Error is CimException cimEx)
        {
            LogCimException("    ERROR", cimEx);
        }
        else if (observer.Error is { } other)
        {
            Log.Line($"    ERROR {other.GetType().Name}: {other.Message}");
        }

        if (observer.Final is { } final)
        {
            Log.Line($"    ReturnValue={Log.Format(final.ReturnValue?.Value)}");
            foreach (var p in final.OutParameters)
            {
                Log.Line($"    out {p.Name} = {Log.Format(p.Value)}");
            }
        }

        return observer.Final;
    }

    public void Dispose() => Session.Dispose();

    private sealed class MethodObserver : IObserver<CimMethodResultBase>
    {
        public ManualResetEventSlim Done { get; } = new();

        public CimMethodResult? Final { get; private set; }

        public Exception? Error { get; private set; }

        public int StreamedCount { get; private set; }

        public void OnNext(CimMethodResultBase value)
        {
            switch (value)
            {
                case CimMethodStreamedResult s:
                    StreamedCount++;
                    Log.Line($"    STREAMED {s.ParameterName} ({s.ItemType}) = {Log.Format(s.ItemValue)}");
                    break;
                case CimMethodResult r:
                    Final = r;
                    break;
            }
        }

        public void OnError(Exception error)
        {
            Error = error;
            Done.Set();
        }

        public void OnCompleted() => Done.Set();
    }
}

/// <summary>
/// A throwaway VHDX created and attached with diskpart in %TEMP%\WinPartedSpike. All destructive
/// spike operations go through <see cref="Disk"/>, which re-finds the disk every time and refuses
/// anything that isn't this exact virtual disk.
/// </summary>
internal sealed class SpikeVhd : IDisposable
{
    private const ushort BusTypeFileBackedVirtual = 15;
    private readonly Smp _smp;
    private string? _objectId;

    private SpikeVhd(Smp smp, string path, int sizeMb)
    {
        _smp = smp;
        FilePath = path;
        SizeMb = sizeMb;
    }

    public string FilePath { get; }

    public int SizeMb { get; }

    public static string Folder => Path.Combine(Path.GetTempPath(), "WinPartedSpike");

    public static SpikeVhd Create(Smp smp, string name, int sizeMb, Action? beforeAttach = null, bool fixedSize = false)
    {
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, $"{name}.vhdx");
        if (File.Exists(path))
        {
            Log.Line($"Leftover {path} found; detaching and deleting it first.");
            Detach(path);
            File.Delete(path);
        }

        var vhd = new SpikeVhd(smp, path, sizeMb);
        var type = fixedSize ? "fixed" : "expandable";
        Log.Line($"Creating {sizeMb} MB {type} VHDX at {path}");
        RunDiskpart($"create vdisk file=\"{path}\" maximum={sizeMb} type={type}\n");
        beforeAttach?.Invoke();
        vhd.AttachedAt = DateTime.Now;
        RunDiskpart($"select vdisk file=\"{path}\"\nattach vdisk\n");
        Log.Line($"Attach finished {(DateTime.Now - vhd.AttachedAt).TotalMilliseconds:F0} ms after it started.");
        vhd.Locate(TimeSpan.FromSeconds(30));
        return vhd;
    }

    public DateTime AttachedAt { get; private set; }

    /// <summary>The disk instance, re-read and re-validated on every call.</summary>
    public CimInstance Disk()
    {
        var disk = _smp.Query("SELECT * FROM MSFT_Disk").FirstOrDefault(d => (string?)Smp.Prop(d, "ObjectId") == _objectId)
            ?? throw new InvalidOperationException("Spike disk disappeared.");
        Validate(disk);
        return disk;
    }

    public List<CimInstance> Partitions()
    {
        // MSFT_Partition.DiskId holds the disk's Path (not its ObjectId).
        var diskPath = (string?)Smp.Prop(Disk(), "Path");
        return [.. _smp.Query("SELECT * FROM MSFT_Partition")
            .Where(p => string.Equals((string?)Smp.Prop(p, "DiskId"), diskPath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => Convert.ToUInt64(Smp.Prop(p, "Offset")))];
    }

    public CimInstance? VolumeOf(CimInstance partition)
    {
        try
        {
            return _smp.Session.EnumerateAssociatedInstances(Smp.Ns, partition, "MSFT_PartitionToVolume", "MSFT_Volume", null, null).FirstOrDefault();
        }
        catch (CimException ex)
        {
            Smp.LogCimException("ASSOC MSFT_PartitionToVolume", ex);
            return null;
        }
    }

    public void LogLayout(string title)
    {
        Log.Line($"--- Layout: {title}");
        var disk = Disk();
        Log.Line($"    Disk PartitionStyle={Smp.Prop(disk, "PartitionStyle")} Size={Smp.Prop(disk, "Size")} LargestFreeExtent={Smp.Prop(disk, "LargestFreeExtent")} NumberOfPartitions={Smp.Prop(disk, "NumberOfPartitions")} IsOffline={Smp.Prop(disk, "IsOffline")} IsReadOnly={Smp.Prop(disk, "IsReadOnly")}");
        foreach (var p in Partitions())
        {
            var v = VolumeOf(p);
            Log.Line($"    #{Smp.Prop(p, "PartitionNumber")} Offset={Smp.Prop(p, "Offset")} Size={Smp.Prop(p, "Size")} MbrType={Smp.Prop(p, "MbrType")} GptType={Smp.Prop(p, "GptType")} Type={Smp.Prop(p, "Type")} IsActive={Smp.Prop(p, "IsActive")} IsHidden={Smp.Prop(p, "IsHidden")} AccessPaths={Log.Format(Smp.Prop(p, "AccessPaths"))} Volume.FileSystem={(v is null ? "<none>" : Log.Format(Smp.Prop(v, "FileSystem")))}");
        }
    }

    public void Dispose()
    {
        Log.Line($"Detaching and deleting {FilePath}");
        try
        {
            Detach(FilePath);
            File.Delete(FilePath);
        }
        catch (Exception ex)
        {
            Log.Line($"Cleanup failed: {ex.Message}. Run the 'cleanup' command.");
        }
    }

    public static void CleanupAll()
    {
        if (!Directory.Exists(Folder))
        {
            Log.Line("Nothing to clean up.");
            return;
        }

        foreach (var file in Directory.GetFiles(Folder, "*.vhdx"))
        {
            Log.Line($"Detaching and deleting {file}");
            Detach(file);
            try
            {
                File.Delete(file);
            }
            catch (Exception ex)
            {
                Log.Line($"Could not delete {file}: {ex.Message}");
            }
        }
    }

    public static void Detach(string path)
    {
        var t0 = DateTime.Now;
        RunDiskpart($"select vdisk file=\"{path}\"\ndetach vdisk\n");
        Log.Line($"Detach finished {(DateTime.Now - t0).TotalMilliseconds:F0} ms after it started.");
    }

    /// <summary>Runs one spike section; a failure is logged and the next section still runs.</summary>
    public static void Section(string title, Action body)
    {
        try
        {
            body();
        }
        catch (Exception ex)
        {
            Log.Line($"SECTION FAILED ({title}): {ex}");
        }
    }

    private void Locate(TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            var imageNumber = _smp.Query("SELECT * FROM MSFT_DiskImage")
                .FirstOrDefault(i => string.Equals((string?)Smp.Prop(i, "ImagePath"), FilePath, StringComparison.OrdinalIgnoreCase)) is { } image
                ? Smp.Prop(image, "Number")
                : null;

            foreach (var disk in _smp.Query("SELECT * FROM MSFT_Disk"))
            {
                var byLocation = string.Equals((string?)Smp.Prop(disk, "Location"), FilePath, StringComparison.OrdinalIgnoreCase);
                var byImage = imageNumber is not null && Equals(Convert.ToUInt32(imageNumber), Convert.ToUInt32(Smp.Prop(disk, "Number")));
                if ((byLocation || byImage) && Convert.ToUInt16(Smp.Prop(disk, "BusType")) == BusTypeFileBackedVirtual)
                {
                    _objectId = (string?)Smp.Prop(disk, "ObjectId");
                    Log.Line($"Spike disk found after {sw.ElapsedMilliseconds} ms (matched by {(byLocation ? "Location" : "MSFT_DiskImage")}).");
                    Log.Instance(disk, "Spike disk right after attach");
                    Validate(disk);
                    return;
                }
            }

            Thread.Sleep(250);
        }

        throw new InvalidOperationException("The attached VHDX did not appear as a disk. Nothing was modified.");
    }

    private void Validate(CimInstance disk)
    {
        var busType = Convert.ToUInt16(Smp.Prop(disk, "BusType"));
        var size = Convert.ToUInt64(Smp.Prop(disk, "Size"));
        var location = (string?)Smp.Prop(disk, "Location");
        var isBoot = Smp.Prop(disk, "IsBoot") as bool? == true;
        var isSystem = Smp.Prop(disk, "IsSystem") as bool? == true;
        var maxSize = (ulong)SizeMb * 1024 * 1024;

        if (busType != BusTypeFileBackedVirtual || size > maxSize || isBoot || isSystem)
        {
            throw new InvalidOperationException(
                $"SAFETY GUARD: refusing disk (BusType={busType}, Size={size}, Location={location}, IsBoot={isBoot}, IsSystem={isSystem}). Nothing was modified.");
        }
    }

    private static void RunDiskpart(string script)
    {
        var scriptPath = Path.Combine(Folder, $"diskpart-{Guid.NewGuid():N}.txt");
        Directory.CreateDirectory(Folder);
        File.WriteAllText(scriptPath, script);
        try
        {
            using var process = Process.Start(new ProcessStartInfo("diskpart.exe", $"/s \"{scriptPath}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            })!;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            Log.Line($"diskpart exit={process.ExitCode}: {string.Join(" | ", output.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).TakeLast(4))}");
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }
}

internal static class SmpConst
{
    public const ushort StyleMbr = 1;
    public const ushort StyleGpt = 2;
    public const string GptBasicData = "{ebd0a0a2-b9e5-4433-87c0-68b6b72699c7}";
}
