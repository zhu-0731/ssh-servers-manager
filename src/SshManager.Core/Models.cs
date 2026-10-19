using System.Net;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SshManager.Core;

public enum AuthKind { Password, PrivateKey }
public enum ForwardDirection { Local, Remote }
public enum TunnelState { Stopped, Connecting, Running, Stopping, Error, Retrying }

public sealed record Server
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "";
    public AuthKind Auth { get; set; }
    public string PrivateKeyPath { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 15;
    public override string ToString() => Name;
}

public sealed record Profile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新方案";
    public string Description { get; set; } = "";
    public override string ToString() => Name;
}

public sealed record ForwardRule
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string ProfileId { get; set; } = "";
    public string ServerId { get; set; } = "";
    public string Name { get; set; } = "";
    public ForwardDirection Direction { get; set; }
    public string ListenHost { get; set; } = "127.0.0.1";
    public int ListenPort { get; set; } = 13306;
    public string TargetHost { get; set; } = "127.0.0.1";
    public int TargetPort { get; set; } = 3306;
    public bool AutoReconnect { get; set; }
}

public sealed class Configuration
{
    public int SchemaVersion { get; set; } = 1;
    public List<Server> Servers { get; set; } = [];
    public List<Profile> Profiles { get; set; } = [];
    public List<ForwardRule> Rules { get; set; } = [];

    public Profile CopyProfile(string sourceId, string name)
    {
        var source = Profiles.Single(p => p.Id == sourceId);
        var profile = source with { Id = Guid.NewGuid().ToString("N"), Name = name };
        var copies = Rules.Where(r => r.ProfileId == sourceId).Select(r => CopyRule(r, profile.Id)).ToList();
        Profiles.Add(profile);
        Rules.AddRange(copies);
        return profile;
    }

    public static ForwardRule CopyRule(ForwardRule source, string profileId) =>
        source with { Id = Guid.NewGuid().ToString("N"), ProfileId = profileId };

    public void Validate()
    {
        Require(SchemaVersion == 1, "不支持的配置版本。");
        Require(Servers != null && Profiles != null && Rules != null, "配置集合不能为空。");
        Unique(Servers!.Select(s => s.Id)); Unique(Profiles!.Select(p => p.Id)); Unique(Rules!.Select(r => r.Id));
        foreach (var s in Servers) ValidateServer(s);
        foreach (var p in Profiles) Require(!string.IsNullOrWhiteSpace(p.Name), "方案名称不能为空。");
        foreach (var r in Rules)
        {
            ValidateRule(r);
            Require(Profiles.Any(p => p.Id == r.ProfileId), "规则引用了不存在的方案。");
            Require(Servers.Any(s => s.Id == r.ServerId), "规则引用了不存在的服务器。");
        }
    }

    public static void ValidateServer(Server s)
    {
        Require(!string.IsNullOrWhiteSpace(s.Name), "服务器名称不能为空。");
        Host(s.Host); Port(s.Port); Require(!string.IsNullOrWhiteSpace(s.Username), "用户名不能为空。");
        Require(Enum.IsDefined(s.Auth), "认证方式无效。");
        Require(s.TimeoutSeconds is >= 3 and <= 120, "连接超时必须为 3–120 秒。");
    }

    public static void ValidateRule(ForwardRule r)
    {
        Require(!string.IsNullOrWhiteSpace(r.Name), "规则名称不能为空。");
        Require(Enum.IsDefined(r.Direction), "转发方向无效。");
        Require(IPAddress.TryParse(r.ListenHost, out _), "监听地址必须为 IPv4 或 IPv6 地址。");
        Host(r.TargetHost); Port(r.ListenPort); Port(r.TargetPort);
    }

    private static void Host(string h) => Require(!string.IsNullOrWhiteSpace(h) && !h.Any(char.IsWhiteSpace) && !h.Contains('\0'), "主机地址无效。");
    private static void Port(int p) => Require(p is >= 1 and <= 65535, "端口必须为 1–65535。");
    private static void Unique(IEnumerable<string> ids)
    {
        var values = ids.ToList();
        Require(values.All(id => !string.IsNullOrWhiteSpace(id)) && values.Distinct().Count() == values.Count, "配置 ID 缺失或重复。");
    }
    private static void Require([DoesNotReturnIf(false)] bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}

public static class JsonFormat
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };
}
