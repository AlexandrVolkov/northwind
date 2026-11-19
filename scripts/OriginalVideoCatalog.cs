using Godot;

namespace Northwind;

/// <summary>Prepared Theora movies; original SMK and ffmpeg are not runtime dependencies.</summary>
public sealed class OriginalVideoCatalog
{
    public const string Root = "res://assets/imported/video/";
    public int FormatVersion { get; set; }
    public OriginalVideoEntry[] Videos { get; set; } = [];
    public static OriginalVideoCatalog Load() => GameData.Read<OriginalVideoCatalog>(Root + "manifest.json");
    public static VideoStreamTheora Stream(OriginalVideoEntry movie) => GD.Load<VideoStreamTheora>(Root + movie.File)
        ?? throw new InvalidOperationException($"Missing prepared video: {movie.File}");
}

public sealed class OriginalVideoEntry
{
    public string Source { get; set; } = "";
    public string SourceSha256 { get; set; } = "";
    public string File { get; set; } = "";
    public string Role { get; set; } = "";
    public string OutputSha256 { get; set; } = "";
    public long OutputBytes { get; set; }
    public int PresentationFrames { get; set; }
    public OriginalVideoHeader Header { get; set; } = new();
    public OriginalVideoPlayback Playback { get; set; } = new();
}

public sealed class OriginalVideoPlayback
{
    public int[] DisplayRect { get; set; } = [];
    public int CanvasWidth { get; set; }
    public int CanvasHeight { get; set; }
    public int RepeatCount { get; set; }
    public bool SkipOnKey { get; set; }
}

public sealed class OriginalVideoHeader
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int Frames { get; set; }
    public string FrameRate { get; set; } = "";
    public double DurationSeconds { get; set; }
}
