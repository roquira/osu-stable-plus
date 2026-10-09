using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using NUnit.Framework;
using Osu.StablePlus.Utils;
using Osu.StablePlus.Utils.IL;

namespace Osu.StablePlus.Tests;

[TestFixture, NonParallelizable]
public class IlCacheTests
{
    [Test]
    public void ScopesNestAndReleaseTheirCacheOnce()
    {
        var before = IlCache.Current;
        using (IlCache.Begin())
        {
            var cache = IlCache.Current;
            Assert.That(cache, Is.Not.Null);
            var inner = IlCache.Begin();
            inner.Dispose();
            inner.Dispose();
            Assert.That(IlCache.Current, Is.SameAs(cache));
        }
        Assert.That(IlCache.Current, Is.SameAs(before));
    }

    [Test]
    public void CachedSignatureSearchesReturnTheUncachedMembers()
    {
        LoadOsu();
        var random = new Random(20261009);
        var module = OsuAssembly.Assembly.ManifestModule;
        OpCode[][] Bodies(IEnumerable<MethodBase> members) => members
            .Where(m => m.Module == module && m.GetMethodBody() != null)
            .Select(m => new OpCodeReader(m.GetMethodBody()!.GetILAsByteArray()).GetOpCodes().ToArray())
            .Where(ops => ops.Length > 0).ToArray();
        IEnumerable<(OpCode[] signature, bool entire)> Signatures(OpCode[][] bodies)
        {
            for (var sample = 0; sample < 6; sample++)
            {
                var body = bodies[random.Next(bodies.Length)];
                var length = Math.Min(body.Length, 8);
                yield return (body.Skip(random.Next(body.Length - length + 1)).Take(length).ToArray(), false);
                yield return (body, true);
            }
            yield return (Enumerable.Repeat(OpCodes.Break, 16).ToArray(), false);
        }
        var methods = Signatures(Bodies(OsuAssembly.Types.SelectMany(t => t.GetMethods(AccessTools.allDeclared)))).ToArray();
        var constructors = Signatures(Bodies(OsuAssembly.Types.SelectMany(t => t.GetConstructors(AccessTools.allDeclared)))).ToArray();
        var expectedMethods = methods.Select(s => OpCodeMatcher.FindMethodBySignature(null, s.signature, s.entire)).ToArray();
        var expectedConstructors = constructors.Select(s => OpCodeMatcher.FindConstructorBySignature(null, s.signature, s.entire)).ToArray();
        Assert.That(expectedMethods.Last(), Is.Null);

        using (IlCache.Begin())
        {
            // Inherited members must keep the reflected type an uncached search returns.
            for (var i = 0; i < methods.Length; i++)
                Assert.That(OpCodeMatcher.FindMethodBySignature(null, methods[i].signature, methods[i].entire), Is.SameAs(expectedMethods[i]));
            for (var i = 0; i < constructors.Length; i++)
                Assert.That(OpCodeMatcher.FindConstructorBySignature(null, constructors[i].signature, constructors[i].entire), Is.SameAs(expectedConstructors[i]));
        }
    }

    [Test]
    public void CachedInstructionsMatchUncachedInstructions()
    {
        LoadOsu();
        var methods = OsuAssembly.Types.Take(300).SelectMany(t => t.GetMethods(AccessTools.allDeclared).Cast<MethodBase>()
            .Concat(t.GetConstructors(AccessTools.allDeclared))).Where(m => m.GetMethodBody() != null).ToArray();
        var expected = methods.Select(m => MethodReader.GetInstructions(m).ToArray()).ToArray();
        Assert.That(expected.Sum(e => e.Length), Is.GreaterThan(1000));

        using (IlCache.Begin())
            for (var i = 0; i < methods.Length; i++)
            {
                var cached = MethodReader.GetInstructions(methods[i]).ToArray();
                Assert.That(cached.Select(c => c.Opcode), Is.EqualTo(expected[i].Select(e => e.Opcode)), methods[i].ToString());
                Assert.That(cached.Select(Operand), Is.EqualTo(expected[i].Select(Operand)), methods[i].ToString());
                Assert.That(MethodReader.GetInstructions(methods[i]).Select(c => c.Operand).Zip(cached, (a, b) => ReferenceEquals(a, b.Operand)),
                    Is.All.True, "Cached reads should share their operands.");
            }
    }

    [Test]
    public void CachedReferenceQueriesMatchInstructionOperands()
    {
        LoadOsu();
        var random = new Random(20261009);
        var methods = OsuAssembly.Types.Take(600).SelectMany(t => t.GetMethods(AccessTools.allDeclared).Cast<MethodBase>()
            .Concat(t.GetConstructors(AccessTools.allDeclared))).Where(m => m.GetMethodBody() != null).ToArray();
        var reads = methods.Select(m => MethodReader.GetInstructions(m).Select(i => i.Operand).ToArray()).ToArray();
        static bool Queryable(object? operand) => operand is MemberInfo or string or sbyte or byte or int or long or float or double;
        var pool = reads.SelectMany(r => r).Where(Queryable).Distinct().ToArray();
        Assert.That(methods.Any(m => m.DeclaringType!.IsGenericType), "Generic methods should use Harmony's reader.");

        var mismatches = new List<string>();
        using (IlCache.Begin())
            for (var i = 0; i < methods.Length; i++)
            {
                var operands = reads[i].Where(Queryable).Concat(Enumerable.Range(0, 8).Select(_ => pool[random.Next(pool.Length)])).ToList();
                // The same number boxed as another type is a different operand.
                operands.AddRange(operands.OfType<sbyte>().Select(value => (object)(int)value)
                    .Concat(operands.OfType<float>().Select(value => (object)(double)value)).ToArray());
                foreach (var operand in operands)
                    if (MethodReader.References(methods[i], operand!) != reads[i].Any(o => Equals(o, operand)))
                        mismatches.Add($"{methods[i].DeclaringType}::{methods[i]} / {operand} ({operand!.GetType().Name})");
            }
        Assert.That(mismatches, Is.Empty);
    }

    // Harmony creates new branch-target and local objects on every read; compare those by description.
    private static object? Operand(IlInstruction instruction) => instruction.Operand is null or string or ValueType or MemberInfo or ParameterInfo
        ? instruction.Operand
        : instruction.Operand.GetType().FullName + ": " + instruction.Operand;

    private static void LoadOsu()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OSU_PATH")))
            Assert.Ignore("Set OSU_PATH to an installed stable directory for IL cache integration tests.");
        Osu.StablePlus.Stubs.Tests.OsuLoader.UpdateAndLoad().GetAwaiter().GetResult();
    }
}
