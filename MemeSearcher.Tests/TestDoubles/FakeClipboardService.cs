using MemeSearcher.Services;

namespace MemeSearcher.Tests.TestDoubles;

public class FakeClipboardService : IClipboardService
{
    public List<string> CopiedTexts { get; } = [];

    public Task SetTextAsync(string text)
    {
        CopiedTexts.Add(text);
        return Task.CompletedTask;
    }
}
