using System.Globalization;

namespace MemeSearcher.Infrastructure.Ffmpeg;

public sealed record VideoRenderInput(string MediaPath, double StartSeconds, double EndSeconds);

public sealed record VideoCaption(string Text, string Position = "bottom");

public sealed record VideoRenderPlan(
    IReadOnlyList<string> Arguments,
    IReadOnlyList<VideoRenderInput> Inputs,
    string OutputPath,
    VideoCaption? Caption);

/// <summary>
/// Builds the deterministic ffmpeg argument plan for the video-first composer without starting a
/// process or touching search/domain state. Execution belongs on the existing ffmpeg locator and
/// ProcessRunner seam; this type only owns validation, component order and render arguments.
/// </summary>
public static class VideoComposerRenderPlanner
{
    public static VideoRenderPlan Create(
        IReadOnlyList<VideoRenderInput> inputs,
        string outputPath,
        VideoCaption? caption = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        if (inputs.Count == 0)
        {
            throw new ArgumentException("At least one render input is required.", nameof(inputs));
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("An explicit output path is required.", nameof(outputPath));
        }

        if (caption is not null)
        {
            if (string.IsNullOrWhiteSpace(caption.Text))
            {
                throw new ArgumentException("Caption text cannot be empty when a caption is supplied.", nameof(caption));
            }

            if (caption.Position is not ("top" or "center" or "bottom"))
            {
                throw new ArgumentException("Caption position must be top, center, or bottom.", nameof(caption));
            }
        }

        var normalizedOutput = Path.GetFullPath(outputPath);
        var normalizedInputs = new VideoRenderInput[inputs.Count];
        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            if (string.IsNullOrWhiteSpace(input.MediaPath))
            {
                throw new ArgumentException($"Render input {i + 1} has no media path.", nameof(inputs));
            }

            if (!double.IsFinite(input.StartSeconds) || !double.IsFinite(input.EndSeconds)
                || input.StartSeconds < 0 || input.EndSeconds <= input.StartSeconds)
            {
                throw new ArgumentException($"Render input {i + 1} has an invalid time range.", nameof(inputs));
            }

            var normalizedPath = Path.GetFullPath(input.MediaPath);
            if (PathEquals(normalizedPath, normalizedOutput))
            {
                throw new ArgumentException("Render output must not overwrite a source media file.", nameof(outputPath));
            }

            normalizedInputs[i] = input with { MediaPath = normalizedPath };
        }

        var arguments = new List<string> { "-y" };
        foreach (var input in normalizedInputs)
        {
            arguments.Add("-ss");
            arguments.Add(FormatSeconds(input.StartSeconds));
            arguments.Add("-to");
            arguments.Add(FormatSeconds(input.EndSeconds));
            arguments.Add("-i");
            arguments.Add(input.MediaPath);
        }

        var filters = new List<string>();
        string videoMap;
        string audioMap;

        if (normalizedInputs.Length == 1)
        {
            videoMap = "0:v:0";
            audioMap = "0:a:0?";
        }
        else
        {
            var concatInputs = string.Concat(Enumerable.Range(0, normalizedInputs.Length)
                .Select(i => $"[{i}:v:0][{i}:a:0]"));
            filters.Add($"{concatInputs}concat=n={normalizedInputs.Length}:v=1:a=1[vbase][abase]");
            videoMap = "[vbase]";
            audioMap = "[abase]";
        }

        if (caption is not null)
        {
            var source = normalizedInputs.Length == 1 ? "[0:v:0]" : "[vbase]";
            var output = "[vout]";
            var y = caption.Position switch
            {
                "top" => "h*0.06",
                "center" => "(h-text_h)/2",
                _ => "h-text_h-(h*0.06)",
            };
            filters.Add($"{source}drawtext=text='{EscapeDrawText(caption.Text)}':x=(w-text_w)/2:y={y}{output}");
            videoMap = output;
        }

        if (filters.Count > 0)
        {
            arguments.Add("-filter_complex");
            arguments.Add(string.Join(";", filters));
        }

        arguments.Add("-map");
        arguments.Add(videoMap);
        arguments.Add("-map");
        arguments.Add(audioMap);
        arguments.Add("-c:v");
        arguments.Add("libx264");
        arguments.Add("-c:a");
        arguments.Add("aac");
        arguments.Add(normalizedOutput);

        return new VideoRenderPlan(arguments, normalizedInputs, normalizedOutput, caption);
    }

    private static string FormatSeconds(double value) => value.ToString("0.#########", CultureInfo.InvariantCulture);

    private static bool PathEquals(string left, string right) =>
        string.Equals(left, right, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string EscapeDrawText(string value)
    {
        // drawtext has its own filter grammar. This escaping is for one ArgumentList token; it is
        // not shell escaping and never makes the caption executable.
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal)
            .Replace(":", "\\:", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal)
            .Replace(";", "\\;", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal);
    }
}
