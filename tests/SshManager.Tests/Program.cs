using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using SshManager.Core;

var passed = 0;
var root = Path.Combine(Environment.CurrentDirectory, "runtime", "test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var server = new Server { Name = "fixture", Host = "127.0.0.1", Username = "fixture" };
var profile = new Profile { Name = "测试" };
ForwardRule Rule(int port = 14001) => new() { Name = "规则", ProfileId = profile.Id, ServerId = server.Id, ListenPort = port };
Configuration Config() => new() { Servers = [server with { }], Profiles = [profile with { }], Rules = [Rule()] };

void Assert(bool value, string message = "assertion failed") { if (!value) throw new Exception(message); }
void Throws(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new Exception("expected InvalidDataException"); }
async Task Test(string name, Func<Task> test)
{
    await test(); passed++; Console.WriteLine($"PASS {name}");
}
Task Sync(Action action) { action(); return Task.CompletedTask; }
async Task Wait(Func<bool> predicate, int milliseconds = 12000)
{
    var clock = Stopwatch.StartNew();
    while (!predicate())
    {
        if (clock.ElapsedMilliseconds > milliseconds) throw new TimeoutException("test condition not reached");
        await Task.Delay(30);
    }
}

await Test("configuration copy creates independent IDs and values", () => Sync(() =>
{
    var c = Config(); var copy = c.CopyProfile(profile.Id, "副本");
    var cloned = c.Rules.Single(r => r.ProfileId == copy.Id);
    Assert(cloned.Id != c.Rules[0].Id && cloned.ServerId == server.Id);
    cloned.TargetPort = 42; Assert(c.Rules[0].TargetPort == 3306); c.Validate();
}));
await Test("invalid ports, references, direction and duplicate IDs are rejected", () => Sync(() =>
{
    var c = Config(); c.Rules[0].TargetPort = 0; Throws(c.Validate);
    c = Config(); c.Rules[0].ServerId = "missing"; Throws(c.Validate);
    c = Config(); c.Rules.Add(c.Rules[0] with { }); Throws(c.Validate);
    c = Config(); c.Rules[0].Direction = (ForwardDirection)8; Throws(c.Validate);
}));
await Test("automatic key lookup is optional, ordered, and excludes public and unrelated files", () => Sync(() =>
{
    Configuration.ValidateServer(server with { Auth = AuthKind.PrivateKey, PrivateKeyPath = "" });
    var home = Path.Combine(root, "discovery"); var ssh = Path.Combine(home, ".ssh"); Directory.CreateDirectory(ssh);
    foreach (var name in new[] { "id_rsa", "id_ed25519", "id_rsa.pub", "known_hosts", "config", "custom.ppk", "custom.pem" })
        File.WriteAllText(Path.Combine(ssh, name), "fixture filename only");
    var paths = PrivateKeyDiscovery.Find(home);
    Assert(paths.Count == 4 && Path.GetFileName(paths[0]) == "id_ed25519");
    Assert(paths.All(p => !p.EndsWith(".pub") && Path.GetFileName(p) != "config" && Path.GetFileName(p) != "known_hosts"));
    Assert(PrivateKeyDiscovery.Find(Path.Combine(root, "no-home")).Count == 0);
}));
await Test("atomic persistence, backup and sanitized export/import", () => Sync(() =>
{
    var c = Config(); var store = new ConfigStore(Path.Combine(root, "storage"));
    store.Save(c); c.Profiles[0].Name = "第二次"; store.Save(c);
    Assert(store.Load().Profiles[0].Name == "第二次"); Assert(File.Exists(store.FilePath + ".bak"));
    c.Servers[0].Auth = AuthKind.PrivateKey; c.Servers[0].PrivateKeyPath = "sensitive-key-path";
    var path = Path.Combine(root, "export.json"); ConfigStore.Export(c, path);
    Assert(!File.ReadAllText(path).Contains("sensitive-key-path"));
    var imported = ConfigStore.Read(path); var destination = Config(); ConfigStore.ImportInto(destination, imported);
    destination.Validate(); Assert(destination.Servers[0].Id != destination.Servers[1].Id);
    Assert(destination.Rules[1].ServerId == destination.Servers[1].Id);
}));
await Test("unknown sensitive import fields and malformed config are rejected", () => Sync(() =>
{
    var path = Path.Combine(root, "invalid.json"); File.WriteAllText(path, "{\"password\":\"do-not-import\"}");
    try { ConfigStore.Read(path); throw new Exception("accepted unknown field"); } catch (JsonException) { }
    File.WriteAllText(path, "{\"Servers\":null}"); Throws(() => ConfigStore.Read(path));
}));
await Test("Windows credential manager round trip and deletion", () => Sync(() =>
{
    var credentials = new WindowsCredentialStore(); var id = "test-" + Guid.NewGuid().ToString("N");
    try { credentials.Write(id, "测试-secret"); Assert(credentials.Read(id) == "测试-secret"); }
    finally { credentials.Delete(id); }
    Assert(credentials.Read(id) == null);
}));
await Test("host trust persists, rejects changed keys and respects refusal", () => Sync(() =>
{
    var directory = Path.Combine(root, "trust"); var trust = new HostTrustStore(directory); var prompts = 0;
    Assert(trust.Verify(server, "ssh-rsa", "one", _ => { prompts++; return true; }));
    Assert(new HostTrustStore(directory).Verify(server, "ssh-rsa", "one", _ => throw new Exception("unexpected prompt")));
    Throws(() => trust.Verify(server, "ssh-rsa", "changed", _ => true));
    Assert(!trust.Verify(server, "ssh-ed25519", "new", _ => false)); Assert(prompts == 1);
}));
await Test("conflicts include wildcard and remote server scope", () => Sync(() =>
{
    var a = Rule(); var b = Rule(); b.ListenHost = "0.0.0.0";
    Assert(TunnelManager.Conflicts(server, a, server, b));
    a.Direction = b.Direction = ForwardDirection.Remote;
    Assert(!TunnelManager.Conflicts(server, a, server with { Host = "other.example" }, b));
}));
await Test("duplicate start is idempotent and stop is isolated", async () =>
{
    var factory = new FakeFactory(); var manager = new TunnelManager(factory); var a = Rule(); var b = Rule(14002);
    try
    {
        manager.Start(server, a); manager.Start(server, a); manager.Start(server, b);
        await Wait(() => manager.Get(a.Id).State == TunnelState.Running && manager.Get(b.Id).State == TunnelState.Running);
        Assert(factory.Created == 2); await manager.StopAsync(a.Id);
        Assert(manager.Get(a.Id).State == TunnelState.Stopped && manager.Get(b.Id).State == TunnelState.Running);
    }
    finally { await manager.StopAllAsync(true); }
    Assert(factory.Disposed == 2);
});
await Test("cancel during connection cleans up and never returns to running", async () =>
{
    var factory = new FakeFactory { Slow = true }; var manager = new TunnelManager(factory); var a = Rule();
    manager.Start(server, a); await Wait(() => factory.Created > 0); await manager.StopAsync(a.Id);
    Assert(manager.Get(a.Id).State == TunnelState.Stopped && factory.Disposed == 1);
    await manager.StopAllAsync(true);
});
await Test("reconnect after drop, stop during retry cancels future attempts", async () =>
{
    var factory = new FakeFactory(); var manager = new TunnelManager(factory); var a = Rule() with { AutoReconnect = true };
    try
    {
        manager.Start(server, a); await Wait(() => manager.Get(a.Id).State == TunnelState.Running);
        factory.Last!.Alive = false;
        await Wait(() => factory.Created >= 2 && manager.Get(a.Id).State == TunnelState.Running);
        factory.Last!.Alive = false; await Wait(() => manager.Get(a.Id).State == TunnelState.Retrying);
        await manager.StopAsync(a.Id); var count = factory.Created; await Task.Delay(2200);
        Assert(factory.Created == count && manager.Get(a.Id).State == TunnelState.Stopped);
    }
    finally { await manager.StopAllAsync(true); }
});
await Test("one failed connection does not stop a healthy rule", async () =>
{
    var factory = new FakeFactory(); var manager = new TunnelManager(factory); var a = Rule(); var b = Rule(14002);
    try
    {
        manager.Start(server, a); await Wait(() => manager.Get(a.Id).State == TunnelState.Running);
        factory.Fail = true; manager.Start(server, b); await Wait(() => manager.Get(b.Id).State == TunnelState.Error);
        Assert(manager.Get(a.Id).State == TunnelState.Running);
    }
    finally { await manager.StopAllAsync(true); }
});

if (args.Length >= 3 && args[0] == "--integration")
{
    var start = new ProcessStartInfo(args[1]) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
    start.ArgumentList.Add(args[2]); start.ArgumentList.Add(Path.Combine(root, "fixture-key.pem"));
    using var process = Process.Start(start) ?? throw new Exception("fixture start failed");
    var stderr = process.StandardError.ReadToEndAsync();
    var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
    using var metadata = JsonDocument.Parse(line ?? throw new Exception("fixture did not report ports"));
    var sshPort = metadata.RootElement.GetProperty("sshPort").GetInt32();
    var echoPort = metadata.RootElement.GetProperty("echoPort").GetInt32();
    var keyPath = metadata.RootElement.GetProperty("keyPath").GetString()!;
    var credentials = new MemoryCredentials();
    var actualServer = server with { Port = sshPort };
    credentials.Write(actualServer.Id, "fixture-password");
    var trust = new HostTrustStore(Path.Combine(root, "integration-trust"));
    var factory = new SshTunnelFactory(credentials, trust, (_, _) => true);
    var manager = new TunnelManager(factory);
    var capturedLogs = new List<string>(); manager.Logged += e => { lock (capturedLogs) capturedLogs.Add(e.Message); };
    int FreePort() { using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp); socket.Bind(new IPEndPoint(IPAddress.Loopback, 0)); return ((IPEndPoint)socket.LocalEndPoint!).Port; }
    async Task Echo(int port)
    {
        using var client = new TcpClient(); await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(TimeSpan.FromSeconds(5));
        var bytes = Encoding.UTF8.GetBytes("ssh-manager-真实通信"); await client.GetStream().WriteAsync(bytes);
        var received = new byte[bytes.Length]; await client.GetStream().ReadExactlyAsync(received).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert(received.SequenceEqual(bytes), "echo mismatch");
    }
    async Task Running(ForwardRule rule)
    {
        await Wait(() => manager.Get(rule.Id).State is TunnelState.Running or TunnelState.Error, 22000);
        var status = manager.Get(rule.Id); Assert(status.State == TunnelState.Running, status.Detail);
    }
    try
    {
        await Test("real SSH password auth, local and remote forwarding with independent stop", async () =>
        {
            var local = Rule(FreePort()) with { TargetPort = echoPort };
            var remote = Rule(FreePort()) with { Direction = ForwardDirection.Remote, TargetPort = echoPort };
            manager.Start(actualServer, local); manager.Start(actualServer, remote);
            await Running(local); await Running(remote); await Echo(local.ListenPort); await Echo(remote.ListenPort);
            await manager.StopAsync(local.Id); await Echo(remote.ListenPort);
            // A fresh bind proves that stop released the local listening socket.
            using var released = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            released.Bind(new IPEndPoint(IPAddress.Loopback, local.ListenPort));
            await manager.StopAsync(remote.Id);
        });
        await Test("real SSH encrypted private key authentication", async () =>
        {
            var keyed = actualServer with { Id = Guid.NewGuid().ToString("N"), Auth = AuthKind.PrivateKey, PrivateKeyPath = keyPath };
            credentials.Write(keyed.Id, "fixture-key-passphrase");
            var local = Rule(FreePort()) with { ServerId = keyed.Id, TargetPort = echoPort };
            manager.Start(keyed, local); await Running(local); await Echo(local.ListenPort); await manager.StopAsync(local.Id);
        });
        await Test("real SSH multiple server endpoints run independently in one profile", async () =>
        {
            var secondServer = actualServer with { Id = Guid.NewGuid().ToString("N"), Port = metadata.RootElement.GetProperty("sshPort2").GetInt32() };
            credentials.Write(secondServer.Id, "fixture-password");
            var a = Rule(FreePort()) with { TargetPort = echoPort };
            var b = Rule(FreePort()) with { ServerId = secondServer.Id, TargetPort = echoPort };
            manager.Start(actualServer, a); manager.Start(secondServer, b);
            await Running(a); await Running(b); await Echo(a.ListenPort); await Echo(b.ListenPort);
            await manager.StopAsync(a.Id); await Echo(b.ListenPort); await manager.StopAsync(b.Id);
        });
        await Test("real SSH automatically discovered encrypted key skips invalid candidates", async () =>
        {
            var home = Path.Combine(root, "automatic-encrypted"); Directory.CreateDirectory(Path.Combine(home, ".ssh"));
            File.WriteAllText(Path.Combine(home, ".ssh", "id_ed25519"), "not a private key");
            File.Copy(keyPath, Path.Combine(home, ".ssh", "id_rsa"));
            var automatic = actualServer with { Id = Guid.NewGuid().ToString("N"), Auth = AuthKind.PrivateKey, PrivateKeyPath = "" };
            credentials.Write(automatic.Id, "fixture-key-passphrase");
            var autoManager = new TunnelManager(new SshTunnelFactory(credentials, trust, (_, _) => true, () => PrivateKeyDiscovery.Find(home)));
            var rule = Rule(FreePort()) with { ServerId = automatic.Id, TargetPort = echoPort };
            try
            {
                autoManager.Start(automatic, rule); await Wait(() => autoManager.Get(rule.Id).State is TunnelState.Running or TunnelState.Error);
                Assert(autoManager.Get(rule.Id).State == TunnelState.Running, autoManager.Get(rule.Id).Detail); await Echo(rule.ListenPort);
            }
            finally { await autoManager.StopAllAsync(true); }
        });
        await Test("real SSH automatically discovered key connects without specifying a file or passphrase", async () =>
        {
            var home = Path.Combine(root, "automatic-plain"); Directory.CreateDirectory(Path.Combine(home, ".ssh"));
            File.Copy(metadata.RootElement.GetProperty("autoKeyPath").GetString()!, Path.Combine(home, ".ssh", "id_ed25519"));
            var automatic = actualServer with { Id = Guid.NewGuid().ToString("N"), Auth = AuthKind.PrivateKey, PrivateKeyPath = "" };
            var autoManager = new TunnelManager(new SshTunnelFactory(credentials, trust, (_, _) => true, () => PrivateKeyDiscovery.Find(home)));
            var rule = Rule(FreePort()) with { ServerId = automatic.Id, TargetPort = echoPort };
            try
            {
                autoManager.Start(automatic, rule); await Wait(() => autoManager.Get(rule.Id).State is TunnelState.Running or TunnelState.Error);
                Assert(autoManager.Get(rule.Id).State == TunnelState.Running, autoManager.Get(rule.Id).Detail); await Echo(rule.ListenPort);
            }
            finally { await autoManager.StopAllAsync(true); }
        });
        await Test("real SSH occupied local port and denied remote listener report errors", async () =>
        {
            using var blocker = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            blocker.Bind(new IPEndPoint(IPAddress.Loopback, 0)); blocker.Listen();
            var occupied = ((IPEndPoint)blocker.LocalEndPoint!).Port;
            var a = Rule(occupied) with { TargetPort = echoPort };
            var b = Rule(occupied) with { Direction = ForwardDirection.Remote, TargetPort = echoPort };
            manager.Start(actualServer, a); manager.Start(actualServer, b);
            await Wait(() => manager.Get(a.Id).State == TunnelState.Error && manager.Get(b.Id).State == TunnelState.Error, 22000);
        });
        await Test("real SSH wrong password fails without credential leakage", async () =>
        {
            var bad = actualServer with { Id = Guid.NewGuid().ToString("N") }; credentials.Write(bad.Id, "wrong-secret-fixture");
            var rule = Rule(FreePort()) with { ServerId = bad.Id };
            manager.Start(bad, rule); await Wait(() => manager.Get(rule.Id).State == TunnelState.Error, 22000);
            lock (capturedLogs) Assert(!string.Join("\n", capturedLogs).Contains("wrong-secret-fixture"));
        });
        await Test("real SSH refused first-use key cannot connect", async () =>
        {
            var rejecting = new TunnelManager(new SshTunnelFactory(credentials, new HostTrustStore(Path.Combine(root, "reject-trust")), (_, _) => false));
            var rule = Rule(FreePort());
            try { rejecting.Start(actualServer, rule); await Wait(() => rejecting.Get(rule.Id).State == TunnelState.Error, 22000); }
            finally { await rejecting.StopAllAsync(true); }
        });
        await Test("real SSH cancellation while waiting for host confirmation releases connection", async () =>
        {
            var prompted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var waiting = new TunnelManager(new SshTunnelFactory(credentials, new HostTrustStore(Path.Combine(root, "cancel-trust")), (_, token) =>
            {
                prompted.TrySetResult();
                token.WaitHandle.WaitOne(); return false;
            }));
            var rule = Rule(FreePort());
            try
            {
                waiting.Start(actualServer, rule); await prompted.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await waiting.StopAsync(rule.Id).WaitAsync(TimeSpan.FromSeconds(5));
                Assert(waiting.Get(rule.Id).State == TunnelState.Stopped);
            }
            finally { await waiting.StopAllAsync(true); }
        });
        await Test("real SSH changed host fingerprint is rejected with a specific error", async () =>
        {
            var known = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(root, "integration-trust", "known-hosts.json")))!;
            var directory = Path.Combine(root, "changed-trust");
            ConfigStore.AtomicWrite(Path.Combine(directory, "known-hosts.json"), JsonSerializer.Serialize(known.ToDictionary(pair => pair.Key, _ => "wrong-fingerprint")));
            var changed = new TunnelManager(new SshTunnelFactory(credentials, new HostTrustStore(directory), (_, _) => throw new Exception("changed fingerprint must not prompt")));
            var rule = Rule(FreePort());
            try
            {
                changed.Start(actualServer, rule); await Wait(() => changed.Get(rule.Id).State == TunnelState.Error, 22000);
                Assert(changed.Get(rule.Id).Detail.Contains("指纹已变化"));
            }
            finally { await changed.StopAllAsync(true); }
        });
        await Test("real SSH server disconnect updates running rule to error", async () =>
        {
            var rule = Rule(FreePort()) with { TargetPort = echoPort };
            manager.Start(actualServer, rule); await Running(rule); await Echo(rule.ListenPort);
            await process.StandardInput.WriteLineAsync("stop"); await process.StandardInput.FlushAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await Wait(() => manager.Get(rule.Id).State == TunnelState.Error);
        });
    }
    finally
    {
        await manager.StopAllAsync(true);
        if (!process.HasExited) { await process.StandardInput.WriteLineAsync("stop"); process.StandardInput.Close(); }
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (TimeoutException) { process.Kill(entireProcessTree: true); }
        await stderr;
    }
}
Console.WriteLine($"All {passed} tests passed.");

sealed class FakeFactory : ITunnelFactory
{
    public int Created, Disposed;
    public bool Slow, Fail;
    public FakeSession? Last;
    public ITunnelSession Create(Server server, ForwardRule rule, Action<string> warning, CancellationToken token)
    {
        Interlocked.Increment(ref Created);
        Last = new FakeSession(this, Slow, Fail); return Last;
    }
}
sealed class FakeSession(FakeFactory owner, bool slow, bool fail) : ITunnelSession
{
    public volatile bool Alive = true;
    public bool IsAlive => Alive;
    public async Task OpenAsync(CancellationToken token)
    {
        if (slow) await Task.Delay(Timeout.Infinite, token);
        if (fail) throw new IOException("fixture failure");
    }
    public void Dispose() { Alive = false; Interlocked.Increment(ref owner.Disposed); }
}
sealed class MemoryCredentials : ICredentialStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> values = new();
    public string? Read(string serverId) => values.GetValueOrDefault(serverId);
    public void Write(string serverId, string secret) => values[serverId] = secret;
    public void Delete(string serverId) => values.TryRemove(serverId, out _);
}
