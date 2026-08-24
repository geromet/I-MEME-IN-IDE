using MemeSearcher.Core.Search;

namespace MemeSearcher.Tests.Search;

/// <summary>#17: SearchResult and CompositeMatchComponent must actually share the per-clip MatchComponent shape, not just look similar - this is the structural guarantee the whole refactor exists to establish.</summary>
public class MatchComponentTests
{
    [Fact]
    public void SearchResult_IsAMatchComponent()
    {
        var result = new SearchResult(Guid.NewGuid(), 1.0, 2.0, "a long bus", "ə lɔŋ bʌs", ["ə", "l"], 0.8, ["ə", "l"]);

        MatchComponent component = result;
        Assert.Equal(result.MediaId, component.MediaId);
        Assert.Equal(result.Phonemes, component.Phonemes);
        Assert.Equal(result.Score, component.Score);
    }

    [Fact]
    public void CompositeMatchComponent_IsAMatchComponent()
    {
        var component = new CompositeMatchComponent(Guid.NewGuid(), 1.0, 2.0, "a long", "ə lɔŋ", ["ə", "l"], 0.9, 0, 2);

        MatchComponent asBase = component;
        Assert.Equal(component.MediaId, asBase.MediaId);
        Assert.Equal(component.Phonemes, asBase.Phonemes);
        Assert.Equal(component.Score, asBase.Score);
    }
}
