# Front Page Foundry — project rules

A 1920s factory game drawn as the town newspaper. Godot 4.7.2 .NET, C#. Goal: the complete, polished game on Steam.
Repo: `~/Desktop/gamedev/front-page-foundry` (`~/front-page-foundry` is a symlink to it).

## Read first
1. `docs/GDD.md` — the design and source of truth; §16 is the decisions log.
2. `docs/PROGRESS.md` — where the build stands, what is next, blockers. Update it at every milestone and before stopping.
3. `README.md` — run, test, layout.
4. `~/.claude/workspace/tools/languages.md` → "Godot 4.7.2 .NET" (toolchain gotchas); `tools/codex.md` → Codex image generation (the art route).

## Architecture (keep it)
- All rules live in `src/Sim`: plain C#, **no Godot types**, deterministic 60 UPS, integer/fixed-point state, money in cents. The Godot layer (`game/`) only reads state and issues commands.
- Every player action is a `Command` applied through `World.Apply`; the log replays to the same state hash.
- Every sim feature gets xUnit tests in `tests/Sim.Tests`. Every control gets a `--selftest` check. The determinism tests must keep passing.
- Content (items, buildings, recipes) is data in `src/Sim` tables, never switch statements.
- Player-facing text lives in `assets/text/en.csv` (Godot translations, `Tr("KEY")`). English only.
- Fonts SIL OFL only, licence beside each font. Music: new renders of public-domain rags (published ≤1930, composer died before 1956). SFX CC0 or self-made; log every source in `docs/LICENCES.md`. Every AI-generated asset goes in `docs/AI-ASSETS.md` (Steam disclosure).

## How to work
- Follow the GDD roadmap M1→M15 in order. Don't cut scope: Nolan chose "everything". Where the GDD says **(proposal)**, decide sensibly and record it in GDD §16.
- Verify before claiming: `dotnet test tests/Sim.Tests` green; `godot --path . -- --selftest` PASS; a screenshot via `-- --demo --screenshot=... --frames=N` looked at with the Read tool; measured UPS/FPS for performance claims. Never report done without that evidence.
- Run `godot` and `dotnet` **non-sandboxed**. Screenshots need the real display (the window pops up briefly; KWin tiles it).
- Performance target (GDD §14): 50,000 structures and 500,000 goods on belts at 60 UPS on this PC. Profile before optimizing.

## Needs Nolan (stop and ask, one line each)
- Style-bible approval before mass art generation; any change to how the game looks or feels that he hasn't seen.
- Anything that costs money (Steamworks fee, paid assets, paid compute). The Steam price point.
- Git: not under version control yet. Ask about `git init` + private GitHub repo. **Never commit, push or publish without his explicit yes.**
- Steam store page, builds, uploads: anything outward-facing.

## Nolan's rules
- Replies: no fluff. Answer or action first, 1–5 short lines, exact paths/numbers/commands. No preamble, recaps, offered alternatives. Reasoning only when asked.
- He has no Godot experience: give exact clicks when he must do something in the editor.
- Never `pkill -f` / `pgrep -f` a pattern in your own command line. Collect PIDs, then `kill`.
- Don't restart or kill processes you didn't start.
- Honesty over optimism: report the actual error.
