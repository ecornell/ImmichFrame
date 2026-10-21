namespace ImmichFrame.Core.Logic;

/// <summary>Where <c>DownloadImages</c> keeps its cached image files.</summary>
public sealed record ImageCacheLocation(string Directory)
{
    /// <summary>
    /// Fallback when nothing is registered. The app registers a directory under the config folder
    /// instead, because the application directory is root-owned in the container image.
    /// </summary>
    public static ImageCacheLocation Default { get; } =
        new(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ImageCache"));
}
