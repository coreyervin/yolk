using Yolk.Application.Abstractions;
using Yolk.Domain.Configuration;

namespace Yolk.TestSupport;

/// <summary>An in-memory settings store, so tests never touch the registry.</summary>
public sealed class FakeSettingsRepository : ISettingsRepository
{
    public StoredSettings Stored { get; set; } = new(null, null, null, null, null);

    public int SaveCount { get; private set; }

    public StoredSettings Load() => Stored;

    public void Save(double interval, ThresholdRange threshold, StopAfter stopAfter)
    {
        SaveCount++;
        Stored = new StoredSettings(interval, threshold.Min, threshold.Max, LegacyThreshold: null, stopAfter);
    }
}

public sealed class FakeStartupRegistration : IStartupRegistration
{
    public bool Enabled { get; set; }
    public bool Registrable { get; set; } = true;
    public Exception? RegisterFailure { get; set; }
    public Exception? UnregisterFailure { get; set; }
    public int RegisterCalls { get; private set; }
    public int UnregisterCalls { get; private set; }

    public bool IsEnabled() => Enabled;

    public void Register()
    {
        RegisterCalls++;
        if (RegisterFailure is { } failure)
        {
            throw failure;
        }
        Enabled = true;
    }

    public void Unregister()
    {
        UnregisterCalls++;
        if (UnregisterFailure is { } failure)
        {
            throw failure;
        }
        Enabled = false;
    }

    public bool CanRegister() => Registrable;
}
