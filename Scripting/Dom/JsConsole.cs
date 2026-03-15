namespace Lite.Scripting.Dom;

public class JsConsole
{
    public void log(object? value)   => Console.WriteLine($"[JS] {value}");
    public void warn(object? value)  => Console.WriteLine($"[JS WARN] {value}");
    public void error(object? value) => Console.WriteLine($"[JS ERROR] {value}");
}
