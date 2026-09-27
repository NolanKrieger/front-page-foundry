# Front Page Foundry — Game Design Document

*Working title. Status: v0.1 draft, 2026-09-23. Written from Nolan's brainstorm answers. Anything marked **(proposal)** fills a gap the brainstorm left open and needs a yes or no.*

---

## 1. Pitch & pillars

**Pitch.** You found a works on the edge of a 1920s American mill town and grow it into an industrial empire, from the first iron ingot to automobiles, aeroplanes and endless prestige commissions. You lay out belts and machines on an infinite grid. The town paper covers every milestone, market swing and disaster, and the game itself looks like that paper, printed in ink, halftone and engraving.

**Pillars**

1. **Deep systems, calm surface.** About 90 items, four kinds of power, a live market and credit. Minimalism applies to the *look only*: paper, ink, white space and a restrained palette. The systems are not simplified.
2. **The newspaper is the interface.** The build menu is the classified ads, tooltips are clippings, stats are the market page, and the tutorial is the first editions. Everything the player needs to know gets printed.
3. **Money is the only gate.** Every building is buyable from day one. Eras (coal → water → steam → electric) arrive because you can afford them, not because a tech tree allows them.
4. **Real stakes.** It always runs in real time, there is one autosaving save per company, and bankruptcy ends the company.

---

## 2. Core loop

```
extract / import raw goods ──► process on belts through one-job machines ──► sell at a freight depot
        ▲                                                                           │
        └──── buy more machines, land and power ◄── cash + credit ◄── market price ◄┘
                          │
                          └──► the paper reacts: headlines, telegram orders, market page
```

- **Minute to minute:** place and route belts, fix bottlenecks with the flow overlay, and watch prices on the market page.
- **Hour to hour:** chase a product chain (steel → autos → aeroplanes), move power up an era, and expand across the map for richer patches.
- **Session to session:** fill telegram orders, trigger milestone headlines, finish prestige commissions, and push the share price (the score).

---

## 3. Grid & placement

| Aspect | Rule |
|---|---|
| Grid | Infinite square grid. The sim is organized in **32×32-tile chunks**. Only chunks that contain structures tick. |
| Footprints | 1×1, 1×2, 2×2, 3×3. No building is bigger than 3×3. |
| Ports | Each machine has typed input/output ports on its edges. Rotating moves the ports. |
| Land | Every tile has a land price. Building pays for the land plus the structure (see §9, town). |
| Terrain blocks | River tiles accept only bridges, waterwheels, pumps, barge landings and the dam. Town tiles are unbuildable. |

**Controls (mouse + keyboard only; no controller support was chosen)**

| Input | Action |
|---|---|
| LMB / LMB-drag | Place. Dragging lays belts, pipes, roads, rail or poles along a path. |
| R | Rotate the held item or the hovered building |
| RMB | Cancel the held item / delete the hovered building (refund, see §5) |
| Q | Pipette: pick up the hovered building type |
| Ctrl+Z / Ctrl+Y | Undo / redo (money reverses too) |
| P | Planning mode (pencil ghosts, free) |
| B | Blueprint: select an area, then save or stamp |
| F | Flow overlay |
| Tab | Open the paper (classified ads = build menu) |
| WASD / MMB-drag, wheel | Pan, zoom |
| Esc | Menu. **The only way to pause.** |

---

## 4. Logistics

| Piece | Footprint | Behaviour | Cost tier |
|---|---|---|---|
| **Belt** (straight/turn) | 1×1 | Single lane **(proposal)**. Three speed tiers: canvas 2 items/s, rubber 4/s, steel slat 8/s **(proposal)**. | $ |
| **Splitter/merger** | 1×2 | 2 in → 2 out, alternating evenly. Works as a pure splitter or pure merger depending on what's connected. | $ |
| **Sorting splitter** | 1×2 | One item type is sent left and everything else right. The filter is set with the pipette, with no menu **(proposal)**. | $$ |
| **Trestle bridge** | entry + exit | Carries a belt or pipe over up to 4 tiles of belts, pipes, roads, rail **or river**. This is the "underground/bridge" piece and the way belts cross water. | $$ |
| **Road + Truck Depot** | 1×1 / 2×2 | Depots load and unload belts. Trucks run A↔B schedules on roads. | $$–$$$ |
| **Rail + Rail Station** | 1×1 / 3×3 | Point-to-point trains with block signals. Rail on hills costs 3× and trains run at 60% speed. | $$$ |
| **Barge Landing** | 2×2 (bank) | River shipping between landings: the cheapest per item, and slow. It was part of the chosen "Rivers" option. | $$ |

- **Vehicles:** trucks, locomotives and barges can be bought from the classified ads. A **Motor Truck** you build yourself and deliver to a Truck Depot joins your fleet at cost.
- **Hybrid item rendering:**
  - **Zoomed in** (a tile ≥ 24 px on screen): every item is drawn as a small engraved icon sliding along the belt.
  - **Zoomed out:** belts switch to **flow lines**, dashed ink strokes whose dash density scales with items/min. A backed-up belt prints its dashes in the accent red.
  - Machines show a glyph of their output at far zoom.
  - The switch cross-fades over ±15% zoom so it never pops.

---

## 5. Machines

**One job per machine, no recipe menus.** A machine does one job (smelting, pressing, drawing wire and so on), and **what arrives on its input ports selects the recipe**. Design rule: within one machine, no recipe's input set may be a subset of another's, so the choice is never ambiguous. The only settings in the whole game are the Receiving Dock's order and the sorting splitter's filter.

**Buying.** The whole catalog is visible and buyable from day one, and price is the only gate.

**Escalating per-copy cost:**

```
price(n) = base × (1 + 0.04·n)^1.5        n = copies of this machine type already owned
```

| Copies owned (n) | Price multiplier |
|---|---|
| 0 | 1.0× |
| 10 | 1.7× |
| 25 | 2.8× |
| 50 | 5.2× |
| 100 | 11.2× |

- The formula pushes players toward efficient layouts over spamming machines.
- Logistics pieces (belt, pipe, pole, road, rail, bridge) are **exempt** so routing is never punished **(proposal)**.
- Demolishing refunds 75% of the most recent copy's price, and n goes down by one.

**Power column key:** **Any** = runs on whichever source reaches it (§7). **Elec** = needs electricity.

### Extraction & trade

| Machine | Size | Job | Power | Base $ |
|---|---|---|---|---|
| Mine Head | 2×2 | Must sit on an ore/coal/limestone/sand/clay/sulfur/bauxite patch and outputs that raw | Any | 400 |
| Logging Camp | 2×2 | Harvests forest tiles within radius 3 → Timber | Any | 350 |
| Pump Jack | 2×2 | On an oil seep → Crude Oil (moved as barrels) | Any | 1,500 |
| Water Pump | 1×1 | On a riverbank → water into a pipe | Any | 250 |
| Receiving Dock | 2×2 | Buys the ordered raw at the market price onto a belt (one of the two settings in the game) | — | 800 |
| Freight Depot | 2×2 | Sells any item fed to it at the current market price | — | 600 |
| Exposition Yard | 3×3 | Accepts deliveries for prestige commissions (§6) | — | 25,000 |

### Processing (T1)

| Machine | Size | Job | Power | Base $ |
|---|---|---|---|---|
| Smelter | 2×2 | Ore → ingot | Any | 600 |
| Coke Oven | 2×2 | Coal → coke | Any | 700 |
| Open-Hearth Furnace | 3×3 | Iron + coke + limestone → steel | Any | 6,000 |
| Reduction Works | 3×3 | Bauxite + coke → aluminum | **Elec** | 40,000 |
| Glassworks | 2×2 | Sand + limestone → glass; glass → plate glass | Any | 900 |
| Kiln | 2×2 | Clay → porcelain | Any | 500 |
| Cement Works | 2×2 | Limestone + clay → cement | Any | 1,200 |
| Sawmill | 2×2 | Timber → lumber | Any | 450 |
| Refinery | 3×3 | Crude → gasoline + lubricant + tar (3 outputs) | Any | 12,000 |
| Vulcanizer | 2×2 | Raw rubber + sulfur → rubber | Any | 1,800 |
| Textile Mill | 2×2 | Cotton → cloth | Any | 1,500 |
| Chemical Works | 2×2 | Varnish, Japan enamel | Any | 2,500 |

