using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using ProxyGAyer.Core;

var directory = Path.GetFullPath(args.Single());
Directory.CreateDirectory(directory);
var tests = new List<(string Name, Func<Task> Run)>();
void Test(string name, Action run) => tests.Add((name, () => { run(); return Task.CompletedTask; }));
void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Expected {expected}, got {actual}");
}
void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}
StateStore Store(string name) => new(Path.Combine(directory, name));
var profile = ProxyProfile.Default;
var changed = profile with { Http = new("localhost", 8123), Https = new("localhost", 8123) };
var git = ServiceCatalog.All.Single(s => s.Id == "git");

Test("Endpoint validation and IPv6", () =>
{
    Equal("http://[::1]:8080", ProxyEndpoint.Parse("[::1]", "8080").Url);
    Equal("http://localhost:7890", ProxyEndpoint.Parse("localhost", "7890").Url);
    Equal(7890, ProxyEndpoint.Parse("localhost", "۷۸۹۰").Port);
    Equal(8080, ProxyEndpoint.Parse("localhost", "٨٠٨٠").Port);
    Equal("xn--mgbh0fb.xn--mgba3a4f16a", ProxyEndpoint.Parse("مثال.ایران", "8080").Host);
    foreach (var host in new[] { "", "http://host", "host/path", "a; whoami", "host\nSet-Content x", "x@host", "a%20b" })
        Throws<ArgumentException>(() => ProxyEndpoint.Parse(host, "8080"));
    foreach (var port in new[] { "0", "65536", "-1", "8;9", "", "+80" })
        Throws<ArgumentException>(() => ProxyEndpoint.Parse("localhost", port));
});

Test("Reset restores original across repeated Set and restart", () =>
{
    var resources = new MemoryResources();
    resources.Values["a"] = "original";
    var store = Store("repeat");
    var manager = new ChangeManager(store, resources, new());
    manager.Apply("git", profile, new Dictionary<string, string?> { ["a"] = "first", ["b"] = "new" });
    manager.Apply("git", changed, new Dictionary<string, string?> { ["a"] = "second", ["b"] = "newer" });
    Equal("original", manager.State.Changes["git"].Resources[0].Before);
    var reopened = new ChangeManager(store, resources, store.Load());
    reopened.Reset("git");
    Equal("original", resources.Read("a"));
    Equal<string?>(null, resources.Read("b"));
    Equal(0, reopened.State.Changes.Count);
});

Test("External edits block reset before any resource is restored", () =>
{
    var resources = new MemoryResources();
    var manager = new ChangeManager(Store("external"), resources, new());
    manager.Apply("git", profile, new Dictionary<string, string?> { ["a"] = "applied", ["b"] = "applied" });
    resources.Values["b"] = "external edit";
    Throws<InvalidOperationException>(() => manager.Reset("git"));
    Equal("applied", resources.Read("a"));
    Equal("external edit", resources.Read("b"));
    Throws<InvalidOperationException>(() => manager.Apply("git", profile, new Dictionary<string, string?> { ["a"] = "new" }));
});

Test("Partial write failure rolls back completed writes", () =>
{
    var resources = new MemoryResources { FailOnceAt = "b" };
    resources.Values["a"] = "original";
    var manager = new ChangeManager(Store("rollback"), resources, new());
    Throws<IOException>(() => manager.Apply("npm", profile, new Dictionary<string, string?> { ["a"] = "new", ["b"] = "new" }));
    Equal("original", resources.Read("a"));
    Equal<string?>(null, resources.Read("b"));
    Equal(0, manager.State.Changes.Count);
});

Test("Pending journal recovers a crash during a second Set", () =>
{
    var resources = new MemoryResources();
    resources.Values["a"] = "first";
    resources.Values["b"] = "second";
    var state = new AppState();
    state.Changes["git"] = new ChangeSet { Pending = true, Resources =
        [new("a", "original", "second", "first"), new("b", null, "second", "first")] };
    var store = Store("crash");
    store.Save(state);
    var manager = new ChangeManager(store, resources, store.Load());
    manager.Reset("git");
    Equal("original", resources.Read("a"));
    Equal<string?>(null, resources.Read("b"));
});

Test("Overlapping services cannot overwrite another backup", () =>
{
    var resources = new MemoryResources();
    var manager = new ChangeManager(Store("overlap"), resources, new());
    manager.Apply("first", profile, new Dictionary<string, string?> { ["shared"] = "first" });
    Throws<InvalidOperationException>(() => manager.Apply("second", profile, new Dictionary<string, string?> { ["shared"] = "second" }));
    Equal("first", resources.Read("shared"));
});

