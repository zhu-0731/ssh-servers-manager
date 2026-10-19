namespace SshManager.App;

internal static class AppIcon
{
    public static Icon Load()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("SshManager.App.Assets.app.ico")
            ?? throw new InvalidOperationException("应用图标资源缺失。");
        using var icon = new Icon(stream, new Size(32, 32));
        return (Icon)icon.Clone();
    }
}
