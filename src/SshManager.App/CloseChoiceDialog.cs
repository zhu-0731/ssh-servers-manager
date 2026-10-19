namespace SshManager.App;

internal enum CloseChoice { Cancel, Tray, Exit }

internal sealed class CloseChoiceDialog : Form
{
    public CloseChoice Choice { get; private set; } = CloseChoice.Cancel;
    public CloseChoiceDialog(int activeRules, Icon icon)
    {
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
        Text = "关闭 SSH Servers Manager"; Icon = icon; Width = 550; Height = 270;
        Font = new Font("Microsoft YaHei UI", 10); StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        var description = new Label
        {
            Dock = DockStyle.Fill, Padding = new Padding(20), AutoSize = false,
            Text = $"当前有 {activeRules} 条活动通道。请选择关闭方式：\n\n最小化到托盘：保存配置，通道继续运行。\n彻底退出：保存配置，停止全部通道并退出。"
        };
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12)
        };
        var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        var tray = new Button { Text = "最小化到托盘", AutoSize = true };
        var exit = new Button { Text = "彻底退出", AutoSize = true };
        tray.Click += (_, _) => { Choice = CloseChoice.Tray; DialogResult = DialogResult.OK; };
        exit.Click += (_, _) => { Choice = CloseChoice.Exit; DialogResult = DialogResult.OK; };
        actions.Controls.Add(cancel); actions.Controls.Add(exit); actions.Controls.Add(tray);
        Controls.Add(description); Controls.Add(actions); CancelButton = cancel; AcceptButton = tray;
        AutoScaleDimensions = new SizeF(96, 96); PerformAutoScale();
    }
}
