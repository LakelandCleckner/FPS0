# FPS0

A first-person shooter built solo in Unity, focused on data-driven combat systems: a stat and modifier engine, a single damage pipeline, status effects, rolled loot with perks, and GOAP enemy AI.

Internal development builds only; nothing has been released. Current version: **0.0.17** (see [CHANGELOG.md](CHANGELOG.md)).

## Highlights

**One damage chokepoint.** Every hit, status tick and chain link resolves through `WeaponHitResolver.ResolveHit`. Crit, defense, precision, falloff and feedback are written once and apply everywhere. All multipliers are applied before damage is written, so the floating number always equals the health lost.

**Stats and modifiers as data.** Any number gameplay can change is a stat; anything that changes one is a modifier. Modifiers sum within a bucket and multiply across buckets, and buckets are assets, so rebalancing is an asset edit. Resolution is cached against version stamps, with derived stats and cycle guards.

**Phase-ordered effects.** Hit effects run Modifier, StatusApplication, Application, Reaction, with authored order kept within a phase (a stable sort, deliberately).

**Status effects and DOTs.** Per-target, per-status stack pools with independent duration, stacking, eviction, intensity and tick-timer axes. Ticks go through the same resolver, so they crit, respect resistances and credit every contributing weapon on a kill. Zero allocations per tick, guarded by a reflection-based context reset audit.

**Per-owner event bus.** Hit, kill, ammo, reload and swap events per combatant, filtered by hit source so DOT ticks don't trigger on-hit perks by accident. Nothing polls.

**Weapons.** Type, archetype and weapon layers; fire behaviour (auto, semi, burst) and delivery (hitscan, projectile, pellets) as independent axes. Range falloff, bullet magnetism, precision and recoil are per-archetype bands driven by stats. 22 archetypes and 110 named weapons, each archetype in all five elements with its own signature intrinsics.

**Elements.** Five damage types with their own identities and statuses: Ballistic Fracture into Break, Fire Burn, Frost Chill into Freeze, Shock Conductive arcs, Toxic Poison. Every status has custom body overlays and auras that scale with stacks, and shots carry element tracers and trails.

**Loot.** Owned weapons and armor are instances with rolled perks. Five rarity tiers decide how many trait columns roll and at which tier; enemies drop rolled items, and overflow goes to a vault.

**Enemies.** An enemy is an archetype plus rank, element and trait layers composed at spawn, so any body can take any element, shield or death effect. Ranks scale stats and tactics and show on overhead health bars; shields absorb, overflow and break with element blasts.

**Enemy AI.** CrashKonijn GOAP over NavMesh with planner-chosen goals and code-built capabilities from behaviour profiles: melee hordes with attack tokens, kamikazes, ranged shooters that use automatic cover, peek, strafe, suppress and dodge, and squads with leader, flanker and suppressor roles. Encounter rooms spawn squads through doors in budgeted waves, and can be generated procedurally. Hit reactions, staggers, pooling with a full reset contract and ragdoll deaths with persistent corpses.

## Systems

| Area | What's built |
| --- | --- |
| Stats | Stat definitions and registry, bucketed resolution, derived modifiers, per-stat clamps, stat breakdowns |
| Combat | Resolver chokepoint, keyed damage derivations, crit, per-type resistance and vulnerability, damage types as assets, chain and AoE primitives, on-kill explosions |
| Status | Central ticker, stack pools, stat-modifier statuses, conversion at a stack count, stun, stagger and knockback, on-kill status transfer, multi-source kill credit, stack-scaled overlays and auras |
| Weapons | 22 archetypes and 110 named weapons across sidearm, rifle, SMG and shotgun; burst, pellets, per-shell reload, range falloff, aim assist, precision, recoil patterns; rounds, element tracers and trails |
| Player | Stat-driven movement, ADS with FOV-matched sensitivity, crouch, shared first-person arms rig, third-person body with procedural weapon handling, mesh-fit hitboxes |
| Loadout | Three weapon slots and five armor slots, carried inventory, vault, slot rules, loadout locks |
| Perks | Recipe and runtime split, listen scopes, timed buffs, 14 perk types including hit conditions, mag refills, armed buffs and status triggers; archetype and weapon intrinsics; rolled columns by rarity and element |
| Loot | Loot tables, rarity weights, world drops, pickup toasts |
| Enemies | Archetypes with rank, element and trait layers, zone recolour, overhead health bars, shields, death blasts and hazard pools, hit reactions and staggers, ragdolls, pooled spawning |
| AI | GOAP with behaviour profiles, attack tokens, melee, kamikaze, ranged combat with cover, strafing, suppression and dodges, rank tactics, squads |
| Encounters | Encounter rooms, waves, spawn doors, procedural room generator |
| UI | Loot-card inventory screen with loadout, character and vault tabs, drag and drop, generated descriptions with keyword popups, stat tooltips, filters and search, gamepad navigation; health bar, perk tracker, damage indicator; floating damage numbers |
| Input | One input router for gameplay, menus, UI and debug actions, with saved binding overrides |
| Tools | FPS0 menu: pellet pattern and recoil pattern editors, falloff test lane, zone editor, item, perk and pool batch editors, Perk Audit and Perk Lab, Enemy Workshop and gallery, Enemy Squads and Encounter Rooms windows, status and shield VFX builders, hitbox and third-person setup tools |

