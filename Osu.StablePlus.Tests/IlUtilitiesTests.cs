using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using NUnit.Framework;
using Osu.StablePlus.Utils.Extensions;
using Osu.StablePlus.Utils.IL;
using static System.Reflection.Emit.OpCodes;

namespace Osu.StablePlus.Tests;

[TestFixture]
public class IlUtilitiesTests
{
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    public void SwitchTablesAreSkippedWithoutTreatingOffsetsAsOpcodes(int count)
    {
        var bytes = new List<byte> { 0x16, 0x45 };
        bytes.AddRange(BitConverter.GetBytes(count));
        for (var index = 0; index < count; index++) bytes.AddRange(BitConverter.GetBytes(-1));
        bytes.AddRange(new byte[] { 0xfe, 0x01, 0x00, 0x2a });
        Assert.That(new OpCodeReader(bytes).GetOpCodes(), Is.EqualTo(new[] { Ldc_I4_0, Switch, Ceq, Nop, Ret }));
    }

    private static IEnumerable<TestCaseData> InvalidIl()
    {
        yield return new TestCaseData(new byte[] { 0xfe });
        yield return new TestCaseData(new byte[] { 0xff });
        yield return new TestCaseData(new byte[] { 0xfe, 0xff });
        yield return new TestCaseData(new byte[] { 0x20, 0x00 }); // Truncated integer operand.
        yield return new TestCaseData(new byte[] { 0x45, 0x00 }); // Truncated switch count.
        yield return new TestCaseData(new byte[] { 0x45, 0xff, 0xff, 0xff, 0xff });
        yield return new TestCaseData(new byte[] { 0x45, 0xff, 0xff, 0xff, 0x7f });
        yield return new TestCaseData(new byte[] { 0x45, 0x01, 0x00, 0x00, 0x00, 0x00 });
    }

    [TestCaseSource(nameof(InvalidIl))]
    public void MalformedIlHasAControlledError(byte[] bytes) =>
        Assert.Throws<InvalidDataException>(() => new OpCodeReader(bytes).GetOpCodes().ToArray());

    [Test]
    public void MatcherFindsOverlappingPrefixesAndTheFinalInstruction()
    {
        Assert.That(OpCodeMatcher.InstructionsMatchesSignature(new byte[] { 0, 0, 0, 0x2a },
            new[] { Nop, Nop, Ret }, false), Is.True);
        Assert.That(OpCodeMatcher.InstructionsMatchesSignature(new byte[] { 0, 0x2a }, new[] { Ret }, false), Is.True);
    }

    [Test]
    public void EntireMethodRequiresExactlyTheWholeMethod()
    {
        var bytes = new byte[] { 0, 0x2a };
        Assert.That(OpCodeMatcher.InstructionsMatchesSignature(bytes, new[] { Nop, Ret }, true), Is.True);
        Assert.That(OpCodeMatcher.InstructionsMatchesSignature(bytes, new[] { Nop }, true), Is.False);
        Assert.That(OpCodeMatcher.InstructionsMatchesSignature(bytes, new[] { Ret }, true), Is.False);
        Assert.That(OpCodeMatcher.InstructionsMatchesSignature(bytes, new[] { Nop, Ret, Nop }, true), Is.False);
        Assert.That(OpCodeMatcher.InstructionsMatchesSignature(bytes, Array.Empty<OpCode>(), false), Is.False);
    }

    [Test]
    public void EmptyAndPartialSignaturesDoNotMatch()
    {
        Assert.That(OpCodeMatcher.InstructionsMatchesSignature(Array.Empty<byte>(), new[] { Ret }, false), Is.False);
        Assert.That(OpCodeMatcher.InstructionsMatchesSignature(new byte[] { 0, 0x2a }, new[] { Ret, Nop }, false), Is.False);
    }

    private static CodeInstruction[] Code(params OpCode[] opcodes) => opcodes.Select(op => new CodeInstruction(op)).ToArray();

    [Test]
    public void InsertAfterHandlesEndOfMethodAndOverlappingPrefix()
    {
        var input = Code(Nop, Nop, Nop, Ret);
        var inserted = Code(Ldc_I4_1);
        Assert.That(input.InsertAfterSignature(new[] { Nop, Nop, Ret }, inserted).Select(i => i.opcode),
            Is.EqualTo(new[] { Nop, Nop, Nop, Ret, Ldc_I4_1 }));
    }

