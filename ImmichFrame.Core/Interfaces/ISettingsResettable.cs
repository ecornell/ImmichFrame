namespace ImmichFrame.Core.Interfaces;

/// <summary>
/// Implemented by long-lived services that cache results derived from configuration.
/// </summary>
/// <remarks>
/// These services read their settings live, so the cache is the only thing standing between a saved
/// change and a visible one. Without this the weather panel would lag a settings change by up to
/// 5 minutes and calendars by up to 15.
/// </remarks>
public interface ISettingsResettable
{
    void ResetCaches();
}
