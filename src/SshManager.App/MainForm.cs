using System.Diagnostics;
using System.Net;
using SshManager.Core;

namespace SshManager.App;

internal sealed class MainForm : Form
{
    private readonly ConfigStore store;
    private readonly Configuration config;
    private readonly ICredentialStore credentials = new WindowsCredentialStore();
    private readonly TunnelManager tunnels;
    private readonly ListBox profiles = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly DataGridView grid = new()
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None, RowHeadersVisible = false,
        AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true,
        BackgroundColor = Color.White, BorderStyle = BorderStyle.None
    };
    private readonly TextBox logs = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 10) };
    private readonly Label summary = new() { Dock = DockStyle.Bottom, Height = 30, Padding = new Padding(8, 5, 0, 0) };
    private readonly Label note = new() { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8), AutoEllipsis = true };
    private readonly NotifyIcon tray = new() { Text = "SSH Servers Manager", Icon = SystemIcons.Application, Visible = true };
    private readonly Queue<string> logLines = new();
    private readonly System.Windows.Forms.Timer refresh = new() { Interval = 700 };
    private readonly SemaphoreSlim operation = new(1, 1);
    private bool exiting, allowClose, firstHide = true;

    private Profile? Current => profiles.SelectedItem as Profile;

    public MainForm(ConfigStore store, Configuration config)
    {
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        this.store = store; this.config = config;
        Text = "SSH Servers Manager"; Width = 1320; Height = 820; MinimumSize = new Size(1040, 650);
        StartPosition = FormStartPosition.CenterScreen; Font = new Font("Microsoft YaHei UI", 10);
        var trust = new HostTrustStore(store.DirectoryPath);
        tunnels = new TunnelManager(new SshTunnelFactory(credentials, trust, ConfirmHost));
        tunnels.Logged += e => OnUi(() => AddLog(e));
        tunnels.Changed += _ => OnUi(RefreshStates);

        var menu = new MenuStrip();
        var file = new ToolStripMenuItem("配置");
        file.DropDownItems.Add("SSH 服务器…", null, (_, _) => Run(() =>
        {
            using var dialog = new ServerManager(config, credentials, Save, id => config.Rules.Any(r => r.ServerId == id && tunnels.IsActive(r.Id)));
            dialog.ShowDialog(this); ReloadRules(); return Task.CompletedTask;
        }));
        file.DropDownItems.Add("导入配置（合并）…", null, (_, _) => Run(Import));
        file.DropDownItems.Add("从 SSH 命令导入…", null, (_, _) => Run(ImportCommand));
        file.DropDownItems.Add("导出全部配置…", null, (_, _) => Run(Export));
        file.DropDownItems.Add("打开数据目录", null, (_, _) => Run(() =>
        {
            Directory.CreateDirectory(store.DirectoryPath); Process.Start(new ProcessStartInfo(store.DirectoryPath) { UseShellExecute = true }); return Task.CompletedTask;
        }));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("退出并停止全部", null, (_, _) => Run(ExitAsync));
        menu.Items.Add(file); MainMenuStrip = menu;

        var left = new Panel { Dock = DockStyle.Left, Width = 225, Padding = new Padding(8) };
        left.Controls.Add(profiles);
        var profileButtons = Bar(DockStyle.Bottom, 88);
        Button(profileButtons, "新建", AddProfile); Button(profileButtons, "编辑", EditProfile);
        Button(profileButtons, "复制", CopyProfile); Button(profileButtons, "删除", DeleteProfile);
        left.Controls.Add(profileButtons);
        left.Controls.Add(new Label { Text = "连接方案", Dock = DockStyle.Top, Height = 34, Font = new Font(Font, FontStyle.Bold) });
        profiles.SelectedIndexChanged += (_, _) => ReloadRules();

        var main = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 370, FixedPanel = FixedPanel.Panel2 };
        split.Panel1.Controls.Add(grid); split.Panel1.Controls.Add(note);
        split.Panel2.Controls.Add(logs);
        split.Panel2.Controls.Add(new Label { Text = "连接日志（当前会话，最多 500 条）", Dock = DockStyle.Top, AutoSize = true });
        main.Controls.Add(split); main.Controls.Add(summary);
        var actions = Bar(DockStyle.Top, 88);
        Button(actions, "新增规则", () => EditRule(true)); Button(actions, "编辑规则", () => EditRule(false));
        Button(actions, "导入 SSH 命令…", ImportCommand);
        Button(actions, "删除规则", DeleteRules); Button(actions, "复制到方案…", CopyRules);
        Button(actions, "启动选中", () => StartRules(SelectedRules())); Button(actions, "停止选中", () => StopRules(SelectedRules()));
        Button(actions, "重连选中", ReconnectRules);
        Button(actions, "启动方案", () => StartRules(ProfileRules())); Button(actions, "停止方案", () => StopRules(ProfileRules()));
        main.Controls.Add(actions);

        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Check", HeaderText = "选择", FillWeight = 40 });
        AddColumn("Name", "规则", 110); AddColumn("Server", "SSH 服务器", 110);
        AddColumn("Direction", "方向", 65); AddColumn("Listen", "监听端点", 140);
        AddColumn("Target", "目标端点", 140); AddColumn("State", "状态", 65); AddColumn("Detail", "详情", 200);
        grid.Columns.Add(new DataGridViewButtonColumn { Name = "Toggle", HeaderText = "操作", Text = "启动", FillWeight = 55 });
        grid.Columns["Toggle"].DisplayIndex = 2;
        grid.Columns["Check"].Frozen = true; grid.Columns["Name"].Frozen = true; grid.Columns["Toggle"].Frozen = true;
        Shown += (_, _) =>
        {
            var screen = Screen.FromControl(this).WorkingArea;
            Width = Math.Min(Width, screen.Width - 40); Height = Math.Min(Height, screen.Height - 40);
            Location = new Point(screen.Left + (screen.Width - Width) / 2, screen.Top + (screen.Height - Height) / 2);
            SizeColumns();
            split.SplitterDistance = Math.Max(split.Panel1MinSize, split.Height - (int)(150 * DeviceDpi / 96f));
        };
        DpiChanged += (_, _) => SizeColumns();
        grid.CellContentClick += (_, e) =>
        {
            if (e.RowIndex < 0 || grid.Columns[e.ColumnIndex].Name != "Toggle") return;
            var id = (string)grid.Rows[e.RowIndex].Tag!;
            Run(() => tunnels.IsActive(id) ? tunnels.StopAsync(id) : StartRules(config.Rules.Where(r => r.Id == id).ToList()));
        };
        grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex > 0 && grid.Columns[e.ColumnIndex].Name != "Toggle") Run(() => EditRule(false)); };
        var copyMenu = new ContextMenuStrip();
        copyMenu.Items.Add("复制监听端口", null, (_, _) => CopyEndpoint(r => r.ListenPort.ToString()));
        copyMenu.Items.Add("复制目标端口", null, (_, _) => CopyEndpoint(r => r.TargetPort.ToString()));
        copyMenu.Items.Add("复制监听地址", null, (_, _) => CopyEndpoint(r => Endpoint(r.ListenHost, r.ListenPort)));
        copyMenu.Items.Add("复制目标地址", null, (_, _) => CopyEndpoint(r => Endpoint(r.TargetHost, r.TargetPort)));
        grid.ContextMenuStrip = copyMenu;
        grid.CellMouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right || e.RowIndex < 0) return;
            grid.ClearSelection(); grid.Rows[e.RowIndex].Selected = true; grid.CurrentCell = grid.Rows[e.RowIndex].Cells[1];
        };

        Controls.Add(main); Controls.Add(left); Controls.Add(menu);
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("打开主界面", null, (_, _) => RestoreWindow());
        trayMenu.Items.Add("停止全部", null, (_, _) => Run(() => tunnels.StopAllAsync()));
        trayMenu.Items.Add("退出并停止全部", null, (_, _) => Run(ExitAsync));
        tray.ContextMenuStrip = trayMenu; tray.DoubleClick += (_, _) => RestoreWindow();
        refresh.Tick += (_, _) => RefreshStates(); refresh.Start();
        FormClosing += ClosingWindow;
        if (config.Profiles.Count == 0) config.Profiles.Add(new Profile { Name = "默认方案" });
        ReloadProfiles();
        // Programmatic forms have no designer-generated scaling baseline.
        AutoScaleDimensions = new SizeF(96, 96); PerformAutoScale();
    }

    private void AddColumn(string name, string header, float weight) => grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = header, FillWeight = weight, ReadOnly = true });
    private static FlowLayoutPanel Bar(DockStyle dock, int height) => new() { Dock = dock, Height = height, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(0, 4, 0, 0) };
    private void SizeColumns()
    {
        var widths = new Dictionary<string, int> { ["Check"] = 44, ["Name"] = 125, ["Toggle"] = 65, ["Server"] = 130, ["Direction"] = 85, ["Listen"] = 190, ["Target"] = 190, ["State"] = 90, ["Detail"] = 330 };
        foreach (var (name, width) in widths) grid.Columns[name].Width = (int)(width * DeviceDpi / 96f);
    }
    private void Button(FlowLayoutPanel bar, string text, Func<Task> action)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 32, Margin = new Padding(3) };
        button.Click += (_, _) => Run(action); bar.Controls.Add(button);
    }

    private async void Run(Func<Task> action)
    {
        if (exiting || !await operation.WaitAsync(0)) return;
        try { await action(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { operation.Release(); if (!IsDisposed) RefreshStates(); }
    }

    private void Save() => store.Save(config);
    private void ReloadProfiles(string? select = null)
    {
        var id = select ?? Current?.Id;
        profiles.DataSource = null; profiles.DataSource = config.Profiles.ToList();
        if (id != null) profiles.SelectedItem = config.Profiles.FirstOrDefault(p => p.Id == id);
        if (profiles.SelectedIndex < 0 && profiles.Items.Count > 0) profiles.SelectedIndex = 0;
        ReloadRules();
    }
    private List<ForwardRule> ProfileRules() => config.Rules.Where(r => r.ProfileId == Current?.Id).ToList();
    private List<ForwardRule> SelectedRules()
    {
        grid.EndEdit();
        var checkedIds = grid.Rows.Cast<DataGridViewRow>().Where(r => r.Cells[0].Value is true).Select(r => (string)r.Tag!).ToHashSet();
        var ids = checkedIds.Count > 0 ? checkedIds : grid.SelectedRows.Cast<DataGridViewRow>().Select(r => (string)r.Tag!).ToHashSet();
        return config.Rules.Where(r => ids.Contains(r.Id)).ToList();
    }
    private void ReloadRules()
    {
        grid.Rows.Clear();
        foreach (var r in ProfileRules())
        {
            var server = config.Servers.FirstOrDefault(s => s.Id == r.ServerId);
            var i = grid.Rows.Add(false, r.Name, server?.Name ?? "服务器缺失", r.Direction == ForwardDirection.Local ? "本地 -L" : "远程 -R",
                Endpoint(r.ListenHost, r.ListenPort), Endpoint(r.TargetHost, r.TargetPort), "", "", "启动");
            grid.Rows[i].Tag = r.Id;
        }
        note.Text = Current?.Description ?? ""; RefreshStates();
    }
    private void RefreshStates()
    {
        if (IsDisposed) return;
        foreach (DataGridViewRow row in grid.Rows)
        {
            var state = tunnels.Get((string)row.Tag!);
            row.Cells["State"].Value = StateText(state.State); row.Cells["Detail"].Value = state.Detail;
            row.Cells["Toggle"].Value = tunnels.IsActive(state.RuleId) ? "停止" : "启动";
            row.DefaultCellStyle.ForeColor = state.State == TunnelState.Error ? Color.Firebrick : state.State == TunnelState.Running ? Color.DarkGreen : Color.FromArgb(40, 45, 55);
        }
        var running = config.Rules.Count(r => tunnels.Get(r.Id).State == TunnelState.Running);
        var failed = config.Rules.Count(r => tunnels.Get(r.Id).State == TunnelState.Error);
        summary.Text = $"全部方案：运行 {running} 条 · 异常 {failed} 条 · 共 {config.Rules.Count} 条规则";
    }
    private static string StateText(TunnelState s) => s switch
    {
        TunnelState.Connecting => "连接中", TunnelState.Running => "运行中", TunnelState.Stopping => "停止中",
        TunnelState.Error => "异常", TunnelState.Retrying => "等待重连", _ => "未启动"
    };
    private static string Endpoint(string host, int port) => host.Contains(':') ? $"[{host}]:{port}" : $"{host}:{port}";

    private Task AddProfile()
    {
        using var dialog = new TextDialog("新建方案", "新方案");
        if (dialog.ShowDialog(this) != DialogResult.OK) return Task.CompletedTask;
        var profile = new Profile { Name = dialog.Value, Description = dialog.Note };
        config.Profiles.Add(profile); Save(); ReloadProfiles(profile.Id); return Task.CompletedTask;
    }
    private Task EditProfile()
    {
        if (Current is not { } p) return Task.CompletedTask;
        using var dialog = new TextDialog("编辑方案", p.Name, p.Description);
        if (dialog.ShowDialog(this) == DialogResult.OK) { p.Name = dialog.Value; p.Description = dialog.Note; Save(); ReloadProfiles(p.Id); }
        return Task.CompletedTask;
    }
    private Task CopyProfile()
    {
        if (Current is not { } p) return Task.CompletedTask;
        using var dialog = new TextDialog("复制方案", p.Name + "（副本）", p.Description);
        if (dialog.ShowDialog(this) != DialogResult.OK) return Task.CompletedTask;
        var copy = config.CopyProfile(p.Id, dialog.Value); copy.Description = dialog.Note;
        Save(); ReloadProfiles(copy.Id); return Task.CompletedTask;
    }
    private async Task DeleteProfile()
    {
        if (Current is not { } p || !Ask($"删除方案“{p.Name}”和其中全部规则？运行的规则会先停止。")) return;
        var rules = config.Rules.Where(r => r.ProfileId == p.Id).ToList();
        await StopRules(rules); config.Rules.RemoveAll(r => r.ProfileId == p.Id); config.Profiles.Remove(p);
        Save(); ReloadProfiles();
    }
    private Task EditRule(bool create)
    {
        if (Current is not { } p) throw new InvalidDataException("请先创建方案。");
        if (config.Servers.Count == 0) throw new InvalidDataException("请先在“配置 → SSH 服务器”添加服务器。");
        var rule = create ? new ForwardRule { ProfileId = p.Id } : SelectedRules().FirstOrDefault();
        if (rule == null) throw new InvalidDataException("请选择一条规则。");
        if (!create && tunnels.IsActive(rule.Id)) throw new InvalidDataException("请先停止规则再编辑；重启后新配置生效。");
        using var dialog = new RuleEditor(rule, config.Servers);
        if (dialog.ShowDialog(this) != DialogResult.OK) return Task.CompletedTask;
        if (create) config.Rules.Add(dialog.Result);
        else config.Rules[config.Rules.FindIndex(r => r.Id == rule.Id)] = dialog.Result;
        Save(); ReloadRules(); return Task.CompletedTask;
    }
    private async Task DeleteRules()
    {
        var rules = SelectedRules(); if (rules.Count == 0 || !Ask($"停止并删除选中的 {rules.Count} 条规则？")) return;
        await StopRules(rules); foreach (var r in rules) config.Rules.Remove(r); Save(); ReloadRules();
    }
    private Task CopyRules()
    {
        var rules = SelectedRules(); if (rules.Count == 0) throw new InvalidDataException("请先选择规则。");
        using var dialog = new ProfilePicker(config.Profiles);
        if (dialog.ShowDialog(this) != DialogResult.OK) return Task.CompletedTask;
        var profile = dialog.Existing ?? new Profile { Name = dialog.NewName };
        if (dialog.Existing == null) config.Profiles.Add(profile);
        config.Rules.AddRange(rules.Select(r => Configuration.CopyRule(r, profile.Id)));
        Save(); ReloadProfiles(profile.Id); return Task.CompletedTask;
    }
    private Task StartRules(List<ForwardRule> rules)
    {
        var failures = new List<string>();
        foreach (var rule in rules)
        {
            try { tunnels.Start(config.Servers.Single(s => s.Id == rule.ServerId), rule); }
            catch (Exception ex) { failures.Add(rule.Name + ": " + ex.Message); }
        }
        if (failures.Count > 0) MessageBox.Show(this, string.Join("\n", failures), "部分规则未启动");
        return Task.CompletedTask;
    }
    private Task StopRules(List<ForwardRule> rules) => Task.WhenAll(rules.Select(r => tunnels.StopAsync(r.Id)));
    private async Task ReconnectRules() { var rules = SelectedRules(); await StopRules(rules); await StartRules(rules); }
    private Task Import()
    {
        using var file = new OpenFileDialog { Filter = "JSON 配置|*.json" };
        if (file.ShowDialog(this) != DialogResult.OK) return Task.CompletedTask;
        var incoming = ConfigStore.Read(file.FileName);
        if (!Ask($"导入 {incoming.Servers.Count} 台服务器、{incoming.Profiles.Count} 个方案和 {incoming.Rules.Count} 条规则？\n导入内容使用新 ID，不覆盖现有配置。密码与私钥需重新配置。")) return Task.CompletedTask;
        ConfigStore.ImportInto(config, incoming); Save(); ReloadProfiles(); return Task.CompletedTask;
    }
    private Task ImportCommand()
    {
        using var dialog = new CommandImportDialog(config.Profiles, Current?.Id);
        if (dialog.ShowDialog(this) != DialogResult.OK) return Task.CompletedTask;
        var result = dialog.Result;
        var profile = dialog.ExistingProfile ?? new Profile { Name = dialog.NewProfileName };
        var candidate = new Configuration
        {
            Servers = [.. config.Servers, result.Server],
            Profiles = dialog.ExistingProfile == null ? [.. config.Profiles, profile] : [.. config.Profiles],
            Rules = [.. config.Rules, .. result.Rules.Select(r => r with { ProfileId = profile.Id })]
        };
        // Persist a complete validated candidate before changing the in-memory configuration.
        store.Save(candidate);
        config.Servers = candidate.Servers; config.Profiles = candidate.Profiles; config.Rules = candidate.Rules;
        ReloadProfiles(profile.Id);
        MessageBox.Show(this, $"已新增服务器及 {result.Rules.Count} 条规则，全部保持停止。\n如果需要密码或私钥口令，请在“配置 → SSH 服务器”填写。", "导入完成");
        return Task.CompletedTask;
    }
    private Task Export()
    {
        using var file = new SaveFileDialog { Filter = "JSON 配置|*.json", FileName = "ssh-manager-config.json" };
        if (file.ShowDialog(this) == DialogResult.OK)
        {
            ConfigStore.Export(config, file.FileName);
            MessageBox.Show(this, "已导出全部配置，包含服务器地址和用户名，不包含凭据、私钥路径或信任记录。", "导出完成");
        }
        return Task.CompletedTask;
    }
    private void CopyEndpoint(Func<ForwardRule, string> select) => Run(() =>
    {
        var rules = SelectedRules(); if (rules.Count > 0) Clipboard.SetText(string.Join(Environment.NewLine, rules.Select(select)));
        return Task.CompletedTask;
    });
    private bool Ask(string message) => MessageBox.Show(this, message, "请确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    private bool ConfirmHost(string message, CancellationToken token)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        OnUi(() =>
        {
            if (token.IsCancellationRequested || exiting) { completion.TrySetResult(false); return; }
            using var dialog = new Form { AutoScaleDimensions = new SizeF(96, 96), AutoScaleMode = AutoScaleMode.Dpi, Text = "核验 SSH 主机指纹", Width = 650, Height = 290, StartPosition = FormStartPosition.CenterParent, Font = Font, MinimizeBox = false, MaximizeBox = false };
            dialog.Controls.Add(new TextBox { Text = message.Replace("\n", Environment.NewLine), Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None });
            var bar = Bar(DockStyle.Bottom, 48);
            var yes = new Button { Text = "信任并连接", DialogResult = DialogResult.OK, AutoSize = true };
            var no = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
            bar.Controls.Add(yes); bar.Controls.Add(no); dialog.Controls.Add(bar); dialog.CancelButton = no;
            using var timer = new System.Windows.Forms.Timer { Interval = 100 };
            timer.Tick += (_, _) => { if (token.IsCancellationRequested || exiting) dialog.Close(); }; timer.Start();
            dialog.AutoScaleDimensions = new SizeF(96, 96); dialog.PerformAutoScale();
            completion.TrySetResult(dialog.ShowDialog(this) == DialogResult.OK && !token.IsCancellationRequested);
        });
        try { return completion.Task.WaitAsync(token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { return false; }
    }

    private void AddLog(TunnelEvent e)
    {
        var name = config.Rules.FirstOrDefault(r => r.Id == e.RuleId)?.Name ?? e.RuleId;
        logLines.Enqueue($"{e.Time:HH:mm:ss} [{name}] {e.Message}");
        while (logLines.Count > 500) logLines.Dequeue();
        logs.Text = string.Join(Environment.NewLine, logLines); logs.SelectionStart = logs.TextLength; logs.ScrollToCaret();
    }
    private void OnUi(Action action)
    {
        if (IsDisposed || Disposing || !IsHandleCreated) return;
        try { BeginInvoke(action); } catch (InvalidOperationException) { }
    }
    private void RestoreWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    private void ClosingWindow(object? sender, FormClosingEventArgs e)
    {
        if (allowClose) return;
        e.Cancel = true;
        if (e.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing) { Run(ExitAsync); return; }
        Hide();
        if (firstHide) { firstHide = false; tray.ShowBalloonTip(4000, "已最小化到托盘", "通道继续运行。选择托盘“退出并停止全部”可释放所有连接。", ToolTipIcon.Info); }
    }
    private async Task ExitAsync()
    {
        exiting = true; Enabled = false; Text = "正在停止全部通道并退出…";
        try { Save(); await tunnels.StopAllAsync(shutdown: true); }
        catch
        {
            // Cleanup must still happen if config persistence fails.
            await tunnels.StopAllAsync(shutdown: true); throw;
        }
        finally { allowClose = true; tray.Visible = false; Close(); }
    }
    internal void CloseForSmokeTest()
    {
        using var image = new Bitmap(Width, Height);
        DrawToBitmap(image, new Rectangle(0, 0, Width, Height));
        image.Save(Path.Combine(AppContext.BaseDirectory, "ui-smoke.png"));
        using var rule = new RuleEditor(config.Rules[0], config.Servers);
        using var server = new ServerEditor(config.Servers[0], credentials);
        using var picker = new ProfilePicker(config.Profiles);
        using var servers = new ServerManager(config, credentials, () => { }, _ => false);
        using var command = new CommandImportDialog(config.Profiles, Current?.Id);
        foreach (var dialog in new Form[] { rule, server, picker, servers, command })
        {
            dialog.Show(); dialog.Close();
        }
        allowClose = true; Close();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { refresh.Dispose(); tray.Dispose(); }
        base.Dispose(disposing);
    }
}
