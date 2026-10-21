using System.Globalization;

namespace ImmichFrame.WebApi;

/// <summary>Formats the photo date shown by clients that use <c>RandomImageAndInfo</c>.</summary>
/// <remarks>
/// Defensive on purpose: Language and PhotoDateFormat can be saved through the settings editor (and
/// predate its validation), and a bad value must cost the date string, not the whole response.
/// </remarks>
public static class AssetDateFormatting
{
    public const string DefaultPhotoDateFormat = "MM/dd/yyyy";

    /// <summary>The format string as passed to .NET (a doubled quote is an escaped quote).</summary>
    public static string ToDotNetFormat(string format) => format.Replace("''", "\\'");

    public static string FormatPhotoDate(DateTimeOffset date, string? photoDateFormat, string? language)
    {
        var culture = ResolveCulture(language);
        var format = string.IsNullOrEmpty(photoDateFormat) ? DefaultPhotoDateFormat : photoDateFormat;

        try
        {
            return date.ToString(ToDotNetFormat(format), culture);
        }
        catch (FormatException)
        {
            return date.ToString(DefaultPhotoDateFormat, culture);
        }
    }

    private static CultureInfo ResolveCulture(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return CultureInfo.InvariantCulture;
        try
        {
            return CultureInfo.GetCultureInfo(language);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}
