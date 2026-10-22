using System.IO.MemoryMappedFiles;
using System.IO.Pipes;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using Erp.Testing;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// G1, compiled code: state kept outside the process's memory. The process-state gate
/// (<see cref="G1ProcessStateTests"/>) walks fields, singletons and framework stores, and the
/// tenant-source scanner flags process-wide stores by name; both look inside the process. A file
/// (a temporary file of list totals keyed without the tenant: critic p05 round 12, plant L9; the
/// digits last chosen by anyone, in a file: critic p04 round 11, plant SL1), a memory-mapped file,
/// a named mutex, semaphore or event, a pipe, a socket, a child process, unmanaged memory or a
/// native call holds state that neither sees, and that a second process shares too, so the
/// non-interference check's fresh process is no clean baseline for it. Every method of the
/// product's compiled code that calls such an API is reviewed one by one in
/// tests/Gates/outside-process-methods.txt; any other fails this gate, and so does a reviewed
/// entry that no longer calls one. Async state machines, lambdas and local functions count for
/// the method they are written in. The source gate (<see cref="TenantBypassScanner"/>, rule
/// outside-process, which since p05 round 13 also matches what p05's external-state rule did)
/// flags the same uses by file; this scan covers the APIs that rule names (special folders, the
/// working directory, keys persisted to the file system, file handles) as well.
/// </summary>
public sealed class G1OutsideProcessStateTests
{
    public const string ReviewedFile = "tests/Gates/outside-process-methods.txt";

    [Fact]
    public void Only_reviewed_methods_keep_or_read_state_outside_the_process()
    {
        var found = OutsideProcessCode.Scan(TenantSettingCode.ProductAssemblies());
        var problems = OutsideProcessCode.Check(found, Repo.ReadReviewedList(ReviewedFile));
        TestContext.Current.TestOutputHelper?.WriteLine($"{found.MethodsScanned} methods scanned, {found.CallsResolved} calls resolved; {found.Methods.Count} reach outside the process:");
        foreach (var (method, uses) in found.Methods.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"  {method}: {string.Join(", ", uses)}");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(found.MethodsScanned >= Ratchet.Min("g1.outsideProcessMethodsScanned"),
            $"g1.outsideProcessMethodsScanned: {found.MethodsScanned}; ratchet minimum {Ratchet.Min("g1.outsideProcessMethodsScanned")}");
        // A scan that resolved no call would pass anything.
        Assert.True(found.CallsResolved >= found.MethodsScanned, $"only {found.CallsResolved} calls resolved in {found.MethodsScanned} methods");
    }

    [Fact]
    public void The_scan_finds_files_memory_maps_named_objects_pipes_sockets_processes_unmanaged_memory_and_native_calls()
    {
        var found = OutsideProcessCode.Scan([typeof(PlantedOutsideProcessCode).Assembly], t => t == typeof(PlantedOutsideProcessCode) || t.DeclaringType == typeof(PlantedOutsideProcessCode));
        var owner = typeof(PlantedOutsideProcessCode).FullName!.Replace('+', '.');
        var expected = new (string Method, string Use)[]
        {
            ("TemporaryTotalsAsync", "Path.GetTempPath"),
            ("TemporaryTotalsAsync", "File.WriteAllTextAsync"),
            ("TemporaryTotalsAsync", "File.ReadAllTextAsync"),
            ("RememberedDigits", "File.Exists"),
            ("RememberedDigits", "File.ReadAllText"),
            ("TotalsFile", "FileInfo..ctor"),
            ("LastCaller", "FileStream..ctor"),
            ("Scratch", "Directory.CreateDirectory"),
            ("Scratch", "Path.GetTempFileName"),
            ("WriterOnPath", "StreamWriter..ctor"),
            ("ReaderOnPath", "StreamReader..ctor"),
            ("Mapped", "MemoryMappedFile.CreateOrOpen"),
            ("NamedMutex", "Mutex..ctor"),
            ("NamedSemaphore", "Semaphore.OpenExisting"),
            ("NamedEvent", "EventWaitHandle..ctor"),
            ("Pipe", "NamedPipeClientStream..ctor"),
            ("Socket", "TcpClient..ctor"),
            ("Child", "Process.Start"),
            ("Unmanaged", "Marshal.AllocHGlobal"),
            ("Native", "PlantedOutsideProcessCode.getpid (native)"),
            ("SpecialFolder", "Environment.GetFolderPath"),
            ("WorkingDirectory", "Environment.get_CurrentDirectory"),
            ("PersistedKeys", "DataProtectionBuilderExtensions.PersistKeysToFileSystem"),
            ("Handle", "SafeFileHandle.get_IsAsync"),
        };
        foreach (var (method, use) in expected)
        {
            Assert.True(found.Methods.TryGetValue($"{owner}.{method}", out var uses) && uses.Contains(use),
                $"{method}: {use} not found; found {string.Join("; ", found.Methods.Select(m => $"{m.Key}: {string.Join(", ", m.Value)}"))}");
        }
        // Streams over memory or a response, unnamed synchronisation objects and path arithmetic
        // stay inside the process.
        Assert.False(found.Methods.ContainsKey($"{owner}.InsideTheProcess"), "flagged: " + string.Join(", ", found.Methods.GetValueOrDefault($"{owner}.InsideTheProcess") ?? []));
        var problems = OutsideProcessCode.Check(found, []);
        Assert.Equal(expected.Select(e => e.Method).Distinct().Count(), problems.Count);
        // A reviewed method passes; a reviewed method that no longer reaches outside is stale.
        var reviewed = found.Methods.Keys.Select(k => (k, "planted")).Append(("Erp.Nowhere.Gone", "stale")).ToList();
        Assert.Equal([$"{ReviewedFile}: 'Erp.Nowhere.Gone' no longer keeps or reads state outside the process; remove the entry"], OutsideProcessCode.Check(found, reviewed));
        Assert.True(expected.Length >= Ratchet.Min("g1.outsideProcessPlantsCaught"),
            $"g1.outsideProcessPlantsCaught: {expected.Length}; ratchet minimum {Ratchet.Min("g1.outsideProcessPlantsCaught")}");
    }
}

