using System.Text.Json.Nodes;
using Lite.Conformance.Harness;
using Lite.Conformance.Test262;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

public static class Es2020Tests
{
    [Test]
    public static void Metadata_HandlesMultilineYamlAndRejectsAmbiguity()
    {
        var metadata = Test262Metadata.Parse("""
            /*---
            description: >
              Text: contains punctuation, [brackets] and quoted scalars.
            esid: sec-test
            includes:
              - "assert.js"
            flags: [onlyStrict, generated]
            features: ["BigInt"]
            negative: { phase: runtime, type: TypeError }
            ---*/
            """);
        Equal(0, metadata.Errors.Length);
        Equal("strict", metadata.Modes.Single());
        Equal("TypeError", metadata.NegativeType);
        Contains("\"modes\"", System.Text.Json.JsonSerializer.Serialize(metadata, ExecutionEvidence.JsonOptions));
        True(Test262Metadata.Parse("/*---\nflags: [onlyStrict, noStrict]\n---*/").Errors.Length > 0);
        True(Test262Metadata.Parse("/*---\nincludes: [../outside.js]\n---*/").Errors.Length > 0);
        True(Test262Metadata.Parse("/*---\nflags: [unknown]\n---*/").Errors.Length > 0);
        True(Test262Metadata.Parse("/*---\nflags: [raw]\nflags: [onlyStrict]\n---*/").Errors.Length > 0);
    }

    [Test]
    public static void NegativeTests_RequireExactPhaseAndErrorType()
    {
        using var fixture = new Fixture();
        Equal("pass", fixture.Run("flags: [raw]\nnegative: { phase: parse, type: SyntaxError }", "const = ;", "raw").Outcome);
        Equal("fail", fixture.Run("flags: [raw]\nnegative: { phase: parse, type: SyntaxError }", "throw new SyntaxError();", "raw").Outcome);
        Equal("fail", fixture.Run("flags: [raw]\nnegative: { phase: runtime, type: TypeError }", "throw new Error('TypeError');", "raw").Outcome);
        Equal("fail", fixture.Run("flags: [raw]\nnegative: { phase: runtime, type: TypeError }", "throw {constructor: TypeError};", "raw").Outcome);
        Equal("pass", fixture.Run("flags: [raw]\nnegative: { phase: parse, type: SyntaxError }", "return 1;", "raw").Outcome);
        Equal("pass", fixture.Run("flags: [module]\nnegative: { phase: runtime, type: TypeError }", "throw new TypeError();", "module").Outcome);
        File.WriteAllText(Path.Combine(fixture.Root, "test", "empty_FIXTURE.js"), "export const existing = 1;");
        Equal("pass", fixture.Run("flags: [module]\nnegative: { phase: resolution, type: SyntaxError }", "import { missing } from './empty_FIXTURE.js';", "module").Outcome);
        Equal("fail", fixture.Run("flags: [module]\nnegative: { phase: resolution, type: SyntaxError }", "throw new SyntaxError();", "module").Outcome);
    }

    [Test]
    public static void AsyncTests_RejectDuplicateAndLateCompletionFailures()
    {
        using var fixture = new Fixture();
        Equal("pass", fixture.Run("flags: [async]", "Promise.resolve().then(() => $DONE());", "sloppy").Outcome);
        Equal("fail", fixture.Run("flags: [async]", "$DONE(); Promise.resolve().then(() => $DONE(new Error('late')));", "sloppy").Outcome);
        Equal("fail", fixture.Run("flags: [async]", "$DONE(); $DONE();", "strict").Outcome);
        Equal("fail", fixture.Run("flags: [async]", "$DONE(); Promise.resolve().then(() => {throw new Error('late');});", "sloppy").Outcome);
        Equal("harness-error", fixture.Run("includes: [missing.js]", "", "sloppy").Outcome);
        Equal("pass", fixture.Run("flags: [raw]", "if (typeof assert !== 'undefined') throw Error('raw loaded harness');", "raw").Outcome);
    }

