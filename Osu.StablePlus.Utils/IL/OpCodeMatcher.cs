using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace Osu.StablePlus.Utils.IL;

public static class OpCodeMatcher
{
    internal const BindingFlags MethodFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    internal const BindingFlags ConstructorFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>
    ///     Search for a method inside the osu! assembly by an IL OpCode signature.
    /// </summary>
    /// <param name="searchType">The type to shrink searching to, otherwise all types will be searched.</param>
    /// <param name="signature">A set of sequential OpCodes to match.</param>
    /// <param name="entireMethod">Whether the signature is the entire method to search for.</param>
    /// <returns>The found method or null if none found.</returns>
    public static MethodInfo? FindMethodBySignature(
        Type? searchType,
        IReadOnlyList<OpCode> signature,
        bool entireMethod = false)
    {
        if (signature.Count <= 0) return null;
        if (searchType == null && IlCache.Current is { } cache) return cache.Methods.Find(signature, entireMethod);

        var searchTypes = searchType == null ? OsuAssembly.Types : [searchType];

        foreach (var type in searchTypes)
            foreach (var method in type.GetMethods(MethodFlags))
            {
                var instructions = method.GetMethodBody()?.GetILAsByteArray();
                if (instructions == null) continue;

                if (InstructionsMatchesSignature(instructions, signature, entireMethod))
                    return method;
            }

        return null;
    }

    /// <summary>
    ///     Search for a constructor inside the osu! assembly by an IL OpCode signature.
    /// </summary>
    /// <param name="searchType">The type to shrink searching to, otherwise all types will be searched.</param>
    /// <param name="signature">A set of sequential OpCodes to match.</param>
    /// <param name="entireMethod">Whether the signature is the entire method to search for.</param>
    /// <returns>The found constructor (method) or null if none found.</returns>
    public static ConstructorInfo? FindConstructorBySignature(
        Type? searchType,
        IReadOnlyList<OpCode> signature,
        bool entireMethod = false)
    {
        if (signature.Count <= 0) return null;
        if (searchType == null && IlCache.Current is { } cache) return cache.Constructors.Find(signature, entireMethod);

        var searchTypes = searchType == null ? OsuAssembly.Types : [searchType];

        foreach (var type in searchTypes)
            foreach (var method in type.GetConstructors(ConstructorFlags))
            {
                var instructions = method.GetMethodBody()?.GetILAsByteArray();
                if (instructions == null) continue;

                if (InstructionsMatchesSignature(instructions, signature, entireMethod))
                    return method;
            }

        return null;
    }

    /// <summary>
    ///     Check if some IL byte instructions contain a certain set of OpCodes.
    /// </summary>
    /// <param name="ilInstructions">
    ///     Raw IL instruction byte data obtained through <see cref="MethodBody.GetILAsByteArray()" /> for example.
    /// </param>
    /// <param name="signature">A set of sequential OpCodes to search for in instructions.</param>
    /// <param name="entireMethod">Whether the signature should be the entire method.</param>
    /// <returns></returns>
    internal static bool InstructionsMatchesSignature(
        IReadOnlyList<byte> ilInstructions,
        IReadOnlyList<OpCode> signature,
        bool entireMethod)
    {
        if (signature.Count == 0) return false;
        var instructions = new OpCodeReader(ilInstructions).GetOpCodes().Select(op => op.Value).ToArray();
        return Matches(instructions, signature.Select(op => op.Value).ToArray(), entireMethod);
    }

    /// <summary>
    ///     Check if decoded opcode values contain (or are exactly) a signature's opcode values.
    /// </summary>
    internal static bool Matches(short[] instructions, short[] signature, bool entireMethod)
    {
        if (signature.Length == 0) return false;
        if (entireMethod) return instructions.SequenceEqual(signature);

        for (var start = 0; start <= instructions.Length - signature.Length; start++)
        {
            var match = true;
            for (var offset = 0; offset < signature.Length; offset++)
                if (instructions[start + offset] != signature[offset])
                {
                    match = false;
                    break;
                }
            if (match) return true;
        }

        return false;
    }
}
