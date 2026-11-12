using WinParted.Spikes;

var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "help";
var needsAdmin = command is "usb" or "behavior" or "progress" or "events" or "fullformat" or "vhd" or "all" or "cleanup";

if (command is not ("usb" or "usb-check" or "facts" or "behavior" or "progress" or "events" or "fullformat" or "vhd" or "all" or "cleanup"))
{
    Console.WriteLine("""
        WinParted spikes. Run from Terminal (Admin) in the repository folder:

          dotnet run --project spikes/WinParted.Spikes -- facts      read-only: dumps disk info (safe on any PC)
          dotnet run --project spikes/WinParted.Spikes -- behavior   MBR limits and error codes (temporary VHDX only)
          dotnet run --project spikes/WinParted.Spikes -- progress   format/resize progress (temporary VHDX only)
          dotnet run --project spikes/WinParted.Spikes -- events     hot-plug events (VHDX, then optional USB stick)
          dotnet run --project spikes/WinParted.Spikes -- fullformat full format on fixed vs expandable VHDX, drive-letter cleanup
          dotnet run --project spikes/WinParted.Spikes -- usb-check  read-only: shows which USB disk the usb test would erase
          dotnet run --project spikes/WinParted.Spikes -- usb        ERASES the single connected USB stick: quick/full format tests
          dotnet run --project spikes/WinParted.Spikes -- vhd        behavior, progress and events
          dotnet run --project spikes/WinParted.Spikes -- all        facts plus the three above, in that order
          dotnet run --project spikes/WinParted.Spikes -- cleanup    detaches and deletes leftover temporary VHDX files

        Logs are written to spikes/output/.
        """);
    return 0;
}

if (needsAdmin && !Log.IsElevated())
{
    Console.WriteLine("This command needs administrator rights. Open Terminal (Admin) and run it again.");
    return 1;
}

using var smp = new Smp();
var commands = command switch
{
    "all" => ["facts", "behavior", "progress", "events"],
    "vhd" => ["behavior", "progress", "events"],
    _ => new[] { command },
};
foreach (var c in commands)
{
    Log.Start(c);
    try
    {
        switch (c)
        {
            case "facts": FactsSpike.Run(smp); break;
            case "behavior": BehaviorSpike.Run(smp); break;
            case "progress": ProgressSpike.Run(smp); break;
            case "events": EventsSpike.Run(smp); break;
            case "fullformat": FullFormatSpike.Run(smp); break;
            case "usb-check": UsbFormatSpike.Check(smp); break;
            case "usb": UsbFormatSpike.Run(smp); break;
            case "cleanup": SpikeVhd.CleanupAll(); break;
        }

        Log.Line("DONE");
    }
    catch (Exception ex)
    {
        Log.Line($"ABORTED: {ex}");
    }
    finally
    {
        Console.WriteLine($"\nLog saved: {Log.FilePath}");
        Log.Stop();
    }
}

Console.WriteLine("\nFinished. Tell Claude the spikes are done; it reads the logs from spikes/output itself.");
return 0;
