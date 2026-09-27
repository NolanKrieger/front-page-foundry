# Handoff: finish Front Page Foundry

You are taking over **Front Page Foundry**, Nolan Krieger's factory game. The goal is the **whole game, complete and polished, ready for a Steam release**: every system in the design doc, balanced and bug-free, with finished art, audio, UI and performance. Work until it is done. This will span many sessions, so keep a written record of progress as you go (see "Continuity").

## Read first, in this order

1. `~/front-page-foundry/docs/GDD.md`: the design. It is the source of truth, and §16 is the decisions log.
2. `~/front-page-foundry/README.md`: how to run, test and navigate the code.
3. All the code, which is small: `src/Sim/*.cs`, `tests/Sim.Tests/WorldTests.cs`, `game/*.cs`.
4. `~/.claude/workspace/tools/languages.md` → "Godot 4.7.2 .NET": toolchain gotchas.
5. `~/.claude/workspace/tools/codex.md` → "Codex built-in image generation": the art route.

## Where things stand (2026-09-23)

- **M0 is done.** Pure C# sim: belts with integer sub-units, curves, side-loading, mine heads with the GDD's yield decay, freight depot, Q/K market glut, escalating cost, and a deterministic state hash. The Godot layer has a paper shader, chunked terrain, engraved oblique buildings, hybrid zoom (goods up close, flow lines far out), and the newspaper HUD (masthead, dateline, classified-ad build menu, hover clippings).
- **Verified:** `dotnet test tests/Sim.Tests` passes 10/10. `godot --path . -- --selftest` passes 14/14 checks: real input path, key/drag/ad clicks, remove and refund, zoom.
- **Only one item exists (Iron Ore)**, and there are three buildings: Belt, Mine Head, Freight Depot.
- **Known gaps between the scaffold and the GDD, to fix early:**
  - **Belt throughput:** the scaffold belt moves 1.875 tiles/s and carries at most 7.5 items/s. The GDD has three tiers: canvas 2/s, rubber 4/s, steel 8/s.
  - **Masthead font:** the scaffold uses the UnifrakturMaguntia blackletter. GDD §11 proposes Playfair Display for mastheads. Pick one and update the other.
  - **Hard-coded content:** items and buildings are enums and switch statements. Moving to data-driven definitions is a prerequisite for the 90 items.
  - **Straight-tile belt model:** belts are one tile each. Moving to GDD §14's lane segments is needed for scale.
  - **Missing systems:** there is no save/load, credit line, power, map features beyond ore, or newspaper events yet.
- **Toolchain:** `godot` (wrapper at `~/.local/bin/godot`), .NET 8 SDK at `~/.dotnet`. Run godot and dotnet commands **non-sandboxed**. Screenshots need the real display: the window pops up briefly and Nolan's KWin tiler resizes it.

## How to work

- **Follow the GDD roadmap M1→M15 in order.** Each milestone's exit criterion is its definition of done. Don't cut scope: Nolan chose "everything". Where the GDD marks something **(proposal)** and it matters, decide sensibly and record the decision in GDD §16. Ask Nolan only about taste and money (see below).
- **Keep the architecture:** all rules live in `src/Sim` (no Godot types, deterministic 60 UPS, integer or plain-arithmetic math). The Godot layer only reads state and issues commands. Every sim feature gets xUnit tests. Every control gets a `--selftest` check. The determinism hash test must keep passing.
- **Verify before claiming anything:**
  - Build and test green.
  - Run the self-test.
  - Take a screenshot with `-- --demo --screenshot=… --frames=N`, and look at it with the Read tool.
  - For performance, measure real UPS and FPS on a large factory. Don't estimate.
  - Never report something as done, fixed or working without that evidence.
- **Performance target:** GDD §14, 50,000 structures and 500,000 items on belts at 60 UPS on this PC (i7-9700K, RTX 2070 SUPER). Profile before optimizing.

## Art, audio, text

- **Art: Codex `image_gen`** (Nolan's choice). Drive Codex non-interactively from your session. Prove one generation works before planning around it.
  - Follow GDD §11's pipeline: style bible → fixed prompt template → chroma-key transparency → normalize to **64 px/tile** → contact-sheet review.
  - **Nolan must approve the style bible before mass generation.**
  - Keep a list of every AI-generated asset for Steam's AI-content disclosure.
- **Music:** public-domain rags (compositions published in 1930 or earlier). Make **new** renders or recordings. Don't use old recordings, which carry separate rights. LMMS is installed at `~/.local/opt/lmms` for arranging and rendering. SFX must be CC0 or self-made; log the licence of each source.
- **Fonts:** SIL OFL only, with the licence file beside each font. **English only.** Keep all player-facing text in string tables.

## Needs Nolan (stop and ask, one line each)

- Style-bible approval, and any change to how the game looks or feels that he hasn't seen.
- Anything that costs money: the Steamworks account/app fee, paid assets, paid compute.
- The Steam price point (the only open GDD question).
- **Git:** the repo is not under version control. Ask whether he wants `git init` plus a private GitHub repo. **Never commit, push or publish without his explicit yes.**
- Steam store page, builds or uploads: anything outward-facing.

## Nolan's rules (these override defaults)

- **Replies: no fluff.** Give the answer or action first, in 1–5 short lines, with exact paths, numbers and commands. No preamble, recaps or offered alternatives. Reasoning only when asked.
- He has **no Godot experience**. When he has to do something in Godot, give the exact clicks.
- **Never** run `pkill -f` or `pgrep -f` on a pattern that is in your own command line; it kills your own shell. Collect PIDs and `kill` them.
- Don't restart or kill processes you didn't start.
- Honesty over optimism: when a tool errors, report the actual error.

## Continuity

- Keep `~/front-page-foundry/docs/PROGRESS.md` current. Record the milestone, what is done with its evidence, what is next, blockers, and decisions. Update it at every milestone and before stopping. A new session must be able to resume from it alone.
- First action: create `~/front-page-foundry/CLAUDE.md` with the project rules above (short), so every future session in this folder loads them automatically.
- Record durable decisions in GDD §16, and toolchain discoveries in `~/.claude/workspace/tools/languages.md`.
