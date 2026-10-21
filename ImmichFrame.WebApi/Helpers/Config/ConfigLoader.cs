using System.Collections;
using System.Text.Json;
using ImmichFrame.Core.Exceptions;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.WebApi.Models;
using YamlDotNet.Serialization;

namespace ImmichFrame.WebApi.Helpers.Config;

public class ConfigLoader(ILogger<ConfigLoader> _logger)
{
    private string FindConfigFile(string dir, params string[] fileNames)
    {
        if (!Directory.Exists(dir))
        {
            return Path.Combine(dir, fileNames.First());
        }

        return Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(f => fileNames.Any(name => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase)))
            ?? Path.Combine(dir, fileNames.First());
    }
    public IServerSettings LoadConfig(string configPath)
    {
        var config = LoadConfigRaw(configPath);
        config.Validate();
        return config;
    }
    private IServerSettings LoadConfigRaw(string configPath)
    {
        var jsonConfigPath = FindConfigFile(configPath, "Settings.json");
        if (File.Exists(jsonConfigPath))
        {
            try
            {
                // System.Text.Json ignores unknown keys, so a flat V1 file would otherwise "succeed"
                // as a V2 config with no General section and no accounts, and the V1 branch below
                // would never run.
                if (JsonHasV2Sections(jsonConfigPath))
                {
                    return LoadConfigJson<ServerSettings>(jsonConfigPath);
                }

                _logger.LogInformation("Settings.json has no 'General' or 'Accounts' section; reading it as the legacy flat format.");
            }
            catch (Exception e)
            {
                _logger.LogWarning("Failed to load config as current version JSON. ({errorMessage})", e.Message);
            }

            try
            {
                var v1 = LoadConfigJson<ServerSettingsV1>(jsonConfigPath);
                return new ServerSettingsV1Adapter(v1);
            }
            catch (Exception e)
            {
                _logger.LogWarning("Failed to load config as old JSON. ({errorMessage})", e.Message);
            }
        }

        var ymlConfigPath = FindConfigFile(configPath, "Settings.yml", "Settings.yaml");
        if (File.Exists(ymlConfigPath))
        {
            try
            {
                // Same reasoning as JSON: the deserializer ignores unmatched properties.
                if (YamlHasV2Sections(ymlConfigPath))
                {
                    return LoadConfigYaml<ServerSettings>(ymlConfigPath);
                }

                _logger.LogInformation("{File} has no 'General' or 'Accounts' section; reading it as the legacy flat format.", Path.GetFileName(ymlConfigPath));
            }
            catch (Exception e)
            {
                _logger.LogWarning("Failed to load config as current version YAML. ({errorMessage})", e.Message);
            }

            try
            {
                var v1 = LoadConfigYaml<ServerSettingsV1>(ymlConfigPath);
                return new ServerSettingsV1Adapter(v1);
            }
            catch (Exception e)
            {
                _logger.LogWarning("Failed to load config as old YAML. ({errorMessage})", e.Message);
            }
        }

        try
        {
            var v1 = LoadConfigFromDictionary<ServerSettingsV1>(Environment.GetEnvironmentVariables());
            return new ServerSettingsV1Adapter(v1);
        }
        catch (Exception e)
        {
            _logger.LogWarning("Failed to load config as env vars ({errorMessage})", e.Message);
        }

        throw new ImmichFrameException("Failed to load configuration");
    }

    // The V2 schema is recognised by its top-level sections, not by their values: a V2 file with an
    // empty Accounts list is a valid (if not yet useful) config the settings editor can complete,
    // whereas reading it as V1 would invent one account with an empty server URL.
    private static readonly string[] V2Sections = ["General", "Accounts"];

    internal static bool JsonHasV2Sections(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.ValueKind == JsonValueKind.Object &&
               doc.RootElement.EnumerateObject().Any(p => V2Sections.Contains(p.Name, StringComparer.Ordinal));
    }

    internal static bool YamlHasV2Sections(string path)
    {
        var root = new DeserializerBuilder().Build().Deserialize<Dictionary<string, object?>>(File.ReadAllText(path));
        return root != null && root.Keys.Any(k => V2Sections.Contains(k, StringComparer.Ordinal));
    }

    internal T LoadConfigFromDictionary<T>(IDictionary env) where T : IConfigSettable, new()
    {
        var config = new T();
        var propertiesSet = 0;

        foreach (var key in env.Keys)
        {
            if (key == null) continue;

            var propertyInfo = typeof(T).GetProperty(key.ToString() ?? string.Empty);

            if (propertyInfo != null)
            {
                config.SetValue(propertyInfo, env[key]?.ToString() ?? string.Empty);
                propertiesSet++;
            }
        }

        if (propertiesSet < 2)
        {
            throw new ImmichFrameException("No environment variables found");
        }

        return config;
    }

    internal T LoadConfigJson<T>(string configPath) where T : IConfigSettable, new()
    {
        try
        {
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                var doc = JsonDocument.Parse(json);
                return doc.Deserialize<T>() ?? throw new FileLoadException("Failed to load config file", configPath);
            }

            throw new FileNotFoundException(configPath);
        }
        catch (Exception ex)
        {
            throw new SettingsNotValidException($"Problem with parsing the settings: {ex.Message}", ex);
        }
    }
    internal T LoadConfigYaml<T>(string configPath) where T : IConfigSettable, new()
    {
        try
        {
            if (File.Exists(configPath))
            {
                var yml = File.ReadAllText(configPath);
                var deserializer = new DeserializerBuilder()
                    .IgnoreUnmatchedProperties()
                    .Build();
                return deserializer.Deserialize<T>(yml) ?? throw new FileLoadException("Failed to load config file", configPath);
            }

            throw new FileNotFoundException(configPath);
        }
        catch (Exception ex)
        {
            throw new SettingsNotValidException($"Problem with parsing the settings: {ex.Message}", ex);
        }
    }
}
