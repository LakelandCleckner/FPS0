# FPS0

A first-person shooter built solo in Unity, focused on data-driven combat systems: a stat and modifier engine, a single damage pipeline, status effects, rolled loot with perks, and GOAP enemy AI.

Internal development builds only; nothing has been released. Current version: **0.0.11** (see [CHANGELOG.md](CHANGELOG.md)).

## Highlights

**One damage chokepoint.** Every hit, status tick and chain link resolves through `WeaponHitResolver.ResolveHit`. Crit, defense, precision, falloff and feedback are written once and apply everywhere. All multipliers are applied before damage is written, so the floating number always equals the health lost.

**Stats and modifiers as data.** Any number gameplay can change is a stat; anything that changes one is a modifier. Modifiers sum within a bucket and multiply across buckets, and buckets are assets, so rebalancing is an asset edit. Resolution is cached against version stamps, with derived stats and cycle guards.

**Phase-ordered effects.** Hit effects run Modifier, StatusApplication, Application, Reaction, with authored order kept within a phase (a stable sort, deliberately).

**Status effects and DOTs.** Per-target, per-status stack pools with independent duration, stacking, eviction, intensity and tick-timer axes. Ticks go through the same resolver, so they crit, respect resistances and credit every contributing weapon on a kill. Zero allocations per tick, guarded by a reflection-based context reset audit.

**Per-owner event bus.** Hit, kill, ammo, reload and swap events per combatant, filtered by hit source so DOT ticks don't trigger on-hit perks by accident. Nothing polls.

**Weapons.** Type, archetype and weapon layers; fire behaviour (auto, semi, burst) and delivery (hitscan, projectile, pellets) as independent axes. Range falloff, bullet magnetism and precision are per-archetype bands driven by stats.

**Loot.** Owned weapons and armor are instances with rolled perks. Five rarity tiers decide how many trait columns roll and at which tier; enemies drop rolled items, and overflow goes to a vault.

**Enemy AI.** CrashKonijn GOAP over NavMesh, with brain-owned perception (sight cone, sense radius, damage and gunfire reactions), a direct rush with flanking, lunging melee attacks, pooling with a full reset contract, and ragdoll deaths with persistent corpses.

## Systems

| Area | What's built |
| --- | --- |
| Stats | Stat definitions and registry, bucketed resolution, derived modifiers, per-stat clamps, stat breakdowns |
| Combat | Resolver chokepoint, keyed damage derivations, crit, per-type resistance and vulnerability, damage types as assets, chain and AoE primitives, on-kill explosions |
| Status | Central ticker, stack pools, stat-modifier statuses, on-kill status transfer, multi-source kill credit |
| Weapons | 12 archetypes across sidearm, rifle, SMG and shotgun; burst, pellets, per-shell reload, range falloff, aim assist, precision |
| Player | Stat-driven movement, ADS with FOV-matched sensitivity, crouch, shared first-person arms rig, third-person body with procedural weapon handling, mesh-fit hitboxes |
| Loadout | Three weapon slots and five armor slots, carried inventory, vault, slot rules, loadout locks |
| Perks | Recipe and runtime split, listen scopes, timed buffs, stat, effect and triggered perk types, rolled columns by rarity |
| Loot | Loot tables, rarity weights, world drops, pickup toasts |
| AI | GOAP goals, actions and sensors, perception, rush and flank movement, melee with lunges, ragdolls, pooled spawning |
| UI | Inventory screen with loadout, character and vault tabs, stat tooltips, filters and search; health bar, perk tracker, damage indicator; floating damage numbers |
| Input | One input router for gameplay, menus, UI and debug actions, with saved binding overrides |
| Tools | Pellet pattern editor, falloff test lane, weapon zone editor, item, perk and pool batch editors, GOAP capability builder, hitbox and third-person setup tools |

## Project layout

```
Assets/Scripts/Combat     stats, resolver, effects, status, weapons, perks, loadout, loot, spawning
Assets/Scripts/AI         GOAP goals, actions, sensors and behaviours
Assets/Scripts/Enemies    melee, ragdoll, crowding, loot dropping
Assets/Scripts/PlayerScripts  movement, aim, crouch, input, third-person body
Assets/Scripts/UI         inventory screen, HUD, UI kit
Assets/Scripts/Weapons    viewmodel rig and gun models
Assets/Editor             authoring and setup tools
Assets/GameData           stat, weapon, armor, perk and loot assets
```

## Tech

- Unity 6000.0.84f1, URP
- C#, new Input System, AI Navigation
- [CrashKonijn GOAP](https://github.com/crashkonijn/GOAP) v3 (kept locally under `Packages/`, not committed)
- Git LFS for large model files

## Roadmap

Enemy variants (elite, miniboss, elemental) and squads, a debug panel, reflex sights and recoil, an item icon generator, a third-person camera with melee weapons, factions, and save data.

## Third-party assets

| Asset | Author | License |
| --- | --- | --- |
| PSX Retro Weapons Pack (arms, pistol, rifle, SMG, shotgun) | Kuptchi | Free for personal and commercial projects |
| UZI, First Person Animations (2026 Remake) | [1Matzh](https://skfb.ly/pN8EM) | [CC BY 4.0](http://creativecommons.org/licenses/by/4.0/) |
| Massive Crosshair Bundle | OccaSoftware | Unity Asset Store EULA |
| Starter Assets | Unity Technologies | Unity Companion License |
| Characters and animations (Ch15, Ch44 and animation packs) | [Mixamo](https://www.mixamo.com) (Adobe) | Mixamo terms |
| Nunito, Space Mono, Yatra One | Google Fonts | SIL Open Font License 1.1 |
| CrashKonijn GOAP | [crashkonijn](https://github.com/crashkonijn/GOAP) | See package |
| Scene Ref Attribute | [KyleBanks](https://github.com/KyleBanks/scene-ref-attribute) | See package |

Third-party assets remain the property of their authors and are included only as part of this project.

## Author

JustChoochit
