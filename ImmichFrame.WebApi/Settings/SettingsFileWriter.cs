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
        var backupTmp = bak + ".tmp";

        try
        {
            DeleteIfExists(tmp);
            DeleteIfExists(backupTmp);
            WriteOwnerOnlyFile(tmp, stream =>
            {
                using var writer = new StreamWriter(stream,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);
                writer.Write(json);
                writer.Flush();
            });

            if (File.Exists(target))
            {
                // Build and permission the backup before committing the new target. If backup
                // preparation fails, Settings.json has not been changed.
                WriteOwnerOnlyFile(backupTmp, stream =>
                {
                    using var source = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read);
                    source.CopyTo(stream);
                });
                File.Move(backupTmp, bak, overwrite: true);
            }

            // tmp lives beside the target, so this rename is atomic and carries its owner-only mode
            // into place without a failure-prone chmod after the content commit.
            File.Move(tmp, target, overwrite: true);

            logger.LogInformation("Settings written to {Path}", target);
        }
        finally
        {
            TryDelete(tmp, logger);
            TryDelete(backupTmp, logger);
        }
    }

    private static void WriteOwnerOnlyFile(string path, Action<FileStream> write)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using var stream = new FileStream(path, options);
        write(stream);
        stream.Flush(flushToDisk: true);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static void TryDelete(string path, ILogger logger)
    {
        try
        {
            DeleteIfExists(path);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not clean up temporary settings file {Path}", path);
        }
    }
}