### Parts (T2–T3)

| Machine | Size | Job | Power | Base $ |
|---|---|---|---|---|
| Press | 2×2 | Ingot → plate/sheet | Any | 1,000 |
| Rolling Mill | 3×3 | Steel → beam | Any | 8,000 |
| Drawing Mill | 2×2 | Rod and wire drawing | Any | 1,400 |
| Tube Mill | 2×2 | Sheet → tube | Any | 2,200 |
| Lathe | 1×1 | Bolts, rivets, pistons | Any | 700 |
| Coiler | 1×1 | Wire → springs | Any | 500 |
| Precision Shop | 2×2 | Gears, bearings, carburetors, gauges, steering gears | Any | 4,000 |
| Foundry | 2×2 | Iron and aluminum castings | Any | 3,000 |
| Boring Mill | 2×2 | Casting → engine block / crankcase | Any | 5,000 |
| Drop Forge | 2×2 | Crankshafts, axles | Any | 4,500 |
| Joinery | 2×2 | Plywood, wing ribs, propellers, instrument panels | Any | 1,200 |
| Doping Shed | 2×2 | Cloth + varnish → doped fabric | Any | 2,000 |
| Wire Coater | 1×1 | Copper wire + rubber → insulated wire | Any | 900 |
| Rubber Works | 2×2 | Hoses, gaskets, gas cells | Any | 2,400 |
| Tire Works | 2×2 | Tires | Any | 3,500 |
| Sheet Metal Shop | 2×2 | Radiators, body panels, stressed-skin panels | Any | 3,500 |
| Electrical Shop | 2×2 | Spark plugs, magnetos, headlamps, harnesses | **Elec** | 9,000 |
| Wheelwright | 1×1 | Wheels | Any | 800 |
| Upholstery | 1×1 | Seats | Any | 700 |
| Frame Shop | 3×3 | Chassis and fuselage frames, wing spars, undercarriage, girders | Any | 10,000 |
| Drivetrain Shop | 2×2 | Transmissions | Any | 6,000 |

### Assembly (T4–T5)

| Machine | Size | Job | Power | Base $ |
|---|---|---|---|---|
| Engine Works | 3×3 | Car engines, radial aero engines | Any | 25,000 |
| Chassis Shop | 3×3 | Rolling chassis | Any | 18,000 |
| Body Shop | 3×3 | Car bodies | Any | 18,000 |
| Airframe Works | 3×3 | Wings, fuselages, tail assemblies, gondolas | **Elec** | 45,000 |
| Final Assembly Line | 3×3 | Automobiles, motor trucks | **Elec** | 30,000 |
| Aircraft Hangar | 3×3 | Aeroplanes | **Elec** | 80,000 |

### Power & logistics buildings

| Building | Size | Job | Base $ |
|---|---|---|---|
| Waterwheel | 1×2 (bank) | Mechanical shaft power to adjacent machines | 300 |
| Boiler | 2×2 | Water + coal → steam | 2,500 |
| Steam Pipe | 1×1 | Carries water and steam only | 10 |
| Power Station | 3×3 | Steam → electricity | 20,000 |
| Power Pole | 1×1 | Electric link, radius 6 | 40 |
| Hydro Dam | 3×3 (across a river ≤ 3 wide) | Electricity, floods upstream | 60,000 + 500 cement delivered **(proposal)** |

---

## 6. Item & recipe tree

90 items. Base prices are **placeholders for the M12 balance pass**. Value added grows by tier: roughly ×1.4 at T1, ×1.3 at T2–T5, and ×1.9 for the aeroplane, because its market is thin. "→ n" means n units out.

### T0 — Raw (12)

| Item | Source | Base $ |
|---|---|---|
| Iron Ore | Mine / Dock | 2 |
| Copper Ore | Mine / Dock | 3 |
| Coal | Mine / Dock | 1.5 |
| Limestone | Mine / Dock | 1 |
| Sand | Mine / Dock | 0.5 |
| Clay | Mine / Dock | 0.8 |
| Sulfur | Mine / Dock | 3 |
| Bauxite | Mine / Dock | 4 |
| Timber | Logging Camp / Dock | 2 |
| Crude Oil | Pump Jack / Dock | 3 |
| Raw Rubber | **Dock only** (imported) | 8 |
| Cotton | **Dock only** (imported) | 5 |

Water and steam are fluids that travel only in pipes; they are not items. All other liquids travel as barrels on belts **(proposal)**.

### T1 — Materials (14)

| Item | Machine | Inputs → out | $ |
|---|---|---|---|
| Iron Ingot | Smelter | 1 Iron Ore | 3.5 |
| Copper Ingot | Smelter | 1 Copper Ore | 5 |
| Coke | Coke Oven | 2 Coal | 4.5 |
| Steel Ingot | Open-Hearth | 2 Iron Ingot, 1 Coke, 1 Limestone | 18 |
| Aluminum Ingot | Reduction Works | 2 Bauxite, 1 Coke | 28 |
| Glass | Glassworks | 2 Sand, 1 Limestone | 4 |
| Lumber | Sawmill | 1 Timber → 2 | 1.6 |
| Porcelain | Kiln | 2 Clay | 3 |
| Cement | Cement Works | 2 Limestone, 1 Clay → 2 | 2.2 |
| Rubber | Vulcanizer | 2 Raw Rubber, 1 Sulfur → 2 | 13 |
| Cloth | Textile Mill | 2 Cotton | 14 |
| Gasoline | Refinery | 4 Crude → 2 Gasoline + 1 Lubricant + 1 Tar | 5 |
| Lubricant | Refinery | (see above) | 6 |
| Tar | Refinery | (see above) | 3 |

### T2 — Parts (26)

| Item | Machine | Inputs → out | $ |
|---|---|---|---|
| Iron Plate | Press | 1 Iron Ingot | 5 |
| Steel Plate | Press | 1 Steel Ingot | 24 |
| Copper Sheet | Press | 1 Copper Ingot | 7 |
| Aluminum Sheet | Press | 1 Aluminum Ingot | 36 |
| Steel Beam | Rolling Mill | 3 Steel Ingot | 75 |
| Steel Rod | Drawing Mill | 1 Steel Ingot → 2 | 12 |
| Steel Wire | Drawing Mill | 1 Steel Rod → 2 | 8 |
| Copper Wire | Drawing Mill | 1 Copper Ingot → 2 | 3.5 |
| Steel Tube | Tube Mill | 1 Steel Plate | 32 |
| Aluminum Tube | Tube Mill | 1 Aluminum Sheet | 46 |
| Bolts | Lathe | 1 Steel Rod → 4 | 4 |
| Rivets | Lathe | 1 Steel Wire → 8 | 1.5 |
| Springs | Coiler | 2 Steel Wire | 21 |
| Gears | Precision Shop | 1 Steel Plate → 2 | 16 |
| Bearings | Precision Shop | 1 Steel Rod, 1 Lubricant → 2 | 12 |
| Iron Casting | Foundry | 3 Iron Ingot, 1 Sand | 16 |
| Aluminum Casting | Foundry | 2 Aluminum Ingot, 1 Sand | 72 |
| Varnish | Chemical Works | 1 Tar, 1 Gasoline → 2 | 5.5 |
| Japan Enamel | Chemical Works | 1 Tar, 1 Varnish → 2 | 6 |
| Plywood | Joinery | 3 Lumber, 1 Varnish | 14 |
| Doped Fabric | Doping Shed | 2 Cloth, 1 Varnish | 44 |
| Plate Glass | Glassworks | 2 Glass | 11 |
| Insulated Wire | Wire Coater | 2 Copper Wire, 1 Rubber → 2 | 13 |
| Rubber Hose | Rubber Works | 2 Rubber, 1 Steel Wire | 44 |
| Gasket | Rubber Works | 1 Rubber, 1 Copper Sheet → 4 | 7 |
| Tire | Tire Works | 3 Rubber, 2 Steel Wire, 1 Cloth | 90 |

