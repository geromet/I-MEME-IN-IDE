using MemeSearcher.Shell;
using MemeSearcher.Tests.TestDoubles;
using MemeSearcher.ViewModels;

namespace MemeSearcher.Tests.ViewModels;

/// <summary>
/// #19: a panel's visibility must survive a restart. There's no real app restart to test against, so
/// this proves the same thing a restart would rely on - two independent PanelSlotViewModels sharing
/// the same store see each other's writes, exactly like two app launches sharing the same
/// settings.json would.
/// </summary>
public class PanelSlotViewModelTests
{
    private static ViewPanelDescriptor Panel(string id = "test-panel", bool visibleByDefault = true) =>
        new(id, "Test Panel", DockZone.Right, new object(), visibleByDefault);

    [Fact]
    public void IsVisible_DefaultsToThePanelsVisibleByDefault()
    {
        var store = new InMemorySettingsStore();

        Assert.True(new PanelSlotViewModel(Panel(visibleByDefault: true), store).IsVisible);
        Assert.False(new PanelSlotViewModel(Panel(visibleByDefault: false), store).IsVisible);
    }

    [Fact]
    public void IsVisible_WrittenByOneInstance_IsReadByAFreshInstanceOverTheSameStore()
    {
        var store = new InMemorySettingsStore();
        var panel = Panel();

        var first = new PanelSlotViewModel(panel, store) { IsVisible = false };

        var second = new PanelSlotViewModel(panel, store);

        Assert.False(second.IsVisible);
    }

    [Fact]
    public void IsVisible_TwoDifferentPanelIds_PersistIndependently()
    {
        var store = new InMemorySettingsStore();

        var a = new PanelSlotViewModel(Panel("panel-a"), store) { IsVisible = false };
        var b = new PanelSlotViewModel(Panel("panel-b"), store) { IsVisible = true };

        Assert.False(new PanelSlotViewModel(Panel("panel-a"), store).IsVisible);
        Assert.True(new PanelSlotViewModel(Panel("panel-b"), store).IsVisible);
    }
}
