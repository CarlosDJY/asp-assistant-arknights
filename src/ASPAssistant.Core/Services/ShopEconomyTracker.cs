// Added for ASP Assistant Arknights on 2026-11-13; see NOTICE.
namespace ASPAssistant.Core.Services;

/// <summary>
/// Smooths <see cref="ShopEconomyReader"/> frames: a value is shown once two reads in a row agree, and a
/// missing read keeps the last shown value. A new round drops the old funds and price until they are read again.
/// </summary>
public sealed class ShopEconomyTracker
{
    private sealed class Field<T>
    {
        private T? _candidate;
        private bool _hasCandidate;
        public T? Value { get; private set; }
        public bool HasValue { get; private set; }

        /// <summary>True when the shown value changed.</summary>
        public bool Observe(T? read, bool present)
        {
            if (!present) return false;
            bool confirmed = _hasCandidate && EqualityComparer<T>.Default.Equals(_candidate, read);
            _candidate = read;
            _hasCandidate = true;
            if (!confirmed || HasValue && EqualityComparer<T>.Default.Equals(Value, read)) return false;
            Value = read;
            HasValue = true;
            return true;
        }

        /// <summary>Hides the shown value; reads already pending (from the new round's frames) still count.</summary>
        public void ClearShown()
        {
            HasValue = false;
            Value = default;
        }

        public void Clear()
        {
            _candidate = default;
            _hasCandidate = HasValue = false;
            Value = default;
        }
    }

    private readonly Field<int> _round = new(), _money = new(), _cost = new();
    private readonly Field<bool> _maxed = new();
    private readonly Field<string> _mode = new();

    public ShopEconomy? Current { get; private set; }

    /// <summary>Feeds one frame; returns true when <see cref="Current"/> changed.</summary>
    public bool Observe(ShopEconomy read)
    {
        bool changed = false;
        bool hadRound = _round.HasValue;
        if (_round.Observe(read.Round ?? 0, read.Round is not null))
        {
            if (hadRound)
            {
                _money.ClearShown();
                _cost.ClearShown();
            }
            changed = true;
        }
        changed |= _money.Observe(read.Money ?? 0, read.Money is not null);
        // 已满级 replaces the price; a read price means the card is not maxed.
        bool levelSeen = read.UpgradeMaxed || read.UpgradeCost is not null;
        changed |= _maxed.Observe(read.UpgradeMaxed, levelSeen);
        changed |= _cost.Observe(read.UpgradeCost ?? 0, read.UpgradeCost is not null);
        changed |= _mode.Observe(read.Mode, read.Mode is not null);
        if (!changed) return false;
        Current = new ShopEconomy(
            _round.HasValue ? _round.Value : null,
            _money.HasValue ? _money.Value : null,
            _maxed is { HasValue: true, Value: true } || !_cost.HasValue ? null : _cost.Value,
            _maxed is { HasValue: true, Value: true },
            _mode.HasValue ? _mode.Value : null,
            read.IsPreparation);
        return true;
    }

    /// <summary>New game: forget everything.</summary>
    public void Reset()
    {
        _round.Clear(); _money.Clear(); _cost.Clear(); _maxed.Clear(); _mode.Clear();
        Current = null;
    }
}
