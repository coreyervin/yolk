namespace Yolk.AppCore;

/// <summary>
/// The "Launch at sign-in" boundary. The tray app's implementation writes the
/// current user's Run registry key; tests use a fake.
/// </summary>
public interface ILoginItemService
{
    bool IsEnabled();

    void Register();

    void Unregister();

    /// <summary>Whether this copy of Yolk can be registered at all.</summary>
    bool CanRegister();
}
