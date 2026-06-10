using System.Reflection;

namespace Lite.Tests;

/// <summary>Temporary: dumps Jint's module API so we can wire modules against the real signatures.</summary>
public static class Probe
{
    public static void Dump()
    {
        var asm = typeof(Jint.Engine).Assembly;

        Console.WriteLine("=== Engine public/internal methods (job/continuation/advanced) ===");
        foreach (var m in typeof(Jint.Engine).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                     .Where(m => m.Name.Contains("Continu", StringComparison.OrdinalIgnoreCase)
                              || m.Name.Contains("Job", StringComparison.OrdinalIgnoreCase)
                              || m.Name.Contains("Advanced", StringComparison.OrdinalIgnoreCase)
                              || m.Name.Contains("Microtask", StringComparison.OrdinalIgnoreCase)))
            Console.WriteLine($"  {(m.IsPublic ? "pub" : "int")} {m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))})");
        var adv = typeof(Jint.Engine).GetProperty("Advanced");
        Console.WriteLine("Advanced prop type: " + (adv?.PropertyType.FullName ?? "(none)"));
        if (adv is not null)
            foreach (var m in adv.PropertyType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                Console.WriteLine($"  Advanced.{m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))})");
        Console.WriteLine("=== Engine.Modules property type ===");
        var modulesProp = typeof(Jint.Engine).GetProperty("Modules");
        Console.WriteLine(modulesProp?.PropertyType.FullName ?? "(none)");
        if (modulesProp is not null)
        {
            foreach (var m in modulesProp.PropertyType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                Console.WriteLine("  " + Sig(m));
        }

        Console.WriteLine("=== Types matching Module/Loader ===");
        foreach (var t in asm.GetTypes().Where(t => t.IsPublic && (t.Name.Contains("Module") || t.Name.Contains("Loader") || t.Name.Contains("ResolvedSpecifier") || t.Name.Contains("ModuleRequest"))))
            Console.WriteLine("  " + t.FullName);

        Console.WriteLine("=== IModuleLoader members ===");
        var loader = asm.GetType("Jint.Runtime.Modules.IModuleLoader");
        if (loader is not null)
            foreach (var m in loader.GetMethods())
                Console.WriteLine("  " + Sig(m));

        Console.WriteLine("=== ModuleFactory members ===");
        var factory = asm.GetType("Jint.Runtime.Modules.ModuleFactory");
        if (factory is not null)
            foreach (var m in factory.GetMethods(BindingFlags.Public | BindingFlags.Static))
                Console.WriteLine("  " + Sig(m));

        Console.WriteLine("=== ResolvedSpecifier ctors ===");
        var rs = asm.GetType("Jint.Runtime.Modules.ResolvedSpecifier");
        if (rs is not null)
        {
            foreach (var c in rs.GetConstructors())
                Console.WriteLine("  ctor(" + string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")");
            foreach (var p in rs.GetProperties())
                Console.WriteLine("  prop " + p.PropertyType.Name + " " + p.Name);
        }

        Console.WriteLine("=== Options module members ===");
        var opts = asm.GetType("Jint.Options");
        if (opts is not null)
        {
            foreach (var m in opts.GetMethods().Where(m => m.Name.Contains("Module")))
                Console.WriteLine("  method " + Sig(m));
            foreach (var p in opts.GetProperties().Where(p => p.Name.Contains("Module")))
                Console.WriteLine("  prop " + p.PropertyType.FullName + " " + p.Name);
        }
        // Extension methods for EnableModules
        foreach (var t in asm.GetTypes().Where(t => t.IsPublic && t.IsAbstract && t.IsSealed))
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == "EnableModules"))
                Console.WriteLine($"  ext {t.Name}.{Sig(m)}");

        Console.WriteLine("=== ModuleRequest members ===");
        var mr = asm.GetType("Jint.Runtime.Modules.ModuleRequest");
        if (mr is not null)
            foreach (var p in mr.GetProperties())
                Console.WriteLine("  prop " + p.PropertyType.Name + " " + p.Name);

        Console.WriteLine("=== SpecifierType values ===");
        var st = asm.GetType("Jint.Runtime.Modules.SpecifierType");
        if (st is not null)
            Console.WriteLine("  " + string.Join(", ", Enum.GetNames(st)));

        Console.WriteLine("=== DefaultModuleLoader ctors ===");
        var dml = asm.GetType("Jint.Runtime.Modules.DefaultModuleLoader");
        if (dml is not null)
            foreach (var c in dml.GetConstructors())
                Console.WriteLine("  ctor(" + string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")");
    }

    private static string Sig(MethodInfo m) =>
        $"{m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})";
}
