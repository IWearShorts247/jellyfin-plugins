using System.Diagnostics;
using System.Globalization;
using System.Text;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Showtime.Trivia;

/// <summary>
/// Renders trivia reels (slides + music) into the content library with the server's own ffmpeg.
/// </summary>
public class RenderTriviaTask : IScheduledTask
{
    private static readonly string[] MusicExtensions = { ".mp3", ".flac", ".m4a", ".aac", ".ogg", ".opus", ".wav" };

    private readonly ILibraryManager _libraryManager;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly ContentIndex _content;
    private readonly ILogger<RenderTriviaTask> _logger;

    public RenderTriviaTask(ILibraryManager libraryManager, IMediaEncoder mediaEncoder, ContentIndex content, ILogger<RenderTriviaTask> logger)
    {
        _libraryManager = libraryManager;
        _mediaEncoder = mediaEncoder;
        _content = content;
        _logger = logger;
    }

    public string Name => "Render trivia reels";

    public string Key => "ShowtimeRenderTrivia";

    public string Description => "Builds fresh Showtime trivia reels from your slide packs, music and movie library.";

    public string Category => "Showtime";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo { Type = TaskTriggerInfoType.DailyTrigger, TimeOfDayTicks = TimeSpan.FromHours(4).Ticks };
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance!.Configuration;
        var root = _content.GetLibraryRoot();
        if (root is null)
        {
            _logger.LogWarning("Showtime: no content library configured; skipping trivia render");
            return;
        }

        var reelDir = Fallback(config.TriviaReelPath, Path.Combine(root, "Trivia Reels"));
        var slideDir = Fallback(config.TriviaSourcePath, Path.Combine(root, "Trivia"));
        var musicDir = Fallback(config.MusicPath, Path.Combine(root, "Music"));
        Directory.CreateDirectory(reelDir);

        var source = new TriviaSource(_libraryManager);
        var packSets = source.FromFolder(slideDir, config.QuestionSeconds, config.ClueSeconds, config.AnswerSeconds);
        var reelSeconds = Math.Max(1, config.TriviaReelMinutes) * 60;
        var reelCount = Math.Max(1, config.TriviaReelCount);
        var neededSets = (reelSeconds * reelCount / Math.Max(10, config.QuestionSeconds + config.AnswerSeconds)) + 5;
        var librarySets = config.GenerateLibraryTrivia
            ? source.FromLibrary(root, Math.Max(0, neededSets - packSets.Count), config.QuestionSeconds, config.ClueSeconds, config.AnswerSeconds)
            : new List<SlideSet>();

        var all = packSets.Concat(librarySets).ToList();
        _logger.LogInformation("Showtime: {Pack} slide-pack items, {Library} generated items", packSets.Count, librarySets.Count);
        if (all.Count == 0)
        {
            _logger.LogWarning("Showtime: no trivia found in {Dir} and library trivia is off or empty", slideDir);
            return;
        }

