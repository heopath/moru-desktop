using System;
using System.Collections.Generic;
using System.Linq;

namespace Moru;

internal static class SkinNamesPolicy
{
    internal static bool IsValid(string? name) => !string.IsNullOrWhiteSpace(name)
        && name.Trim().Length <= 12 && !name.Any(char.IsControl);

    internal static IReadOnlyList<Skin> Visible(IReadOnlyList<Skin> all, Preferences preferences)
    {
        var visible = all.Where(s => s.IsCustom || !preferences.HiddenSkinIds.Contains(s.Id)).ToArray();
        // A damaged preferences file must never leave an invisible, unselectable pet.
        if (visible.Length == 0) visible = all.Count > 0 ? new[] { all[0] } : new[] { new Skin(SkinCatalog.Fallback) };
        return visible.Select(s => preferences.SkinNames.TryGetValue(s.Id, out var name) && IsValid(name)
            ? new Skin(s.Definition with { Name = name.Trim() }, s.Image, s.IsCustom, s.StateImages) : s).ToArray();
    }
}
