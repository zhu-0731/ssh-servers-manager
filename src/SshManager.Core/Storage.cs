using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace SshManager.Core;

public sealed class ConfigStore(string directory)
{
    public string DirectoryPath { get; } = directory;
    public string FilePath => Path.Combine(DirectoryPath, "config.json");

    public Configuration Load()
    {
        if (!File.Exists(FilePath)) return new Configuration();
        return Read(FilePath);
    }

    public static Configuration Read(string path)
    {
        if (new FileInfo(path).Length > 10 * 1024 * 1024) throw new InvalidDataException("配置文件超过 10 MB。");
        var config = JsonSerializer.Deserialize<Configuration>(File.ReadAllText(path), JsonFormat.Options)
            ?? throw new InvalidDataException("配置为空。");
        config.Validate();
        return config;
    }

    public void Save(Configuration config)
    {
        config.Validate();
        AtomicWrite(FilePath, JsonSerializer.Serialize(config, JsonFormat.Options));
    }

    public static void AtomicWrite(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = Encoding.UTF8.GetBytes(content);
                stream.Write(bytes); stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    // Export does not contain Windows credential IDs, private key paths, trust records or runtime state.
    public static void Export(Configuration config, string path)
    {
        var portable = new Configuration
        {
            Servers = config.Servers.Select(s => s with { PrivateKeyPath = "" }).ToList(),
            Profiles = config.Profiles.Select(p => p with { }).ToList(),
            Rules = config.Rules.Select(r => r with { }).ToList()
        };
        portable.Validate();
        AtomicWrite(path, JsonSerializer.Serialize(portable, JsonFormat.Options));
    }

    public static void ImportInto(Configuration destination, Configuration incoming)
    {
        incoming.Validate();
        var servers = incoming.Servers.ToDictionary(s => s.Id, _ => Guid.NewGuid().ToString("N"));
        var profiles = incoming.Profiles.ToDictionary(p => p.Id, _ => Guid.NewGuid().ToString("N"));
        destination.Servers.AddRange(incoming.Servers.Select(s => s with { Id = servers[s.Id] }));
        destination.Profiles.AddRange(incoming.Profiles.Select(p => p with { Id = profiles[p.Id], Name = p.Name + "（导入）" }));
        destination.Rules.AddRange(incoming.Rules.Select(r => r with
        {
            Id = Guid.NewGuid().ToString("N"), ServerId = servers[r.ServerId], ProfileId = profiles[r.ProfileId]
        }));
    }
}

public interface ICredentialStore
{
    string? Read(string serverId);
    void Write(string serverId, string secret);
    void Delete(string serverId);
}

public sealed class WindowsCredentialStore : ICredentialStore
{
    private const string Prefix = "SshServersManager/";
    public string? Read(string serverId)
    {
        if (!CredRead(Prefix + serverId, 1, 0, out var pointer))
        {
            var code = Marshal.GetLastWin32Error();
            if (code == 1168) return null;
            throw new System.ComponentModel.Win32Exception(code);
        }
        try
        {
            var c = Marshal.PtrToStructure<Credential>(pointer);
            return Marshal.PtrToStringUni(c.Blob, (int)c.BlobSize / 2);
        }
        finally { CredFree(pointer); }
    }
    public void Write(string serverId, string secret)
    {
        var bytes = Encoding.Unicode.GetBytes(secret);
        if (bytes.Length > 2560) throw new InvalidDataException("凭据长度超过 Windows 限制。");
        var pointer = Marshal.AllocHGlobal(Math.Max(1, bytes.Length));
        try
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            var c = new Credential { Type = 1, TargetName = Prefix + serverId, Blob = pointer, BlobSize = (uint)bytes.Length, Persist = 2, UserName = "SSH" };
            if (!CredWrite(ref c, 0)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            for (var i = 0; i < bytes.Length; i++) Marshal.WriteByte(pointer, i, 0);
            Array.Clear(bytes); Marshal.FreeHGlobal(pointer);
        }
    }
    public void Delete(string serverId)
    {
        if (!CredDelete(Prefix + serverId, 1, 0) && Marshal.GetLastWin32Error() != 1168)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint BlobSize;
        public IntPtr Blob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr pointer);
}

public sealed class HostTrustStore
{
    private readonly string path;
    private readonly Dictionary<string, string> keys;
    private readonly object sync = new();
    public HostTrustStore(string directory)
    {
        path = Path.Combine(directory, "known-hosts.json");
        keys = File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
            ?? throw new InvalidDataException("主机信任记录为空。") : [];
    }

    public bool Verify(Server server, string algorithm, string fingerprint, Func<string, bool> confirm)
    {
        // Serialize first-use prompts and persist before trusting. Changed keys are never automatically replaced.
        lock (sync)
        {
            var id = $"[{server.Host.Trim().ToLowerInvariant()}]:{server.Port}/{algorithm}";
            if (keys.TryGetValue(id, out var known))
            {
                if (known != fingerprint) throw new InvalidDataException("服务器指纹已变化，请通过可信渠道核验后管理 known-hosts.json。");
                return true;
            }
            if (!confirm($"首次连接 {server.Host}:{server.Port}\n算法：{algorithm}\n指纹：{fingerprint}\n\n请通过可信渠道核对后，确认是否信任？")) return false;
            keys[id] = fingerprint;
            try { ConfigStore.AtomicWrite(path, JsonSerializer.Serialize(keys, JsonFormat.Options)); }
            catch { keys.Remove(id); throw; }
            return true;
        }
    }
}
