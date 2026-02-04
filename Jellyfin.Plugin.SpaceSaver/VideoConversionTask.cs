using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.SpaceSaver.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SpaceSaver;

/// <summary>
/// Scheduled task for converting videos to H265/HEVC codec.
/// </summary>
public class VideoConversionTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly ILogger<VideoConversionTask> _logger;
    private readonly IFileSystem _fileSystem;

    /// <summary>
    /// Initializes a new instance of the <see cref="VideoConversionTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="mediaEncoder">Instance of the <see cref="IMediaEncoder"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger"/> interface.</param>
    /// <param name="fileSystem">Instance of the <see cref="IFileSystem"/> interface.</param>
    public VideoConversionTask(
        ILibraryManager libraryManager,
        IMediaEncoder mediaEncoder,
        ILogger<VideoConversionTask> logger,
        IFileSystem fileSystem)
    {
        _libraryManager = libraryManager;
        _mediaEncoder = mediaEncoder;
        _logger = logger;
        _fileSystem = fileSystem;
    }

    /// <inheritdoc />
    public string Name => "Convert Videos to H265";

    /// <inheritdoc />
    public string Key => "SpaceSaverVideoConversion";

    /// <inheritdoc />
    public string Description => "Scans the library for videos matching criteria and converts them to H265 codec for space savings.";

    /// <inheritdoc />
    public string Category => "Library";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = GetConfig();

        if (!config.EnableScheduledTask)
        {
            _logger.LogInformation("Video conversion task is disabled in configuration");
            return;
        }

        _logger.LogInformation("Starting video conversion task");

        var videos = await GetEligibleVideosAsync(cancellationToken);
        _logger.LogInformation("Found {Count} videos eligible for conversion", videos.Count);

        if (videos.Count == 0)
        {
            progress?.Report(100);
            return;
        }

        int processedCount = 0;
        int successCount = 0;
        int failedCount = 0;

        foreach (var video in videos)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                _logger.LogInformation("Processing: {Path}", video.Path);
                var success = await ConvertVideoAsync(video, cancellationToken);

                if (success)
                {
                    successCount++;
                    _logger.LogInformation("Successfully converted: {Path}", video.Path);
                }
                else
                {
                    failedCount++;
                    _logger.LogWarning("Failed to convert: {Path}", video.Path);
                }
            }
            catch (Exception ex)
            {
                failedCount++;
                _logger.LogError(ex, "Error converting video: {Path}", video.Path);
            }

            processedCount++;
            progress?.Report((double)processedCount / videos.Count * 100);
        }

        _logger.LogInformation(
            "Video conversion task completed. Processed: {Processed}, Success: {Success}, Failed: {Failed}",
            processedCount,
            successCount,
            failedCount);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return new[]
        {
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerType.Daily,
                TimeOfDayTicks = TimeSpan.FromHours(2).Ticks
            }
        };
    }

    private PluginConfiguration GetConfig()
    {
        return Plugin.Instance?.Configuration ?? new PluginConfiguration();
    }

    private async Task<List<Video>> GetEligibleVideosAsync(CancellationToken cancellationToken)
    {
        var eligibleVideos = new List<Video>();
        var config = GetConfig();

        var query = new InternalItemsQuery
        {
            MediaTypes = new[] { MediaType.Video },
            IsVirtualItem = false,
            Recursive = true,
            SourceTypes = new[] { SourceType.Library },
            DtoOptions = new DtoOptions(true)
        };

        var pageSize = 100;
        var totalCount = _libraryManager.GetCount(query);

        for (int startIndex = 0; startIndex < totalCount; startIndex += pageSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            query.StartIndex = startIndex;
            query.Limit = pageSize;

            var items = _libraryManager.GetItemList(query).OfType<Video>();

            foreach (var video in items)
            {
                if (string.IsNullOrEmpty(video.Path) || !File.Exists(video.Path))
                {
                    continue;
                }

                try
                {
                    var mediaInfo = await GetMediaInfoAsync(video, cancellationToken);
                    if (IsEligibleForConversion(mediaInfo, config))
                    {
                        eligibleVideos.Add(video);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get media info for: {Path}", video.Path);
                }
            }
        }

        return eligibleVideos;
    }

    private async Task<MediaBrowser.Model.MediaInfo.MediaInfo> GetMediaInfoAsync(Video video, CancellationToken cancellationToken)
    {
        var mediaInfo = await _mediaEncoder.GetMediaInfo(
            new MediaInfoRequest
            {
                MediaType = DlnaProfileType.Video,
                ExtractChapters = false,
                MediaSource = new MediaBrowser.Model.Dto.MediaSourceInfo
                {
                    Path = video.Path,
                    Protocol = MediaProtocol.File,
                    VideoType = video.VideoType ?? VideoType.VideoFile
                }
            },
            cancellationToken);

        return mediaInfo;
    }

    private bool IsEligibleForConversion(MediaBrowser.Model.MediaInfo.MediaInfo mediaInfo, PluginConfiguration config)
    {
        var videoStream = mediaInfo.MediaStreams?.FirstOrDefault(s => s.Type == MediaStreamType.Video);
        if (videoStream == null)
        {
            return false;
        }

        // Check if codec is in excluded list
        var codec = videoStream.Codec?.ToLowerInvariant();
        if (config.ExcludedCodecs.Any(c => c.Equals(codec, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // Check resolution
        var height = videoStream.Height ?? 0;
        var minHeight = config.MinResolution switch
        {
            MinimumResolution.P720 => 720,
            MinimumResolution.P1080 => 1080,
            MinimumResolution.P4K => 2160,
            _ => 720
        };

        return height >= minHeight;
    }

    private async Task<bool> ConvertVideoAsync(Video video, CancellationToken cancellationToken)
    {
        var config = GetConfig();
        var ffmpegPath = _mediaEncoder.EncoderPath;

        if (string.IsNullOrEmpty(ffmpegPath) || !File.Exists(ffmpegPath))
        {
            _logger.LogError("FFmpeg not found at: {Path}", ffmpegPath);
            return false;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "jellyfin-spacesaver");
        Directory.CreateDirectory(tempDir);

        var tempOutputPath = Path.Combine(tempDir, $"{Guid.NewGuid()}.mkv");

        try
        {
            // Build ffmpeg arguments
            var preset = config.Preset.ToString().ToLowerInvariant();
            var crf = Math.Clamp(config.CRF, 0, 51);

            var arguments = $"-i \"{video.Path}\" -c:v libx265 -preset {preset} -crf {crf} " +
                          $"-c:a copy -c:s copy -movflags +faststart \"{tempOutputPath}\"";

            _logger.LogDebug("FFmpeg command: {Command} {Args}", ffmpegPath, arguments);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = tempDir
                }
            };

            process.Start();

            // Read output for logging
            var errorOutput = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                _logger.LogError("FFmpeg conversion failed with exit code {Code}. Error: {Error}", process.ExitCode, errorOutput);
                return false;
            }

            // Verify output file exists and has reasonable size
            if (!File.Exists(tempOutputPath))
            {
                _logger.LogError("Output file not created: {Path}", tempOutputPath);
                return false;
            }

            var originalSize = new FileInfo(video.Path).Length;
            var newSize = new FileInfo(tempOutputPath).Length;

            _logger.LogInformation(
                "Conversion complete. Original: {OriginalSize} bytes, New: {NewSize} bytes, Savings: {Savings}%",
                originalSize,
                newSize,
                (originalSize - newSize) * 100.0 / originalSize);

            // Replace original file if configured
            if (config.ReplaceOriginalFile)
            {
                var originalPath = video.Path;
                var backupPath = originalPath + ".backup";

                // Create backup
                File.Move(originalPath, backupPath);

                try
                {
                    // Move new file to original location
                    File.Move(tempOutputPath, originalPath);

                    // Delete backup on success
                    File.Delete(backupPath);

                    // Refresh library item
                    await _libraryManager.UpdateItemAsync(
                        video,
                        video.GetParent(),
                        ItemUpdateType.MetadataImport,
                        cancellationToken);

                    _logger.LogInformation("Original file replaced: {Path}", originalPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to replace original file, restoring backup");

                    // Restore backup
                    if (File.Exists(backupPath))
                    {
                        if (File.Exists(originalPath))
                        {
                            File.Delete(originalPath);
                        }

                        File.Move(backupPath, originalPath);
                    }

                    throw;
                }
            }
            else
            {
                _logger.LogInformation("Converted file saved to: {Path}", tempOutputPath);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Conversion failed for: {Path}", video.Path);

            // Clean up temp file
            if (File.Exists(tempOutputPath))
            {
                try
                {
                    File.Delete(tempOutputPath);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }

            return false;
        }
    }
}
