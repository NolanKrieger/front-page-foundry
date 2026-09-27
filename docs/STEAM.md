# Steam — store page, disclosure, platform wiring (M15)

Everything a Steam release needs from the project side. What only Nolan can do is marked **Nolan**.

## Store page draft

**Title:** Front Page Foundry
**Tagline (short description, ≤ 300 chars):** A factory game printed in a 1920s newspaper. Found a works on the edge of a mill town, run belts and machines from the first iron ingot to automobiles and aeroplanes, and read about it every morning in *The Carvell Falls Courier* — your build menu is the classifieds, your score is the share price.

**About this game**

You are the company. The paper is everything else.

Front Page Foundry is a deep factory-building game drawn as a steel engraving on cream newsprint. Lay belts, feed machines, route goods through splitters and bridges, and turn ore into steel, steel into parts, parts into a motorcar and, one day, an aeroplane. Then the Exposition comes calling with commissions that never end.

- **The paper is the interface.** *The Carvell Falls Courier* prints a new edition when something happens: your first steel, a glut, a banker's warning, a commission filled. The classified advertisements are the build menu. The market page shows what your goods fetch. The telegram desk brings orders from firms with names.
- **One job per machine, no menus.** What arrives selects the recipe. Route wisely; the sorting splitter is your friend.
- **A market with memory.** Flood one good and its price falls; it recovers in two days. Prices drift, events strike, and the credit line charges interest every week. One missed payment and the company folds.
- **Ninety goods, sixty machines, five tiers.** Iron, copper, steel, aluminium, glass, rubber, cloth, oil — then engines, wheels, wings, and the works that put them together.
- **Four ways to move goods.** Belts, motor trucks on roads, trains with block signals, barges on the river.
- **Three eras of power.** Waterwheels on the bank, boilers and steam mains, power stations and poles, a dam across the river that drowns the valley above it.
- **Plan in pencil.** Sketch a layout for nothing and watch it build itself as the till allows; copy a working block with a blueprint and stamp it anywhere.
- **An infinite map** with rivers, hills, forests and a town that grows around your works and never over it.
- **Original ragtime** on a gramophone that gains a banjo and a cornet as the works grows.

**Features list (Steam bullets):** single-player · achievements · cloud saves · English · mouse and keyboard · Windows and Linux.

**Tags (Nolan picks on Steamworks):** Automation, Base Building, Management, Economy, Simulation, Sandbox, Historical, 1920s, Hand-drawn, Relaxing, Singleplayer.

**Price:** **Nolan** (GDD §16 open item 1).

**System requirements (measured here on an i7-9700K / RTX 2070 SUPER; the sim is one-core):**
- Minimum: 64-bit Windows 10 or Linux, dual-core 2.5 GHz, 4 GB RAM, OpenGL 3.3 / GL Compatibility, 700 MB disk.
- Recommended: quad-core 3 GHz, 8 GB RAM, any GPU from the last decade, 1920×1080.

## AI disclosure (Steamworks content survey — shown on the store page)

Suggested text, from `docs/AI-ASSETS.md` and `assets/audio/README.md`:

> The engraved building plates and goods icons in Front Page Foundry were generated with OpenAI's image model through the developer's own prompt templates and then processed by our own tools; no existing artwork was copied. Everything else — the game's design, code, procedural drawing, fonts (SIL OFL), shaders, music and sound (composed and synthesised by our own scripts) — was made without generative AI. No generative AI runs while you play.

Keep `docs/AI-ASSETS.md` current: every plate and icon is logged there with its source generation.

## Achievements

The 18 in `docs/ACHIEVEMENTS.md`, checked by `game/Achievements.cs`. Steam names = the IDs; display names = `ACH_<ID>` rows in `assets/text/en.csv`. Wire-up (**Nolan**'s App ID first): add a Steamworks binding (GodotSteam's C# build, or Steamworks.NET), implement `IAchievementSink` in `game/Steam.cs` with `SteamUserStats.SetAchievement(id)` + `StoreStats()`, and pass it to `Achievements` in `Main._Ready`. Until then `NoSteam` keeps the local record so unlocks are not lost.

## Cloud saves

Saves live in `user://companies/<slug>/` (`save.bin`, `save.bak1`, `save.bak2`, `achievements.json`); settings in `user://settings.json`. On the Steamworks **Cloud** page add an Auto-Cloud root of the Godot user data folder (Windows `%APPDATA%/Godot/app_userdata/Front Page Foundry/companies`, Linux `~/.local/share/godot/app_userdata/Front Page Foundry/companies`) with pattern `*` recursive, quota ≥ 200 MB / 100 files per user. The format carries its own checksum, so a half-synced file falls back to a backup by itself.

## Builds

`tools/release.sh` runs the tests, the self-test, exports Linux and Windows (`export_presets.cfg`, templates 4.7.2 mono) into `build/`, zips them and writes `SHA256SUMS`. Depots: one per OS; upload with `steamcmd +login <account> +run_app_build <script.vdf>` (**Nolan**'s account and App ID). Launch options: the executable with no arguments.

## Release checklist

- [ ] **Nolan:** Steam Direct fee, App ID, store assets sized to Steam's specs (capsules 616×353 / 231×87 / 1232×706 / 374×448 header, page background, at least 5 screenshots 1920×1080 — `docs/review/` has 1080p frames), trailer (see `docs/TRAILER.md`), price, tags, release date.
- [ ] **Nolan:** git init + private repo (never done by the agent).
- [x] Export presets, version 0.9.0-rc1, icon; `build/front-page-foundry-0.9.0-rc1-linux-x86_64.tar.gz` and `-windows-x86_64.zip` with `SHA256SUMS` (`bash tools/release.sh`).
- [x] Exported Linux binary passes the full self-test (132 checks).
- [x] Windows export built (not run here: no Windows box — **Nolan** runs it once on Windows).
- [ ] Steamworks binding for achievements (needs the App ID).
- [ ] Cloud path configured on the partner site.
- [ ] Content survey filled with the disclosure text above.