### T3 — Components (24)

| Item | Machine | Inputs → out | $ |
|---|---|---|---|
| Engine Block | Boring Mill | 2 Iron Casting | 45 |
| Crankcase | Boring Mill | 1 Aluminum Casting | 95 |
| Piston | Lathe | 1 Iron Casting → 2 | 11 |
| Crankshaft | Drop Forge | 2 Steel Ingot | 50 |
| Axle | Drop Forge | 2 Steel Rod | 32 |
| Spark Plug | Electrical Shop | 1 Porcelain, 1 Copper Wire, 1 Steel Wire | 20 |
| Magneto | Electrical Shop | 4 Copper Wire, 1 Iron Plate, 2 Bearings | 60 |
| Headlamp | Electrical Shop | 1 Glass, 2 Copper Wire, 1 Iron Plate | 24 |
| Wiring Harness | Electrical Shop | 4 Insulated Wire | 70 |
| Carburetor | Precision Shop | 2 Copper Sheet, 1 Springs | 48 |
| Gauge | Precision Shop | 1 Glass, 1 Gears, 1 Springs | 55 |
| Steering Gear | Precision Shop | 2 Gears, 1 Steel Rod | 60 |
| Radiator | Sheet Metal Shop | 3 Copper Sheet, 1 Rubber Hose | 85 |
| Body Panel | Sheet Metal Shop | 2 Steel Plate, 1 Japan Enamel | 70 |
| Transmission | Drivetrain Shop | 4 Gears, 2 Bearings, 1 Iron Casting | 140 |
| Wheel | Wheelwright | 1 Tire, 2 Lumber, 4 Bolts | 140 |
| Seat | Upholstery | 2 Cloth, 2 Springs, 2 Lumber | 95 |
| Chassis Frame | Frame Shop | 2 Steel Beam, 8 Bolts | 240 |
| Fuselage Frame | Frame Shop | 6 Steel Tube, 4 Steel Wire | 300 |
| Wing Spar | Frame Shop | 2 Aluminum Tube, 2 Lumber | 125 |
| Undercarriage | Frame Shop | 2 Steel Tube, 2 Springs, 2 Wheel | 500 |
| Wing Rib | Joinery | 1 Plywood, 2 Bolts → 2 | 15 |
| Propeller | Joinery | 3 Plywood, 2 Varnish | 75 |
| Instrument Panel | Joinery | 1 Plywood, 4 Gauge | 300 |

### T4 — Assemblies (7)

| Item | Machine | Inputs | $ |
|---|---|---|---|
| Car Engine | Engine Works | 1 Engine Block, 4 Piston, 1 Crankshaft, 1 Carburetor, 4 Spark Plug, 1 Radiator | 480 |
| Radial Aero Engine | Engine Works | 1 Crankcase, 9 Piston, 1 Crankshaft, 2 Magneto, 1 Carburetor, 18 Spark Plug | 1,050 |
| Rolling Chassis | Chassis Shop | 1 Chassis Frame, 2 Axle, 4 Wheel, 1 Transmission, 1 Steering Gear | 1,400 |
| Car Body | Body Shop | 6 Body Panel, 2 Plate Glass, 2 Seat, 2 Headlamp, 1 Instrument Panel | 1,300 |
| Wing | Airframe Works | 2 Wing Spar, 12 Wing Rib, 8 Doped Fabric, 6 Steel Wire | 1,100 |
| Fuselage | Airframe Works | 1 Fuselage Frame, 10 Doped Fabric, 2 Seat, 1 Instrument Panel | 1,600 |
| Tail Assembly | Airframe Works | 2 Steel Tube, 4 Wing Rib, 3 Doped Fabric | 340 |

### T5 — Flagships (3)

| Item | Machine | Inputs | $ |
|---|---|---|---|
| **Automobile** | Final Assembly Line | 1 Car Engine, 1 Rolling Chassis, 1 Car Body, 1 Wiring Harness, 1 Gasoline | 4,200 |
| Motor Truck | Final Assembly Line | 1 Car Engine, 1 Rolling Chassis, 2 Steel Beam, 4 Body Panel, 1 Wiring Harness | 3,100 |
| **Aeroplane** | Aircraft Hangar | 1 Radial Aero Engine, 2 Wing, 1 Fuselage, 1 Tail Assembly, 1 Undercarriage, 1 Propeller | 11,000 |

### T6 — Prestige parts (4)

| Item | Machine | Inputs | $ |
|---|---|---|---|
| Aluminum Girder | Frame Shop | 3 Aluminum Tube, 12 Rivets | 200 |
| Gas Cell | Rubber Works | 6 Cloth, 2 Rubber | 150 |
| Gondola | Airframe Works | 8 Aluminum Sheet, 6 Plate Glass, 4 Seat | 950 |
| Stressed-Skin Panel | Sheet Metal Shop | 2 Aluminum Sheet, 16 Rivets | 130 |

### Prestige commissions (endless)

Goods are delivered to the **Exposition Yard**. Each delivery is paid at the current market price, exactly like the depot. Completing a commission earns **headlines** and a permanent goodwill term in the share price (a score, not a mechanical reward).

| # | Commission | Deliver |
|---|---|---|
| 1 | Coast-to-Coast Motorcade | 500 Automobile, 200 Motor Truck |
| 2 | The Carvell Falls Skyscraper | 4,000 Steel Beam, 30,000 Rivets, 3,000 Cement, 2,500 Plate Glass |
| 3 | Air Mail Fleet | 100 Aeroplane |
| 4 | Suspension Bridge over the Gorge | 20,000 Steel Wire, 3,000 Steel Beam, 8,000 Cement |
| 5 | Zeppelin *Columbia* | 600 Aluminum Girder, 80 Gas Cell, 6 Radial Aero Engine, 2 Gondola, 400 Doped Fabric |
| 6 | Transatlantic Airliner (×4) | 1,200 Stressed-Skin Panel, 16 Radial Aero Engine, 8 Undercarriage, 12 Instrument Panel, 80 Seat |
| 7+ | Procedural | Built from templates 1–6, with quantities × 1.5ᵏ and generated names ("Zeppelin *Liberty*", "The Second Skyscraper", …) |

---

## 7. Power

Eras are **economic, not gated**. Every source is buyable on day one, and each step up costs more but pays back through efficiency and reach.

| Era | Source | How it reaches machines | Trade-off |
|---|---|---|---|
| 1. Coal | Every **Any** machine has a small firebox that burns coal fed by belt | Belt a coal line to each machine | Free to set up; the most coal per kW; a lot of belt routing |
| 2. Water | **Waterwheel** on a riverbank | Shaft power to machines whose footprint touches it (and to a 1-tile chain of adjacent machines) | No fuel, capped kW per wheel, factory pulled to the river |
| 3. Steam | **Boiler** (water + coal) → **pipes** | Machines connected to a pipe network | 2× the fuel efficiency of fireboxes; one coal line to the boilers only; water pumps needed |
| 4. Electric | **Power Station** (steam → electricity) + **poles** | Machines within pole radius | 3× firebox efficiency, flexible layout, required by **Elec** machines, and electric motors run **+25% faster** |
| 4b. Hydro | **Hydro Dam** across a river ≤ 3 tiles wide | Feeds the electric grid | No fuel and a big output, but **floods upstream valley tiles**. Planning mode previews the flood, and structures on flood tiles must be cleared first. |

