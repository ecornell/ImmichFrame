using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ImmichFrame.WebApi.Models;

namespace ImmichFrame.WebApi.Settings;

/// <summary>
/// Persists settings to <c>Settings.json</c>.
/// </summary>
public sealed class SettingsFileWriter(ConfigLocation location, ILogger<SettingsFileWriter> logger)
{
    /// <remarks>
    /// These must round-trip through <c>ConfigLoader.LoadConfigJson</c>, which deserializes with
    /// <em>default</em> <see cref="JsonSerializerOptions"/> — case-sensitive PascalCase. Using
    /// <see cref="JsonSerializerDefaults.Web"/> here would emit camelCase, which silently fails to
    /// bind on the next load and falls through to the V1/env-var path instead of erroring.
    /// </remarks>
    private static readonly JsonSerializerOptions FileOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string TargetPath => location.SettingsJsonPath;

    /// <summary>
    /// Writes the settings atomically: a full temp file is flushed to disk and then swapped into
    /// place, so a crash mid-write can never leave a truncated config. The previous file is kept
    /// alongside as <c>Settings.json.bak</c>.
    /// </summary>
    public void Write(ServerSettings live)
    {
        Directory.CreateDirectory(location.Directory);

        var json = JsonSerializer.Serialize(SettingsFileProjection.ToFileModel(live), FileOptions);

        var target = location.SettingsJsonPath;
        var tmp = target + ".tmp";
        var bak = target + ".bak";

        try
        {
            File.WriteAllText(tmp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            using (var fs = new FileStream(tmp, FileMode.Open, FileAccess.ReadWrite))
            {
                fs.Flush(flushToDisk: true);
            }

            if (File.Exists(target))
            {
                try
                {
                    File.Replace(tmp, target, bak, ignoreMetadataErrors: true);
                }
                catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or UnauthorizedAccessException)
                {
                    // File.Replace is unsupported on some bind-mounted filesystems (overlayfs, NFS,
                    // SMB). A plain rename is still atomic there.
                    logger.LogDebug(ex, "File.Replace unavailable, falling back to copy + move.");
                    File.Copy(target, bak, overwrite: true);
                    File.Move(tmp, target, overwrite: true);
                }
            }
            else
            {
                File.Move(tmp, target);
            }

            logger.LogInformation("Settings written to {Path}", target);
        }
        finally
        {
            try
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not clean up temporary settings file {Path}", tmp);
            }
        }
    }
}
