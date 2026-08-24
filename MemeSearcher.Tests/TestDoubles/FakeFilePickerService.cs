using MemeSearcher.Services;

namespace MemeSearcher.Tests.TestDoubles;

public class FakeFilePickerService : IFilePickerService
{
    public IReadOnlyList<string> FilesToReturn { get; set; } = [];
    public string? ClipExportPathToReturn { get; set; }
    public string? LastSuggestedFileName { get; private set; }

    public Task<IReadOnlyList<string>> PickMediaFilesAsync() => Task.FromResult(FilesToReturn);

    public Task<string?> PickClipExportPathAsync(string suggestedFileName)
    {
        LastSuggestedFileName = suggestedFileName;
        return Task.FromResult(ClipExportPathToReturn);
    }

    public string? TemplateExportPathToReturn { get; set; }
    public string? TemplateImportPathToReturn { get; set; }

    public Task<string?> PickTemplateExportPathAsync(string suggestedFileName)
    {
        LastSuggestedFileName = suggestedFileName;
        return Task.FromResult(TemplateExportPathToReturn);
    }

    public Task<string?> PickTemplateImportPathAsync() => Task.FromResult(TemplateImportPathToReturn);
}
