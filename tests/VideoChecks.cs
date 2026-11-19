using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private async Task VideoChecks()
    {
        var catalog = OriginalVideoCatalog.Load();
        Check(catalog.FormatVersion == 1 && catalog.Videos.Length == 2, "Both prepared movies are bundled");
        var player = new VideoStreamPlayer { Expand = true, Size = new Vector2(640, 480), BufferingMsec = 100 };
        AddChild(player);
        foreach (var movie in catalog.Videos)
        {
            var placement = movie.Playback;
            Check(placement.DisplayRect.Length == 4 && placement.DisplayRect[2] == movie.Header.Width && placement.DisplayRect[3] == movie.Header.Height,
                "Original video placement preserves native dimensions");
            Check(placement is { CanvasWidth: 640, CanvasHeight: 480, RepeatCount: 1, SkipOnKey: true }, "Source playback setup is a single skippable movie on the original canvas");
            using var source = Godot.FileAccess.Open(OriginalVideoCatalog.Root + movie.File, Godot.FileAccess.ModeFlags.Read);
            Check((long)source.GetLength() == movie.OutputBytes && Convert.ToHexStringLower(SHA256.HashData(source.GetBuffer((long)source.GetLength()))) == movie.OutputSha256,
                $"{movie.File}: prepared movie matches converter hash");
            using (var stream = OriginalVideoCatalog.Stream(movie))
            {
                player.Stream = stream; player.Play();
                await ToSignal(GetTree().CreateTimer(.35), SceneTreeTimer.SignalName.Timeout);
                Check(player.IsPlaying() && player.StreamPosition > .1, $"{movie.File}: Godot decodes and advances Theora playback");
                Check(Math.Abs(player.GetStreamLength() - movie.Header.DurationSeconds) < .1, "Theora duration matches original frame timeline");
                using var texture = player.GetVideoTexture();
                Check(texture != null && texture.GetWidth() == movie.Header.Width && texture.GetHeight() == movie.Header.Height, "Decoded video retains original dimensions");
                player.Paused = true; double position = player.StreamPosition;
                await ToSignal(GetTree().CreateTimer(.12), SceneTreeTimer.SignalName.Timeout);
                Check(Math.Abs(player.StreamPosition - position) < .001, "Pause freezes video timestamp");
                player.Paused = false;
                await ToSignal(GetTree().CreateTimer(.15), SceneTreeTimer.SignalName.Timeout);
                Check(player.StreamPosition > position, "Resume advances video timestamp");
                if (DisplayServer.GetName() != "headless")
                {
                    double wait = 5 - player.StreamPosition;
                    if (wait > 0) await ToSignal(GetTree().CreateTimer(wait), SceneTreeTimer.SignalName.Timeout);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var frame = texture!.GetImage();
                    Check(frame != null && !frame.IsEmpty(), "Graphical renderer exposes decoded video pixels");
                    DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://build"));
                    frame!.SavePng($"res://build/{movie.Role}-decoded.png");
                    System.IO.File.WriteAllText(ProjectSettings.GlobalizePath($"res://build/{movie.Role}-decoded.json"),
                        JsonSerializer.Serialize(new { source = movie.Source, position = player.StreamPosition }));
                }
                player.Stop(); Check(!player.IsPlaying(), "Stop releases movie playback"); player.Stream = null;
            }
        }
        RemoveChild(player); player.Free();
        await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout);
    }
}