Test("Changing target paths requires Reset first", () =>
{
    var resources = new MemoryResources();
    var manager = new ChangeManager(Store("paths"), resources, new());
    manager.Apply("git", profile, new Dictionary<string, string?> { ["old"] = "value" });
    Throws<InvalidOperationException>(() => manager.Apply("git", profile, new Dictionary<string, string?> { ["new"] = "value" }));
    Equal("value", resources.Read("old"));
    Equal<string?>(null, resources.Read("new"));
});

Test("INI proxy updates preserve other sections and settings", () =>
{
    const string input = "[global]\nindex-url = https://packages.invalid\nproxy = old\n[install]\nproxy = install-override\n";
    var output = ConfigPlanner.SetIni(input, "global", "proxy", profile.Http.Url);
    Equal(true, output.Contains("index-url = https://packages.invalid"));
    Equal(true, output.Contains("[install]\nproxy = install-override"));
    Equal(1, output.Split("proxy = " + profile.Http.Url).Length - 1);
});

Test("npm and Yarn writers preserve unrelated values", () =>
{
    var npm = ConfigPlanner.SetProperties("//registry.invalid/:_authToken=secret\nproxy=old\n", new() { ["proxy"] = profile.Http.Url }, '=');
    Equal(true, npm.Contains("_authToken=secret"));
    Equal(false, npm.Contains("proxy=old"));
    var yarn = ConfigPlanner.SetProperties("nested:\n  httpProxy: nested-value\nhttpProxy: old\n", new() { ["httpProxy"] = "\"new\"" }, ':');
    Equal(true, yarn.Contains("  httpProxy: nested-value"));
    Equal(false, yarn.Contains("httpProxy: old"));
});

Test("Yarn handles quoted keys and rejects unsafe YAML updates", () =>
{
    var output = ConfigPlanner.SetYamlProperties("\"httpProxy\": old\nnodeLinker: node-modules\n",
        new() { ["httpProxy"] = "\"new\"" });
    Equal(false, output.Contains("old"));
    Equal(true, output.Contains("nodeLinker: node-modules"));
    foreach (var input in new[] { "httpProxy: |\n  old\n", "{httpProxy: old}", "---\nhttpProxy: old\n", "httpProxy: &name old\n", "httpProxy: \"multiple\n  lines\"\n", "httpProxy: old\n  continuation\n" })
        Throws<InvalidOperationException>(() => ConfigPlanner.SetYamlProperties(input, new() { ["httpProxy"] = "\"new\"" }));
});

Test("Every advertised service has a valid plan", () =>
{
    var resources = new MemoryResources();
    var planner = new ConfigPlanner(resources, directory, directory, _ => null);
    Equal(ServiceCatalog.All.Count, ServiceCatalog.All.Select(s => s.Id).Distinct().Count());
    foreach (var service in ServiceCatalog.All)
    {
        var plan = planner.Plan(service, profile);
        Equal(!service.IsSession, plan.Count > 0);
        if (service.SingleProxy)
            Throws<ArgumentException>(() => planner.Plan(service, profile with { Https = new("localhost", 9999) }));
    }
});

Test("Session environment is isolated and uses HTTP transport for HTTPS", () =>
{
    var before = Environment.GetEnvironmentVariable("http_proxy");
    var cargo = ServiceCatalog.All.Single(s => s.Id == "cargo");
    var start = SessionLauncher.CreateStartInfo(cargo, profile);
    Equal(profile.Http.Url, start.Environment["CARGO_HTTP_PROXY"]);
    Equal(profile.Https.Url, start.Environment["https_proxy"]);
    Equal(false, start.UseShellExecute);
    Equal(before, Environment.GetEnvironmentVariable("http_proxy"));
});

Test("File adapter restores the exact original bytes", () =>
{
    var path = Path.Combine(directory, "file-test", "config");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    byte[] original = [0xef, 0xbb, 0xbf, 0x61, 0x0d, 0x0a];
    File.WriteAllBytes(path, original);
    var resources = new WindowsResources();
    var manager = new ChangeManager(Store("files"), resources, new());
    manager.Apply("file", profile, new Dictionary<string, string?> { ["file|" + path] = Convert.ToBase64String(Encoding.UTF8.GetBytes("new")) });
    manager.Reset("file");
    Equal(Convert.ToBase64String(original), Convert.ToBase64String(File.ReadAllBytes(path)));
});

