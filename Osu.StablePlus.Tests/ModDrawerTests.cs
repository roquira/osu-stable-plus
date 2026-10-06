using NUnit.Framework;
using Osu.StablePlus.Hook.Patches.Mods.CustomRate;

namespace Osu.StablePlus.Tests;

[TestFixture]
public class ModDrawerTests
{
    [Test]
    public void HoverLeaveIsImmediateAndDraggingKeepsDrawerOpen()
    {
        var drawer = new ModDrawerState();
        drawer.Update(false, false);
        Assert.That(drawer.Expanded, Is.False);
        drawer.Update(true, false);
        Assert.That(drawer.Expanded, Is.True);
        drawer.Update(false, false);
        Assert.That(drawer.Expanded, Is.False);
        drawer.Update(false, true);
        Assert.That(drawer.Expanded, Is.True);
        drawer.Update(false, false);
        Assert.That(drawer.Expanded, Is.False);
        drawer.Update(false, false);
        Assert.That(drawer.Expanded, Is.False);
    }

    [Test]
    public void PinKeepsDrawerOpenUntilUnpinnedOrReset()
    {
        var drawer = new ModDrawerState();
        drawer.TogglePin();
        drawer.Update(false, false);
        Assert.That(drawer.Expanded && drawer.Pinned, Is.True);
        drawer.TogglePin();
        drawer.Update(false, false);
        Assert.That(drawer.Expanded || drawer.Pinned, Is.False);
        drawer.TogglePin();
        drawer.Reset();
        Assert.That(drawer.Expanded || drawer.Pinned, Is.False);
    }

    [TestCase(435, 1)]
    [TestCase(405, 1)]
    [TestCase(357.5f, 0.5f)]
    [TestCase(310, 0)]
    [TestCase(300, 0)]
    [TestCase(445, 1)]
    public void ButtonFadeFollowsTravelAndClampsBounce(float currentTop, float opacity) =>
        Assert.That(ModDrawerState.ButtonOpacity(435, 310, currentTop), Is.EqualTo(opacity).Within(0.0001));
}
