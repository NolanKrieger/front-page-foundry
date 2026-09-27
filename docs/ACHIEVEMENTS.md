# Achievements

Steam achievements, framed in-game as clippings pinned to the archive (GDD §13). Listed here from M8 so
M13 implements against a fixed set. Every trigger is a signal the sim already emits; the game layer
listens and unlocks, the sim never knows Steam exists. IDs are the Steam API names.

| ID | Name | Clipping | Trigger (sim signal) | Signal exists |
|---|---|---|---|---|
| `FIRST_POUR` | First Pour | FIRST STEEL POURED IN CARVELL FALLS | first `SteelIngot` made (`Paper` key `FIRST_STEEL`) | yes |
| `HORSELESS` | Horseless | FOUNDRY'S FIRST MOTORCAR ROLLS OUT | first `Automobile` (`FIRST_CAR`) | yes |
| `HAULAGE` | Haulage | FIRST MOTOR TRUCK LEAVES THE LINE | first `MotorTruck` (`FIRST_TRUCK`) | yes |
| `BARNSTORMER` | Barnstormer | CARVELL AEROPLANE TAKES WING | first `Aeroplane` (`FIRST_PLANE`) | yes |
| `LIGHTS_ON` | Lights On | ELECTRIFICATION | first `PowerStation` placed (`ELECTRIFICATION` edition) | yes (M7) |
| `THE_DAM_HELD` | The Dam Held | first hydro dam through a drought week | `DAM` edition printed, then a Drought event ends with the dam standing | needs a counter (M13) |
| `A_CAR_FOR_EVERY_GARAGE` | A Car for Every Garage | 1,000 CARS | 1,000th `Automobile` (`CARS_1000`) | yes |
| `DIVERSIFIED` | Diversified | ten lines sold at over 80% of base in one week | weekly ledger: ≥10 goods with average sale ≥ 0.8 × base | needs a weekly stat (M13) |
| `DEBT_FREE` | Debt-Free | the balance cleared | credit drawn ≥ $50,000 at some point, then $0 owed | needs a high-water mark (M13) |
| `CAPTAIN_OF_INDUSTRY` | Captain of Industry | share price $100 | `SharePriceCents` ≥ 10,000 at a weekly close | yes (price exists; check at week end) |
| `COMMISSION_1` … `COMMISSION_6` | Commission I–VI | each exposition commission fulfilled | prestige (M11) | M11 |
| `BANKERS_WARNING` | Close Call | a Banker's Warning survived | `BANKERS_WARNING` printed, next payment made | yes |
| `FRONT_PAGE_100` | Old Hand | 100 front pages in the archive | `Paper.Editions.Count` ≥ 100 | yes |

Rules:
- Unlocks are per company (one save each) and never revoked; the game layer records unlocked IDs in the
  company folder so an offline session unlocks on the next Steam sync (M13 owns the API binding).
- No achievement for losing, spending real money, or anything the player cannot see coming.
- Names and clipping text are localisable rows in `assets/text/*.csv` (`ACH_<ID>_NAME`, `ACH_<ID>_TEXT`), added at M13.