- **Source priority on a machine (proposal):** electric > steam > shaft > firebox. A machine uses the best source connected to it.
- **Shortage:** each network computes satisfaction = supply ÷ demand, and connected machines run at that fraction of their speed.
- **Chemical inputs are not power.** Coke going into a furnace is a recipe input even when the furnace is electrified.
- **Headlines:** the first waterwheel, boiler, power station and dam each print a front page ("ELECTRIFICATION REACHES CARVELL FALLS").

---

## 8. Economy

### Market price model

For each item *i*:

```
sell_price_i = base_i × trend_i(t) × event_i(t) × 1 / (1 + Q_i / K_i)
buy_price_i  = base_i × trend_i(t) × event_i(t) × (1 + B_i / K_i)        (Receiving Dock)

Q_i ← Q_i · e^(−Δt/τ) + units_sold_this_tick        glut: your own sales, decaying
B_i ← B_i · e^(−Δt/τ) + units_bought_this_tick      your own purchases push the buy price up
```

| Term | Meaning | Starting value (proposal) |
|---|---|---|
| K_i | Market depth: units that halve the price. It grows +3% per in-game month (national demand growth). | Raw 5,000 · T1 2,000 · T2 800 · T3 300 · T4 60 · Automobile 40 · Aeroplane 6 |
| τ | Recovery time constant | 2 in-game days |
| trend_i | Slow log-space mean-reverting random walk (Ornstein–Uhlenbeck) around 1.0, σ scaled by difficulty | Bounded 0.6–1.6 |
| event_i | Temporary multipliers from random fictional events (§10) | ×0.5–×2.0 for 3–10 days |

The result is that flooding one good crashes its price, which then recovers. **Saturation and trends/events together** push diversification, and that is the core economic puzzle.

### Money systems

| System | Rule |
|---|---|
| **Freight Depot** | Pays the current sell price per unit, instantly. Depots can be built anywhere. |
| **Credit line** | Borrow and repay freely up to limit **L = $10,000 + 0.5 × resale value of assets**. Purchases that exceed cash draw on it automatically **(proposal)**. Interest is charged **every in-game week** on the balance. |
| **Weekly interest** | 0.5% / 1.0% / 1.5% per week by difficulty (§13) **(proposal)** |
| **Bankruptcy = game over** | When cash and remaining credit can't cover the weekly interest, the payment is missed and the paper prints a *Notice of Default*. **One missed payment ends the company**: a final "FOUNDRY FOLDS" edition prints and the save becomes a read-only archive. Because there is no grace, the paper runs a *Banker's Warning* a week ahead whenever projected cash + credit won't cover next week's interest **(proposal)**. |
| **Share price = score only** | `share = (net worth + 4 × trailing-4-week profit + goodwill) / 10,000 shares`. The market page reports it. There is no share trading and no investor funding. |

### Calendar

**1 in-game day = 120 real seconds**, so 1 week = 14 minutes and 50 hours = 1,500 days ≈ 4 years. The game runs from **spring 1920 into 1924**. Dates are cosmetic, the paper's dateline and visual seasons only; there is no real history.

---

## 9. Map generation

Infinite and procedural from a seed, generated per 32×32 chunk on demand.

| Layer | Generation | Gameplay |
|---|---|---|
| Elevation | 2-octave simplex noise | **Hills**: rail costs 3× and trains run slower; machines on hills pay +25% land **(proposal)** |
| Moisture | Simplex noise | **Forests** where it's moist: Logging Camps harvest them, and clearing costs $20/tile to build there |
| Rivers | Traced downhill from sources via flow accumulation, with meander noise; width 1–3 | Only bridges, waterwheels, pumps, landings and dams. They power hydro. |
| Ore patches | Poisson-disk seeded per chunk; richness × (1 + distance/2,000 tiles) | Farther away means richer, so you expand outward |
| Town | "Carvell Falls", centered near spawn on the river | Growing town, land price |

**Patches never run out; they slow.** Per patch:

```
rate(W) = rate₀ × (0.25 + 0.75 × e^(−W / W_d))
W   = units already extracted from this patch
W_d = richness constant (≈ 20,000 for the starter patch, scaled by distance)
```

Output falls toward 25% and never reaches zero, which gently pushes expansion. The flow overlay shows each patch's current %.

**Growing town**
- The town adds blocks every few in-game days. The growth rate is proportional to your trailing profit, so prosperity grows it **(proposal)**.
- The town grows *around* your structures and ghost plans and never demolishes them. You route around it.
- **Land price per tile:** `$2 × (1 + 4 × town_density_within_8_tiles)` for rural land up to the town edge. Land is paid once, on build.

---

## 10. The newspaper

**The paper: *The Carvell Falls Courier*.** Every screen that isn't the factory is a page of it.

| Page | Function |
|---|---|
| **Front page** | Printed **only when something happens** (event-driven editions): milestones, events, market shocks, orders, the first building of each power era. At most one edition per in-game day. Lesser news goes to the inside columns. |
| **Classified ads** | The build menu. Every building is an ad with an engraving, its current escalated price, a one-line pitch and a footprint glyph. Columns: Extraction · Mills & Furnaces · Shops · Works · Power · Transport. |
| **Market page** | A live "stop-press" commodity table: sell and buy price for every item, 7-day sparkline, your share price, cash, credit used / limit. |
| **Telegram desk** | Incoming orders ("NEED 200 IRON PLATE BY THURSDAY STOP GARVEY HARDWARE STOP"). They are filled by selling through any depot, at the market price. **Reward: headlines only.** A missed order also makes a headline ("GARVEY HARDWARE LOOKS ELSEWHERE"). |
| **Clippings** | Tooltips are printed as cut-out clippings. |
| **Archive** | Every past edition, readable. For a bankrupt company, the archive is what remains. |

### Tutorial through the paper

The first editions teach the game. Each headline sets a goal, the matching ad is circled in red pencil, and a column explains the mechanic. There are no pop-ups.

| Ed. | Headline | Teaches |
|---|---|---|
| 1 | NEW WORKS TO RISE ON CARVELL RIVER | Camera, buy a Mine Head on the iron patch |
| 2 | FIRST ORE HAULED — WHERE TO NEXT? | Belts, Smelter, coal fireboxes |
| 3 | IRON FROM CARVELL FALLS HITS MARKET | Freight Depot, the market page |
| 4 | MARKET JITTERS: TOO MUCH IRON? | Saturation, diversifying (Press → plates) |
| 5 | BANKER EXTENDS CREDIT TO YOUNG FIRM | Credit line, weekly interest, the risk of default |
| 6 | TELEGRAM FOR THE FOUNDRY | Orders, splitters, the flow overlay |

### Example headlines & events (about 15)

| Type | Headline | Effect |
|---|---|---|
| Milestone | FIRST STEEL POURED IN CARVELL FALLS | — |
| Milestone | ELECTRIFICATION REACHES CARVELL FALLS | — |
| Milestone | FOUNDRY'S FIRST MOTORCAR ROLLS OUT | — |
| Milestone | 1,000 CARS: "A CAR FOR EVERY GARAGE" | — |
| Milestone | CARVELL AEROPLANE TAKES WING | — |
| Milestone | DAM COMPLETED — VALLEY FLOODS AS PLANNED | — |
| Event | RUBBER SHORTAGE — FREIGHTER LOST OFF HATTERAS | Raw Rubber buy ×1.8, 6 days |
| Event | MOTOR CRAZE SWEEPS NATION | Automobile sell ×1.4, 8 days |
| Event | STEEL GLUT ON EASTERN SEABOARD | Steel Ingot/Plate/Beam sell ×0.7, 5 days |
| Event | COPPER STRIKE IN MONTANA HILLS | Copper Ore buy and sell ×0.6, 7 days |
| Event | AIR MAIL CONTRACTS ANNOUNCED | Aeroplane sell ×1.5, market depth K ×2, 10 days |
| Event | BANK RATE RAISED | Weekly interest +0.5 pp, 2 weeks |
| Event | DROUGHT LOWERS CARVELL RIVER | Waterwheel and dam output ×0.5, 5 days |
| Event | OIL GUSHER IN TEXAS | Crude buy and sell ×0.5, 6 days |
| Event | GLASS DEMAND SOARS WITH NEW HIGH-RISES | Glass and Plate Glass sell ×1.3, 7 days |
| Shock | COTTON FUTURES TUMBLE | Cotton buy and sell ×0.7, 4 days |
| Default | NOTICE OF DEFAULT: FOUNDRY MISSES PAYMENT | Ends the company (one missed payment, §16) |
| Game over | FOUNDRY FOLDS — CREDITORS SEIZE WORKS | Company ends |

