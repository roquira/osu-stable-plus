using System;
using NUnit.Framework;
using Osu.StablePlus.Injector;

namespace Osu.StablePlus.Tests;

[TestFixture]
public sealed class InjectorArgumentsTests
{
    [Test]
    public void NoArgumentsAttachToRunningOsu()
    {
        var parsed = InjectorArguments.Parse(Array.Empty<string>());

        Assert.That(parsed.Mode, Is.EqualTo(InjectorMode.Attach));
    }

    [Test]
    public void LaunchOptionUsesExplicitPath()
    {
        const string path = @"E:\Games With Spaces\osu!\osu!.exe";

        var parsed = InjectorArguments.Parse(new[] { "--launch", path });

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Mode, Is.EqualTo(InjectorMode.Launch));
            Assert.That(parsed.OsuPath, Is.EqualTo(path));
        });
    }

    [Test]
    public void PidArgumentsSelectExplicitProcess()
    {
        var parsed = InjectorArguments.Parse(new[] { "--pid", "1234" });

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Mode, Is.EqualTo(InjectorMode.ProcessId));
            Assert.That(parsed.ProcessId, Is.EqualTo(1234));
        });
    }

    [TestCase("--pid")]
    [TestCase("--pid", "0")]
    [TestCase("--pid", "not-a-number")]
    [TestCase("--launch")]
    [TestCase("--launch", "")]
    [TestCase(@"E:\Games\osu!\osu!.exe")]
    [TestCase("one", "two", "three")]
    public void InvalidArgumentsAreRejected(params string[] args)
    {
        Assert.That(() => InjectorArguments.Parse(args), Throws.TypeOf<ArgumentException>());
    }
}
