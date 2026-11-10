// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

/// <summary>
/// Sits between raw detection results and the overlay, providing:
///
/// 1. <b>Position refresh</b> — an operator's <see cref="ShopItem.OcrRegion"/> (card
///    boundary) is updated on every scan so the overlay marker always tracks the
///    current image-detected card position.
///
/// 2. <b>Grace period</b> — an operator must be absent from detection results for
///    <see cref="GracePeriodScans"/> consecutive scans before it is removed from
///    the stable set.  This prevents a single missed frame from triggering a
///    fade-out / fade-in flicker.
///
/// <see cref="TryUpdate"/> returns <see langword="true"/> only when the stable set
/// actually changes (item added, removed or repositioned), so callers can skip overlay updates
/// on no-op scans.
/// </summary>
public sealed class ShopStateStabilizer
{
    /// <summary>
    /// Number of consecutive scans an operator must be absent before it is
    /// evicted from the stable set. A value of 2 tolerates one missed scan.
    /// </summary>
    public int GracePeriodScans { get; init; } = 2;

    private readonly Dictionary<string, Entry> _stable = [];

    private record Entry(ShopItem Item, int MissCount);

    /// <summary>
    /// Feeds new detection results into the stabilizer.
    /// </summary>
    /// <param name="detectedItems">Raw shop items from the latest detection scan.</param>
    /// <param name="stableItems">
    /// The current stable set with locked positions.  Only meaningful when the
    /// method returns <see langword="true"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the stable operator set changed and the overlay
    /// should be updated; <see langword="false"/> if nothing meaningful changed.
    /// </returns>
    public bool TryUpdate(IReadOnlyList<ShopItem> detectedItems, out IReadOnlyList<ShopItem> stableItems)
    {
        var detectedIds = detectedItems.Select(i => i.Id).ToHashSet();
        bool changed = false;

        // Newly detected operators: update position or add on first appearance.
        foreach (var item in detectedItems)
        {
            if (_stable.TryGetValue(item.Id, out var existing))
            {
                changed |= existing.Item.OcrRegion != item.OcrRegion;
                // Card still present — update card boundary and reset miss counter.
                _stable[item.Id] = existing with { Item = item, MissCount = 0 };
            }
            else
            {
                // First time seeing this card slot this shop session.
                _stable[item.Id] = new Entry(item, 0);
                changed = true;
            }
        }

        // Absent cards: increment miss counter; evict after grace period.
        foreach (var id in _stable.Keys.Except(detectedIds).ToList())
        {
            var entry = _stable[id];
            if (entry.MissCount + 1 >= GracePeriodScans)
            {
                _stable.Remove(id);
                changed = true;
            }
            else
            {
                _stable[id] = entry with { MissCount = entry.MissCount + 1 };
            }
        }

        stableItems = _stable.Values.Select(e => e.Item).ToList();
        return changed;
    }

    /// <summary>Clears all stable state (call when the game screen changes).</summary>
    public void Reset()
    {
        _stable.Clear();
    }
}
