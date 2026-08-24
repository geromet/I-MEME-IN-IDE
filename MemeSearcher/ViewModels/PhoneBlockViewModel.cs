using System;
using MemeSearcher.Core.Search;

namespace MemeSearcher.ViewModels;

/// <summary>
/// One rectangle in the inspector's phone timeline (#15). Width is proportional to the phone's own
/// duration when real timing exists, so an aligned word's timeline visibly shows its phones taking
/// different amounts of time - a floor keeps a very short (or untimed) phone from collapsing to
/// nothing.
/// </summary>
public class PhoneBlockViewModel(MatchedPhone phone)
{
    private const double PixelsPerSecond = 400;
    private const double MinWidth = 22;

    public string Symbol { get; } = phone.Symbol;

    public bool IsAligned { get; } = phone.IsPhoneLevelAligned;

    public double? StartSeconds { get; } = phone.StartSeconds;

    public bool HasTiming { get; } = phone.StartSeconds is not null && phone.EndSeconds is not null;

    public double Width { get; } = Math.Max(
        MinWidth,
        (phone.EndSeconds is { } end && phone.StartSeconds is { } start ? end - start : 0) * PixelsPerSecond);

    public string TimingTooltip { get; } = phone.StartSeconds is { } s && phone.EndSeconds is { } e
        ? $"{phone.Symbol}: {s:F2}s - {e:F2}s ({(phone.IsPhoneLevelAligned ? "aligned" : "estimated")})"
        : $"{phone.Symbol}: no timing";
}
