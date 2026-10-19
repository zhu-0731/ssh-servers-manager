using System.Globalization;
using System.Net;
using System.Text;

namespace SshManager.Core;

public sealed record ParsedSshCommand(Server Server, IReadOnlyList<ForwardRule> Rules, IReadOnlyList<string> Warnings);

/// <summary>Parses a bounded subset of OpenSSH syntax. Never starts a process or evaluates shell input.</summary>
public static class SshCommandParser
{
    public static ParsedSshCommand Parse(string command, string? defaultUsername = null)
    {
        var words = Tokenize(command);
        if (words.Count < 2 || !new[] { "ssh", "ssh.exe" }.Contains(Path.GetFileName(words[0].Replace('\\', '/')), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("请输入以 ssh 或 ssh.exe 开头的完整命令。");

        string? destination = null, username = null, hostname = null, identity = null;
        int? port = null, timeout = null;
        AuthKind? authentication = null;
        var specs = new List<(ForwardDirection Direction, string Spec)>();
        var warnings = new List<string>();
        var gateway = false;
        var optionsEnded = false;
        for (var index = 1; index < words.Count; index++)
        {
            var word = words[index];
            if (word == "--" && !optionsEnded) { optionsEnded = true; continue; }
            if (optionsEnded || !word.StartsWith('-') || word == "-")
            {
                if (destination != null) throw new InvalidDataException("仅支持一个 SSH 连接命令，不支持远程命令、脚本或额外的位置参数。");
                destination = word; continue;
            }
            for (var position = 1; position < word.Length; position++)
            {
                var option = word[position];
                if ("NTnfvqC".Contains(option))
                {
                    warnings.Add(option == 'C' ? "-C 压缩选项尚未应用，仍使用 SSH 库默认设置。" : $"-{option} 的终端、后台或输出行为由桌面软件管理，不按命令执行。");
                    continue;
                }
                if (option == 'g') { gateway = true; continue; }
                if (!"pliLRo".Contains(option))
                    throw new InvalidDataException($"暂不支持 -{option}；跳板机、SOCKS、外部配置和远程命令不能导入。");
                var value = position + 1 < word.Length ? word[(position + 1)..] :
                    ++index < words.Count ? words[index] : throw new InvalidDataException($"-{option} 缺少参数。");
                if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException($"-{option} 参数不能为空。");
                switch (option)
                {
                    case 'p': Set(ref port, Port(value), "SSH 端口"); break;
                    case 'l': Set(ref username, value, "用户名"); break;
                    case 'i': Set(ref identity, value, "私钥文件（当前仅支持一个 -i）"); break;
                    case 'L': specs.Add((ForwardDirection.Local, value)); break;
                    case 'R': specs.Add((ForwardDirection.Remote, value)); break;
                    case 'o':
                        var separator = value.IndexOfAny(['=', ' ', '\t']);
                        if (separator <= 0) throw new InvalidDataException("-o 需使用 Key=Value 或引号内的 Key Value。");
                        var key = value[..separator].Trim().ToLowerInvariant();
                        var setting = value[(separator + 1)..].Trim().TrimStart('=').Trim();
                        if (setting.Length == 0) throw new InvalidDataException("-o 选项值不能为空。");
                        switch (key)
                        {
                            case "port": Set(ref port, Port(setting), "SSH 端口"); break;
                            case "user": Set(ref username, setting, "用户名"); break;
                            case "hostname": Set(ref hostname, setting, "主机"); break;
                            case "identityfile": Set(ref identity, setting, "私钥文件"); break;
                            case "connecttimeout":
                                var seconds = Number(setting, 3, 120, "连接超时必须为 3–120 秒。");
                                Set(ref timeout, seconds, "连接超时"); break;
                            case "localforward": specs.Add((ForwardDirection.Local, NormalizeForward(setting))); break;
                            case "remoteforward": specs.Add((ForwardDirection.Remote, NormalizeForward(setting))); break;
                            case "preferredauthentications":
                                var auth = setting.ToLowerInvariant() switch
                                {
                                    "password" => AuthKind.Password, "publickey" => AuthKind.PrivateKey,
                                    _ => throw new InvalidDataException("当前认证方式仅支持 password 或 publickey，组合认证请在界面单独选择。")
                                };
                                Set(ref authentication, auth, "认证方式"); break;
                            default: throw new InvalidDataException("该 -o 选项尚未支持，不能静默导入。支持 Port、User、HostName、IdentityFile、ConnectTimeout、LocalForward、RemoteForward、PreferredAuthentications。");
                        }
                        break;
                }
                break; // The rest of this token belongs to the option argument.
            }
        }

        if (destination == null) throw new InvalidDataException("命令缺少服务器地址。");
        string? destinationUser = null;
        string destinationHost;
        if (destination.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(destination, UriKind.Absolute, out var uri) || uri.Host.Length == 0 ||
                uri.AbsolutePath is not ("" or "/") || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Contains(':'))
                throw new InvalidDataException("SSH URI 无效；仅支持 ssh://用户名@主机:端口，不允许密码或路径。");
            destinationUser = uri.UserInfo.Length > 0 ? Uri.UnescapeDataString(uri.UserInfo) : null;
            if (destinationUser?.Contains(':') == true || uri.Port == 0) throw new InvalidDataException("SSH URI 不允许包含密码或端口 0。");
            destinationHost = uri.Host.Trim('[', ']');
            if (uri.Port > 0) Set(ref port, Port(uri.Port.ToString(CultureInfo.InvariantCulture)), "SSH 端口");
        }
        else
        {
            var at = destination.LastIndexOf('@');
            if (at >= 0)
            {
                destinationUser = destination[..at]; destinationHost = destination[(at + 1)..];
                if (destinationUser.Length == 0 || destinationUser.Contains('@')) throw new InvalidDataException("用户名格式无效。");
            }
            else destinationHost = destination;
            if (destinationHost.StartsWith('['))
            {
                if (!destinationHost.EndsWith(']') || !IPAddress.TryParse(destinationHost[1..^1], out _)) throw new InvalidDataException("服务器 IPv6 方括号格式无效。");
                destinationHost = destinationHost[1..^1];
            }
            if (destinationHost.Contains(':') && !IPAddress.TryParse(destinationHost, out _))
                throw new InvalidDataException("请用 -p 指定 SSH 端口，或使用 ssh://用户名@主机:端口。");
        }
        if (username != null && destinationUser != null && username != destinationUser)
            warnings.Add("-l / User 与地址中的用户名不同，采用显式指定的 -l / User。");
        username ??= destinationUser;
        if (username == null)
        {
            username = defaultUsername ?? Environment.UserName;
            warnings.Add("命令未指定用户名，暂用当前 Windows 用户名；请在保存前核对。");
        }
        if (identity?.StartsWith("$HOME/") == true) identity = "~/" + identity[6..];
        if (identity?.StartsWith("${HOME}/") == true) identity = "~/" + identity[8..];
        var server = new Server
        {
            Name = $"{username}@{hostname ?? destinationHost}:{port ?? 22}",
            Host = hostname ?? destinationHost, Port = port ?? 22, Username = username,
            Auth = authentication ?? AuthKind.PrivateKey, PrivateKeyPath = identity ?? "", TimeoutSeconds = timeout ?? 15
        };
        if (identity != null && server.Auth == AuthKind.Password)
            throw new InvalidDataException("私钥文件与 password 认证选项冲突，请选择一种认证方式。");
        Configuration.ValidateServer(server);
        if (specs.Count > 100) throw new InvalidDataException("单条命令最多导入 100 条转发规则。");
        var rules = specs.Select(pair => ParseForward(pair.Spec, pair.Direction, gateway && pair.Direction == ForwardDirection.Local ? "0.0.0.0" : "127.0.0.1"))
            .Select(rule => rule with { ServerId = server.Id }).ToList();
        if (rules.Any(r => IPAddress.Parse(r.ListenHost).Equals(IPAddress.Any) || IPAddress.Parse(r.ListenHost).Equals(IPAddress.IPv6Any)))
            warnings.Add("存在对外监听规则；保存前核对暴露范围，远程监听仍受服务端 SSH 策略约束。");
        if (rules.Count == 0) warnings.Add("未包含 -L / -R；仅保存服务器，之后可手动新增转发规则。");
        if (identity == null && server.Auth == AuthKind.PrivateKey) warnings.Add("未指定私钥，按常用路径自动查找；如使用密码，请在下方切换认证方式。");
        return new(server, rules, warnings.Distinct().ToList());
    }

    private static ForwardRule ParseForward(string spec, ForwardDirection direction, string defaultBind)
    {
        var parts = SplitAddress(spec);
        if (parts.Count is not (3 or 4)) throw new InvalidDataException("转发格式应为 [监听IP:]监听端口:目标地址:目标端口；不支持 Unix socket、动态远程代理或自动分配端口。");
        var bind = parts.Count == 4 ? parts[0] : defaultBind;
        bind = bind switch { "" or "*" => "0.0.0.0", "localhost" => "127.0.0.1", _ => bind };
        if (!IPAddress.TryParse(bind, out _)) throw new InvalidDataException("监听地址仅支持 IP、localhost 或 *。");
        var offset = parts.Count == 4 ? 1 : 0;
        var listenPort = Port(parts[offset]); var target = parts[offset + 1]; var targetPort = Port(parts[offset + 2]);
        if (target.Contains(':') && !IPAddress.TryParse(target, out _)) throw new InvalidDataException("目标 IPv6 地址格式无效。");
        var rule = new ForwardRule
        {
            Name = $"{(direction == ForwardDirection.Local ? "本地" : "远程")} {listenPort} → {target}:{targetPort}",
            Direction = direction, ListenHost = bind, ListenPort = listenPort, TargetHost = target, TargetPort = targetPort
        };
        Configuration.ValidateRule(rule); return rule;
    }

    private static List<string> SplitAddress(string value)
    {
        var parts = new List<string>(); var part = new StringBuilder(); var bracket = false; var closed = false;
        foreach (var c in value)
        {
            if (c == '[') { if (bracket || closed || part.Length > 0) throw new InvalidDataException("IPv6 方括号格式无效。"); bracket = true; }
            else if (c == ']') { if (!bracket) throw new InvalidDataException("IPv6 方括号格式无效。"); bracket = false; closed = true; }
            else if (c == ':' && !bracket) { parts.Add(part.ToString()); part.Clear(); closed = false; }
            else { if (closed) throw new InvalidDataException("IPv6 方括号后有多余字符。"); part.Append(c); }
        }
        if (bracket) throw new InvalidDataException("IPv6 方括号未闭合。");
        parts.Add(part.ToString()); return parts;
    }

    private static string NormalizeForward(string value)
    {
        var parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 ? parts[0] + ":" + parts[1] : value;
    }
    private static int Port(string value) => Number(value, 1, 65535, "端口必须为 1–65535 的整数，不支持动态端口 0。");
    private static int Number(string value, int minimum, int maximum, string message) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= minimum && number <= maximum ? number : throw new InvalidDataException(message);
    private static void Set<T>(ref T? field, T value, string name) where T : struct
    {
        if (field.HasValue && !field.Value.Equals(value)) throw new InvalidDataException($"{name}重复且不一致，请先明确唯一值。");
        field = value;
    }
    private static void Set(ref string? field, string value, string name)
    {
        if (field != null && field != value) throw new InvalidDataException($"{name}重复且不一致，请先明确唯一值。");
        field = value;
    }

    private static List<string> Tokenize(string command)
    {
        if (string.IsNullOrWhiteSpace(command) || command.Length > 16384) throw new InvalidDataException("请输入一条不超过 16 KB 的 SSH 命令。");
        var text = command.Trim(); var tokens = new List<string>(); var token = new StringBuilder();
        if (text.Contains("$(", StringComparison.Ordinal)) throw new InvalidDataException("不支持 shell 命令替换，请粘贴具体的 SSH 参数。");
        var quote = '\0'; var started = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '\0') throw new InvalidDataException("命令包含无效字符。");
            if (quote == '\'')
            {
                if (c == '\'') quote = '\0'; else token.Append(c);
                continue;
            }
            if (c is '\\' or '^' or '`' && i + 1 < text.Length && text[i + 1] is '\r' or '\n')
            {
                i++; if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                continue;
            }
            if (quote == '"')
            {
                if (c == '"') quote = '\0';
                else if (c == '\\' && i + 1 < text.Length && text[i + 1] == '"') { token.Append('"'); i++; }
                else token.Append(c);
                continue;
            }
            if (c is '\'' or '"') { quote = c; started = true; }
            else if (c is ';' or '|' or '&' or '>' or '<' or '`' or '\r' or '\n') throw new InvalidDataException("仅支持单条 SSH 命令，不支持管道、重定向、脚本或多条命令。");
            else if (c == '\\' && i + 1 < text.Length && (text[i + 1] == ' ' || text[i + 1] == '\'')) { token.Append(text[++i]); started = true; }
            else if (char.IsWhiteSpace(c))
            {
                if (started) { tokens.Add(token.ToString()); token.Clear(); started = false; }
            }
            else { token.Append(c); started = true; }
        }
        if (quote != '\0') throw new InvalidDataException("命令中的引号未闭合。");
        if (started) tokens.Add(token.ToString());
        if (tokens.Count > 1024) throw new InvalidDataException("命令参数过多。");
        return tokens;
    }
}