    [Test]
    public void InsertBeforeHandlesOverlapAndRepeatedMatches()
    {
        Assert.That(Code(Nop, Nop, Nop, Ret).InsertBeforeSignature(new[] { Nop, Nop, Ret }, Code(Ldc_I4_1))
            .Select(i => i.opcode), Is.EqualTo(new[] { Nop, Ldc_I4_1, Nop, Nop, Ret }));
        Assert.That(Code(Ret, Ret).InsertBeforeSignature(new[] { Ret }, Code(Nop), true).Select(i => i.opcode),
            Is.EqualTo(new[] { Nop, Ret, Nop, Ret }));
    }

    [Test]
    public void MissingOrTruncatedSignatureThrowsAMatchErrorInsteadOfIndexingPastEnd()
    {
        foreach (var signature in new[] { new[] { Ret, Nop }, new[] { Nop, Nop, Nop, Nop } })
        {
            Assert.Throws<InvalidOperationException>(() => Code(Nop, Nop, Ret).InsertBeforeSignature(signature, Code(Nop)).ToArray());
            Assert.Throws<InvalidOperationException>(() => Code(Nop, Nop, Ret).InsertAfterSignature(signature, Code(Nop)).ToArray());
            Assert.Throws<InvalidOperationException>(() => Code(Nop, Nop, Ret).NoopSignature(signature).ToArray());
            Assert.Throws<InvalidOperationException>(() => Code(Nop, Nop, Ret).NoopAfterSignature(signature, 1).ToArray());
        }
    }

    [Test]
    public void EmptyTranspilerSignaturesAreRejected()
    {
        var empty = Array.Empty<OpCode>();
        Assert.Throws<ArgumentException>(() => Code(Ret).InsertBeforeSignature(empty, Code(Nop)).ToArray());
        Assert.Throws<ArgumentException>(() => Code(Ret).InsertAfterSignature(empty, Code(Nop)).ToArray());
        Assert.Throws<ArgumentException>(() => Code(Ret).NoopSignature(empty).ToArray());
        Assert.Throws<ArgumentException>(() => Code(Ret).NoopAfterSignature(empty, 1).ToArray());
    }

    [Test]
    public void NoopRetainsBranchLabelsAndExceptionBlocks()
    {
        var method = new DynamicMethod("labels", typeof(void), Type.EmptyTypes);
        var input = Code(Ldc_I4, Ret);
        input[0].operand = 123;
        var label = method.GetILGenerator().DefineLabel();
        input[0].labels.Add(label);
        var block = new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock);
        input[0].blocks.Add(block);
        var output = input.NoopSignature(new[] { Ldc_I4 }).ToArray();
        Assert.That(output[0], Is.SameAs(input[0]));
        Assert.That(output[0].opcode, Is.EqualTo(Nop));
        Assert.That(output[0].operand, Is.Null);
        Assert.That(output[0].labels, Is.EqualTo(new[] { label }));
        Assert.That(output[0].blocks, Is.EqualTo(new[] { block }));
        Assert.That(output[1].opcode, Is.EqualTo(Ret));
    }

    [Test]
    public void NoopSignatureReplacesAllDisjointMatches()
    {
        Assert.That(Code(Ldc_I4_0, Pop, Ldc_I4_0, Pop, Ret).NoopSignature(new[] { Ldc_I4_0, Pop }, true)
            .Select(i => i.opcode), Is.EqualTo(new[] { Nop, Nop, Nop, Nop, Ret }));
    }

    [Test]
    public void NoopAfterHandlesRepeatedMatchesAndZeroReplacements()
    {
        Assert.That(Code(Ldc_I4_0, Pop, Ldc_I4_0, Pop, Ret).NoopAfterSignature(new[] { Ldc_I4_0 }, 1, true)
            .Select(i => i.opcode), Is.EqualTo(new[] { Ldc_I4_0, Nop, Ldc_I4_0, Nop, Ret }));
        Assert.That(Code(Nop, Ret).NoopAfterSignature(new[] { Ret }, 0).Select(i => i.opcode),
            Is.EqualTo(new[] { Nop, Ret }));
    }

    [Test]
    public void NoopAfterRequiresEnoughRoomFollowingEveryMatch()
    {
        Assert.Throws<InvalidOperationException>(() => Code(Nop, Ret).NoopAfterSignature(new[] { Ret }, 1).ToArray());
        Assert.Throws<InvalidOperationException>(() => Code(Nop, Ret).NoopAfterSignature(new[] { Nop }, uint.MaxValue).ToArray());
        Assert.Throws<InvalidOperationException>(() => Code(Nop, Pop, Nop).NoopAfterSignature(new[] { Nop }, 1, true).ToArray());
    }
}
