namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

/// <summary>Match Visual Settings' immediate hover response; retain drag and pin ownership.</summary>
internal sealed class ModDrawerState
{
    internal bool Expanded { get; private set; }
    internal bool Pinned { get; private set; }

    internal void Update(bool inside, bool dragging)
    {
        Expanded = inside || dragging || Pinned;
    }

    internal void TogglePin() { Pinned = !Pinned; Expanded = true; }
    internal void Reset() { Expanded = Pinned = false; }

    // Visual Settings leaves the first 30 logical pixels of travel unfaded,
    // then fades the underlying buttons according to the actual drawer height.
    internal static float ButtonOpacity(float collapsedTop, float expandedTop, float currentTop)
    {
        var fadeTravel = System.Math.Max(1, collapsedTop - expandedTop - 30);
        return System.Math.Max(0, System.Math.Min(1, 1 - (collapsedTop - currentTop - 30) / fadeTravel));
    }
}