Test("Git reads the generated proxy without changing user configuration", () =>
{
    var fixture = Path.Combine(directory, "git-home");
    Directory.CreateDirectory(fixture);
    var file = Path.Combine(fixture, ".gitconfig");
    const string original = "[user]\n\tname = Fixture\n[http]\n\tproxy = http://old.invalid:80\n";
    File.WriteAllText(file, original);
    var resources = new WindowsResources();
    var planner = new ConfigPlanner(resources, fixture, fixture, _ => null);
    var manager = new ChangeManager(Store("git-backup"), resources, new());
    manager.Apply(git.Id, profile, planner.Plan(git, profile));
    var info = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var arg in new[] { "config", "--file", file, "--get", "http.proxy" }) info.ArgumentList.Add(arg);
    using var process = Process.Start(info)!;
    var output = process.StandardOutput.ReadToEnd().Trim();
    process.WaitForExit();
    Equal(0, process.ExitCode);
    Equal(profile.Http.Url, output);
    manager.Reset(git.Id);
    Equal(original, File.ReadAllText(file));
});

Test("Batch applies only checked services and Reset remains independent", () =>
{
    var resources = new MemoryResources();
    var store = Store("batch-selected");
    var manager = new ChangeManager(store, resources, new());
    var planner = new ConfigPlanner(resources, directory, directory, _ => null);
    manager.Apply(git.Id, changed, planner.Plan(git, changed));
    var gitKey = manager.State.Changes[git.Id].Resources.Single().Key;
    var originalGit = resources.Read(gitKey);
    var selected = ServiceCatalog.All.Where(s => s.Id is "cmd" or "npm").ToArray();
    var results = new BatchProxyApplier(manager, planner).Apply(selected, profile);
    Equal(2, results.Count);
    Equal(true, results.All(r => r.Success));
    Equal(changed, manager.State.Profiles[git.Id]);
    Equal(originalGit, resources.Read(gitKey));
    Equal(false, resources.Values.Keys.Any(k => k.StartsWith("reg|")));
    Equal(3, store.Load().Changes.Count);
    manager.Reset("npm");
    Equal(true, manager.State.Changes.ContainsKey("cmd"));
    Equal(true, manager.State.Changes.ContainsKey("git"));
    Equal(originalGit, resources.Read(gitKey));
});

Test("Incompatible batch endpoints fail before any service is modified", () =>
{
    var resources = new MemoryResources();
    var manager = new ChangeManager(Store("batch-incompatible"), resources, new());
    var planner = new ConfigPlanner(resources, directory, directory, _ => null);
    var selected = new[] { ServiceCatalog.All.Single(s => s.Id == "cmd"), git };
    Throws<ArgumentException>(() => new BatchProxyApplier(manager, planner).Apply(selected,
        profile with { Https = new("localhost", 9999), SameForHttps = false }));
    Equal(0, manager.State.Changes.Count);
    Equal(0, manager.State.Profiles.Count);
    Equal(0, resources.Values.Count);
});

Test("A failed batch service rolls back while other services continue", () =>
{
    var resources = new MemoryResources();
    var manager = new ChangeManager(Store("batch-partial"), resources, new());
    var planner = new ConfigPlanner(resources, directory, directory, _ => null);
    var selected = new[] { ServiceCatalog.All.Single(s => s.Id == "cmd"),
        ServiceCatalog.All.Single(s => s.Id == "npm"), ServiceCatalog.All.Single(s => s.Id == "gradle") };
    var npmKey = planner.Plan(selected[1], profile).Keys.Single();
    resources.FailOnceAt = npmKey;
    var results = new BatchProxyApplier(manager, planner).Apply(selected, profile);
    Equal(2, results.Count(r => r.Success));
    Equal("npm", results.Single(r => !r.Success).Service.Id);
    Equal<string?>(null, resources.Read(npmKey));
    Equal(false, manager.State.Changes.ContainsKey("npm"));
    Equal(true, manager.State.Changes.ContainsKey("gradle"));
    Equal(true, manager.State.Changes.ContainsKey("cmd"));
});

Test("Repeated batch Set keeps the original pre-Set backup", () =>
{
    var resources = new MemoryResources();
    var store = Store("batch-backup");
    var manager = new ChangeManager(store, resources, new());
    var planner = new ConfigPlanner(resources, directory, directory, _ => null);
    var npm = ServiceCatalog.All.Single(s => s.Id == "npm");
    var key = planner.Plan(npm, profile).Keys.Single();
    var original = Convert.ToBase64String(Encoding.UTF8.GetBytes("proxy=http://original.invalid:8000\r\nregistry=https://packages.invalid\r\n"));
    resources.Values[key] = original;
    manager.Apply(npm.Id, changed, planner.Plan(npm, changed));
    var batch = new BatchProxyApplier(manager, planner);
    batch.Apply([npm], profile);
    batch.Apply([npm], changed);
    var reopened = new ChangeManager(store, resources, store.Load());
    reopened.Reset(npm.Id);
    Equal(original, resources.Read(key));
});

