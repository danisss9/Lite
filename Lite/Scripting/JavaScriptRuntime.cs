using Jint;

namespace Lite.Scripting;

/// <summary>Language options shared by the browser and the ECMA-262 harness.</summary>
internal static class JavaScriptRuntime
{
    internal static readonly ScriptParsingOptions ScriptParsing = ScriptParsingOptions.Default with
    {
        AllowReturnOutsideFunction = false,
        Tolerant = false,
        RetainFunctionSourceText = true,
    };

    internal static void Configure(Options options, bool canBlock = false)
    {
        // Strictness belongs to source text, not to the embedding application.
        options.Strict = false;
        options.RetainFunctionSourceText = true;
        options.AgentCanSuspend = canBlock;
    }
}
