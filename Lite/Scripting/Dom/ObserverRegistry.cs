using System.Runtime.CompilerServices;
using Jint;

namespace Lite.Scripting.Dom;

/// <summary>Registrations follow their Jint realm's lifetime instead of the last loaded page.</summary>
internal sealed class ObserverRegistry<T> where T : class
{
    private readonly ConditionalWeakTable<Engine, List<T>> _realms = new();
    internal List<T> For(Engine engine) => _realms.GetValue(engine, _ => []);
    internal void Register(Engine engine, T observer) => For(engine).Add(observer);
}
