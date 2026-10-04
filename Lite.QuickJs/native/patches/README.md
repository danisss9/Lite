# QuickJS patch order

The build applies numbered `.patch` files to the pinned, unmodified QuickJS
2026-06-04 archive. A failed `git apply --check` stops the build. Never edit the
cached source tree to make a build pass.

`0001-strict-tail-call-trampoline.patch` releases the current bytecode frame
before entering a strict tail call. `0008-proper-tail-calls.patch` extends
coverage to the remaining tail positions: conditional-expression and catch
shared returns (resolved through phase 2 label addresses), the `nip_catch`
form inside catch blocks, sloppy callers (the tail-call conversion is
unconditional and the interpreter falls back to a plain returning call), and
`eval(...)` calls whose callee shadows `%eval%` through the new `tail_eval`
opcode. All six tail-position Test262 cases
(`tco-non-eval-{function,function-dynamic,global,with}`, `tco-cond`,
`tco-catch`) pass with the series applied.

Patches 0002-0007 fix published engine defects: strict assignment to an
unresolvable reference and direct-eval `var` binding visibility (0002, 0006),
module namespace TDZ reads on define and set (0003, 0004), Annex B call
assignment targets (0005), and ambiguous re-export resolution (0007).
