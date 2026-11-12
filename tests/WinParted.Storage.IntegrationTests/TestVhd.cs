using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Management.Infrastructure;
using WinParted.Core.Actions;
using WinParted.Core.Model;
using WinParted.Core.Operations;
using WinParted.Storage;

namespace WinParted.Storage.IntegrationTests;

/// <summary>A test that changes disks: runs only elevated, and only on <see cref="TestVhd"/> disks.</summary>
public sealed class AdminFactAttribute : FactAttribute
{
    public AdminFactAttribute()
    {
        if (!TestVhd.IsElevated)
        {
            Skip = "Needs an elevated terminal (Terminal (Admin)).";
        }
    }
}

/// <summary>
/// A throwaway VHDX in %TEMP%\WinPartedTests, created and attached with diskpart (the approach the
/// spikes proved), detached and deleted on dispose (task 7.3). <see cref="Execute"/> refuses any
/// request whose target is not this exact virtual disk.
/// </summary>
public sealed class TestVhd : IDisposable
{
    private const ushort BusTypeFileBackedVirtual = 15;

    private TestVhd(string path, int sizeMb, SmpStorageProvider provider)
    {
        FilePath = path;
        SizeMb = sizeMb;
        Provider = provider;
    }

    public static bool IsElevated => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    public static string Folder => Path.Combine(Path.GetTempPath(), "WinPartedTests");

    public string FilePath { get; }

    public int SizeMb { get; }

    public SmpStorageProvider Provider { get; }

    public DiskId Id { get; private set; }

    /// <summary>When diskpart finished attaching (for event latency checks).</summary>
    public DateTime AttachedAt { get; private set; }

    public static TestVhd Create(string name, int sizeMb, SmpStorageProvider provider)
    {
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, $"{name}-{Guid.NewGuid():N}.vhdx");
        RunDiskpart($"create vdisk file=\"{path}\" maximum={sizeMb} type=expandable\nattach vdisk\n");
        var vhd = new TestVhd(path, sizeMb, provider) { AttachedAt = DateTime.UtcNow };
        vhd.Id = vhd.Locate();
        return vhd;
    }

    /// <summary>The disk as it is now, after checking it is still this VHDX.</summary>
    public async Task<Disk> DiskAsync()
    {
        var snapshot = await Provider.ReadSnapshotAsync();
        return Validate(snapshot.FindDisk(Id) ?? throw new InvalidOperationException("Test VHDX disappeared."));
    }

    public async Task<StorageSnapshot> SnapshotAsync() => await Provider.ReadSnapshotAsync();

    /// <summary>Builds a request against the current snapshot and executes it, after the guard.</summary>
    public async Task Execute(Func<StorageSnapshot, OperationRequest> build, IProgress<OperationProgress>? progress = null)
    {
        var snapshot = await Provider.ReadSnapshotAsync();
        var request = build(snapshot);
        var targetDisk = request.Target switch
        {
            DiskTarget t => t.Disk,
            PartitionTarget t => t.Partition.Disk,
            UnallocatedTarget t => t.Disk,
            _ => throw new InvalidOperationException("Unknown target."),
        };

        if (targetDisk != Id)
        {
            throw new InvalidOperationException("SAFETY GUARD: request targets a disk other than the test VHDX.");
        }

        Validate(snapshot.FindDisk(Id)!);
        await Provider.ExecuteAsync(request, progress);
    }

    public static TargetFingerprint Fingerprint(StorageSnapshot s, ActionTarget t) =>
        TargetFingerprint.Of(s, t) ?? throw new InvalidOperationException("Target not in snapshot.");

    public void Dispose()
    {
        RunDiskpart($"select vdisk file=\"{FilePath}\"\ndetach vdisk\n");
        try
        {
            File.Delete(FilePath);
        }
        catch (IOException)
        {
        }
    }

    private Disk Validate(Disk disk)
    {
        if (disk.Bus != BusType.Virtual
            || !string.Equals(disk.Location, FilePath, StringComparison.OrdinalIgnoreCase)
            || disk.IsBoot || disk.IsSystem
            || disk.Size > (ulong)SizeMb * 1024 * 1024)
        {
            throw new InvalidOperationException($"SAFETY GUARD: disk {disk.Number} is not the test VHDX {FilePath}.");
        }

        return disk;
    }

    private DiskId Locate()
    {
        using var session = CimSession.Create(null);
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(30))
        {
            foreach (var d in session.QueryInstances(@"root\Microsoft\Windows\Storage", "WQL", "SELECT * FROM MSFT_Disk"))
            {
                if (string.Equals(d.CimInstanceProperties["Location"]?.Value as string, FilePath, StringComparison.OrdinalIgnoreCase)
                    && Convert.ToUInt16(d.CimInstanceProperties["BusType"]?.Value) == BusTypeFileBackedVirtual)
                {
                    return new DiskId((string)d.CimInstanceProperties["ObjectId"].Value);
                }
            }

            Thread.Sleep(250);
        }

        throw new InvalidOperationException($"Attached VHDX {FilePath} did not appear.");
    }

    private static void RunDiskpart(string script)
    {
        Directory.CreateDirectory(Folder);
        var scriptPath = Path.Combine(Folder, $"diskpart-{Guid.NewGuid():N}.txt");
        File.WriteAllText(scriptPath, script);
        try
        {
            using var p = Process.Start(new ProcessStartInfo("diskpart.exe", $"/s \"{scriptPath}\"")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            })!;
            p.StandardOutput.ReadToEnd();
            p.WaitForExit();
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }
}
