using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace SshManager.Core;

public interface ITunnelSession : IDisposable
{
    Task OpenAsync(CancellationToken token);
    bool IsAlive { get; }
}

public interface ITunnelFactory
{
    ITunnelSession Create(Server server, ForwardRule rule, Action<string> warning, CancellationToken token);
}

public sealed class SshTunnelFactory(ICredentialStore credentials, HostTrustStore trust, Func<string, CancellationToken, bool> confirm,
    Func<IReadOnlyList<string>>? discoverKeys = null) : ITunnelFactory
{
    public ITunnelSession Create(Server server, ForwardRule rule, Action<string> warning, CancellationToken token) =>
        new SshTunnelSession(server, rule, credentials, trust, confirm, warning, token, discoverKeys ?? (() => PrivateKeyDiscovery.Find()));
}

internal sealed class SshTunnelSession : ITunnelSession
{
    private readonly Server server;
    private readonly ForwardRule rule;
    private readonly ICredentialStore credentials;
    private readonly HostTrustStore trust;
    private readonly Func<string, CancellationToken, bool> confirm;
    private readonly Action<string> warning;
    private readonly CancellationToken lifetime;
    private SshClient? client;
    private ForwardedPort? port;
    private readonly List<PrivateKeyFile> keys = [];
    private readonly Func<IReadOnlyList<string>> discoverKeys;
    private volatile bool broken;

    public SshTunnelSession(Server server, ForwardRule rule, ICredentialStore credentials, HostTrustStore trust,
        Func<string, CancellationToken, bool> confirm, Action<string> warning, CancellationToken lifetime, Func<IReadOnlyList<string>> discoverKeys)
    {
        this.server = server; this.rule = rule; this.credentials = credentials; this.trust = trust;
        this.confirm = confirm; this.warning = warning; this.lifetime = lifetime;
        this.discoverKeys = discoverKeys;
    }

    public bool IsAlive => !broken && client?.IsConnected == true && port?.IsStarted == true;

    public async Task OpenAsync(CancellationToken token)
    {
        var secret = credentials.Read(server.Id);
        AuthenticationMethod auth;
        if (server.Auth == AuthKind.Password)
        {
            if (secret == null) throw new InvalidDataException("缺少密码，请编辑服务器并保存凭据。");
            auth = new PasswordAuthenticationMethod(server.Username, secret);
        }
        else
        {
            var automatic = string.IsNullOrWhiteSpace(server.PrivateKeyPath);
            var paths = automatic ? discoverKeys() : new[] { PrivateKeyDiscovery.ExpandPath(server.PrivateKeyPath) };
            if (paths.Count == 0) throw new InvalidDataException("常用路径中未找到私钥，请在服务器配置中选择文件；当前未接入 SSH Agent。");
            var skipped = false;
            foreach (var path in paths)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (!File.Exists(path)) throw new InvalidDataException("私钥文件不存在，请重新选择或留空自动查找。");
                    keys.Add(string.IsNullOrEmpty(secret) ? new PrivateKeyFile(path) : new PrivateKeyFile(path, secret));
                }
                catch (Exception ex) when (automatic && ex is SshException or IOException or CryptographicException or UnauthorizedAccessException)
                {
                    skipped = true;
                }
            }
            if (keys.Count == 0) throw new InvalidDataException("找到的私钥无法读取或解密，请选择正确文件并填写私钥口令。");
            if (skipped) warning("部分自动发现的私钥无法读取或解密，已跳过；如需使用请指定文件并填写口令。");
            auth = new PrivateKeyAuthenticationMethod(server.Username, keys.ToArray());
        }
        var info = new ConnectionInfo(server.Host, server.Port, server.Username, auth)
        {
            Timeout = TimeSpan.FromSeconds(server.TimeoutSeconds),
            ChannelCloseTimeout = TimeSpan.FromSeconds(3)
        };
        client = new SshClient(info) { KeepAliveInterval = TimeSpan.FromSeconds(10) };
        Exception? trustFailure = null;
        client.HostKeyReceived += (_, e) =>
        {
            e.CanTrust = false;
            try
            {
                token.ThrowIfCancellationRequested();
                var fingerprint = "SHA256:" + Convert.ToBase64String(SHA256.HashData(e.HostKey)).TrimEnd('=');
                e.CanTrust = trust.Verify(server, e.HostKeyName, fingerprint, message => confirm(message, lifetime));
                if (!e.CanTrust) trustFailure = new InvalidDataException("未信任服务器主机指纹，连接已取消。");
            }
            catch (Exception ex) { trustFailure = ex; }
        };
        client.ErrorOccurred += (_, _) => broken = true;
        try { await client.ConnectAsync(token).ConfigureAwait(false); }
        catch when (trustFailure != null) { throw new InvalidDataException(trustFailure.Message, trustFailure); }
        token.ThrowIfCancellationRequested();
        port = rule.Direction == ForwardDirection.Local
            ? new ForwardedPortLocal(rule.ListenHost, (uint)rule.ListenPort, rule.TargetHost, (uint)rule.TargetPort)
            : new ForwardedPortRemote(rule.ListenHost, (uint)rule.ListenPort, rule.TargetHost, (uint)rule.TargetPort);
        port.Exception += (_, e) => warning("目标连接或转发请求失败：" + TunnelManager.Describe(e.Exception));
        client.AddForwardedPort(port);
        // ForwardedPort.Start has no async overload; the manager runs sessions on a worker thread.
        port.Start();
        if (!port.IsStarted) throw new InvalidDataException("服务器未确认转发监听，请检查端口占用和 SSH 转发策略。");
        token.ThrowIfCancellationRequested();
    }

    public void Dispose()
    {
        // Disconnect first so remote cancel cannot wait indefinitely on an unresponsive peer.
        try { client?.Dispose(); }
        finally { port?.Dispose(); foreach (var key in keys) key.Dispose(); }
    }
}

