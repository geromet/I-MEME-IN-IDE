using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemeSearcher.Core.Search;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Services;

namespace MemeSearcher.ViewModels;

/// <summary>One clip's contribution to a composite result, with the media's display title resolved (addendum §16/§22).</summary>
public class CompositeComponentRowViewModel(CompositeMatchComponent component, string mediaTitle, string? mediaPath)
{
    public Guid MediaId { get; } = component.MediaId;
    public string? MediaPath { get; } = mediaPath;
    public string MediaTitle { get; } = mediaTitle;
    public double StartSeconds { get; } = component.StartSeconds;
    public double EndSeconds { get; } = component.EndSeconds;
    public string TimeRangeDisplay { get; } = $"{FormatTimestamp(component.StartSeconds)} - {FormatTimestamp(component.EndSeconds)}";
    public string SourceText { get; } = component.SourceText;
    public string Ipa { get; } = component.Ipa;
    public string ScoreDisplay { get; } = $"{component.Score:P0}";

    private static string FormatTimestamp(double seconds) => TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss\.ff");
}

/// <summary>
/// Display-formatted projection of a CompositeSearchResult (handoff §45, addendum §16/§21-22)
/// plus (Milestone 5, addendum §33) exporting the assembled clip - stitching every component's
/// source file together via FFmpegClipExtractor.ExtractCompositeAsync.
/// </summary>
public partial class CompositeSearchResultRowViewModel : ObservableObject
{
    private readonly FFmpegClipExtractor _clipExtractor;
    private readonly IFilePickerService _filePicker;

    public string ScoreDisplay { get; }
    public string SourceCountDisplay { get; }
    public IReadOnlyList<CompositeComponentRowViewModel> Components { get; }

    [ObservableProperty]
    private string _exportStatus = "";

    public CompositeSearchResultRowViewModel(
        CompositeSearchResult result,
        IReadOnlyDictionary<Guid, string> mediaTitles,
        IReadOnlyDictionary<Guid, string> mediaPaths,
        FFmpegClipExtractor clipExtractor,
        IFilePickerService filePicker)
    {
        _clipExtractor = clipExtractor;
        _filePicker = filePicker;

        ScoreDisplay = $"{result.OverallScore:P0}";
        SourceCountDisplay = result.Components.Count == 1
            ? "1 source"
            : $"{result.Components.Count} sources";
        Components = result.Components
            .Select(c => new CompositeComponentRowViewModel(
                c,
                mediaTitles.GetValueOrDefault(c.MediaId, c.MediaId.ToString()[..8]),
                mediaPaths.GetValueOrDefault(c.MediaId)))
            .ToList();
    }

    [RelayCommand(CanExecute = nameof(CanExportClip))]
    private async Task ExportClipAsync()
    {
        if (!CanExportClip())
        {
            return;
        }

        var outputPath = await _filePicker.PickClipExportPathAsync("assembled.mp4");
        if (outputPath is null)
        {
            return;
        }

        ExportStatus = "Exporting assembled clip...";

        var result = await _clipExtractor.ExtractCompositeAsync(
            Components.Select(c => (c.MediaPath!, c.StartSeconds, c.EndSeconds)).ToList(),
            outputPath);

        ExportStatus = result.Success
            ? $"Exported to {Path.GetFileName(outputPath)}."
            : $"Export failed: {result.Error}";
    }

    private bool CanExportClip() => Components.All(c => c.MediaPath is not null);
}