Test("Batch supports custom apps and deduplicates selected ids", () =>
{
    var resources = new MemoryResources();
    var manager = new ChangeManager(Store("batch-custom"), resources, new());
    var planner = new ConfigPlanner(resources, directory, directory, _ => null);
    var custom = new ServiceDefinition("custom-fixture", "Fixture", "CUSTOM APPS", AdapterKind.Session,
        "Fixture", Path.Combine(directory, "does-not-exist.exe"));
    var results = new BatchProxyApplier(manager, planner).Apply([custom, custom], profile);
    Equal(1, results.Count);
    Equal(true, results.Single().Success);
    Equal(profile, manager.State.Profiles[custom.Id]);
    Equal(0, resources.Values.Count);
});

Test("Empty batch is rejected without saved changes", () =>
{
    var resources = new MemoryResources();
    var manager = new ChangeManager(Store("batch-empty"), resources, new());
    var planner = new ConfigPlanner(resources, directory, directory, _ => null);
    Throws<ArgumentException>(() => new BatchProxyApplier(manager, planner).Apply([], profile));
    Equal(0, manager.State.Changes.Count);
});

Test("Batch retains different HTTP and HTTPS endpoints for compatible services", () =>
{
    var resources = new MemoryResources();
    var manager = new ChangeManager(Store("batch-split"), resources, new());
    var planner = new ConfigPlanner(resources, directory, directory, _ => null);
    var selected = ServiceCatalog.All.Where(s => s.Id is "cmd" or "npm").ToArray();
    var split = profile with { Https = new("localhost", 9000), SameForHttps = false };
    var results = new BatchProxyApplier(manager, planner).Apply(selected, split);
    Equal(true, results.All(r => r.Success));
    Equal(split, manager.State.Profiles["cmd"]);
    var npmText = Encoding.UTF8.GetString(Convert.FromBase64String(resources.Read(manager.State.Changes["npm"].Resources.Single().Key)!));
    Equal(true, npmText.Contains("proxy=http://127.0.0.1:7890"));
    Equal(true, npmText.Contains("https-proxy=http://localhost:9000"));
});

tests.Add(("HTTP test actually uses the supplied proxy", async () =>
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    var local = new ProxyEndpoint("127.0.0.1", port);
    var received = ReplyOnce(listener, "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nOK");
    await ProxyTester.TestAsync(new(local, local), "http://proxygayer-test.invalid/");
    var line = await received;
    Equal(true, line.StartsWith("GET http://proxygayer-test.invalid/ HTTP/1.1"));
}));

tests.Add(("HTTPS uses its own proxy and a CONNECT tunnel", async () =>
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    var received = ReplyOnce(listener, "HTTP/1.1 407 Proxy Authentication Required\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
    var failed = false;
    try { await ProxyTester.TestAsync(new(new("127.0.0.1", 1), new("127.0.0.1", port)), "https://proxygayer-test.invalid/"); }
    catch (HttpRequestException) { failed = true; }
    Equal(true, failed);
    Equal(true, (await received).StartsWith("CONNECT proxygayer-test.invalid:443 HTTP/1.1"));
}));

var failures = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception exception) { failures++; Console.WriteLine("FAIL " + test.Name + "\n" + exception); }
}
Console.WriteLine($"{tests.Count - failures}/{tests.Count} passed");
return failures == 0 ? 0 : 1;

static async Task<string> ReplyOnce(TcpListener listener, string reply)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    using var client = await listener.AcceptTcpClientAsync(timeout.Token);
    await using var stream = client.GetStream();
    var bytes = new List<byte>();
    var buffer = new byte[1];
    while (bytes.Count < 16384)
    {
        if (await stream.ReadAsync(buffer, timeout.Token) == 0) break;
        bytes.Add(buffer[0]);
        if (bytes.Count >= 4 && Encoding.ASCII.GetString(bytes.TakeLast(4).ToArray()) == "\r\n\r\n") break;
    }
    await stream.WriteAsync(Encoding.ASCII.GetBytes(reply), timeout.Token);
    return Encoding.ASCII.GetString(bytes.ToArray()).Split("\r\n")[0];
}

sealed class MemoryResources : IResourceStore
{
    public Dictionary<string, string?> Values { get; } = [];
    public string? FailOnceAt { get; set; }
    public string? Read(string key) => Values.GetValueOrDefault(key);
    public void Write(string key, string? value)
    {
        if (key == FailOnceAt) { FailOnceAt = null; throw new IOException("Injected write failure"); }
        if (value is null) Values.Remove(key);
        else Values[key] = value;
    }
}
