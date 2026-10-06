using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Osu.StablePlus.Stubs.Helpers;
using Osu.StablePlus.Stubs.Root;
using Osu.StablePlus.Utils.IL;
using Osu.StablePlus.Utils.Lazy;
using static System.Reflection.Emit.OpCodes;

namespace Osu.StablePlus.Stubs.Online;

public static class BanchoClient
{
    [Stub]
    public static readonly LazyType Class = new("osu.Online.BanchoClient", () =>
        MethodReader.GetInstructions(OsuDirect.HandlePickup.Reference).Select(i => i.Operand)
            .OfType<FieldInfo>().First(f => f.IsStatic).DeclaringType!);

    [Stub]
    public static readonly LazyMethod Connect = new("BanchoClient::Connect", () =>
        Class.Reference.GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Single(m =>
            m.ReturnType == typeof(void) && m.GetParameters().Length == 0 &&
            MethodReader.GetInstructions(m).Any(i => i.Operand is MethodInfo call &&
                call.DeclaringType == typeof(TimeZone) && call.Name == nameof(TimeZone.GetUtcOffset))));

    [Stub]
    public static readonly LazyMethod<bool> HasCredentials = new("GameBase::HasCredentials", () =>
        MethodReader.GetInstructions(Connect.Reference).Select(i => i.Operand).OfType<MethodInfo>()
            .First(m => m.DeclaringType == GameBase.Class.Reference && m.ReturnType == typeof(bool)));

    [Stub]
    public static readonly LazyField<object?> Password = new("ConfigManager::Password", () =>
        MethodReader.GetInstructions(HasCredentials.Reference).Select(i => i.Operand).OfType<FieldInfo>().First());

    [Stub]
    public static readonly LazyField<bool> Connected = new("BanchoClient::Connected", () =>
    {
        var disconnected = Convert.ToInt32(Enum.Parse(OsuString.Class.Reference, "BanchoClient_Disconnected"));
        var disconnect = Class.Reference.GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Single(m =>
            m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(bool) }) &&
            MethodReader.GetInstructions(m).Any(i => i.Opcode == Ldc_I4 && Equals(i.Operand, disconnected)));
        return MethodReader.GetInstructions(disconnect).Select(i => i.Operand).OfType<FieldInfo>().First();
    });

    [Stub]
    public static readonly LazyMethod Login = new("Options::Login(string, string)", () =>
        GameModes.Options.Options.Class.Reference.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic).Single(m =>
            m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(string), typeof(string) }) &&
            MethodReader.GetInstructions(m).Any(i => i.Opcode == Stsfld && Equals(i.Operand, Password.Reference))));

    [Stub]
    public static readonly LazyMethod Logout = new("Options::Logout()", () =>
        Login.Reference.DeclaringType!.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic).Single(m =>
            m.ReturnType == typeof(void) && m.GetParameters().Length == 0 &&
            MethodReader.GetInstructions(m).Any(i => i.Opcode == Stsfld && Equals(i.Operand, Password.Reference))));


    [Stub]
    public static readonly LazyMethod Send = new("BanchoClient::SendPackets(bool)", () =>
        Class.Reference.GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Single(m =>
            m.ReturnType == typeof(void) &&
            m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(bool) }) &&
            MethodReader.GetInstructions(m).Any(i => i.Opcode == Newobj && i.Operand is ConstructorInfo ctor &&
                ctor.DeclaringType == typeof(MemoryStream))));
}
