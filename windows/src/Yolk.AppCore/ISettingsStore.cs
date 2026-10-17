namespace Yolk.AppCore;

/// <summary>
/// Where <see cref="AppModel"/> persists its settings. The tray app uses the
/// registry; tests use <see cref="InMemorySettingsStore"/>.
/// </summary>
public interface ISettingsStore
{
    double? ReadDouble(string key);

    /// <summary>Null removes the key.</summary>
    void WriteDouble(string key, double? value);

    string? ReadString(string key);

    /// <summary>Null removes the key.</summary>
    void WriteString(string key, string? value);
}

/// <summary>An in-memory store, so tests never touch the real user's settings.</summary>
public sealed class InMemorySettingsStore : ISettingsStore
{
    private readonly Dictionary<string, double> _doubles = [];
    private readonly Dictionary<string, string> _strings = [];

    public double? ReadDouble(string key) => _doubles.TryGetValue(key, out var v) ? v : null;

    public void WriteDouble(string key, double? value)
    {
        if (value is { } v)
        {
            _doubles[key] = v;
        }
        else
        {
            _doubles.Remove(key);
        }
    }

    public string? ReadString(string key) => _strings.GetValueOrDefault(key);

    public void WriteString(string key, string? value)
    {
        if (value is not null)
        {
            _strings[key] = value;
        }
        else
        {
            _strings.Remove(key);
        }
    }
}