    [Test]
    public static void HostAdapter_InstallsNestedRealmsAndDetachesBuffers()
    {
        using var fixture = new Fixture();
        Equal("pass", fixture.Run("features: [cross-realm]", """
            const first = $262.createRealm();
            const second = first.createRealm();
            assert.notSameValue(first.global.Array, Array);
            assert.notSameValue(first.global.Array, second.global.Array);
            assert.sameValue(typeof second.agent.start, 'function');
            assert.throws(second.global.SyntaxError, function() { second.evalScript('return 1;'); });
            first.evalScript('globalThis.marker=42;');
            assert.sameValue(first.global.marker, 42);
            assert.sameValue(typeof marker, 'undefined');
            const buffer = new ArrayBuffer(8);
            $262.detachArrayBuffer(buffer);
            assert.sameValue(buffer.byteLength, 0);
            """, "sloppy").Outcome);
    }

    [Test]
    public static void Worker_RecoversAfterHardTimeout()
    {
        using var fixture = new Fixture();
        fixture.Write("flags: [raw]", "while (true) {}");
        using var worker = new Test262Runner.WorkerClient(fixture.Root);
        Equal("timeout", worker.Run(new("test/case.js", "raw"), timeoutMs: 1000).Outcome);
        fixture.Write("flags: [raw]", "1+1;");
        Equal("pass", worker.Run(new("test/case.js", "raw")).Outcome);
    }

    [Test]
    public static void Readiness_RejectsMissingModesDuplicatesAndSmokeEvidence()
    {
        var metadata = Test262Metadata.Parse("/*---\nfeatures: [BigInt]\n---*/");
        var inventory = new Test262Inventory("pin", "hash", true, [], [new("test/case.js", "source", metadata, "included", "target", false)]);
        TestEvidence Pass(string mode) => new("test262", "test/case.js", "pass", "ok", [new(mode, 0, null)],
            Context: "javascript", Kind: "language", JavaScript: new(mode, "hash", "full", 0, 1, null, null, null, null, null, 1));
        var sloppy = Pass("sloppy"); var strict = Pass("strict");
        Equal(1, Es2020Readiness.Evaluate([sloppy], inventory).PassedExecutions);
        Equal(2, Es2020Readiness.Evaluate([sloppy, strict], inventory).PassedExecutions);
        Equal(1, Es2020Readiness.Evaluate([sloppy, strict, strict], inventory).PassedExecutions);
        Equal(1, Es2020Readiness.Evaluate([sloppy, strict with { JavaScript = strict.JavaScript! with { Selection = "smoke" } }], inventory).PassedExecutions);
        True(Es2020Readiness.Evaluate([sloppy with { JavaScript = sloppy.JavaScript! with { ShardCount = 8 } }], inventory)
            .Blockers.Contains("es2020-incomplete-or-conflicting-shards"));
        True(Es2020Readiness.Evaluate([sloppy with { JavaScript = sloppy.JavaScript! with { ShardIndex = 1, ShardCount = 8 } }], inventory)
            .Blockers.Contains("es2020-invalid-execution-identity"));
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(ConformancePaths.EnsureArtifacts(), "es2020-fixture-" + Guid.NewGuid().ToString("N"));
        internal Fixture()
        {
            Directory.CreateDirectory(Path.Combine(Root, "test")); Directory.CreateDirectory(Path.Combine(Root, "harness"));
            foreach (var file in new[] { "assert.js", "sta.js", "doneprintHandle.js" })
                File.Copy(Path.Combine(Test262Catalog.Root, "harness", file), Path.Combine(Root, "harness", file));
        }
        internal void Write(string metadata, string code) => File.WriteAllText(Path.Combine(Root, "test", "case.js"), "/*---\n" + metadata + "\n---*/\n" + code);
        internal Test262Outcome Run(string metadata, string code, string mode)
        { Write(metadata, code); return Test262Execution.Run(Root, "test/case.js", mode); }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
