using Yolk.Application.Abstractions;
using Yolk.Application.Sessions;
using Yolk.Domain.Sessions;

namespace Yolk.Application.Features.KeepAwake;

/// <summary>Stop keeping the machine awake, at the user's request.</summary>
public sealed record StopKeepAwake;

internal sealed class StopKeepAwakeHandler(SessionRuntime runtime) : ICommandHandler<StopKeepAwake>
{
    public void Handle(StopKeepAwake command) => runtime.End(StopReason.UserRequested);
}
