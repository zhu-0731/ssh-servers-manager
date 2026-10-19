using SshManager.Core;

namespace SshManager.App;

internal class EditorForm : Form
{
    protected readonly TableLayoutPanel Fields = new() { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true, Padding = new Padding(16) };
    private readonly Button save = new() { Text = "保存", AutoSize = true };
    protected EditorForm(string title, int height = 440)
    {
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        Text = title; Width = 560; Height = height; StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 10); MinimizeBox = false; MaximizeBox = false;
        Fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        Fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        save.Click += (_, _) => { try { Save(); DialogResult = DialogResult.OK; } catch (Exception ex) { MessageBox.Show(this, ex.Message, "请检查配置"); } };
        footer.Controls.Add(cancel); footer.Controls.Add(save);
        Controls.Add(Fields); Controls.Add(footer); AcceptButton = save; CancelButton = cancel;
        AutoScaleDimensions = new SizeF(96, 96); PerformAutoScale();
    }
    protected virtual void Save() { }
    protected T Add<T>(string label, T control) where T : Control
    {
        var row = Fields.RowCount++;
        Fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Fields.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 9, 3, 9) }, 0, row);
        control.Dock = DockStyle.Top; control.Margin = new Padding(3, 5, 3, 5);
        Fields.Controls.Add(control, 1, row); return control;
    }
    protected TextBox Input(string label, string value = "") => Add(label, new TextBox { Text = value });
    protected Label FieldLabel(Control control) => (Label)Fields.GetControlFromPosition(0, Fields.GetRow(control))!;
    protected void SetFieldVisible(Control control, bool visible)
    {
        control.Visible = visible; FieldLabel(control).Visible = visible;
    }
    protected NumericUpDown Number(string label, int value, int maximum = 65535, int minimum = 1) =>
        Add(label, new NumericUpDown { Minimum = minimum, Maximum = maximum, Value = value });
    protected ComboBox Choice(string label, object[] options, int index = 0)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        combo.Items.AddRange(options); if (options.Length > 0) combo.SelectedIndex = index;
        return Add(label, combo);
    }
}

internal sealed class TextDialog : EditorForm
{
    private readonly TextBox name;
    private readonly TextBox note;
    public string Value => name.Text.Trim();
    public string Note => note.Text.Trim();
    public TextDialog(string title, string value, string description = "") : base(title, 260)
    {
        name = Input("名称", value); note = Input("备注", description);
    }
    protected override void Save() { if (Value.Length == 0) throw new InvalidDataException("名称不能为空。"); }
}

