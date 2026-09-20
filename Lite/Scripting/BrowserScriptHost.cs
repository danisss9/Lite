using Jint.Native;
using Jint.Runtime;
using Jint.Runtime.Modules;

namespace Lite.Scripting;

internal sealed class BrowserScriptHost(HttpModuleLoader loader) : Host
{
    public override List<KeyValuePair<JsValue, JsValue>> GetImportMetaProperties(Module moduleRecord) =>
        [new("url", loader.ResponseUrl(moduleRecord.Location))];
}
