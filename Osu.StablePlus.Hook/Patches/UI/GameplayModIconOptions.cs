using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Osu.StablePlus.Hook.Patches.Mods.CustomRate;
using Osu.StablePlus.Utils;
using Osu.StablePlus.Utils.IL;
using static System.Reflection.Emit.OpCodes;

namespace Osu.StablePlus.Hook.Patches.UI;

internal sealed class GameplayModIconOptions : PatchOptions
{
    private const BindingFlags Members = NativeModMenu.Members;
    internal static readonly ConstructorInfo Slider = OsuAssembly.Types.SelectMany(t => t.GetConstructors(Members))
        .Single(c => c.GetParameters().Length == 4 && c.GetParameters()[0].ParameterType.FullName == "osu_common.Helpers.OsuString" &&
            c.GetParameters()[2].ParameterType == typeof(string) && c.GetParameters()[3].ParameterType == typeof(bool) &&
            MethodReader.GetInstructions(c).Any(i => Equals(i.Operand, NativeModMenu.Slider)));
    private static readonly Type Number = Slider.GetParameters()[1].ParameterType;
    private static readonly Type Integer = MethodReader.GetInstructions(Slider).Where(i => i.Opcode == Isinst)
        .Select(i => i.Operand).OfType<Type>().Single();
    private static readonly MethodInfo Get = Number.GetMethods(Members).Single(m => m.IsVirtual && m.ReturnType == typeof(double) && m.GetParameters().Length == 0);
    private static readonly MethodInfo Set = Number.GetMethods(Members).Single(m => m.IsVirtual && m.ReturnType == typeof(void) &&
        m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(double));
    private static readonly object Binding = CreateBinding();

    private static object CreateBinding()
    {
        var binding = Integer.GetConstructor([typeof(int)])!.Invoke([0]);
        var bounds = MethodReader.GetInstructions(Slider).Select(i => i.Operand).OfType<MethodInfo>()
            .Where(m => m.DeclaringType == Number && m.ReturnType == typeof(double) && m != Get).Distinct().ToArray();
        if (bounds.Length != 2) throw new InvalidOperationException("Expected numeric slider minimum and maximum getters.");
        for (var i = 0; i < bounds.Length; i++)
            MethodReader.GetInstructions(bounds[i]).Select(instruction => instruction.Operand).OfType<FieldInfo>().Single()
                .SetValue(binding, i == 0 ? 0d : 100d);
        return binding;
    }

    internal static int Percent
    {
        get => Math.Max(0, Math.Min(100, (int)(double)Get.Invoke(Binding, null)));
        set => Set.Invoke(Binding, [(double)Math.Max(0, Math.Min(100, value))]);
    }

    public override IEnumerable<object> CreateOptions()
    {
        var title = CustomStrings.CustomStrings.AddOsuString("GameplayModIconOpacity", "Gameplay mod icon opacity");
        yield return Slider.Invoke([Enum.ToObject(Slider.GetParameters()[0].ParameterType, title), Binding, "%", true]);
    }

    public override void Load(Settings config) => Percent = config.GameplayModIconOpacity;
    public override void Save(Settings config) => config.GameplayModIconOpacity = Percent;
}
