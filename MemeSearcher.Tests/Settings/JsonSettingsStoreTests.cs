using MemeSearcher.Core.Settings;
using MemeSearcher.Infrastructure.Settings;

namespace MemeSearcher.Tests.Settings;

public class JsonSettingsStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"memesearcher-settings-{Guid.NewGuid():N}.json");

    private static readonly SettingDefinition Choice = new(
        "test.choice", "Test", "Choice", "", SettingKind.Choice, "a",
        [new SettingChoice("a", "A"), new SettingChoice("b", "B")]);

    private static readonly SettingDefinition Text = new(
        "test.text", "Test", "Text", "", SettingKind.Text, "default");

    public void Dispose()
    {
        File.Delete(_path);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Get_ReturnsDefaultWhenNothingStored()
    {
        Assert.Equal("a", new JsonSettingsStore(_path).Get(Choice));
    }

    [Fact]
    public void SetThenReload_PersistsAcrossInstances()
    {
        new JsonSettingsStore(_path).Set(Choice, "b");

        Assert.Equal("b", new JsonSettingsStore(_path).Get(Choice));
    }

    [Fact]
    public void Set_RejectsAValueOutsideTheChoiceList()
    {
        var store = new JsonSettingsStore(_path);

        Assert.Throws<ArgumentException>(() => store.Set(Choice, "c"));
    }

    [Fact]
    public void Set_RaisesChangedWithOldAndNewValues()
    {
        var store = new JsonSettingsStore(_path);
        SettingChangedEventArgs? seen = null;
        store.Changed += (_, e) => seen = e;

        store.Set(Choice, "b");

        Assert.NotNull(seen);
        Assert.Equal("a", seen.OldValue);
        Assert.Equal("b", seen.NewValue);
    }

    [Fact]
    public void Set_ToTheSameValueDoesNotRaiseChanged()
    {
        var store = new JsonSettingsStore(_path);
        store.Set(Text, "x");

        var raised = false;
        store.Changed += (_, _) => raised = true;
        store.Set(Text, "x");

        Assert.False(raised);
    }

    /// <summary>
    /// A hand-edited or version-skewed file must not be able to hand an illegal value to an
    /// external tool - that is the failure class this milestone exists to close.
    /// </summary>
    [Fact]
    public void Get_FallsBackToDefaultWhenTheStoredValueIsNoLongerLegal()
    {
        File.WriteAllText(_path, """{"test.choice":"gone"}""");

        Assert.Equal("a", new JsonSettingsStore(_path).Get(Choice));
    }

    [Fact]
    public void Load_TreatsACorruptFileAsEmptyRatherThanThrowing()
    {
        File.WriteAllText(_path, "{ this is not json");

        Assert.Equal("a", new JsonSettingsStore(_path).Get(Choice));
    }
}
