using Lite.QuickJs;

namespace Lite.Conformance.Test262;

/// <summary>Test262 host APIs installed into QuickJS realms only.</summary>
internal sealed class Test262Host
{
    internal Test262Host(QuickJsRuntime runtime, QuickJsRealm realm)
    {
        new QuickJsTest262Host(runtime).Install(realm);
    }
}
