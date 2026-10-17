namespace Yolk.AppCore;

/// <summary>The "Stop after…" menu.</summary>
public enum StopAfter
{
    Never,
    Minutes30,
    Hour1,
    Hours4,
    Hours8,
}

public static class StopAfterExtensions
{
    public static double? Timeout(this StopAfter option) => option switch
    {
        StopAfter.Minutes30 => 1800,
        StopAfter.Hour1 => 3600,
        StopAfter.Hours4 => 14400,
        StopAfter.Hours8 => 28800,
        _ => null,
    };

    public static string Title(this StopAfter option) => option switch
    {
        StopAfter.Minutes30 => "30 minutes",
        StopAfter.Hour1 => "1 hour",
        StopAfter.Hours4 => "4 hours",
        StopAfter.Hours8 => "8 hours",
        _ => "Never",
    };

    /// <summary>
    /// The persisted name — the same strings the Mac app stores. Renaming one
    /// silently resets that stored preference, so add values, don't rename them.
    /// </summary>
    public static string StorageKey(this StopAfter option) => option switch
    {
        StopAfter.Minutes30 => "minutes30",
        StopAfter.Hour1 => "hour1",
        StopAfter.Hours4 => "hours4",
        StopAfter.Hours8 => "hours8",
        _ => "never",
    };

    public static StopAfter? FromStorageKey(string? key) =>
        Enum.GetValues<StopAfter>().Cast<StopAfter?>().FirstOrDefault(o => o!.Value.StorageKey() == key);
}