        var music = Directory.Exists(musicDir)
            ? Directory.EnumerateFiles(musicDir, "*.*", SearchOption.AllDirectories)
                .Where(f => MusicExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)).ToList()
            : new List<string>();

        // Deal sets round-robin from a shuffled deck so reels don't repeat each other until the deck runs out.
        var deck = new Queue<SlideSet>(all.OrderBy(_ => Random.Shared.Next()));
        for (var reel = 1; reel <= reelCount; reel++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sets = new List<SlideSet>();
            var seconds = 0;
            while (seconds < reelSeconds)
            {
                if (deck.Count == 0)
                {
                    foreach (var s in all.OrderBy(_ => Random.Shared.Next()))
                    {
                        deck.Enqueue(s);
                    }
                }

                var next = deck.Dequeue();
                sets.Add(next);
                seconds += next.Seconds;
                if (sets.Count > all.Count && seconds > reelSeconds / 2)
                {
                    break; // Small packs: don't loop the same slides within one reel.
                }
            }

            var output = Path.Combine(reelDir, $"Trivia Reel {reel:00}.mp4");
            await RenderReel(sets, music, output, cancellationToken).ConfigureAwait(false);
            progress.Report(100.0 * reel / reelCount);
        }

        foreach (var stale in Directory.EnumerateFiles(reelDir, "Trivia Reel *.mp4"))
        {
            if (int.TryParse(Path.GetFileNameWithoutExtension(stale)[12..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > reelCount)
            {
                File.Delete(stale);
            }
        }

        _content.Invalidate();
        _libraryManager.QueueLibraryScan();
    }

    private static string Fallback(string configured, string fallback)
        => string.IsNullOrWhiteSpace(configured) ? fallback : configured;

    private static string Escape(string path) => path.Replace("'", "'\\''", StringComparison.Ordinal);

    private async Task RenderReel(List<SlideSet> sets, List<string> music, string output, CancellationToken cancellationToken)
    {
        var work = Path.Combine(Plugin.Instance!.DataFolderPath, "render", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            // 1. Draw every frame to a same-size JPEG and write an ffmpeg concat list.
            var list = new StringBuilder();
            string? last = null;
            var total = 0;
            foreach (var frame in sets.SelectMany(s => s.Frames))
            {
                string path;
                try
                {
                    path = frame.Render(work);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Showtime: skipped a trivia frame that failed to render");
                    continue;
                }

                list.Append(CultureInfo.InvariantCulture, $"file '{Escape(path)}'\nduration {frame.Seconds}\n");
                last = path;
                total += frame.Seconds;
            }

            if (last is null)
            {
                return;
            }

            list.Append(CultureInfo.InvariantCulture, $"file '{Escape(last)}'\n"); // concat demuxer needs the last frame twice
            var listPath = Path.Combine(work, "slides.txt");
            await File.WriteAllTextAsync(listPath, list.ToString(), cancellationToken).ConfigureAwait(false);

            // 2. Encode: stills at 24 fps, shuffled music bed faded in/out (or silence).
            var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-f", "concat", "-safe", "0", "-i", listPath };
            var tracks = music.OrderBy(_ => Random.Shared.Next()).Take(12).ToList();
            var filter = new StringBuilder("[0:v]fps=24,format=yuv420p,setsar=1[v];");
            if (tracks.Count == 0)
            {
                args.AddRange(new[] { "-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo" });
                filter.Append("[1:a]anull[a]");
            }
            else
            {
                for (var i = 0; i < tracks.Count; i++)
                {
                    args.AddRange(new[] { "-i", tracks[i] });
                    filter.Append(CultureInfo.InvariantCulture, $"[{i + 1}:a]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo[m{i}];");
                }

                filter.Append(string.Concat(Enumerable.Range(0, tracks.Count).Select(i => $"[m{i}]")));
                filter.Append(CultureInfo.InvariantCulture, $"concat=n={tracks.Count}:v=0:a=1,apad,afade=t=in:d=2,afade=t=out:st={Math.Max(0, total - 4)}:d=4,volume=0.8[a]");
            }

            var temp = Path.Combine(work, "reel.mp4");
            args.AddRange(new[]
            {
                "-filter_complex", filter.ToString(), "-map", "[v]", "-map", "[a]",
                "-t", total.ToString(CultureInfo.InvariantCulture),
                "-c:v", "libx264", "-preset", "veryfast", "-tune", "stillimage", "-crf", "20", "-g", "48",
                "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", temp,
            });

            await RunFfmpeg(args, cancellationToken).ConfigureAwait(false);
            File.Move(temp, output, overwrite: true);
            _logger.LogInformation("Showtime: rendered {Output} ({Seconds}s, {Sets} items)", output, total, sets.Count);
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private async Task RunFfmpeg(List<string> args, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(_mediaEncoder.EncoderPath)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start ffmpeg");
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        _ = process.StandardOutput.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg exited {process.ExitCode}: {await stderr.ConfigureAwait(false)}");
        }
    }
}