/// <summary>Planted shapes for the scan's self-test (never called).</summary>
internal static class PlantedOutsideProcessCode
{
    public static async Task<string> TemporaryTotalsAsync(string key, int total)
    {
        var path = Path.Combine(Path.GetTempPath(), "erp-list-totals-" + key);
        await File.WriteAllTextAsync(path, total.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return await File.ReadAllTextAsync(path);
    }

    public static Func<string> RememberedDigits() => () => File.Exists("/tmp/erp-digits") ? File.ReadAllText("/tmp/erp-digits") : "latn";

    public static long TotalsFile() => new FileInfo("/tmp/erp-totals").Length;

    public static void LastCaller(byte[] who)
    {
        using var stream = new FileStream("/tmp/erp-last-caller", FileMode.Create);
        stream.Write(who);
    }

    public static string Scratch()
    {
        Directory.CreateDirectory("/tmp/erp-scratch");
        return Path.GetTempFileName();
    }

    public static void WriterOnPath(string text)
    {
        using var writer = new StreamWriter("/tmp/erp-last");
        writer.Write(text);
    }

    public static string ReaderOnPath()
    {
        using var reader = new StreamReader("/tmp/erp-last");
        return reader.ReadToEnd();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")] // named kernel objects; the scan reads the call whatever the platform
    public static MemoryMappedFile Mapped() => MemoryMappedFile.CreateOrOpen("erp-totals", 4096);

    public static Mutex NamedMutex() => new(false, "erp-last-tenant");

    [System.Runtime.Versioning.SupportedOSPlatform("windows")] // named kernel objects; the scan reads the call whatever the platform
    public static Semaphore NamedSemaphore() => Semaphore.OpenExisting("erp-count");

    [System.Runtime.Versioning.SupportedOSPlatform("windows")] // named kernel objects; the scan reads the call whatever the platform
    public static EventWaitHandle NamedEvent() => new(false, EventResetMode.AutoReset, "erp-switch");

    public static NamedPipeClientStream Pipe() => new("erp-totals");

    public static System.Net.Sockets.TcpClient Socket() => new();

    public static void Child() => System.Diagnostics.Process.Start("sh", "-c true");

    public static void Unmanaged() => Marshal.AllocHGlobal(16);

    public static int Native() => getpid();

    [DllImport("libc")]
    private static extern int getpid();

    public static string SpecialFolder() => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string WorkingDirectory() => Environment.CurrentDirectory;

    public static void PersistedKeys(Microsoft.AspNetCore.DataProtection.IDataProtectionBuilder keys) =>
        Microsoft.AspNetCore.DataProtection.DataProtectionBuilderExtensions.PersistKeysToFileSystem(keys, new DirectoryInfo("/keys"));

    public static bool Handle(Microsoft.Win32.SafeHandles.SafeFileHandle handle) => handle.IsAsync;

    public static string InsideTheProcess(Stream response)
    {
        using var writer = new StreamWriter(response, leaveOpen: true);
        writer.Write("x");
        using var memory = new MemoryStream();
        using var reader = new StreamReader(memory);
        using var mutex = new Mutex();
        using var semaphore = new Semaphore(1, 1);
        return Path.Combine("a", "b") + reader.ReadToEnd();
    }
}

/// <summary>Finds the methods whose bodies call an API that keeps or reads state outside the
/// process's memory.</summary>
public static class OutsideProcessCode
{
    public sealed record Result(IReadOnlyDictionary<string, IReadOnlyList<string>> Methods, int MethodsScanned, int CallsResolved);

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value);

    /// <summary>Types every member of which reaches outside the process.</summary>
    private static readonly HashSet<string> OutsideTypes = new(StringComparer.Ordinal)
    {
        "System.IO.File", "System.IO.FileInfo", "System.IO.FileSystemInfo", "System.IO.Directory", "System.IO.DirectoryInfo",
        "System.IO.FileStream", "System.IO.RandomAccess", "System.IO.FileSystemWatcher", "System.IO.DriveInfo",
        "System.IO.Compression.ZipFile", "System.IO.Compression.ZipFileExtensions",
        "Microsoft.Extensions.FileProviders.PhysicalFileProvider", "System.Runtime.InteropServices.NativeMemory",
        "Microsoft.Win32.Registry", "Microsoft.Win32.RegistryKey", "Microsoft.Win32.SafeHandles.SafeFileHandle",
    };

    /// <summary>Namespaces every type of which reaches outside the process.</summary>
    private static readonly string[] OutsideNamespaces =
        ["System.IO.MemoryMappedFiles", "System.IO.Pipes", "System.IO.IsolatedStorage", "System.Net.Sockets"];

    /// <summary>What a called method is, when it reaches outside the process (null otherwise).</summary>
    public static string? Classify(MethodBase called)
    {
        var type = called.DeclaringType;
        if ((called.Attributes & MethodAttributes.PinvokeImpl) != 0)
        {
            return $"{type?.Name}.{called.Name} (native)";
        }
        if (type is null)
        {
            return null;
        }
        var definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        var full = definition.FullName ?? "";
        var ns = definition.Namespace ?? "";
        var use = $"{definition.Name}.{called.Name}";
        if (OutsideTypes.Contains(full) || OutsideNamespaces.Any(n => ns == n || ns.StartsWith(n + ".", StringComparison.Ordinal)))
        {
            return use;
        }
        var parameters = called.GetParameters();
        return full switch
        {
            "System.IO.Path" when called.Name is "GetTempPath" or "GetTempFileName" => use,
            // A reader or writer opened on a path (not on a stream the caller holds).
            "System.IO.StreamReader" or "System.IO.StreamWriter" when called is ConstructorInfo && parameters.Length > 0 && parameters[0].ParameterType == typeof(string) => use,
            // A named mutex, semaphore or event is shared by every process that opens the name.
            "System.Threading.Mutex" or "System.Threading.Semaphore" or "System.Threading.EventWaitHandle"
                when called.Name is "OpenExisting" or "TryOpenExisting" || (called is ConstructorInfo && parameters.Any(p => p.Name == "name" && p.ParameterType == typeof(string))) => use,
            "System.Diagnostics.Process" when called.Name is "Start" => use,
            // A special folder or the working directory: a place on disk that other processes share
            // (p05 round 13's source rule external-state covered these by name; one rule since).
            "System.Environment" when called.Name is "GetFolderPath" or "get_CurrentDirectory" or "set_CurrentDirectory" => use,
            // Data-protection keys written to a folder outlive the process and are read by the next.
            "Microsoft.AspNetCore.DataProtection.DataProtectionBuilderExtensions" when called.Name is "PersistKeysToFileSystem" => use,
            "System.Runtime.InteropServices.Marshal" when called.Name.StartsWith("AllocHGlobal", StringComparison.Ordinal) ||
                                                          called.Name.StartsWith("AllocCoTaskMem", StringComparison.Ordinal) ||
                                                          called.Name.StartsWith("ReAlloc", StringComparison.Ordinal) => use,
            _ => null,
        };
    }

    public static Result Scan(IEnumerable<Assembly> assemblies, Func<Type, bool>? types = null)
    {
        var methods = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var scanned = 0;
        var resolved = 0;
        foreach (var assembly in assemblies)
        {
            Type[] all;
            try
            {
                all = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                all = e.Types.OfType<Type>().ToArray();
            }
            foreach (var type in all.Where(t => types?.Invoke(t) ?? true))
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                foreach (var method in type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags)))
                {
                    byte[]? il;
                    try
                    {
                        il = method.GetMethodBody()?.GetILAsByteArray();
                    }
                    catch (Exception e) when (e is BadImageFormatException or InvalidOperationException)
                    {
                        continue;
                    }
                    if (il is null)
                    {
                        continue;
                    }
                    scanned++;
                    var typeArguments = method.DeclaringType is { IsGenericType: true } declaring ? declaring.GetGenericArguments() : null;
                    var methodArguments = method is MethodInfo { IsGenericMethod: true } generic ? generic.GetGenericArguments() : null;
                    foreach (var token in MethodTokens(il))
                    {
                        MethodBase? called;
                        try
                        {
                            called = method.Module.ResolveMethod(token, typeArguments, methodArguments);
                        }
                        catch (Exception e) when (e is ArgumentException or BadImageFormatException or TypeLoadException or FileNotFoundException or MissingMemberException)
                        {
                            continue;
                        }
                        if (called is null)
                        {
                            continue;
                        }
                        resolved++;
                        if (Classify(called) is { } use)
                        {
                            var owner = TenantSettingCode.Owner(method);
                            if (!methods.TryGetValue(owner, out var uses))
                            {
                                methods[owner] = uses = new SortedSet<string>(StringComparer.Ordinal);
                            }
                            uses.Add(use);
                        }
                    }
                }
            }
        }
        return new Result(methods.ToDictionary(m => m.Key, m => (IReadOnlyList<string>)m.Value.ToList(), StringComparer.Ordinal), scanned, resolved);
    }

    public static IReadOnlyList<string> Check(Result found, IReadOnlyList<(string Entry, string Reason)> reviewed)
    {
        var problems = new List<string>();
        var allowed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (entry, reason) in reviewed)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                problems.Add($"{G1OutsideProcessStateTests.ReviewedFile}: '{entry}' needs a reason after '#'");
            }
            allowed.Add(entry);
        }
        foreach (var (method, uses) in found.Methods.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            if (!allowed.Contains(method))
            {
                problems.Add($"{method} keeps or reads state outside the process ({string.Join(", ", uses)}): only reviewed methods may; " +
                             $"review it in {G1OutsideProcessStateTests.ReviewedFile} or keep the state in the tenant's own rows");
            }
        }
        foreach (var stale in allowed.Where(a => !found.Methods.ContainsKey(a)).Order(StringComparer.Ordinal))
        {
            problems.Add($"{G1OutsideProcessStateTests.ReviewedFile}: '{stale}' no longer keeps or reads state outside the process; remove the entry");
        }
        return problems;
    }

    /// <summary>The method tokens a body's call, callvirt, newobj, ldftn, ldvirtftn and jmp load.</summary>
    private static IEnumerable<int> MethodTokens(byte[] il) =>
        Operands(il).Where(o => o.Type == OperandType.InlineMethod).Select(o => o.Token);

    /// <summary>Every token operand of a method body (methods, fields, strings, types and the
    /// tokens ldtoken loads), with its operand type.</summary>
    public static IEnumerable<(OperandType Type, int Token)> Operands(byte[] il)
    {
        var i = 0;
        while (i < il.Length)
        {
            short value = il[i];
            if (value == 0xFE && i + 1 < il.Length)
            {
                value = unchecked((short)(0xFE00 | il[i + 1]));
                i += 2;
            }
            else
            {
                i += 1;
            }
            if (!OpCodesByValue.TryGetValue(value, out var op))
            {
                yield break; // not IL we understand: stop rather than misread operands
            }
            if (op.OperandType is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineString or OperandType.InlineTok or OperandType.InlineType
                && i + 4 <= il.Length)
            {
                yield return (op.OperandType, BitConverter.ToInt32(il, i));
            }
            i += op.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, i),
                _ => 4,
            };
        }
    }
}
