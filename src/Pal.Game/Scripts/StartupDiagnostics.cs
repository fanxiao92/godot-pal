using System;
using System.IO;
using Pal.Game.Profiles;

namespace Pal.Game;

internal static class StartupDiagnostics
{
    internal static string Format(string? selectedProfile, string? scene, string? staticProfile, string detail) =>
        $"Static scene startup failed (selected-profile={selectedProfile ?? "unselected"}, scene={scene ?? "unselected"}, static-profile={staticProfile ?? "unselected"}): {detail}";

    internal static string? SuppliedM4Profile(string[] arguments)
    {
        for (int index = 0; index < arguments.Length; index++)
        {
            if (arguments[index] == "--m4-first-battle-profile" && index + 1 < arguments.Length
                && !arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
                return arguments[index + 1];
        }
        return null;
    }
}
