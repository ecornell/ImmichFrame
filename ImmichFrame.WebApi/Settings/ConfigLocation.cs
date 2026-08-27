namespace ImmichFrame.WebApi.Settings;

/// <summary>
/// The resolved configuration directory. Promoted out of a local in <c>Program.cs</c> so the write
/// path and the admin API can report and use the same location the loader read from.
/// </summary>
public sealed record ConfigLocation(string Directory)
{
    public string SettingsJsonPath => Path.Combine(Directory, "Settings.json");

    /// <summary>
    /// True when a settings file could actually be written here. The standard container runs as a
    /// non-root user against a root-owned <c>/app</c>, so this is frequently false.
    /// </summary>
    public bool CanPersist()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);

            var probe = Path.Combine(Directory, $".immichframe-write-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Config files that <c>ConfigLoader</c> would rank below <c>Settings.json</c>.</summary>
    public IEnumerable<string> ShadowedFiles()
    {
        if (!System.IO.Directory.Exists(Directory)) yield break;

        foreach (var name in new[] { "Settings.yml", "Settings.yaml" })
        {
            var match = System.IO.Directory
                .EnumerateFiles(Directory, "*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));

            if (match is not null) yield return match;
        }
    }
}
