# CSS 2.1 Phase 6: parallel lanes

Phase 6 of [the CSS 2.1 plan](../css21-conformance-plan.md) (the engine queue) is split into lanes that
separate agent sessions work at the same time. Each lane has its own branch and worktree, its own case
list and a set of files it owns. The integrator session merges lane branches into
`css21/phase6-engine-queue` in batches and runs the full baseline after each batch.

Base commit for wave 1: the partial-class split on `css21/phase6-engine-queue`. That split moved
`BoxEngine`, `Drawer` and `Parser` into per-area partial files.

## Batch 3 lanes (from 22 November)

Batch 3 merges the lanes whose files collided in batches 1–2. It takes only the **failing**
cases: manual-pending and unsupported cases are out of its scope. Branches are `css21/b3-<lane>`,
and worktrees are `C:\Repos\Lite\.claude\worktrees\b3-<lane>` (lane letters lower-cased). Base commit: `113ff7e1`.

| Lane | Scope | Merges | Notes file |
| --- | --- | --- | --- |
| T | Text: inline, line box and fonts | 6.A + 6.J | [T-text.md](T-text.md) |
| F | Flow: block, margins, floats and clear | 6.B + 6.C | [F-flow.md](F-flow.md) |
| P | Positioning and the viewport | 6.D | [P-positioning.md](P-positioning.md) |
| Tb | Tables | 6.E | [Tb-tables.md](Tb-tables.md) |
| G | Generated content, counters, quotes and lists | 6.F | [G-generated.md](G-generated.md) |
| Bd | Bidi | 6.G | [Bd-bidi.md](Bd-bidi.md) |
| V | Visual: borders, outline and backgrounds | 6.H + 6.I | [V-visual.md](V-visual.md) |
| S | Selectors, cascade and syntax | 6.K | [S-cascade.md](S-cascade.md) |
| J | Judges and evidence (no engine changes) | 6.M + 6.X | [J-judges.md](J-judges.md) |

- **Splitter routing.** `scripts/split-css21-lanes.py` keeps the batch-1/2 stem and directory rules,
  and `MERGED` folds their areas into the new lanes. Two exceptions run first:
  - the judge-misread cases named in the 6.M hand-ins (`JUDGE_NAMES`) go to J;
  - single reroutes go to their named lanes (`CASE_LANES`).
- **Batch-2 lists.** The batch-2 lists are kept in `artifacts/lanes-batch2/`.
- **Thin lanes.** A lane that drops under about 20 failures folds into a neighbour for the next batch.
- **Stuck cases.** A case that survives two batches goes to J with its diagnosis, as an exclusion
  proposal for the user's ruling.

The lane notes below (6.A–6.X) are the batch-1/2 archive. The batch-3 notes link to them for the
full diagnoses.

## Batch 1–2 lanes (archive)

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
- `judges.txt`: the unsupported and manual-pending cases (6.M in batches 1–2; out of batch-3 scope);
- `<lane>-pass.txt`: the lane's own passing cases, its regression list (200–2,900 cases);
- `canary.txt`: about 960 passing cases (107 per lane in batch 3, 80 in batches 1–2);
- `summary.md`: counts per lane.

Assignment goes by test-name stem first, then by WPT directory. A case that belongs to another lane
moves there: add a line to the other lane's notes under "Handed in". Do not edit the list files.
The integrator regenerates them after every batch.

`artifacts/` is ignored by git. The integrator copies each batch's lists, the official catalog
and the WPT manifest into every lane worktree's `Lite.Conformance\artifacts\`. The master copies
live in the main checkout, `C:\Repos\Lite\Lite.Conformance\artifacts\lanes\`.

## Rules for every lane

1. **Work in your own worktree and branch** (batch 3: `C:\Repos\Lite\.claude\worktrees3-<lane>`, branch
   `css21/b3-<lane>`; batches 1–2 used `p6-<lane>`).
   Never commit in `C:\Repos\Lite`. That is the integrator's checkout.
2. **Edit only the files your notes file lists as owned.** If a fix needs a file you do not own:
   - keep the edit minimal and put it in a commit of its own;
   - name it in your notes under "Edits outside owned files", so the integrator can expect the
     merge.
   Never edit these:
   - `docs/css21-conformance-plan.md` or `docs/conformance-status.md` (the integrator folds your
     notes in);
   - `css21-requirements.json` statuses (only `scripts/gen_css21_obligation_status.py` writes them);
   - `css21-pass-conditions.json` and `css21-pass-condition-reviews.json` (only J writes them; 6.M in batches 1–2);
   - `css21-applicability.json` (only J writes it, after the user rules; send reclassification candidates to J's
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
   dotnet run --project Lite.Conformance/Lite.Conformance.csproj -c Release --no-build -- --suite css21-full --media screen --filter "@Lite.Conformance/artifacts/lanes/<lane>-pass.txt" --report Lite.Conformance/artifacts/<lane>-pass-after.json
   dotnet run --project Lite.Conformance/Lite.Conformance.csproj -c Release --no-build -- --suite css21-full --media screen --filter "@Lite.Conformance/artifacts/lanes/canary.txt" --report Lite.Conformance/artifacts/canary-after.json
   ```

   - **Units:** all pass.
   - **Curated gates:** css21 51 passes with 1 published expected-fail, wpt 86 with 2.
   - **Lane list:** report what moved, case by case.
   - **Your pass list (`<lane>-pass.txt`) and the canary:** every case still passes. When a commit
     touches a file another lane owns, also run that lane's pass list. A loss on any pass list, the
     canary or the curated gates blocks the commit. (Batch 1: the 300-case canary caught 3 of the
     132 losses the merge found; the lanes' own pass lists held most of the rest.)

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
   - the reading of each check (units, gates, lane list, pass lists, canary).
7. **Finish a batch, then stop.** After a coherent batch, or when the lane runs out of fixable
   cases, end your turn with a short report. The integrator merges, runs the full baseline, and
   sends you the next queue.

## Integration (the integrator only)

- **Merge order in a batch:** S → T → Bd → F → P → Tb → G → V → J. T goes before Bd because they share
  `ReorderForBidi` and the line metrics. (Batches 1–2: 6.J → 6.K → 6.C → 6.B → 6.D → 6.A → 6.E → 6.F → 6.H → 6.I → 6.M → 6.X.)
  Units and gates run after each merge.
- **The full upstream baseline** (`scripts/run-css21-baseline.ps1 -StartWptServe`) runs on the
  committed merge head, after the lanes have paused their conformance runs. It is diffed per case
  against the previous batch.
- **0-loss rule:** a lost case reverts the responsible commit, which goes back to its lane with
  the case named.
- **After the batch:** regenerate the lane lists, fold the notes into `css21-conformance-plan.md`,
  run the obligation generator, commit, and take a fresh baseline (the flip needs two runs).
