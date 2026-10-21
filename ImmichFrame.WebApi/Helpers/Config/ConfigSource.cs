namespace ImmichFrame.WebApi.Helpers.Config;

public enum ConfigSource
{
    Unknown,
    Json,
    Yaml,
    EnvironmentVariables
}

/// <summary>
/// Records where the startup configuration was read from, for warnings in the settings editor.
/// </summary>
/// <remarks>
/// Set by the <c>IServerSettings</c> registration in <c>Program.cs</c>. Kept as its own singleton
/// rather than calling the loader again, because the test fixtures replace that registration and
/// must not trigger a real load. Stays <see cref="ConfigSource.Unknown"/> in that case.
/// </remarks>
public sealed class ConfigSourceInfo
{
    public ConfigSource Source { get; set; } = ConfigSource.Unknown;
}
