using System.Reflection;
using Jint;
using Jint.Native.Object;

namespace Lite.Conformance.Test262;

/// <summary>The only adapter to Jint's internal conformance host; never installed in Lite pages.</summary>
internal sealed class Test262Host : IDisposable
{
    private readonly IDisposable _agents;
    internal Test262Host(Engine engine)
    {
        var assembly = typeof(Engine).Assembly;
        var hostType = assembly.GetType("Jint.Test262Object", throwOnError: true)!;
        var install = hostType.GetMethod("Install", BindingFlags.Public | BindingFlags.Static, null, [typeof(Engine)], null)
            ?? throw new MissingMethodException("Stock Jint Test262Object.Install(Engine) is unavailable");
        var container = (ObjectInstance)install.Invoke(null, [engine])!;
        var agentsType = assembly.GetType("Jint.Test262AgentManager", throwOnError: true)!;
        _agents = (IDisposable)Activator.CreateInstance(agentsType, nonPublic: true)!;
        var installAgents = agentsType.GetMethod("InstallAgent", BindingFlags.Public | BindingFlags.Instance, null,
            [typeof(Engine), typeof(ObjectInstance)], null) ?? throw new MissingMethodException("Stock Jint Test262 agent API unavailable");
        installAgents.Invoke(_agents, [engine, container]);
        foreach (var name in new[] { "createRealm", "detachArrayBuffer", "evalScript", "gc", "global", "IsHTMLDDA", "agent" })
            if (container.Get(name).IsUndefined()) throw new InvalidOperationException($"Stock Jint Test262 host missing {name}");
    }
    public void Dispose() => _agents.Dispose();
}
