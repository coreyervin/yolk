using System.Security;
using Yolk.Application.Abstractions;

namespace Yolk.Application.Features.LaunchAtSignIn;

/// <summary>
/// Whether Yolk starts at sign-in, read from the system every time rather than
/// cached — the user can remove the entry in Task Manager behind Yolk's back.
/// </summary>
public sealed record LaunchAtSignInStatus(bool Enabled, bool CanEnable, string? Problem);

public sealed record GetLaunchAtSignIn;

public sealed record SetLaunchAtSignIn(bool Enabled);

internal sealed class GetLaunchAtSignInHandler(IStartupRegistration registration)
    : IQueryHandler<GetLaunchAtSignIn, LaunchAtSignInStatus>
{
    public LaunchAtSignInStatus Handle(GetLaunchAtSignIn query) => LaunchAtSignInRules.Current(registration);
}

internal sealed class SetLaunchAtSignInHandler(IStartupRegistration registration)
    : ICommandHandler<SetLaunchAtSignIn, LaunchAtSignInStatus>
{
    public LaunchAtSignInStatus Handle(SetLaunchAtSignIn command)
    {
        if (!registration.CanRegister())
        {
            // Refused up front rather than attempted and silently reverted.
            return LaunchAtSignInRules.Current(registration);
        }
        try
        {
            if (command.Enabled)
            {
                registration.Register();
            }
            else
            {
                registration.Unregister();
            }
            return new LaunchAtSignInStatus(command.Enabled, CanEnable: true, Problem: null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        {
            // Report the truth: the system state did not change.
            var problem = command.Enabled
                ? $"Yolk could not be added to your startup apps. {error.Message}"
                : $"Yolk could not be removed from your startup apps. {error.Message}";
            return new LaunchAtSignInStatus(registration.IsEnabled(), CanEnable: true, problem);
        }
    }
}

internal static class LaunchAtSignInRules
{
    public const string UnavailableProblem = "Launch at sign-in isn't available for this copy of Yolk.";

    public static LaunchAtSignInStatus Current(IStartupRegistration registration) =>
        registration.CanRegister()
            ? new LaunchAtSignInStatus(registration.IsEnabled(), CanEnable: true, Problem: null)
            : new LaunchAtSignInStatus(Enabled: false, CanEnable: false, UnavailableProblem);
}
