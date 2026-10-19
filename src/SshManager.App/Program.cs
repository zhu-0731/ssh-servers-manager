using SshManager.Core;

namespace SshManager.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var instanceName = args.Contains("--smoke-test") ? "Local\\SshServersManager.Smoke" : "Local\\SshServersManager.Desktop";
        using var singleton = new Mutex(true, instanceName, out var first);
        if (!first) { MessageBox.Show("软件已经运行，请查看任务栏或系统托盘。", "SSH Servers Manager"); return; }
        var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SshServersManager");
        try
        {
            // Automated UI smoke check uses isolated data and exits without opening connections.
            if (args.Contains("--smoke-test"))
            {
                var sampleServer = new Server { Name = "开发服务器", Host = "192.0.2.10", Username = "developer" };
                var sampleProfile = new Profile { Name = "开发环境", Description = "双向转发 · 独立端口管理" };
                var sample = new Configuration { Servers = [sampleServer], Profiles = [sampleProfile], Rules = [
                    new ForwardRule { Name = "开发数据库", ServerId = sampleServer.Id, ProfileId = sampleProfile.Id, TargetHost = "10.0.0.5" },
                    new ForwardRule { Name = "本机网站", ServerId = sampleServer.Id, ProfileId = sampleProfile.Id, Direction = ForwardDirection.Remote, ListenPort = 18080, TargetPort = 8080 }
                ] };
                using var smoke = new MainForm(new ConfigStore(Path.Combine(AppContext.BaseDirectory, "smoke-data")), sample);
                smoke.Shown += (_, _) => smoke.BeginInvoke(() => smoke.CloseForSmokeTest());
                Application.Run(smoke);
            }
            else
            {
                var store = new ConfigStore(dataDirectory);
                Application.Run(new MainForm(store, store.Load()));
            }
        }
        catch (Exception ex)
        {
            if (args.Contains("--smoke-test"))
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke-error.txt"), ex.ToString());
                Environment.ExitCode = 1; return;
            }
            MessageBox.Show($"无法启动，配置不会被覆盖。\n\n{ex.Message}\n\n数据目录：{dataDirectory}\n如配置损坏，请先备份，再检查 config.json 和 config.json.bak。", "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.ExitCode = 1;
        }
    }
}
