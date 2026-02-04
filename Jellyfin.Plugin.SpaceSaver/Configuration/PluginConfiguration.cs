using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SpaceSaver.Configuration;

/// <summary>
/// Minimum resolution options for video conversion.
/// </summary>
public enum MinimumResolution
{
    /// <summary>
    /// 720p (1280x720).
    /// </summary>
    P720,

    /// <summary>
    /// 1080p (1920x1080).
    /// </summary>
    P1080,

    /// <summary>
    /// 4K (3840x2160).
    /// </summary>
    P4K
}

/// <summary>
/// H265 encoding preset options.
/// </summary>
public enum H265Preset
{
    /// <summary>
    /// Ultra fast encoding.
    /// </summary>
    Ultrafast,

    /// <summary>
    /// Super fast encoding.
    /// </summary>
    Superfast,

    /// <summary>
    /// Very fast encoding.
    /// </summary>
    Veryfast,

    /// <summary>
    /// Faster encoding.
    /// </summary>
    Faster,

    /// <summary>
    /// Fast encoding.
    /// </summary>
    Fast,

    /// <summary>
    /// Medium encoding (balanced).
    /// </summary>
    Medium,

    /// <summary>
    /// Slow encoding (better compression).
    /// </summary>
    Slow,

    /// <summary>
    /// Slower encoding.
    /// </summary>
    Slower,

    /// <summary>
    /// Very slow encoding (best compression).
    /// </summary>
    Veryslow
}

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        MinResolution = MinimumResolution.P720;
        ExcludedCodecs = new List<string> { "hevc", "av1" };
        Preset = H265Preset.Medium;
        CRF = 23;
        ReplaceOriginalFile = false;
        EnableScheduledTask = true;
        MaxConcurrentConversions = 1;
    }

    /// <summary>
    /// Gets or sets the minimum resolution for conversion.
    /// </summary>
    public MinimumResolution MinResolution { get; set; }

    /// <summary>
    /// Gets or sets the list of codecs to exclude from conversion.
    /// </summary>
    public IReadOnlyList<string> ExcludedCodecs { get; set; }

    /// <summary>
    /// Gets or sets the H265 encoding preset.
    /// </summary>
    public H265Preset Preset { get; set; }

    /// <summary>
    /// Gets or sets the CRF (Constant Rate Factor) value for H265 encoding (0-51, lower is better quality).
    /// </summary>
    public int CRF { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to replace the original file after successful conversion.
    /// </summary>
    public bool ReplaceOriginalFile { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the scheduled task is enabled.
    /// </summary>
    public bool EnableScheduledTask { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of concurrent video conversions.
    /// </summary>
    public int MaxConcurrentConversions { get; set; }
}
