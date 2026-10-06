using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Osu.StablePlus.Hook.Patches;
using Osu.StablePlus.Stubs.Helpers;
using Osu.StablePlus.Stubs.Wrappers;
using Osu.StablePlus.Utils.IL;

namespace Osu.StablePlus.Hook;

/// <summary>Save all global options after native binding changes, independently of rate edits.</summary>
internal sealed class SettingsPersistence : IDisposable
{
    private readonly List<object> bindings = new();
    private readonly EventHandler changed;
    private readonly MethodInfo remove;
    private bool queued;
    private bool disposed;

    internal SettingsPersistence(IReadOnlyList<PatchOptions> options, string directory, Action<Action> schedule)
    {
        // Stable strips event names, but the observable interface retains its add/remove methods.
        var observable = ValueChangedObservable.Class.Reference;
        var mapping = BindableBool.Class.Reference.GetInterfaceMap(observable);
        var handlers = Enumerable.Range(0, mapping.InterfaceMethods.Length).Where(i =>
            mapping.InterfaceMethods[i].GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(EventHandler) })).ToArray();
        int Find(string operation) => handlers.Single(i => MethodReader.GetInstructions(mapping.TargetMethods[i])
            .Any(instruction => instruction.Operand is MethodInfo method && method.DeclaringType == typeof(Delegate) && method.Name == operation));
        var add = mapping.InterfaceMethods[Find(nameof(Delegate.Combine))];
        remove = mapping.InterfaceMethods[Find(nameof(Delegate.Remove))];
        changed = (_, _) =>
        {
            if (queued || disposed) return;
            queued = true;
            // Save after option callbacks (including DT/HT pitch updates), once per update.
            schedule(() =>
            {
                queued = false;
                if (disposed) return;
                try
                {
                    var settings = new Settings();
                    foreach (var option in options) option.Save(settings);
                    Settings.WriteToDisk(settings, directory);
                }
                catch (Exception e) { Console.WriteLine("[Settings] Unable to save: " + e); }
            });
        };
        foreach (var option in options)
            foreach (var field in option.GetType().GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var value = field.GetValue(null);
                if (field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(BindableWrapper<>))
                    value = field.FieldType.GetProperty(nameof(BindableWrapper<bool>.Bindable))!.GetValue(value, null);
                // Reference identity: a bindable's Equals may compare values.
                if (value == null || !observable.IsInstanceOfType(value) || bindings.Any(b => ReferenceEquals(b, value))) continue;
                add.Invoke(value, new object[] { changed });
                bindings.Add(value);
            }
    }

    public void Dispose()
    {
        disposed = true;
        foreach (var binding in bindings) remove.Invoke(binding, new object[] { changed });
        bindings.Clear();
    }
}
