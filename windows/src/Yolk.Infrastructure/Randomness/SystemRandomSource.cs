using Yolk.Application.Abstractions;

namespace Yolk.Infrastructure.Randomness;

internal sealed class SystemRandomSource : IRandomSource
{
    public double NextUnit() => Random.Shared.NextDouble();
}
