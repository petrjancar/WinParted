using Microsoft.Management.Infrastructure;
using Microsoft.Management.Infrastructure.Options;
using WinParted.Core.Operations;

namespace WinParted.Storage.Cim;

/// <summary>Small helpers over a CIM session for the Storage Management API.</summary>
internal sealed class Smp(CimSession session)
{
    public const string StorageNamespace = @"root\Microsoft\Windows\Storage";
    public const string Cimv2 = @"root\cimv2";
    public const string BitLockerNamespace = @"root\cimv2\Security\MicrosoftVolumeEncryption";

    public CimSession Session { get; } = session;

    public List<CimInstance> Query(string className, string ns = StorageNamespace) =>
        [.. Session.QueryInstances(ns, "WQL", $"SELECT * FROM {className}")];

    public static object? Prop(CimInstance instance, string name) => instance.CimInstanceProperties[name]?.Value;

    public static string? Str(CimInstance i, string name) => Prop(i, name) as string;

    public static bool Bool(CimInstance i, string name) => Prop(i, name) is bool b && b;

    public static ulong U64(CimInstance i, string name) => Prop(i, name) is { } v ? Convert.ToUInt64(v) : 0;

    public static ushort U16(CimInstance i, string name) => Prop(i, name) is { } v ? Convert.ToUInt16(v) : (ushort)0;

    public static char? Letter(CimInstance i) => Prop(i, "DriveLetter") is char c && c != '\0' ? char.ToUpperInvariant(c) : null;

    public static CimMethodParametersCollection Params(IEnumerable<(string Name, object Value, CimType Type)> args)
    {
        var p = new CimMethodParametersCollection();
        foreach (var (name, value, type) in args)
        {
            p.Add(CimMethodParameter.Create(name, value, type, CimFlags.In));
        }

        return p;
    }

    /// <summary>
    /// Invokes an SMP method and throws <see cref="StorageOperationException"/> with a plain-language
    /// message when it fails (task 8.2).
    /// </summary>
    public CimMethodResult Invoke(CimInstance target, string method, params (string Name, object Value, CimType Type)[] args)
    {
        try
        {
            var result = Session.InvokeMethod(StorageNamespace, target, method, Params(args));
            return Check(result, method, args);
        }
        catch (CimException ex)
        {
            throw Failure(method, ex);
        }
    }

    /// <summary>Like <see cref="Invoke"/>, but forwards SMP's WriteProgress callbacks as progress.</summary>
    public CimMethodResult InvokeWithProgress(
        CimInstance target,
        string method,
        IProgress<OperationProgress>? progress,
        params (string Name, object Value, CimType Type)[] args)
    {
        var options = new CimOperationOptions
        {
            EnableMethodResultStreaming = true,
            WriteProgress = (_, _, _, percent, _) => progress?.Report(new OperationProgress(Math.Min(percent, 100u))),
        };

        var observer = new MethodObserver();
        using (Session.InvokeMethodAsync(StorageNamespace, target, method, Params(args), options).Subscribe(observer))
        {
            observer.Done.Wait();
        }

        if (observer.Error is CimException cimEx)
        {
            throw Failure(method, cimEx);
        }

        if (observer.Error is { } other)
        {
            throw new StorageOperationException(
                StorageErrorMapper.Create(StorageErrorKind.Unknown, null, $"{method} failed", other.Message), other);
        }

        return observer.Final is { } final
            ? Check(final, method, args)
            : throw new StorageOperationException(StorageErrorMapper.Create(StorageErrorKind.Unknown, null, $"{method} returned no result"));
    }

    private static CimMethodResult Check(CimMethodResult result, string method, (string Name, object Value, CimType Type)[] args)
    {
        var code = result.ReturnValue?.Value is { } v ? Convert.ToUInt32(v) : 0u;
        if (code == 0)
        {
            return result;
        }

        var full = args.Any(a => a.Name == "Full" && a.Value is true);
        throw new StorageOperationException(StorageErrorMapper.Map(code, method, ExtendedMessage(result), full));
    }

    private static string? ExtendedMessage(CimMethodResult result) =>
        result.OutParameters["ExtendedStatus"]?.Value switch
        {
            CimInstance status => Prop(status, "Message") as string,
            string s => s,
            _ => null,
        };

    private static StorageOperationException Failure(string method, CimException ex) =>
        new(StorageErrorMapper.Create(
                ex.NativeErrorCode == NativeErrorCode.AccessDenied ? StorageErrorKind.AccessDenied : StorageErrorKind.Unknown,
                (uint)ex.StatusCode,
                $"{method} failed: {ex.NativeErrorCode}",
                ex.Message),
            ex);

    private sealed class MethodObserver : IObserver<CimMethodResultBase>
    {
        public ManualResetEventSlim Done { get; } = new();

        public CimMethodResult? Final { get; private set; }

        public Exception? Error { get; private set; }

        public void OnNext(CimMethodResultBase value)
        {
            if (value is CimMethodResult r)
            {
                Final = r;
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
