using System.Reflection;
using Jint;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Interop;
using Lite.Scripting;

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
        CompleteApi(container);
        foreach (var name in new[] { "createRealm", "detachArrayBuffer", "evalScript", "gc", "global", "IsHTMLDDA", "agent" })
            if (container.Get(name).IsUndefined()) throw new InvalidOperationException($"Stock Jint Test262 host missing {name}");

        void CompleteApi(ObjectInstance api)
        {
            installAgents.Invoke(_agents, [engine, api]);
            var evalScript = api.Get("evalScript");
            var syntaxError = ((ObjectInstance)api.Get("global")).Get("SyntaxError");
            api.Set("evalScript", new ClrFunction(engine, "evalScript", (_, args) =>
            {
                // Stock evalScript uses permissive script parsing defaults. Validate
                // Script grammar before invoking its realm-aware evaluation routine.
                try { Engine.PrepareScript(TypeConverter.ToString(args[0]), options: new ScriptPreparationOptions
                    { ParsingOptions = JavaScriptRuntime.ScriptParsing }); }
                catch (Exception error) when (error is Acornima.SyntaxErrorException || error.InnerException is Acornima.SyntaxErrorException)
                { throw new JavaScriptException(engine.Construct(syntaxError, [(Jint.Native.JsValue)error.Message])); }
                return engine.Invoke(evalScript, args);
            }));
            // Stock Install creates further realms without installing the agent API.
            // Reuse this test's manager so all realms still share the same agent cluster.
            var createRealm = api.Get("createRealm");
            api.Set("createRealm", new ClrFunction(engine, "createRealm", (_, _) =>
            {
                var nested = (ObjectInstance)engine.Invoke(createRealm);
                CompleteApi(nested);
                return nested;
            }));
        }
    }
    public void Dispose() => _agents.Dispose();
}
