// Modified for ASP Assistant Arknights on 2026-11-12; see NOTICE.
namespace ASPAssistant.Core.Services;

/// <summary>
/// Turns sporadic terminal-level readings into a level that holds for the whole run.
/// <para>
/// The level is only visible on the shop's upgrade card, so most frames carry no reading (battle, shop collapsed,
/// menus). The terminal only changes through that same card, so while the card is hidden the last level stays
/// valid: missing readings never clear or weaken it. Within a run the level never goes down, so readings are
/// weighed by direction:
/// <list type="bullet">
/// <item>one level up (the normal upgrade): <see cref="Confirmations"/> identical readings;</item>
/// <item>several levels up at once (missed upgrades, or a misread): <see cref="JumpConfirmations"/>;</item>
/// <item>down: only a new run explains that (e.g. the ban screen was missed), so it takes
/// <see cref="DecreaseConfirmations"/> readings spread over at least <see cref="DecreaseMinSpan"/>.</item>
/// </list>
/// A level restored from the last session is only a hint: the first stable reading replaces it in either direction.
/// </para>
/// </summary>
public sealed class TerminalLevelTracker
{
    public const int MaxLevel = 6;

    public int Confirmations { get; init; } = 2;
    public int JumpConfirmations { get; init; } = 3;
    public int DecreaseConfirmations { get; init; } = 4;
    public TimeSpan DecreaseMinSpan { get; init; } = TimeSpan.FromSeconds(15);

    public int? Level { get; private set; }

    /// <summary>True while <see cref="Level"/> comes from the last session and has not been seen in this one.</summary>
    public bool IsRestored { get; private set; }

    private int? _candidate;
    private int _count;
    private DateTime _candidateSince;

    /// <summary>Feeds one reading (null = card not visible); returns true when <see cref="Level"/> or <see cref="IsRestored"/> changed.</summary>
    public bool Observe(int? reading, DateTime? now = null)
    {
        if (reading is not { } value || value is < 1 or > MaxLevel) return false;
        var time = now ?? DateTime.UtcNow;
        if (value == Level)
        {
            ClearCandidate();
            if (!IsRestored) return false;
            IsRestored = false;
            return true;
        }

        if (value == _candidate) _count++;
        else { _candidate = value; _count = 1; _candidateSince = time; }

        var unconfirmed = Level is null || IsRestored;
        var accepted = unconfirmed
            ? _count >= Confirmations
            : value > Level
                ? _count >= (value - Level == 1 ? Confirmations : JumpConfirmations)
                : _count >= DecreaseConfirmations && time - _candidateSince >= DecreaseMinSpan;
        if (!accepted) return false;
        Level = value;
        IsRestored = false;
        ClearCandidate();
        return true;
    }

    /// <summary>Starts from the level saved by the last session, until the shop confirms or replaces it.</summary>
    public void Restore(int level)
    {
        if (level is < 1 or > MaxLevel) return;
        Level = level;
        IsRestored = true;
        ClearCandidate();
    }

    public void Reset()
    {
        Level = null;
        IsRestored = false;
        ClearCandidate();
    }

    private void ClearCandidate()
    {
        _candidate = null;
        _count = 0;
    }
}