public sealed record TunnelSnapshot(string RuleId, TunnelState State, string Detail);
public sealed record TunnelEvent(DateTimeOffset Time, string RuleId, string Message);

public sealed class TunnelManager(ITunnelFactory factory)
{
    private sealed class Entry(Server server, ForwardRule rule)
    {
        public Server Server { get; } = server with { };
        public ForwardRule Rule { get; } = rule with { };
        public CancellationTokenSource Cancel { get; } = new();
        public Task Work { get; set; } = Task.CompletedTask;
        public TunnelState State { get; set; } = TunnelState.Connecting;
        public string Detail { get; set; } = "";
    }

    private readonly object sync = new();
    private readonly Dictionary<string, Entry> entries = [];
    private bool closing;
    public event Action<TunnelSnapshot>? Changed;
    public event Action<TunnelEvent>? Logged;

    public TunnelSnapshot Get(string ruleId)
    {
        lock (sync) return entries.TryGetValue(ruleId, out var e)
            ? new(ruleId, e.State, e.Detail) : new(ruleId, TunnelState.Stopped, "");
    }

    public bool IsActive(string ruleId)
    {
        lock (sync) return entries.TryGetValue(ruleId, out var entry) && !entry.Work.IsCompleted;
    }

    public void Start(Server server, ForwardRule rule)
    {
        Configuration.ValidateServer(server); Configuration.ValidateRule(rule);
        lock (sync)
        {
            if (closing) throw new InvalidOperationException("软件正在退出。");
            if (entries.TryGetValue(rule.Id, out var old) && !old.Work.IsCompleted) return;
            foreach (var other in entries.Values.Where(e => !e.Work.IsCompleted))
                if (Conflicts(server, rule, other.Server, other.Rule))
                    throw new InvalidDataException($"监听与正在使用的规则“{other.Rule.Name}”冲突。");
            old?.Cancel.Dispose();
            var entry = new Entry(server, rule);
            entries[rule.Id] = entry;
            entry.Work = Task.Run(() => RunAsync(entry));
            Publish(entry, TunnelState.Connecting, "正在建立 SSH 连接");
        }
    }

