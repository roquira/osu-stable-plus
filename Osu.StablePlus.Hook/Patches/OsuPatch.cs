using System;

namespace Osu.StablePlus.Hook.Patches;

/// <summary>
///     A marker for osu! patches for <see cref="OsuPatchProcessor" /> to handle.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class OsuPatch : Attribute;
