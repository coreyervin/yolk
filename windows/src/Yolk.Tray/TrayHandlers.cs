using Yolk.Application.Abstractions;
using Yolk.Application.Features.KeepAwake;
using Yolk.Application.Features.LaunchAtSignIn;
using Yolk.Application.Features.Settings;
using Yolk.Application.Features.Status;

namespace Yolk.Tray;

/// <summary>
/// The slices the tray app calls, gathered so the forms take one dependency
/// instead of nine. Resolved by the container like any other service.
/// </summary>
internal sealed record TrayHandlers(
    ICommandHandler<StartKeepAwake, StartKeepAwakeResult> Start,
    ICommandHandler<StopKeepAwake> Stop,
    IQueryHandler<GetStatus, StatusDto> GetStatus,
    IQueryHandler<GetSettings, SettingsDto> GetSettings,
    ICommandHandler<ApplyStoredSettings> ApplyStoredSettings,
    ICommandHandler<UpdateSettings, SettingsDto> UpdateSettings,
    IQueryHandler<GetLaunchAtSignIn, LaunchAtSignInStatus> GetLaunchAtSignIn,
    ICommandHandler<SetLaunchAtSignIn, LaunchAtSignInStatus> SetLaunchAtSignIn)
{
    public StatusDto Status() => GetStatus.Handle(new GetStatus(DateTime.Now));

    /// <summary>Starts or stops, whichever the session isn't doing now.</summary>
    public void Toggle()
    {
        if (Status().IsActive)
        {
            Stop.Handle(new StopKeepAwake());
        }
        else
        {
            // A refused power request leaves the session idle, which the icon
            // then shows; there's nothing more useful to say from a tray icon.
            Start.Handle(new StartKeepAwake());
        }
    }
}
