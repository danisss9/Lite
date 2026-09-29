# QuickJS patch order

The build applies numbered `.patch` files to the pinned, unmodified QuickJS
2026-06-04 archive. A failed `git apply --check` stops the build. Never edit the
cached source tree to make a build pass.

`0001-strict-tail-call-trampoline.patch` releases the current bytecode frame
before entering a direct strict tail call. This covers direct and mutual calls
that QuickJS already emits as tail-call opcodes. Conditional expression tail
positions, forwarding through bound/proxy calls, and other spec cases still
need implementation and full Test262 evidence; this patch alone does not make
the engine ES2020 conformant.
