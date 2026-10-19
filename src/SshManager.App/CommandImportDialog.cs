using System.Text;
using SshManager.Core;

namespace SshManager.App;

internal sealed class CommandImportDialog : EditorForm
{
    private readonly TextBox command, preview, serverName, username, newProfileName;
    private readonly ComboBox auth, profile;
    private ParsedSshCommand? parsed;
    public ParsedSshCommand Result { get; private set; } = null!;
    public Profile? ExistingProfile => profile.SelectedItem as Profile;
    public string NewProfileName => newProfileName.Text.Trim();

    public CommandImportDialog(List<Profile> profiles, string? selectedProfileId) : base("从 SSH 命令导入", 700)
    {
        command = Add("SSH 命令", new TextBox
        {
            Multiline = true, Height = 86, MaxLength = 16384, ScrollBars = ScrollBars.Vertical,
            PlaceholderText = "ssh -p 22 -N -L 13306:10.0.0.5:3306 user@192.0.2.10"
        });
        var parse = Add("", new Button { Text = "解析并预览", AutoSize = true });
        parse.Click += (_, _) => ParseCommand();
        preview = Add("解析结果", new TextBox { ReadOnly = true, Multiline = true, Height = 175, ScrollBars = ScrollBars.Vertical });
        command.TextChanged += (_, _) => { parsed = null; preview.Clear(); };
        serverName = Input("服务器名称"); username = Input("用户名");
        auth = Choice("认证方式", ["用户名和密码", "私钥（未指定则自动查找）"], 1);
        profile = Choice("规则保存到", new object[] { "新建方案…" }.Concat(profiles.Cast<object>()).ToArray());
        if (selectedProfileId != null) profile.SelectedItem = profiles.FirstOrDefault(p => p.Id == selectedProfileId);
        if (profile.SelectedIndex < 0) profile.SelectedIndex = 0;
        newProfileName = Input("新方案名称", "命令导入");
        profile.SelectedIndexChanged += (_, _) => newProfileName.Enabled = ExistingProfile == null;
        newProfileName.Enabled = ExistingProfile == null;
        Add("", new Label
        {
            Text = "保存仅新增配置，不连接或执行命令。密码/私钥口令请保存后在服务器管理中填写。不会读取 ssh config 或 Agent。",
            AutoSize = true, MaximumSize = new Size(340, 0)
        });
    }

    private void ParseCommand()
    {
        try
        {
            parsed = SshCommandParser.Parse(command.Text);
            serverName.Text = parsed.Server.Name; username.Text = parsed.Server.Username; auth.SelectedIndex = (int)parsed.Server.Auth;
            var text = new StringBuilder();
            text.AppendLine($"服务器：{parsed.Server.Username}@{parsed.Server.Host}:{parsed.Server.Port}");
            text.AppendLine($"认证：{(parsed.Server.Auth == AuthKind.Password ? "密码" : "私钥")}；私钥：{(parsed.Server.PrivateKeyPath.Length == 0 ? "自动查找" : parsed.Server.PrivateKeyPath)}");
            foreach (var rule in parsed.Rules)
                text.AppendLine($"{(rule.Direction == ForwardDirection.Local ? "本地 -L" : "远程 -R")}  {Endpoint(rule.ListenHost, rule.ListenPort)} → {Endpoint(rule.TargetHost, rule.TargetPort)}");
            foreach (var warning in parsed.Warnings) text.AppendLine("提示：" + warning);
            preview.Text = text.ToString();
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            parsed = null; preview.Text = ex.Message;
        }
    }

    protected override void Save()
    {
        if (parsed == null) throw new InvalidDataException("请先解析命令并检查预览结果。");
        if (ExistingProfile == null && NewProfileName.Length == 0) throw new InvalidDataException("请输入新方案名称。");
        var server = parsed.Server with { Name = serverName.Text.Trim(), Username = username.Text.Trim(), Auth = (AuthKind)auth.SelectedIndex };
        if (server.Auth == AuthKind.Password) server.PrivateKeyPath = "";
        Configuration.ValidateServer(server);
        Result = parsed with { Server = server };
    }
    private static string Endpoint(string host, int port) => host.Contains(':') ? $"[{host}]:{port}" : $"{host}:{port}";
}
