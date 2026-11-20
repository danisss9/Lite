# CSS 2.1 Phase 6: parallel lanes

Phase 6 of [the CSS 2.1 plan](../css21-conformance-plan.md) (the engine queue) is split into lanes that
separate agent sessions work at the same time. Each lane has its own branch and worktree, its own case
list and a set of files it owns. The integrator session merges lane branches into
`css21/phase6-engine-queue` in batches and runs the full baseline after each batch.

Base commit for wave 1: the partial-class split on `css21/phase6-engine-queue`. That split moved
`BoxEngine`, `Drawer` and `Parser` into per-area partial files.

## Lanes

| Lane | Scope | Notes file |
| --- | --- | --- |
| 6.A | Inline formatting and the line box | [6A-inline.md](6A-inline.md) |
| 6.B | Floats and clear | [6B-floats.md](6B-floats.md) |
| 6.C | Block flow, margin collapse and sizing | [6C-block.md](6C-block.md) |
| 6.D | Positioning, stacking and the viewport | [6D-positioning.md](6D-positioning.md) |
| 6.E | Tables | [6E-tables.md](6E-tables.md) |
| 6.F | Generated content, counters, quotes and lists | [6F-generated.md](6F-generated.md) |
| 6.G | Bidi (wave 2, after the first merge batch) | [6G-bidi.md](6G-bidi.md) |
| 6.H | Backgrounds | [6H-backgrounds.md](6H-backgrounds.md) |
| 6.I | Borders, outline and visual effects | [6I-borders.md](6I-borders.md) |
| 6.J | Fonts | [6J-fonts.md](6J-fonts.md) |
| 6.K | Selectors, cascade and syntax | [6K-cascade.md](6K-cascade.md) |
| 6.M | Judges and verdict routes (no engine changes) | [6M-judges.md](6M-judges.md) |
| 6.X | Evidence, exclusions and the `xhtml1` variant (no engine changes) | [6X-evidence.md](6X-evidence.md) |

The plan's lane L (mixed-corpus triage) was dropped. Its stem rules route the WPT `css1/` cases to
their owning lanes, which left L only the four inline-svg cases. Those are an open scope decision,
so they belong to 6.X.

## Case lists

`scripts/split-css21-lanes.py` reads a baseline's shard reports and writes these files to
`Lite.Conformance/artifacts/lanes/`:

- `<lane>.txt`: the lane's failing, crashing, timing-out and empty cases;
- `<lane>.tsv`: the same cases with their cluster and the runner's detail;
- `judges.txt`: the unsupported and manual-pending cases, which belong to 6.M;
- `canary.txt`: 300 passing cases, 25 per lane;
- `summary.md`: counts per lane.

Assignment goes by test-name stem first, then by WPT directory. A case that belongs to another lane
moves there: add a line to the other lane's notes under "Handed in". Do not edit the list files.
The integrator regenerates them after every batch.

**`artifacts/` is ignored by git, so the lists are not in your worktree.** Read them from the main
checkout, `C:\Repos\Lite\Lite.Conformance\artifacts\lanes\`. Copy the `.txt` files you run into
your own worktree's `Lite.Conformance\artifacts\lanes\` (create it), because `--filter @file`
takes a path.

## Rules for every lane

1. **Work in your own worktree and branch** (`C:\Repos\Lite-p6-<lane>`, branch `css21/p6-<lane>`).
   Never commit in `C:\Repos\Lite`. That is the integrator's checkout.
2. **Edit only the files your notes file lists as owned.** If a fix needs a file you do not own:
   - keep the edit minimal and put it in a commit of its own;
   - name it in your notes under "Edits outside owned files", so the integrator can expect the
     merge.
   Never edit these:
   - `docs/css21-conformance-plan.md` or `docs/conformance-status.md` (the integrator folds your
     notes in);
   - `css21-requirements.json` statuses (only `scripts/gen_css21_obligation_status.py` writes them);
   - `css21-pass-conditions.json` and `css21-pass-condition-reviews.json` (only 6.M writes them);
   - `css21-applicability.json` (only 6.X writes it; send reclassification candidates to 6.X's
     notes under "Handed in").
3. **Fix the behavior, not the test.** There are no test-specific hacks. Cite the spec section in
   the code comment and the commit message. Every fix comes with a focused unit test in
   `Lite.Tests` (the custom runner: add the method where its neighbours are registered).
4. **Verify each commit, locally and on one shard.** All lanes run at the same time on one
   12-core machine, so:
   - never start `wpt serve` (port 8000 belongs to the integrator);
   - never run `scripts/run-css21-baseline.ps1`;
   - never run the HTML5 gate;
   - run at most one conformance process at a time.

   ```powershell
   dotnet build Lite.sln -c Release
   dotnet run --project Lite.Tests -c Release --no-build
   dotnet run --project Lite.Conformance/Lite.Conformance.csproj -c Release --no-build -- --suite css21
   dotnet run --project Lite.Conformance/Lite.Conformance.csproj -c Release --no-build -- --suite wpt
   dotnet run --project Lite.Conformance/Lite.Conformance.csproj -c Release --no-build -- --suite css21-full --media screen --filter "@Lite.Conformance/artifacts/lanes/<lane>.txt" --report Lite.Conformance/artifacts/<lane>-after.json
   dotnet run --project Lite.Conformance/Lite.Conformance.csproj -c Release --no-build -- --suite css21-full --media screen --filter "@Lite.Conformance/artifacts/lanes/canary.txt" --report Lite.Conformance/artifacts/canary-after.json
   ```

   - **Units:** all pass.
   - **Curated gates:** css21 51 passes with 1 published expected-fail, wpt 86 with 2.
   - **Lane list:** report what moved, case by case.
   - **Canary:** all 300 still pass. A loss on the canary or the curated gates blocks the commit.

   A case that times out or flakes under contention counts only after a re-run on its own. The
   known worker-state flake is `tables-102`. Local serving matches upstream for css/CSS2 (checked
   on 16 and 19 November), so a local result is good lane evidence.
5. **Commits:** one concern per commit. The message names the spec section, the cases that moved,
   and the units and gates read. It ends with the attribution line
   `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Do not push, and do not merge other
   lanes.
6. **Record progress in your notes file** after every commit, under "Progress", with these points:
   - what moved, with case names;
   - what still fails and why, honestly;
   - what was handed to another lane;
   - the reading of each check (units, gates, lane list, canary).
7. **Finish a batch, then stop.** After a coherent batch, or when the lane runs out of fixable
   cases, end your turn with a short report. The integrator merges, runs the full baseline, and
   sends you the next queue.

## Integration (the integrator only)

- **Merge order in a batch:** 6.J → 6.K → 6.C → 6.B → 6.D → 6.A → 6.E → 6.F → 6.H → 6.I → 6.M → 6.X.
  Units and gates run after each merge.
- **The full upstream baseline** (`scripts/run-css21-baseline.ps1 -StartWptServe`) runs on the
  committed merge head, after the lanes have paused their conformance runs. It is diffed per case
  against the previous batch.
- **0-loss rule:** a lost case reverts the responsible commit, which goes back to its lane with
  the case named.
- **After the batch:** regenerate the lane lists, fold the notes into `css21-conformance-plan.md`,
  run the obligation generator, commit, and take a fresh baseline (the flip needs two runs).
