using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;

namespace Osu.StablePlus.Utils.IL;

/// <summary>
///     Shares decoded IL between bindings while they are resolved in bulk, such as during patch installation.
///     Otherwise every whole-assembly signature search, <see cref="MethodReader.GetInstructions" /> and
///     <see cref="MethodReader.References" /> call decodes the same method bodies again.
///     Results are identical with or without a cache.
/// </summary>
public static class IlCache
{
    private static readonly object Sync = new();
    private static int depth;
    private static volatile Scope? current;

    internal static Scope? Current => current;

    /// <summary>
    ///     Cache decoded IL until the returned handle is disposed. Scopes may nest and may end on another thread.
    /// </summary>
    public static IDisposable Begin()
    {
        lock (Sync)
        {
            if (depth++ == 0) current = new Scope();
            return new Handle();
        }
    }

    private sealed class Handle : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            lock (Sync)
                if (--depth == 0) current = null;
        }
    }

    internal sealed class Scope
    {
        internal readonly ConcurrentDictionary<MethodBase, MethodReader.Body> Bodies = new();
        internal readonly ConcurrentDictionary<MethodBase, MethodReader.Operands?> Operands = new();
        private readonly ConcurrentDictionary<Module, ConcurrentDictionary<int, MemberInfo>> members = new();

        internal readonly SignatureIndex<MethodInfo> Methods =
            new(type => type.GetMethods(OpCodeMatcher.MethodFlags));

        internal readonly SignatureIndex<ConstructorInfo> Constructors =
            new(type => type.GetConstructors(OpCodeMatcher.ConstructorFlags));

        /// <summary>
        ///     Resolves a metadata token outside any generic context, as Harmony does for non-generic methods.
        /// </summary>
        internal MemberInfo Resolve(Module module, int token) =>
            members.GetOrAdd(module, _ => new ConcurrentDictionary<int, MemberInfo>())
                .GetOrAdd(token, module.ResolveMember);
    }

    /// <summary>
    ///     Every osu! type's members in the same order as an uncached search, with each method body decoded
    ///     at most once. Inherited members share their declaring method's opcodes.
    /// </summary>
    internal sealed class SignatureIndex<T> where T : MethodBase
    {
        private readonly Func<Type, T[]> getMembers;
        private readonly Dictionary<IntPtr, short[]?> opcodes = new();
        private T[][]? members;

        internal SignatureIndex(Func<Type, T[]> getMembers) => this.getMembers = getMembers;

        internal T? Find(IReadOnlyList<OpCode> signature, bool entireMethod)
        {
            var values = signature.Select(op => op.Value).ToArray();
            lock (opcodes)
            {
                members ??= OsuAssembly.Types.Select(getMembers).ToArray();
                foreach (var type in members)
                    foreach (var member in type)
                        if (Decode(member) is { } body && OpCodeMatcher.Matches(body, values, entireMethod))
                            return member;
            }

            return null;
        }

        private short[]? Decode(T member)
        {
            var handle = member.MethodHandle.Value;
            if (opcodes.TryGetValue(handle, out var body)) return body;
            // Decoding errors propagate exactly as they would from an uncached search, and are retried.
            var il = member.GetMethodBody()?.GetILAsByteArray();
            return opcodes[handle] = il == null ? null : new OpCodeReader(il).GetOpCodes().Select(op => op.Value).ToArray();
        }
    }
}
