namespace ShortcutList.Services;

// Pure boundary: a successful shell request must never be reclassified as a launch failure.
public sealed record LaunchOutcome(bool Requested, Exception? RequestError, Exception? HistoryError);
public static class LaunchCoordinator
{
    public static LaunchOutcome Execute(Action request, Action persistHistory)
    {
        try { request(); }
        catch (Exception ex) { return new(false, ex, null); }
        try { persistHistory(); return new(true, null, null); }
        catch (Exception ex) { return new(true, null, ex); }
    }
}