internal sealed class ServerEditor : EditorForm
{
    private readonly Server original;
    private readonly ICredentialStore credentials;
    private readonly TextBox name, host, username, key, secret;
    private readonly NumericUpDown port, timeout;
    private readonly ComboBox auth;
    private readonly CheckBox clear;
    private readonly Button browse;
    private readonly Button discover;
    private readonly Label authHint;
    public Server Result { get; private set; }
    public ServerEditor(Server server, ICredentialStore credentials) : base("SSH 服务器", 610)
    {
        original = server; Result = server; this.credentials = credentials;
        name = Input("名称", server.Name); host = Input("IP / 域名", server.Host); port = Number("SSH 端口", server.Port);
        username = Input("用户名", server.Username);
        auth = Choice("认证方式", ["用户名和密码（无需私钥）", "私钥文件"], (int)server.Auth);
        key = Input("私钥文件（可选）", server.PrivateKeyPath);
        key.PlaceholderText = "留空自动查找 ~/.ssh 下的常用密钥";
        browse = Add("", new Button { Text = "选择私钥文件…", AutoSize = true });
        browse.Click += (_, _) => { using var file = new OpenFileDialog(); if (file.ShowDialog(this) == DialogResult.OK) key.Text = file.FileName; };
        discover = Add("", new Button { Text = "查找本机常用私钥…", AutoSize = true });
        discover.Click += async (_, _) =>
        {
            try
            {
                var paths = PrivateKeyDiscovery.Find();
                using var menu = new ContextMenuStrip();
                menu.Items.Add("自动查找（留空，连接时依次尝试）", null, (_, _) => key.Clear());
                foreach (var path in paths) menu.Items.Add(path, null, (_, _) => key.Text = path);
                if (paths.Count == 0) { MessageBox.Show(this, "未找到常用私钥。已检查用户目录和 HOME 的 .ssh 中的 id_ed25519、id_ecdsa、id_rsa 及 .pem/.key/.ppk 文件。可手动选择其他位置。", "查找结果"); return; }
                var chosen = new TaskCompletionSource();
                menu.Closed += (_, _) => chosen.TrySetResult();
                menu.Show(discover, new Point(0, discover.Height));
                await chosen.Task;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "查找失败"); }
        };
        secret = Input("SSH 登录密码"); secret.UseSystemPasswordChar = true;
        authHint = Add("", new Label { AutoSize = true, MaximumSize = new Size(340, 0) });
        clear = Add("", new CheckBox { Text = "清除已保存凭据（用于无口令私钥）", AutoSize = true });
        timeout = Number("连接超时（秒）", server.TimeoutSeconds, 120, 3);
        auth.SelectedIndexChanged += (_, _) => { secret.Clear(); clear.Checked = false; UpdateAuthentication(); };
        UpdateAuthentication();
    }
    private void UpdateAuthentication()
    {
        var isKey = auth.SelectedIndex == (int)AuthKind.PrivateKey;
        Fields.SuspendLayout();
        SetFieldVisible(key, isKey); SetFieldVisible(browse, isKey); SetFieldVisible(discover, isKey); SetFieldVisible(clear, isKey);
        key.Enabled = browse.Enabled = isKey;
        FieldLabel(secret).Text = isKey ? "私钥口令（可选）" : "SSH 登录密码";
        authHint.Text = isKey
            ? "私钥路径留空会自动查找常用密钥并依次尝试。无口令私钥无需填写口令；SSH Agent 暂未接入。"
            : "使用 SSH 用户名和密码连接，无需私钥文件。已保存密码留空可保留。";
        Fields.ResumeLayout(true);
    }
    protected override void Save()
    {
        var result = original with
        {
            Name = name.Text.Trim(), Host = host.Text.Trim(), Port = (int)port.Value, Username = username.Text.Trim(),
            Auth = (AuthKind)auth.SelectedIndex, PrivateKeyPath = auth.SelectedIndex == (int)AuthKind.PrivateKey ? key.Text.Trim() : "", TimeoutSeconds = (int)timeout.Value
        };
        Configuration.ValidateServer(result);
        if (clear.Checked && secret.Text.Length > 0) throw new InvalidDataException("清除凭据与输入新凭据不能同时使用。");
        if (result.Auth == AuthKind.PrivateKey && !string.IsNullOrWhiteSpace(result.PrivateKeyPath) && !File.Exists(PrivateKeyDiscovery.ExpandPath(result.PrivateKeyPath))) throw new InvalidDataException("私钥文件不存在，可留空自动查找。");
        if (secret.Text.Length > 0) credentials.Write(result.Id, secret.Text);
        else if (clear.Checked || result.Auth != original.Auth) credentials.Delete(result.Id);
        Result = result;
    }
}

