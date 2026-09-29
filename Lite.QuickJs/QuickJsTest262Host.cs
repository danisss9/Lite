namespace Lite.QuickJs;

/// <summary>Installs the Test262 realm primitives in QuickJS contexts owned by one runtime.</summary>
internal sealed class QuickJsTest262Host(QuickJsRuntime runtime)
{
    internal void Install(QuickJsRealm realm)
    {
        using var api = realm.Object();
        using var global = realm.Global();
        api.Set("global", global);

        using var eval = realm.HostFunction("evalScript", 1, (caller, args) =>
        {
            var source = args.Length == 0 ? "undefined" : args[0].AsString();
            using var result = realm.Eval(source, "<Test262 evalScript>");
            return caller.ImportFrom(result);
        });
        api.Set("evalScript", eval);

        using var create = realm.HostFunction("createRealm", 0, (caller, _) =>
        {
            var child = runtime.CreateRealm();
            Install(child);
            using var childGlobal = child.Global();
            using var childApi = childGlobal.Get("$262");
            return caller.ImportFrom(childApi);
        });
        api.Set("createRealm", create);

        using var detach = realm.HostFunction("detachArrayBuffer", 1, (caller, args) =>
        {
            if (args.Length == 0) throw new ArgumentException("detachArrayBuffer requires a buffer");
            args[0].DetachArrayBuffer();
            return caller.Undefined();
        });
        api.Set("detachArrayBuffer", detach);

        using var gc = realm.HostFunction("gc", 0, (caller, _) =>
        {
            runtime.CollectGarbage();
            return caller.Undefined();
        });
        api.Set("gc", gc);

        using var agent = realm.Object();
        using var start = realm.HostFunction("start", 1, (_, _) =>
            throw new NotSupportedException("Test262 agent execution is not implemented for QuickJS"));
        agent.Set("start", start);
        api.Set("agent", agent);

        using var htmlDda = realm.Object();
        htmlDda.SetIsHtmlDda();
        api.Set("IsHTMLDDA", htmlDda);
        global.Set("$262", api);
    }
}
