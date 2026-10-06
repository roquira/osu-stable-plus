using System;
using System.Runtime.Serialization;
using HarmonyLib;
using NUnit.Framework;
using Osu.StablePlus.Hook;
using Osu.StablePlus.Stubs.Online;
using Osu.StablePlus.Stubs.Root;

namespace Osu.StablePlus.Tests;

[TestFixture, NonParallelizable]
public class OfflineSessionTests
{
    [TestCase(null, true)]
    [TestCase("ppy.sh", true)]
    [TestCase("PPY.SH.", true)]
    [TestCase("c.ppy.sh", true)]
    [TestCase("osu.ppy.sh", true)]
    [TestCase("https://ppy.sh", true)]
    [TestCase("", true)]
    [TestCase("-other", true)]
    [TestCase("example.com", false)]
    [TestCase("localhost", false)]
    public void OfficialAndMalformedServerArgumentsRequireOfflineMode(string? server, bool expected)
    {
        var args = server == null ? new[] { "E:\\Games\\osu!\\osu!.exe" }
            : new[] { "E:\\Games With Spaces\\osu!\\osu!.exe", "-devserver", server, "-other" };
        Assert.That(OfflineSession.RequiresOfflineMode(args), Is.EqualTo(expected));
    }

    [Test]
    public void AmbiguousServerArgumentsRequireOfflineMode()
    {
        Assert.That(OfflineSession.RequiresOfflineMode(new[] { "osu!.exe", "-devserver" }), Is.True);
        Assert.That(OfflineSession.RequiresOfflineMode(new[] { "osu!.exe", "-devserver", "example.com", "-devserver", "ppy.sh" }), Is.True);
        Assert.That(OfflineSession.RequiresOfflineMode(new[] { "osu!.exe", "-DEVSERVER", "example.com" }), Is.True);
    }

    [Test]
    public void GateForcesLogoutBeforeActivationAndKeepsLoginBlocked()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OSU_PATH")))
            Assert.Ignore("Set OSU_PATH for native offline-session checks.");
        Osu.StablePlus.Stubs.Tests.OsuLoader.UpdateAndLoad().GetAwaiter().GetResult();
        var args = new[] { "osu!.exe" };
        var oldConnected = BanchoClient.Connected.Get();
        var oldPassword = BanchoClient.Password.Get();
        var oldOptions = GameBase.Options.Get();
        var logoutProbe = new Harmony("osu-stable-plus.tests.logout-probe");
        logoutProbe.Patch(BanchoClient.Logout.Reference,
            prefix: new HarmonyMethod(typeof(OfflineSessionTests), nameof(SimulateNativeLogout)));
        var options = FormatterServices.GetUninitializedObject(BanchoClient.Login.Reference.DeclaringType!);
        GC.SuppressFinalize(options); // No native UI resources were constructed.
        GameBase.Options.Set(options);
        try
        {
            BanchoClient.Connected.Set(true);
            BanchoClient.Password.Set(FormatterServices.GetUninitializedObject(BanchoClient.Password.Reference.FieldType));
            logoutSucceeds = false;
            logoutCalls = 0;
            // A native logout that does not clear the session must still fail closed.
            Assert.Throws<InvalidOperationException>(() => OfflineSession.Initialize(args, action => action()));
            Assert.That(logoutCalls, Is.EqualTo(1));
            // Disconnected clients can still retain a credential object; logout clears it.
            BanchoClient.Connected.Set(false);
            logoutSucceeds = true;
            Assert.That(OfflineSession.Initialize(args, action => action()), Is.True);
            Assert.That(logoutCalls, Is.EqualTo(2));
            foreach (var method in new[] { BanchoClient.Login.Reference, BanchoClient.Connect.Reference, BanchoClient.Send.Reference })
                Assert.That(PatchProcessor.GetPatchInfo(method).Owners, Does.Contain("osu-stable-plus.offline-session"));

            // Exercise the patched native entry points. No game UI, saved credentials,
            // network connection or authentication is needed by their skipped bodies.
            BanchoClient.Login.Invoke(options, ["offline-test", "not-a-real-password"]);
            BanchoClient.Connect.Invoke();
            BanchoClient.Send.Invoke(parameters: [true]);
            Assert.That(BanchoClient.Password.Get(), Is.Null);
            Assert.That(BanchoClient.Connected.Get(), Is.False);
            Assert.That(OfflineSession.Initialize(args), Is.True);
        }
        finally
        {
            BanchoClient.Connected.Set(oldConnected);
            BanchoClient.Password.Set(oldPassword);
            GameBase.Options.Set(oldOptions);
            logoutProbe.Unpatch(BanchoClient.Logout.Reference, HarmonyPatchType.All, logoutProbe.Id);
        }
    }

    private static bool logoutSucceeds;
    private static int logoutCalls;

    private static bool SimulateNativeLogout()
    {
        // The real UI and network teardown need a running game. Substitute just
        // that boundary while exercising the gate and actual patched entry points.
        logoutCalls++;
        Assert.That(PatchProcessor.GetPatchInfo(BanchoClient.Login.Reference).Owners,
            Does.Contain("osu-stable-plus.offline-session"));
        Assert.That(PatchProcessor.GetPatchInfo(BanchoClient.Connect.Reference).Owners,
            Does.Contain("osu-stable-plus.offline-session"));
        if (logoutSucceeds)
        {
            BanchoClient.Connected.Set(false);
            BanchoClient.Password.Set(null);
        }
        return false;
    }
}