internal sealed class RuleEditor : EditorForm
{
    private readonly ForwardRule original;
    private readonly TextBox name, listenHost, targetHost;
    private readonly NumericUpDown listenPort, targetPort;
    private readonly ComboBox server, direction;
    private readonly CheckBox reconnect;
    private readonly Label explanation;
    public ForwardRule Result { get; private set; }
    public RuleEditor(ForwardRule rule, List<Server> servers) : base("端口转发规则", 590)
    {
        original = rule; Result = rule;
        name = Input("规则名称", rule.Name);
        server = Choice("SSH 服务器", servers.Cast<object>().ToArray(), Math.Max(0, servers.FindIndex(s => s.Id == rule.ServerId)));
        direction = Choice("转发方向", ["本地转发 -L", "远程转发 -R"], (int)rule.Direction);
        explanation = Add("", new Label { AutoSize = true, MaximumSize = new Size(340, 0) });
        listenHost = Input("监听 IP 地址", rule.ListenHost); listenPort = Number("监听端口", rule.ListenPort);
        targetHost = Input("目标 IP / 域名", rule.TargetHost); targetPort = Number("目标端口", rule.TargetPort);
        reconnect = Add("", new CheckBox { Text = "连接成功后掉线自动重连", Checked = rule.AutoReconnect, AutoSize = true });
        Add("", new Label { Text = "默认仅回环访问。对外开放需显式改监听地址；远程开放还需服务端允许。", AutoSize = true, MaximumSize = new Size(340, 0) });
        direction.SelectedIndexChanged += (_, _) => Explain(); Explain();
    }
    private void Explain() => explanation.Text = direction.SelectedIndex == 0
        ? "在本机监听，目标地址由 SSH 服务器访问。"
        : "在 SSH 服务器监听，目标地址由本机访问。";
    protected override void Save()
    {
        if (server.SelectedItem is not Server selected) throw new InvalidDataException("请选择服务器。");
        var result = original with
        {
            Name = name.Text.Trim(), ServerId = selected.Id, Direction = (ForwardDirection)direction.SelectedIndex,
            ListenHost = listenHost.Text.Trim(), ListenPort = (int)listenPort.Value,
            TargetHost = targetHost.Text.Trim(), TargetPort = (int)targetPort.Value, AutoReconnect = reconnect.Checked
        };
        Configuration.ValidateRule(result); Result = result;
    }
}

internal sealed class ProfilePicker : EditorForm
{
    private readonly ComboBox profile;
    private readonly TextBox name;
    public Profile? Existing => profile.SelectedItem as Profile;
    public string NewName => name.Text.Trim();
    public ProfilePicker(List<Profile> profiles) : base("复制规则到方案", 270)
    {
        profile = Choice("目标方案", new object[] { "新建方案…" }.Concat(profiles.Cast<object>()).ToArray());
        name = Input("新方案名称", "新方案");
        profile.SelectedIndexChanged += (_, _) => name.Enabled = Existing == null;
    }
    protected override void Save() { if (Existing == null && NewName.Length == 0) throw new InvalidDataException("请输入新方案名称。"); }
}

internal sealed class ServerManager : Form
{
    private readonly ListBox list = new() { Dock = DockStyle.Fill };
    private readonly Configuration config;
    private readonly ICredentialStore credentials;
    private readonly Action save;
    private readonly Func<string, bool> serverActive;
    public ServerManager(Configuration config, ICredentialStore credentials, Action save, Func<string, bool> serverActive)
    {
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        this.config = config; this.credentials = credentials; this.save = save; this.serverActive = serverActive;
        Text = "SSH 服务器管理"; Width = 600; Height = 420; StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 10);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50 };
        foreach (var (label, action) in new (string, Action)[] { ("新增", () => Edit(null)), ("编辑", () => Edit(list.SelectedItem as Server)), ("删除", Delete) })
        {
            var button = new Button { Text = label, AutoSize = true }; button.Click += (_, _) => Guard(action); bar.Controls.Add(button);
        }
        Controls.Add(list); Controls.Add(bar); Reload();
        AutoScaleDimensions = new SizeF(96, 96); PerformAutoScale();
    }
    private void Reload() { list.DataSource = null; list.DataSource = config.Servers.ToList(); }
    private void Edit(Server? server)
    {
        if (server != null && serverActive(server.Id)) throw new InvalidDataException("请先停止该服务器的全部规则后编辑。");
        using var dialog = new ServerEditor(server ?? new Server(), credentials);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (server == null) config.Servers.Add(dialog.Result);
        else config.Servers[config.Servers.FindIndex(s => s.Id == server.Id)] = dialog.Result;
        save(); Reload();
    }
    private void Delete()
    {
        if (list.SelectedItem is not Server server) return;
        if (config.Rules.Any(r => r.ServerId == server.Id)) throw new InvalidDataException("该服务器仍被规则引用，请先删除或修改相关规则。");
        if (MessageBox.Show(this, $"删除服务器“{server.Name}”及其凭据？", "确认删除", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        credentials.Delete(server.Id); config.Servers.Remove(server); save(); Reload();
    }
    private void Guard(Action action) { try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "操作失败"); } }
}
