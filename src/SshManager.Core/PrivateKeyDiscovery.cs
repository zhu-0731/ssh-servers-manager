namespace SshManager.Core;

public static class PrivateKeyDiscovery
{
    public static string ExpandPath(string path)
    {
        path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (path == "~") return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path.StartsWith("~/") || path.StartsWith("~\\"))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
        return Path.GetFullPath(path);
    }

    // Inspect file names only; key contents are opened only during a requested connection.
    // A supplied home directory makes discovery testable without touching the user's keys.
    public static IReadOnlyList<string> Find(string? homeDirectory = null)
    {
        var homes = homeDirectory != null ? new[] { homeDirectory } : new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetEnvironmentVariable("HOME") ?? ""
        };
        var result = new List<string>();
        foreach (var home in homes.Where(h => !string.IsNullOrWhiteSpace(h)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var ssh = Path.Combine(home, ".ssh");
            if (!Directory.Exists(ssh)) continue;
            foreach (var name in new[] { "id_ed25519", "id_ecdsa", "id_rsa" })
            {
                var path = Path.Combine(ssh, name);
                if (File.Exists(path)) result.Add(path);
            }
            try
            {
                result.AddRange(Directory.EnumerateFiles(ssh).Where(path =>
                    new[] { ".pem", ".key", ".ppk" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                    .Order(StringComparer.OrdinalIgnoreCase).Take(16));
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