Events are **fictional and random**. There are no rival companies; the market is the only opponent.

---

## 11. Art direction

**The look:** a 1920s newspaper come to life. Cream newsprint, black ink, engraving hatch lines, halftone dots, occasional two-color spot printing in one accent (printer's red) for signals: selection, blocked, danger **(proposal palette)**.

| Token | Value (proposal) |
|---|---|
| Paper | `#EFE6D2` with grain and fiber texture |
| Ink | `#1E1B18` |
| Accent (spot red) | `#A8322D` |
| Pencil (planning ghosts) | `#6E6A63` graphite, sketchy stroke |

**Projection: "engraved 3/4" implemented as an oblique view.**
- Grid tiles stay square on screen, so belts read as cleanly as top-down.
- Each sprite shows its roof foreshortened plus its **front face** and height, drawn like an engraving plate.
- Sprites anchor to the bottom edge of their footprint and are **y-sorted** by row.
- Visual height is capped at ~1.5 tiles above the footprint.
- **Occlusion fix:** buildings in front of the cursor or a selected belt fade to a 35% ink-wash outline, and the flow overlay always draws on top.
- **Rotation art:** one sprite per machine. Rotating moves printed port markers (inked arrows), not the sprite, which cuts the asset count 4× **(proposal)**. Belts, pipes, rail and roads are procedural and draw all directions.

**Shader stack (Godot, applied to the whole world):** halftone for tone (screen angle 45°), paper texture multiply, ink-bleed edge (a slight blur, then a threshold), a subtle registration offset on the accent channel, and newsprint vignetting.

**Seasons are visual only.** Snow on roofs, bare trees, autumn hatching, and a seasonal masthead engraving. No mechanical effect.

**Typography:** period faces under the SIL Open Font License: *IM Fell* (headlines, body), *Old Standard TT* (market tables) and *Playfair Display* (mastheads) **(proposal; OFL allows commercial use)**.

### Asset pipeline: Codex image generation

Codex's built-in `image_gen` makes PNGs with no API key (mechanics in `~/.claude/workspace/tools/codex.md`).

1. **Style bible:** generate and approve **one canonical reference sheet** (3 machines, 1 terrain patch, 5 item icons, in the exact oblique angle and ink style). Every later prompt references it.
2. **Fixed prompt template:**
   - Style block: "1920s newspaper steel engraving, black ink crosshatch on flat chroma-green background, oblique 3/4 view, front face visible, orthographic, no perspective, no text, no shadow on background".
   - Per-asset fields: subject, footprint (e.g. 2×2 = square base 2 units wide), visual height, detail notes.
3. **Transparency:** the chroma-key background is removed with the documented `remove_chroma_key.py` flow. Verify the RGBA output, zero-alpha corners and fringe count.
4. **Normalize:** downscale to **64 px per tile at 1× zoom** (a 3×3 building ≈ 192×250 px with its height), snap the footprint base to the tile edge, convert to pure ink (a threshold keeps the halftone for the shader), and store in `assets/`.
5. **Review:** contact sheets per batch compared against the style bible, with a rejection log so bad patterns aren't repeated.
6. **Consistency in the engine:** because the shader stack re-renders tone and paper, small style drift between generations is flattened.

**Steam disclosure:** Steam requires developers to disclose AI-generated content in the Steamworks content survey, and the disclosure is shown on the store page. Record which assets are generated from the start.

---

## 12. Audio

**Music: public-domain rags and early jazz.**
- **Compositions** published in 1930 or earlier are public domain in the US.
- **Recordings carry separate rights.** US sound recordings published in 1925 or earlier are PD as of 2026, but other countries differ. Plan: **new renders or performances** of PD scores (a commercially licensed sampled piano, or a hired pianist), processed with gramophone crackle, wow and flutter.
- **Global safety for a Steam release:** prefer composers who **died before 1956**, so the compositions are also PD in life+70 countries. Examples: Scott Joplin (d. 1917), James Scott (d. 1938), Jelly Roll Morton (d. 1941). Avoid e.g. Joseph Lamb (d. 1960) and Eubie Blake (d. 1983) for worldwide builds.
- Adaptive layering **(proposal)**: solo piano early, then a small combo (piano, banjo, cornet) as the factory grows.

**SFX (proposal):**
- Sounds: printing press thunk (a new edition arrives), telegraph clicks (orders), typewriter (planning mode), belt rattle and machine loops (distance-attenuated and thinned out at far zoom), steam hiss, waterwheel creak.
- Sources: self-recorded foley, **Freesound CC0-only** (commercial-safe, filtered by license), and Sonniss GDC bundles (royalty-free commercial). **Not** the BBC Sound Effects archive (non-commercial license).

---

## 13. Time, difficulty, saves, platform

| Topic | Decision |
|---|---|
| Time | **Always real time.** No speed controls. **Pause only by opening the menu** (Esc). |
| Difficulty | **3 presets only** (below) |
| Saves | **One save per company, always autosaving** (every in-game day, plus on quit). No manual slots, no reloading older states. |
| Crash safety | Writes are atomic (temp file → fsync → rename). Two rolling backups are used **only** to recover a corrupt latest save automatically, and players can't choose them **(proposal)**. |
| Players | **Single-player only** |
| Input | Mouse + keyboard |
| Language | **English only at launch**; localize later if it sells. All text lives in string tables from day one so that stays cheap. |
| Steam features | **Achievements** (framed as clippings) + **Cloud saves** (syncs the company folders) |

**Difficulty presets (proposal values)**

| | Boom Times | Steady Trade | Hard Times |
|---|---|---|---|
| Starting cash | $8,000 | $5,000 | $3,000 |
| Base credit | $15,000 | $10,000 | $5,000 |
| Weekly interest | 0.5% | 1.0% | 1.5% |
| Trend volatility σ | 0.5× | 1× | 1.5× |
| Event frequency | 0.7× | 1× | 1.4× |
| Patch richness W_d | 1.5× | 1× | 0.7× |

**Example achievements:** First Pour (first steel) · Horseless (first automobile) · Barnstormer (first aeroplane) · Lights On (first power station) · Diversified (10 items sold at over 80% of base in one week) · Debt-Free (clear a $50k balance) · The Dam Held · Captain of Industry (share price $100) · Commission 1–6.

---

## 14. Technical architecture

**Engine:** Godot **4.7 .NET**, all code in **C#**.

| Layer | Location (the scaffold is authoritative for exact paths) | Rule |
|---|---|---|
| **Simulation** | `src/Sim` (plain C# library) | **No Godot types.** It owns all game state and rules and is deterministic. |
| **Tests** | `tests/` (xUnit) | Unit tests for recipes, belts, power, market and saves, plus golden-replay determinism tests |
| **Presentation** | `game/` (Godot project) | Reads sim snapshots, renders, interpolates between ticks, and turns input into sim **commands** |

**Determinism**
- Fixed tick of **60 UPS**, decoupled from render FPS.
- Positions use integer fixed point (1/256 tile). **No floats in sim state.** Money is an integer in cents.
- Seeded PCG random number generators, one stream per system (worldgen, market, events), and entities iterate in stable ID order.
- Every player action is a **command** (Place, Remove, Rotate, SetDockOrder, …). The same seed and command log always reproduce the same state, which powers undo/redo, replay tests and bug repros.

**Grid & chunks.** A 32×32 chunk holds a dense tile array (terrain, land-owned flag) plus structure references. Only chunks with structures or moving items are ticked; empty terrain costs nothing.

**Belts as transport lines (why it scales)**
- Connected belt tiles are merged into **transport lines** (maximal chains). A line stores its items as an ordered list of `(item, gap to the next item)`.
- Each tick a moving line only shrinks the **front gap**. Every item behind it keeps its relative gap, so the cost is **O(1) per line per tick, not O(items)**.
- Work happens only at the ends: insertion, extraction, merges and splits.
- A fully backed-up line is marked asleep and costs nothing until its head frees up.
- This is the approach that lets factory games move hundreds of thousands of items at 60 UPS.
- Rendering walks a line's gap list only for lines visible on screen.

**Machines.** State machine: *starved / working / blocked / unpowered*. Input buffers per port; craft progress in ticks. The recipe is chosen by the input set (§5 subset rule, checked by a unit test over the whole recipe table).

**Power.** Networks (shaft, steam, electric) are graph components, recomputed only when the topology changes. Each network gets a satisfaction ratio per tick, which scales machine speed.

**Market & events.** Evaluated once per in-game hour on the same fixed-point math; the event RNG is seeded.

**Save format.**
- A versioned binary body (MessagePack-style, hand-rolled or via a library) compressed with Brotli.
- Plus a small JSON header: company name, date, share price and difficulty, for the company list.
- A schema version field and migration functions per version.

**Performance targets (proposal):** 50,000 structures and 500,000 items on belts at 60 UPS on Nolan's i7-9700K.

---

## 15. Roadmap: build everything, in order

Nolan's scope is the whole game. The milestones order the work; nothing is cut.

| M | Milestone | Done when |
|---|---|---|
| **M0** | **Scaffold**: Godot .NET project, `src/Sim`, xUnit tests, grid, belt, mine, depot, cash | A belt carries ore from a mine to a depot and cash goes up, with tests green |
| M1 | **Logistics core**: transport lines, turns, splitter/merger, sorting splitter, trestle bridge, rotate/delete/pipette, undo/redo command log | 10k items flowing at 60 UPS; determinism replay test passes |
| M2 | **Style spike**: style bible via Codex, shader stack, oblique y-sort, occlusion fade, hybrid zoom (items ↔ flow lines) | One screen of factory looks like a newspaper engraving |
| M3 | **Machines framework + T0–T2 content** (subset-rule test, fireboxes) | Iron → steel → plates/rods/wire chain works |
| M4 | **Economy**: market model, saturation, trends, Receiving Dock, credit line, escalating costs, bankruptcy, share price | A company can go bankrupt, or thrive |
| M5 | **Newspaper v1**: event-driven editions, classified-ad build menu, clippings, market page, telegram desk, archive, tutorial editions 1–6 | A new player learns the game from the paper alone |
| M6 | **Map gen**: infinite chunks, slowing patches, forests, hills, rivers, town growth, land price | Seeded worlds generate endlessly and pan smoothly |
| M7 | **Power eras**: waterwheel adjacency, boilers + pipes, power station + poles, hydro dam + flood preview | Each era clearly beats the last on efficiency |
| M8 | **T3–T5 content**: automobile and aeroplane chains, Motor Truck | Automobile and aeroplane produced end to end |
| M9 | **Long-range logistics**: roads + trucks, rail + stations + signals, barges | Remote patches feed the main works |
| M10 | **QoL**: planning ghosts + auto-build, blueprints, flow overlay v2 | Layouts can be stamped at scale |
| M11 | **Prestige**: Exposition Yard, commissions 1–6, procedural commissions | Endless goals after aeroplanes |
| M12 | **Audio**: rag renders, SFX set, adaptive layers | Full soundscape |
| M13 | **Saves & platform**: company saves, crash recovery, difficulty presets, settings, Steam achievements + cloud | A company survives crashes and syncs between PCs |
| M14 | **Balance & perf**: 50+ hour curve to aeroplanes, price/K tuning, perf targets | Playtesters reach aeroplanes around the 50-hour mark |
| M15 | **Ship**: Steam page (AI disclosure), trailer, release candidate | Released on Steam |

---

## 16. Decisions log and open questions

Resolved with Nolan on 2026-09-23:

| Question | Decision |
|---|---|
| Belt lanes | **Single lane** |
| Vehicles | **Both**: bought from the ads early; self-built Motor Trucks join the fleet at cost |
| Town and paper | **Carvell Falls** / ***The Carvell Falls Courier*** |
| Barges | **In scope** (M9) |
| Calendar | **1 day = 120 s**; spring 1920 → 1924 over 50 h |
| Rush to the hangar | **Both guards**: high price + credit line capped at $10k + 50% of assets |
| Cost curve | **Doc default** `(1 + 0.04n)^1.5`; logistics stays flat |
| Bankruptcy | **One missed weekly payment** ends the company |
| Languages | **English only** at launch |
| Electric bonus | **+25% speed** for Elec machines |
| Art resolution | **64 px per tile** at 1× zoom |

Decided while building M1 (2026-09-23, proposals resolved by the agent; tell Nolan if any feel wrong):

| Question | Decision |
|---|---|
| Belt geometry | 240 sub-units per tile; **2 goods per tile** (spacing 120) so engraved icons stay readable at 64 px; canvas/rubber/steel = 4/8/16 sub-units per tick = 1/2/4 tiles per second = exactly **2/4/8 goods per second**. Positions are good centres, in slots half a spacing in from each end, so a jammed line holds exactly two per tile. |
| Logistics exempt from escalation | **Yes**: belts, splitters and trestles keep a flat price. |
| Trestle bridge | **One type**; it runs at the tier of the belt feeding it. Placed as a pair with two clicks (entry, then far end), span 1–4, **one price per pair**; deleting either end removes both. The deck is drawn raised so goods stay visible crossing it. |
| Splitter | Lanes take the tier of the belt feeding each lane. Merging is fair by construction: a good parked at the other lane's end gets the slot when it is that lane's turn. Sorting splitter: filter set by pipetting a good (Q) and clicking the splitter; no filter sends everything right. |
| Right mouse button | Cancels whatever the hand holds (tool, pending trestle end, pipetted good); with an empty hand it demolishes, and dragging demolishes along the path. |
| Demolish refund | **75%** of the latest copy's price, as §5 says (the scaffold's 50% is gone). |
| Undo/redo | Ctrl+Z / Ctrl+Y (or Ctrl+Shift+Z). Money reverses exactly, one dragged run is one step, 100 steps deep; goods on removed belts are lost. Undone and redone actions are logged as ordinary commands so a replay needs no undo concept. |
| Belt loops | Allowed. A ring of goods keeps turning even when full. |
| Text | Every player-facing line lives in `assets/text/en.csv`, read by `game/Text.cs` (`Text.Get("KEY")`). |
| Ore depletion | Counted per mine for now; **M6 moves it to the patch** (so remove + undo cannot refresh a seam). |

Decided while building M2 (2026-09-23):

| Question | Decision |
|---|---|
| Plate resolution | Sprites stored at **128 px per tile** (2× the 64 px design size) and drawn at half size with mipmaps, so 2.5× zoom stays crisp. |
| Projection wording | The prompt that yields the GDD's oblique is "military oblique like a board-game piece" (`docs/STYLE-BIBLE.md`); "architectural plan/cabinet oblique" wording fails. |
| Terrain plates | Generated as ink on white and keyed by darkness (the green key fails for textures); one plate per ground type, turned a quarter per cell. |
| Press effects | On by default at 55% strength (halftone 2.2 px cells, bleed 0.35, 1.2 px accent misregistration); a settings toggle comes with M13. |
| Plate-less pieces | Any building or good without a plate draws procedurally (ink block / lump), so content never waits on art. |

Decided while building M3 (2026-09-23):

| Question | Decision |
|---|---|
| Craft times | Placeholders by tier until M14: T1 2–10 s (open hearth 8 s, refinery 8 s), T2 2–6 s, T3 3–12 s, T4 20–45 s, T5 60–120 s, T6 6–40 s. Table in `src/Sim/Recipes.cs`. |
| Machine buffers | Inputs: two runs of the hungriest recipe for that good (at least 2). Outputs: two runs of the fullest recipe; a machine stops starting runs when that is reached. One good leaves per tick. |
| Ports | Front edge, left to right as the building faces; a single port sits right of centre (a 2×2 facing east outputs from its south-east corner). Multi-output recipes use one port per output in order. Goods enter on any edge. |
| Recipe choice | Round-robin among recipes whose inputs are all present, so a lathe fed rods and wire alternates bolts and rivets. |
| Fireboxes | 1 coal = 120 kW·s; every "Any" machine and mine head has one (cap 8). Draw per machine in `Catalog.cs` (mine 5 kW, smelter 8, open hearth 30, engine works 30…). Coal goes to a coke oven's recipe buffer first, then its fire. |
| Starter coal | Every purchase comes with 30 lumps in its firebox, so the first works can light before a coal mine exists; a coal mine can feed itself from its own belt. |
| Electric-only machines | Buyable now, unpowered until the power station (M7). |
| Unfinished buildings | Catalog rows carry `Available=false` until their milestone ships; they are not shown in the ads. |
| Classifieds | Six columns as in §10; Tab (or clicking a tab) turns the column, keys 1–9 pick within it. Default column: Transport. |
| Mines on mixed seams | Yield the commonest seam under the footprint. |

Decided while building M4 (2026-09-23):

| Question | Decision |
|---|---|
| Fixed point | The market keeps Q, B (×1000), log-trend (milli-nats), demand growth and event timers as integers; the only doubles are a 982-entry exp table built at start. State hashes cover all of it. |
| Hourly evaluation | Q/B decay and trend steps happen once an in-game hour (5 real seconds); sales and purchases inside the hour count in the price immediately, so a dump cannot dodge the glut. |
| Trend walk | ln(trend) reverts to 0 at 1/336 per hour and takes a uniform step of ±σ milli-nats (σ = 4 / 8 / 12 by difficulty), clamped to ln 0.6 … ln 1.6. |
| Events | Ten fictional events from §10; each day one rolls with 7% / 10% / 14% chance (by difficulty), never one already running. §10's waterwheel drought and bank-rate effects are carried on the event for M7 and the credit line. |
| Credit line | Limit = base credit + half the resale value (75% of each copy's price). Purchases draw automatically; cash repays daily; interest weekly. |
| Bankruptcy | One missed weekly payment: the default notice and FOUNDRY FOLDS print, the world stops ticking and refuses commands; M13 makes the save a read-only archive. The Banker's Warning fires when, after this week's payment, next week's is beyond cash + credit. |
| Weekly profit | Net-worth change over the week (net worth = cash + resale − debt); trailing four weeks feed the share price. |
| Receiving dock | Buys as fast as its belt takes goods (up to 8/s on steel), only raws, only while affordable. Its order is set by pipetting a good and clicking it, like a sorting splitter's filter. |

Decided while building M5 (2026-09-23):

| Question | Decision |
|---|---|
| Opening the paper | **Tab** opens the Courier (Tab or Esc closes); **Shift+Tab** turns the deck's quick-classifieds column. The quick strip stays for now. |
| Editions per day | One front page per in-game day; further news waits for tomorrow's front page or prints as inside columns. Tutorial editions and the fold are extras that print at once. |
| Tutorial triggers | Ed. 1 at start; 2 on the first mine head; 3 on the first iron ingot; 4 when iron ore or ingot is 15% off its price from your own sales; 5 on the first iron plate; 6 one day later, with the first telegram (20 iron plate, due in 4 days). Each rings its ad in red pencil in the classifieds and on the strip. |
| Telegrams | After ed. 6, an order every 2–4 days while fewer than two are open, for one of the four finest goods the works has made; quantity 100/60/40/20/6/3 by tier plus a little; due in 3–6 days; filled by any depot's sales after the order; missed at the due hour. Headlines both ways, no other reward. |
| Market page | Lists every raw plus every good made, sold or bought, with sell, buy, a seven-day sparkline of closing sell prices and the trend. |
| Milestone editions | First steel, first car, 1,000 cars, first aeroplane, first waterwheel/boiler/power station/dam; first sale is an inside item. |

Decided while building M6 (2026-09-23):

| Question | Decision |
|---|---|
| Rivers | Not traced by flow accumulation (impossible on an infinite map): meandering west→east corridors every 320 rows, the main one at row 26 beside the works, 1–3 tiles wide, in a noise valley so hills keep off them. Flow is west to east (barges and the dam's "upstream" use it). |
| Start area | No forest, hills or random seams within 24×16 of the works, and no random patch centres within 32×24, so the tutorial ground is clean. |
| Patches | Dealt per 32×32 chunk (45% none, 40% one, 15% two), radius 3–5 with a noisy edge, kinds weighted iron 22 / coal 20 / copper 12 / limestone 12 / sand 9 / clay 9 / sulfur 8 / bauxite 8, richness W_d = 20,000 × (1 + distance/2,000). Overlaps go to the patch whose centre is nearest for its size. |
| Land price | Paid on every purchase including belts; not refunded (demolition refunds the structure only, undo refunds everything). Forest clearing $20 a tile, permanent. |
| Town | 16 founding blocks of 3×3 within ±11 of (0, 26), streets one tile wide; growth every 3 days of 1 + trailing profit/$500 blocks (max 4). |
| Flat maps | `World(..., flatMap: true)` for tests and benches: starter seams on bare ground only. |

Decided while building M7 (2026-09-23):

| Question | Decision |
|---|---|
| Power figures (proposals for M14) | Waterwheel 30 kW; boiler 120 kW of steam, one pump waters two boilers; power station takes up to 200 kW of steam and gives 1.5× as electricity; dam 400 kW. Fuel: firebox 120 kW·s a lump, boiler 240, so electricity from a station is 360 kW·s a lump. |
| Networks | Pipes, boilers, pumps and stations conduct; a machine joins a main by touching any of them. Poles link within a radius of 6 and reach machines, stations and dams within 6. Rebuilt only on layout change. |
| Satisfaction | supply ÷ demand per network, counted over machines with a run in hand; machines run at that fraction; boilers burn in proportion to steam actually drawn. |
| Fallback | A network that delivers nothing this tick does not count: the machine takes the next source down (steam → shaft → firebox). Electric-only machines simply stop. |
| Dam and lake | The dam must cover the river's full width (≤ 3) in each of its three columns. Its lake is the 30 columns upstream, 5 rows either side of the river centre, below elevation 0.1; nothing may stand there and the town may not be there. Removing the dam drains it. |
| Flagship proof (M8) | Proven on real machines and real power with a scripted yard crew instead of belts (belts are M1's proof). The crew works from a bill of materials with a 2× allowance per machine type; a plain round-robin crew deadlocks the chain, which is the same trap a player falls into by feeding every machine everything. Result: automobile at 699 s, aeroplane at 761 s, 60 machines. |
| Recipe selection | Stays "what arrives selects the recipe", round-robin among ready recipes, no priority setting. The one-setting rule holds; routing scarce intermediates is the player's craft (sorting splitters) and the M10 overlay must show starvation. |
| Motor Truck | A flagship good with its own front page from M8; trucks join the fleet at cost in M9. |
| Ports and caps | Rule, now tested: a machine has as many output ports as its widest recipe has outputs and never more than its front edge holds; inputs cap at two runs of the hungriest recipe, outputs at two runs of the fullest. |
| Haulage (M9) | One `Haulage` classified column (roads, rail, signals, terminals and the three vehicles) so the Transport strip keeps to belts. Vehicles are bought like buildings but never placed: two clicks on terminals give a route; A↔B for ever; a parked vehicle at either end is reused first. Right-click on a vehicle scraps it for 75%. |
| Terminals | Belts feed any edge; the yard holds 48 / 240 / 320 goods each way; vehicles bring goods to the far yard and the port belt drains it. No fuel or upkeep yet (M14). |
| Rail blocks | Connected rail cut at signals is one block; one train to a block; the train takes the block it enters and releases the ones behind its body; a train in a station holds none. A signal is a flag on a rail tile, bought for the difference over plain rail; on bare ground the signal tool lays signalled rail. |
| Vehicle figures (M14 placeholders) | Truck 24 goods, 3 tiles/s, $3,600; locomotive + 3 cars 120 goods, 5 tiles/s (60% on hills), $15,000; barge 160 goods, 1.25 tiles/s, $2,000. Leave when full, after 5 s with a part load, or after 1 s when the far end has goods waiting. |
| Rail on hills | Costs 3× (structure only, land unchanged); removal refunds the flat price. |
| Planning (M10) | Plans are free pencil ghosts, hold their ground against the town, and are built one a tick, oldest first, from cash on hand only — never from credit. A plan short of money holds the queue (layouts build in the order drawn); a plan that cannot go down is passed over. A real build over a plan supersedes it and undoing that build does not bring the plan back. |
| Blueprints | `B` drags a rectangle: what stands (and what is planned) with its origin inside is copied, settings included. `R` turns, `1`–`9` save, `B` + key loads, click stamps as plans centred on the cursor. Nine slots per session; M13 saves them with the company. |
| Flow overlay v2 | `F` prints over everything at any zoom: flow strokes, goods/min per line, what a starved machine wants, FULL / NO POWER, patch % under heads and jacks, yard counts at terminals, satisfaction % on power sources. |

| Prestige (M11) | The yard pays market price for everything and counts only bill goods. Goodwill = 25% of the bill at base prices, permanent, survives losing the yard. Procedural commissions: template (n−1) mod 6, quantities × 1.5 a round rounded up, names "The {ordinal} …" or "Zeppelin {name}" from fixed lists. |
| Music (M12) | Original ragtime composed and rendered by `tools/audio/build.py` from seeds — not renders of PD scores (no scores, soundfont or pianist on hand; hiring one is a money decision left open). Three pieces, three stems each; banjo at 40 buildings, cornet at 150; six seconds between records. |
| SFX | All synthesised in the same script (no Freesound/Sonniss), so there is nothing to license or attribute. Loops for machines, belts, steam, the wheel; one-shots for the paper, the hand and the fleet. Silent below zoom 0.35, full above 0.6, eight positional loops at most. |
| Saves (M13) | Full snapshot, not a replay: every class writes what it hashes; the command log is saved too (replay/debug), the undo history is not — a loaded company starts with no undo. Power figures are derived each tick and left out of the state hash. Checksum on the body; a damaged latest falls back to the newest backup by itself. |
| Front office | Esc with an empty hand; the only pause. New company = name + preset; loading any company reloads the scene. Scratch worlds (demo, bench, self-test) never save. |
| Achievements | Local per-company file now, Steam sink later. Diversified = ten different goods sold in a week; The Dam Held = a dam standing when a drought ends. |
| Balance (M14) | Prices tuned from the proposals: mills & furnaces ×2 (early ones unchanged), shops ×3, works ×6, reduction works ×3, station and dam ×2; K for parts 500, components 200, assemblies 40, cars and trucks 30. The model (`tests/Sim.Tests/BalanceTests.cs`) puts a brisk player at ~28 h to the aeroplane and a first-timer near 50; money gates the steel and aeroplane stages. The Catalog and Items tables are the source of truth, not §5/§8's figures. |
| Perf (M14) | 42,760 structures with 79k goods moving, 2,000 working machines, 200 trucks and 3,000 waiting plans tick in 1.2 ms; half a million goods in 0.2 ms. Plans are scanned 64 a tick. |
| Ship (M15) | Release candidate 0.9.0-rc1: Linux tar.gz and Windows zip from `tools/release.sh`, the self-test run through the exported binary, store-page draft with the AI disclosure, trailer plan, Steam sink stub. The Steam account, App ID, price and assets are Nolan's; the agent never commits, pushes or publishes. |

Decided in the 2026-09-23 audit (0.9.0-rc2; full notes `docs/AUDIT-2026-09-23.md`; items marked **confirm** are Nolan's):

| Question | Decision |
|---|---|
| Purchases on credit | Every purchase (ads, plans, docks, redo, vehicles) must leave the **next two weekly payments** covered, so spending alone can never fold the company. |
| Banker's Warning | Checked every in-game hour and after each payment; prints at once when it comes on (at most once a week) and clears when resolved. |
| Weekly interest rate | Each week is charged at the rate **posted when the week began**; a BANK RATE RAISED counts from the next week. Together with the two rules above, a fold always comes at least a week after a warning. |
| Supply events | Copper strike, oil gusher and cotton tumble move the sell price with the buy price; the dock's price never falls below the depot's (no dock→depot arbitrage). |
| Trend walk | ln(trend) reverts at 1/336 an hour with the remainder rounded in proportion (it had rounded to zero). |
| Tutorial patience | Ed. 4 also prints on the first iron plate; ed. 5 follows three days after ed. 4 without a press, so the telegram desk always opens. |
| FIRES OUT | The first time a coal-fired machine or mine head stands cold, the paper prints FIRES OUT AT THE WORKS (once per company) with the mine head ringed. |
| Share price | Floored at $0 (profit definition: proposal for Nolan). |
| Seam richness by preset | MapGen deals every patch's W_d scaled by the preset (Boom ×1.5, Hard ×0.7), once. |
| Town | Growth spots that can never take a block are skipped, so the town grows at §16's rate for the whole game; never on the dam's lake. **Confirm** the size (≈2,000 blocks by hour 50). |
| Trains | A train takes the block it enters only while no other train holds that block or any block further along its way (ends head-on deadlocks on signalled lines). **Confirm** (refines M9's rule). |
| Barges | May step diagonally where the river's cells only touch at a corner, and only where both side cells are dry. |
| Dam and plans | A dam's lake supersedes pencil plans under it, like any real build over a plan. |
| Undo | An undo step names the building it undoes (never removes what stands there since), brings a building back under its old id, with its filter/order and its fire as they were. Nothing is undone after the fold. |
| New companies | The founding page refuses empty or taken names and offers a free one ("… 2"); a new company never shares a folder with another. |
| Coal on ore belts | Unchanged rule (a full firebox refuses coal, so a mixed belt jams); made visible (clipping, red overlay tag) and ed. 2 teaches a separate coal belt. **Confirm** or change the rule. |
| Autosave | The world is captured on the main thread and compressed/written on a worker; a failed write flashes in the masthead. |

Still open:
1. Steam price point and store positioning. Decide after a demo and wishlist numbers.
2. ~~Style bible approval~~ — **approved by Nolan 2026-09-23** ("for now"); the plates are being cut by `tools/art/batch.py`; `scratch/style-lab/` is the menu if he changes his mind.
3. **Steam account, App ID and the Steam Direct fee** (Nolan) — the achievement sink and cloud path wait on the App ID (`docs/STEAM.md`).
4. **git init + private GitHub repo** (Nolan) — the agent never commits or pushes.
5. **Playtests** (Nolan) — the M14 curve is a model; the price and depth tables are the knobs.
6. **Audit decisions** (Nolan) — town size, train entry rule, coal-on-ore rule, share-price definition, seasons (§11, not built), HUD scaling above 1080p, home-made Motor Truck scrap price, dam cement, newer-version saves: `docs/AUDIT-2026-09-23.md` "Needs Nolan".
