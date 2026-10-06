using System.Linq;
using JetBrains.Annotations;
using Osu.StablePlus.Utils.Extensions;
using Osu.StablePlus.Utils.Lazy;

namespace Osu.StablePlus.Stubs.Helpers;

[PublicAPI]
public static class ValueChangedObservable
{
    /// <summary>
    ///     Original: <c>osu.Helpers.ValueChangedObservable</c>
    /// </summary>
    [Stub]
    public static readonly LazyType Class = new(
        "osu.Helpers.ValueChangedObservable",
        () => Bindable.Generic.Class.Reference
            .GetInterfaces()
            .Where(type => type.GetMembers().Length == 5)
            .SingleOrNull()!
    );
}
