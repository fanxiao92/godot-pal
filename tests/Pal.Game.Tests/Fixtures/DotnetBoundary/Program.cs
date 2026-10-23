using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

// Windows-only synthetic process topology. No enumeration or lookup of arbitrary processes.
internal static class Program
{
    private static string directory = "";
    private static string role = "";
    private static long Now => Stopwatch.GetTimestamp();
    private static double Elapsed(long start) => (Now - start) * 1000.0 / Stopwatch.Frequency;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static void Save(string name, object value)
    {
        string path = Path.Combine(directory, name);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value, Json));
        File.Move(path + ".tmp", path);
    }
    private static void Event(string name, object? data = null) => File.AppendAllText(
        Path.Combine(directory, role + ".events.jsonl"),
        JsonSerializer.Serialize(new { name, ticks = Now, utc = DateTime.UtcNow, data }) + "\n");

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DuplicateHandle(IntPtr source, IntPtr handle, IntPtr target, out IntPtr duplicate, uint access, bool inherit, uint options);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(IntPtr handle, out uint code);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GetStdHandle(uint kind);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetHandleInformation(IntPtr handle, out uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);

    private static async Task Register(int supervisor)
    {
        // The PID is supplied by this case's creator; duplicate only our own process handle.
        IntPtr target = OpenProcess(0x0040, false, supervisor);
        if (target == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        try
        {
            if (!DuplicateHandle(GetCurrentProcess(), GetCurrentProcess(), target, out var handle,
                    0x00100000 | 0x1000, false, 0)) throw new System.ComponentModel.Win32Exception();
            Save(role + ".handle.json", new { handle = handle.ToInt64(), pid = Environment.ProcessId });
        }
        finally { CloseHandle(target); }
        long start = Now;
        while (!File.Exists(Path.Combine(directory, role + ".ack")))
        {
            if (Elapsed(start) > 750) throw new TimeoutException("owned-handle registration");
            await Task.Delay(5);
        }
    }

    private static async Task<int> Main(string[] args)
    {
        try
        {
            directory = Path.GetFullPath(args[1]); role = args[0];
            return role switch
            {
                "case" => await Supervise(args),
                "root" => await Root(args),
                "writer" => await Writer(args),
                _ => throw new ArgumentException("unknown synthetic role")
            };
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 90;
        }
    }

    private static async Task<int> Root(string[] args)
    {
        string mode = args[2]; int supervisor = int.Parse(args[3]);
        await Register(supervisor);
        Console.WriteLine("ROOT_STDOUT_中文"); Console.Error.WriteLine("ROOT_STDERR_中文");
        if (mode == "timeout") { await Task.Delay(6000); return 0; }
        if (mode == "expected") return 1;
        if (mode == "unexpected") return 3;
        if (mode == "error") { Console.WriteLine("ERROR: BOUNDARY_EXPECTED_ERROR"); return 0; }
        if (mode == "normal") return 0;
        if (mode is not ("stdout-held" or "stderr-held" or "isolated")) throw new ArgumentException("unknown mode");

        // Both child streams are redirected. Isolate the root's OWN extra inheritable handles,
        // leaving exactly the selected root writer inherited for each counterexample.
        var changed = new List<(IntPtr Handle, uint Flags)>();
        Process writer;
        try
        {
            foreach (var standard in new[] { (Name: "stdout", Kind: unchecked((uint)-11)), (Name: "stderr", Kind: unchecked((uint)-12)) })
            {
                IntPtr handle = GetStdHandle(standard.Kind);
                if (handle == IntPtr.Zero || handle == new IntPtr(-1) || !GetHandleInformation(handle, out uint flags))
                    throw new System.ComponentModel.Win32Exception();
                bool inherit = mode == standard.Name + "-held";
                if ((flags & 1) == 0) throw new InvalidOperationException("fixture requires an inheritable root writer");
                if (inherit) continue;
                changed.Add((handle, flags));
                if (!SetHandleInformation(handle, 1, 0)) throw new System.ComponentModel.Win32Exception();
            }
            var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string a in new[] { "exec", typeof(Program).Assembly.Location, "writer", directory, supervisor.ToString() }) info.ArgumentList.Add(a);
            writer = Process.Start(info)!;
        }
        finally
        {
            foreach (var h in changed)
                if (!SetHandleInformation(h.Handle, 1, h.Flags & 1)) throw new System.ComponentModel.Win32Exception();
        }
        using (writer)
        {
            _ = writer.StandardOutput.ReadToEndAsync(); _ = writer.StandardError.ReadToEndAsync();
            long start = Now;
            while (!File.Exists(Path.Combine(directory, "writer.ready")))
            {
                if (Elapsed(start) > 750) throw new TimeoutException("writer ready");
                await Task.Delay(5);
            }
            Event("root-exiting-writer-active", new { writer.HasExited });
        }
        return 0;
    }
    private static async Task<int> Writer(string[] args)
    {
        await Register(int.Parse(args[2]));
        File.WriteAllText(Path.Combine(directory, "writer.ready"), "ready");
        await Task.Delay(4000);
        return 0;
    }

    private sealed class Owned(IntPtr handle)
    {
        public IntPtr Handle { get; } = handle;
        public int? Exit { get; private set; }
        public void Observe()
        {
            uint state = WaitForSingleObject(Handle, 0);
            if (state == 258) return;
            if (state != 0 || !GetExitCodeProcess(Handle, out uint code)) throw new System.ComponentModel.Win32Exception();
            Exit = unchecked((int)code);
        }
    }
    private static void Receive(Dictionary<string, Owned> owned)
    {
        foreach (string name in new[] { "root", "writer" })
        {
            string path = Path.Combine(directory, name + ".handle.json");
            if (owned.ContainsKey(name) || !File.Exists(path)) continue;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            owned[name] = new Owned(new IntPtr(document.RootElement.GetProperty("handle").GetInt64()));
            File.WriteAllText(Path.Combine(directory, name + ".ack"), "ack");
        }
        foreach (var process in owned.Values) process.Observe();
    }
    private static JsonElement? Journal()
    {
        string path = Path.Combine(directory, "commands.json");
        if (!File.Exists(path)) return null;
        try
        {
            using var d = JsonDocument.Parse(File.ReadAllText(path));
            return (d.RootElement.ValueKind == JsonValueKind.Array ? d.RootElement[0] : d.RootElement).Clone();
        }
        catch (JsonException) { return null; } // Concurrent update may be in flight; not a success signal.
    }
    private static bool Flag(JsonElement? record, string name) => record is JsonElement r && r.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

    private static async Task<int> Supervise(string[] args)
    {
        // case <new dir> <mode> <pwsh> <test script>
        if (Directory.Exists(directory)) throw new IOException("refuse to reuse evidence");
        Directory.CreateDirectory(directory);
        string mode = args[2]; var owned = new Dictionary<string, Owned>();
        var info = new ProcessStartInfo(args[3]) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string a in new[] { "-NoLogo", "-NoProfile", "-File", args[4], "-CaseDirectory", directory,
                     "-Mode", mode, "-SupervisorPid", Environment.ProcessId.ToString(), "-DriverDll", typeof(Program).Assembly.Location }) info.ArgumentList.Add(a);
        using var wrapper = Process.Start(info)!;
        var stdout = wrapper.StandardOutput.ReadToEndAsync(); var stderr = wrapper.StandardError.ReadToEndAsync();
        long start = Now; bool earlyExitRecord = false, outerGuard = false, returnedBeforeWriter = false;
        try
        {
            while (!wrapper.HasExited)
            {
                Receive(owned);
                if (owned.TryGetValue("root", out var root) && root.Exit == 0 &&
                    owned.TryGetValue("writer", out var writer) && writer.Exit is null && Flag(Journal(), "exitKnown")) earlyExitRecord = true;
                if (Elapsed(start) > 8000)
                {
                    outerGuard = true; wrapper.Kill();
                    if (!wrapper.WaitForExit(1000)) throw new TimeoutException("own wrapper cleanup incomplete");
                    break;
                }
                await Task.Delay(5);
            }
            Receive(owned);
            returnedBeforeWriter = owned.TryGetValue("writer", out var remaining) && remaining.Exit is null;
            int wrapperExit = wrapper.ExitCode;
            while (owned.Values.Any(p => p.Exit is null) && Elapsed(start) < 8000) { Receive(owned); await Task.Delay(5); }
            double remainingMs = Math.Max(1, 8000 - Elapsed(start));
            await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromMilliseconds(remainingMs));
            File.WriteAllText(Path.Combine(directory, "wrapper.stdout.log"), await stdout);
            File.WriteAllText(Path.Combine(directory, "wrapper.stderr.log"), await stderr);
            bool hasWriter = mode is "stdout-held" or "stderr-held" or "isolated";
            bool complete = !outerGuard && owned.Count == (hasWriter ? 2 : 1) && owned.Values.All(p => p.Exit is not null);
            using var result = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "invocation.json")));
            var invocation = result.RootElement;
            var journal = Journal();
            bool threw = invocation.GetProperty("threw").GetBoolean();
            string text = invocation.GetProperty("text").GetString()!;
            double elapsed = invocation.GetProperty("elapsedMs").GetDouble();
            string failure = journal is JsonElement j && j.TryGetProperty("failureKind", out var kind) ? kind.GetString() ?? "" : "";
            int? recordedExit = journal is JsonElement c && c.TryGetProperty("exitCode", out var exit) && exit.ValueKind == JsonValueKind.Number ? exit.GetInt32() : null;
            bool expected = complete && wrapperExit == 0 && recordedExit == owned["root"].Exit && (mode switch
            {
                "normal" => !threw && recordedExit == 0 && text.Contains("ROOT_STDOUT_中文") && text.Contains("ROOT_STDERR_中文"),
                "expected" => !threw && recordedExit == 1,
                "unexpected" => threw && recordedExit == 3 && failure == "UnexpectedExit",
                "error" => threw && recordedExit == 0 && failure == "LoggedError",
                "timeout" => threw && failure == "ProcessTimeout" && Flag(journal, "timedOut") && Flag(journal, "cleanupComplete"),
                "stdout-held" or "stderr-held" => threw && recordedExit == 0 && failure == "OutputDrainTimeout" &&
                    !Flag(journal, "outputComplete") && earlyExitRecord && returnedBeforeWriter,
                "isolated" => !threw && recordedExit == 0 && returnedBeforeWriter,
                _ => false
            });
            Save("result.json", new { mode, expected, outerGuard, complete, earlyExitRecord, returnedBeforeWriter, elapsed,
                wrapperExit, recordedExit, failure, owned = owned.ToDictionary(p => p.Key, p => p.Value.Exit), journal });
            Console.WriteLine($"{mode}: {(expected ? "PASS" : "FAIL")}; root={recordedExit}; failure={failure}; invocation={elapsed:F1}ms");
            // Elapsed is diagnostic, not an OS/I/O hard-real-time contract. The live owned
            // writer and outer guard distinguish bounded cancellation from waiting for EOF.
            return expected ? 0 : mode == "stdout-held" && complete && !threw && recordedExit == 0 ? 1 : 2;
        }
        finally { foreach (var p in owned.Values) CloseHandle(p.Handle); }
    }
}