## Project layout

```
Assets/Scripts/Combat     stats, resolver, effects, status, weapons, perks, loadout, loot, spawning
Assets/Scripts/AI         perception, mover, brain, cover, squads, GOAP planning
Assets/Scripts/Enemies    definitions, melee and ranged attacks, shields, death effects, ragdoll
Assets/Scripts/Encounters encounter rooms, waves, spawn doors, room generator
Assets/Scripts/Zones      zone sets and look presets shared by guns and enemies
Assets/Scripts/PlayerScripts  movement, aim, crouch, input, third-person body
Assets/Scripts/UI         inventory screen, HUD, UI kit
Assets/Scripts/Weapons    viewmodel rig and gun models
Assets/Editor             authoring and setup tools
Assets/GameData           stat, weapon, armor, perk, enemy and loot assets
Assets/Shaders            zone, enemy, status overlay and shield shaders
```

## Tech

- Unity 6000.0.84f1, URP
- C#, new Input System, AI Navigation
- [CrashKonijn GOAP](https://github.com/crashkonijn/GOAP) 3.1.1, installed from GitHub through the package manifest
- Git LFS for large model files

## Roadmap

Elemental enemy attacks, player-side status visuals, a debug panel, reflex sights, an item icon generator, a third-person camera with melee weapons, factions, and save data.

## Third-party assets

| Asset | Author | License |
| --- | --- | --- |
| PSX Retro Weapons Pack (arms, pistol, rifle, SMG, shotgun) | Kuptchi | Free for personal and commercial projects |
| UZI, First Person Animations (2026 Remake) | [1Matzh](https://skfb.ly/pN8EM) | [CC BY 4.0](http://creativecommons.org/licenses/by/4.0/) |
| Massive Crosshair Bundle | OccaSoftware | Unity Asset Store EULA |
| Starter Assets | Unity Technologies | Unity Companion License |
| Characters and animations (Ch15, Ch44 and animation packs) | [Mixamo](https://www.mixamo.com) (Adobe) | Mixamo terms |
| Nunito, Space Mono, Yatra One, Zeyada, Cinzel | Google Fonts | SIL Open Font License 1.1 |
| Redaction | Forest Young and Jeremy Mickel | SIL Open Font License 1.1 |
| Human Animations | Kevin Iglesias | Unity Asset Store EULA |
| Elemental VFX and shields (bases for the element, status and shield effects) | PixPlays | Unity Asset Store EULA |
| CrashKonijn GOAP | [crashkonijn](https://github.com/crashkonijn/GOAP) | Apache License 2.0 |
| Scene Ref Attribute | [KyleBanks](https://github.com/KyleBanks/scene-ref-attribute) | See package |
| Sizzle cymbal (poison hitmarker) | [pjcohen](https://freesound.org/s/147039/) on Freesound | Freesound license (see link) |
| Cartoon punch (physical hitmarker) | [rsilveira_88](https://freesound.org/s/216200/) on Freesound | Freesound license (see link) |
| Blow torch (fire hitmarker) | [abloemarts242029](https://freesound.org/s/765135/) on Freesound | Freesound license (see link) |
| Footsteps, gunfire, ammo, kill and ambience sounds; prototype assets | Jimmy Vegas, [How to Make an FPS in Unity 6](https://www.youtube.com/playlist?list=PLZ1b66Z1KFKinfPc4Ny9CDjIwcHEz0zc4) | Provided free with the tutorial series |

Third-party assets remain the property of their authors and are included only as part of this project.

## Author

LakelandCleckner