    public async Task StopAsync(string ruleId)
    {
        Entry? entry;
        lock (sync)
        {
            if (!entries.TryGetValue(ruleId, out entry)) return;
            entry.Cancel.Cancel();
            if (!entry.Work.IsCompleted) Publish(entry, TunnelState.Stopping, "正在释放连接");
        }
        await entry.Work.ConfigureAwait(false);
        lock (sync) Publish(entry, TunnelState.Stopped, "已停止");
    }

    public async Task StopAllAsync(bool shutdown = false)
    {
        string[] ids;
        lock (sync) { if (shutdown) closing = true; ids = entries.Keys.ToArray(); }
        await Task.WhenAll(ids.Select(StopAsync)).ConfigureAwait(false);
    }

    private async Task RunAsync(Entry entry)
    {
        var token = entry.Cancel.Token;
        var attempts = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var connected = false;
                try
                {
                    using (var session = factory.Create(entry.Server, entry.Rule, m => Log(entry, m), token))
                    {
                        await session.OpenAsync(token).ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                        connected = true; attempts = 0;
                        Publish(entry, TunnelState.Running, "监听已建立；目标服务可达性尚未验证");
                        while (session.IsAlive) await Task.Delay(500, token).ConfigureAwait(false);
                        throw new IOException("SSH 连接已中断。");
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    if (token.IsCancellationRequested) break;
                    Publish(entry, TunnelState.Error, Describe(ex));
                    // Never automatically repeat credential errors, trust rejection, or an initial connection failure.
                    if (!entry.Rule.AutoReconnect || (!connected && attempts == 0) || ex is SshAuthenticationException or InvalidDataException) break;
                }
                attempts++;
                var delay = Math.Min(30, 1 << Math.Min(attempts, 5));
                Publish(entry, TunnelState.Retrying, $"{delay} 秒后重连（第 {attempts} 次）");
                await Task.Delay(TimeSpan.FromSeconds(delay), token).ConfigureAwait(false);
                Publish(entry, TunnelState.Connecting, "正在重连");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            if (token.IsCancellationRequested) Publish(entry, TunnelState.Stopped, "已停止");
        }
    }

    private void Publish(Entry e, TunnelState state, string detail)
    {
        lock (sync)
        {
            // A stop requested during connect must not be overwritten by a late success callback.
            if (e.Cancel.IsCancellationRequested && state is not (TunnelState.Stopped or TunnelState.Stopping)) return;
            e.State = state; e.Detail = detail;
            Changed?.Invoke(new(e.Rule.Id, state, detail));
            Log(e, detail);
        }
    }

    private void Log(Entry e, string message) => Logged?.Invoke(new(DateTimeOffset.Now, e.Rule.Id, message));

    public static bool Conflicts(Server a, ForwardRule x, Server b, ForwardRule y)
    {
        if (x.Direction != y.Direction || x.ListenPort != y.ListenPort) return false;
        if (x.Direction == ForwardDirection.Remote &&
            (!a.Host.Equals(b.Host, StringComparison.OrdinalIgnoreCase) || a.Port != b.Port)) return false;
        var left = IPAddress.Parse(x.ListenHost); var right = IPAddress.Parse(y.ListenHost);
        return left.Equals(right) || left.Equals(IPAddress.Any) || right.Equals(IPAddress.Any)
            || left.Equals(IPAddress.IPv6Any) || right.Equals(IPAddress.IPv6Any);
    }

    public static string Describe(Exception ex) => ex switch
    {
        SshAuthenticationException => "SSH 认证失败，请检查用户名、密码或私钥。",
        SocketException => "网络连接或监听失败，请检查地址、端口占用与防火墙。",
        SshOperationTimeoutException or TimeoutException => "SSH 操作超时，请检查网络和服务端状态。",
        SshConnectionException => "SSH 连接失败或已中断，请检查主机信任和服务端策略。",
        SshException => "SSH 请求失败，请检查私钥口令、端口占用和服务端转发策略。",
        InvalidDataException => ex.Message,
        IOException => "连接或文件访问失败，请检查网络、私钥和配置文件。",
        _ => $"操作失败（{ex.GetType().Name}），请检查配置与 SSH 服务端策略。"
    };
}
