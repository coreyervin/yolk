using System.Reflection;

namespace Yolk.Core;

/// <summary>
/// The version both Windows products report, read from the build. Its source
/// is <c>windows/Directory.Build.props</c>, which must match the Mac's
/// <c>Sources/YolkKit/Version.swift</c> — <c>make check-version</c> and
/// VersionTests both enforce that.
/// </summary>
public static class YolkVersion
{
    public static readonly string Current =
        typeof(YolkVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
        ?? "0.0.0";
}
