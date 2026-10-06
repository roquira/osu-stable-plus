using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Osu.StablePlus.Hook.Patches.Mods.CustomRate;

namespace Osu.StablePlus.Tests;

[TestFixture]
public class ReplayHeaderTests
{
    [Test]
    public void ReadsNativeFieldsAndLeavesTheStreamAtMetadata()
    {
        using var output = new MemoryStream();
        using (var writer = new BinaryWriter(output, Encoding.UTF8, true))
        {
            void Text(string value) { writer.Write((byte)11); writer.Write(value); }
            writer.Write((byte)0); writer.Write(20260918);
            Text(new string('a', 32)); Text("player"); Text(new string('b', 32));
            foreach (ushort count in new ushort[] { 50, 2, 1, 0, 0, 3 }) writer.Write(count);
            writer.Write(123456); writer.Write((ushort)20); writer.Write(false); writer.Write(64 | 128);
            Text("0|1,"); writer.Write(123456789L);
            writer.Write(4); writer.Write(new byte[] { 1, 2, 3, 4 }); writer.Write(0L);
            ReplayRateMetadata.WriteTail(output, new RateSettings(1.7));
        }
        var bytes = output.ToArray();
        using var input = new MemoryStream(bytes);
        var header = ReplayHeader.Read(new BinaryReader(input, Encoding.UTF8, true));
        Assert.That(header.Mode, Is.Zero);
        Assert.That(header.Version, Is.EqualTo(20260918));
        Assert.That(header.MapHash, Is.EqualTo(new string('a', 32)));
        Assert.That(header.Counts, Is.EqualTo(new ushort[] { 50, 2, 1, 0, 0, 3 }));
        Assert.That(header.Total, Is.EqualTo(123456));
        Assert.That(header.Combo, Is.EqualTo(20));
        Assert.That(header.Mods, Is.EqualTo(64 | 128));
        Assert.That(bytes.Skip(header.FramesOffset).Take(header.FramesLength), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
        Assert.That(header.End, Is.EqualTo(input.Position).And.LessThan(bytes.Length));
    }
}
