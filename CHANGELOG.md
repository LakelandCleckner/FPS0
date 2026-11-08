# Changelog

All notable changes to FPS0. Internal development builds only; nothing has been released.
Versions are assigned retroactively by milestone and stay below 0.1.0 until a first release.
Engine: Unity 6000.0.84f1 (URP).

Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Housekeeping commits (scene saves, .gitignore, editor state, engine version bumps) are omitted.

---

## [0.0.11] - 2026-11-02 to 2026-11-07

Third-person body, player hitboxes, enemy rework and ragdolls, unified input, inventory screen, rarity, loot and vault.

### Added
- Third-person body (Mixamo Ch15, pistol and rifle locomotion packs, legs mask) with `PlayerPresentation` snapshotting movement, crouch, aim and pitch and relaying held-weapon events; `ThirdPersonBodyAnimator`; `BodyVisibility` (shadows-only body and a legs-only mesh in first person); `FirstPersonLegsOffset` drawing the legs back through proxy bones.
- `ThirdPersonWeapon`: world gun on the right hand, left-hand IK, spine aim correction with Hip Aim Weight, fire raise to the shoulder, kick, procedural magazine and shell reloads, equip and stow lowering.
- Third-person grip points, scale and barrel axis on `GunModel`; `WeaponViewmodelSO.thirdPersonController`; TP Body Setup, TP Grip Tool and Mixamo Clip Setup editor tools.
- `PlayerHitbox` per-bone hitboxes on the third-person body; Player Hitbox Setup and Enemy Hitbox Mesh Fit tools sharing `HitboxHullCutter` (per-bone convex hulls under the face limit, separate hand and foot hulls).
- Hitscan and projectile hits on the player, routed through `PlayerHurtbox` with the hitbox's body part; part-aware `PlayerHurtbox.TakeDamage`.
- Brain-owned enemy perception: view cone, 10 m sense radius, line of sight to any player collider; engage on any health drop and on gunfire heard within 80 m (`AINoise`, `PlayerGunfireNoise`); Relentless engagement with a lose-sight grace and search fallback.
- `SurroundRing` tuning: direct rush to contact range led by player velocity, settled-enemy avoidance priority, flanking into the nearest open gap when blocked.
- Melee lunge per move (distance, strike distance, frame window, tracking or committed, wind-up move speed); Reach hit mode (distance and front cone) as the default; clear-line check against other enemies' body blockers.
- BodyBlocker layer with enemy blocker capsules and a player `NavMeshObstacle`.
- `EnemyRagdoll`: ragdoll death with persistent corpses (cap 50, optional lifetime, sink and return to pool), hull corpses, pushes from the killing shot, shots into corpses and on-kill blasts.
- `PlayerInputRouter`: one `PlayerInputs` instance, UI-mode locks with cursor and UI module, binding override save and load; Reload, Swap, Slot1-3, Menu map (Inventory on Tab / View) and a Debug map holding every debug key.
- Inventory screen: loadout tab, carried grid, item detail with layered stat bars and perk columns, character tab with stat sheet, armor, active perks and health; `StatTooltip` breakdown with hover and click-to-pin; Build Inventory Screen tool.
- UI framework: `UIStyleSO`, `UIKit`, `UITile`, `StatSheetSO` (value formats Plain, Bonus, Multiplier, Both), `WeaponStatPreview`, `StatContainer.GetBreakdown`, `IStatSourceLabel`, `StatDefinitionSO.lowerIsBetter`.
- `WeaponTypeSO.inventorySlot` with a per-weapon override; carried weapon cap per slot.
- Item Batch tool (armor, weapons and archetypes from text lines, any-field keys, `from=` copying) and Trait Perk Batch tool; Perk Pool Batch `min=` column rarity.
- Rarity per item instance (Common, Uncommon, Rare, Legendary, Mythic) with `PerkColumn.minRarity`, rarity-picked perk tiers, hand-made Mythic items and rarity colours in the UI.
- Item drops: `LootTableSO`, `RarityWeights` (default 50/30/14/5/1), `ItemDrop` walk-over pickups coloured by rarity, `EnemyLootDropper`, `PlayerVault`, Setup Item Drops tool and a debug drop key.
- Vault tab with store, hold-to-delete (longer from Legendary up), filters, sorting and search.
- `PickupToast` HUD rows for picked-up items.
- Six archetypes (Rapid Auto Rifle, Precision Semi Rifle, Rapid Semi Shotgun, Heavy Sidearm, Tactical SMG, Rapid SMG) and launch weapons Rattler, Marksman, Scatter, Thumper, Cutter, Buzzer.
- Armor stat perks, 24 armor trait perks, per-slot armor pools and 16 generic armor pieces; a sixth trait column at Mythic.
- `StartingLoadoutSO` Randomize with weapon and armor pools and rarity weights.
- `precision_damage` weapon stat: `WeaponArchetypeSO.precisionMultiplier`, `WeaponSO.precisionDelta`, `IPrecisionSource`; applied to direct head hits.
- ADS Nudge and ADS Tilt on `WeaponViewmodelSO`; Debug Center Dot.
- `BurstFireBehavior.Cut`.

### Changed
- Enemy brain states simplified to Wander, Engaged and Search; goals requested only on state change; `PlayerVisible` driven by the brain.
- Enemy x Player collisions off; shots and magnetism line of sight ignore body blockers.
- Enemy hitboxes are mesh-fit hulls; old shapes kept for the ragdoll; enemy head multipliers set to 1.0.
- Enemy melee routes to the closest player hitbox and keeps chasing through the wind-up; NavMeshAgent steering retuned.
- Movement, aim, crouch, fire controller and loadout read input through the router; serialized key fields removed; gamepad sprint and crouch on stick press.
- Debug keys moved off the F row to an Alt layer; falloff HUD on 0.
- Perk rolls never repeat a family on one item; a column with nothing left stays empty.
- Weapons renamed to one family name per archetype (Dueler, Plinker, Burster, Pumper, Slugger, Sprayer); ids unchanged.
- Existing archetypes given authored range, aim assist and falloff; Bursting and Plinking Sidearm handling raised.
- UI text shrinks and wraps to fit; taller uniform loadout cards with a perk chip grid.
- Arms aim breathing and aim walk use the aim pose; sights aligned per viewmodel.
- Sprint, sprint-out or swap cuts an in-progress burst.
- Spelling: "armour" to "armor" in code, comments and UI text.

### Fixed
- ADS held back while a crouch or stand blend played.
- Enemies freezing beside the player when GOAP switched to Attack with the target in range.
- Agent event handlers stacking on every respawn.
- Third-person startup race that hid the gun and left the arm lowered.
- Aggressive Pump Shotgun display name spelling.

### Removed
- `DefenseDebuffTest`, `ChainTestProbe` and the unused `InputSystem_Actions` asset.
- Surround slot assignment (replaced by the direct rush).

---

## [0.0.10] - 2026-10-17 to 2026-10-31

Inventory, armor, perks, player HUD, enemy melee and weapon skins.

### Added
- Per-weapon viewmodel material swaps (`MaterialSwap`, `WeaponSO.viewmodelSkin`), applied to spawned gun renderers.
- `WeaponZones` shader: URP lit, id zone mask with per-zone recolour, brightness, metallic, smoothness and emission.
- `WeaponZoneSetSO` (shared zone convention, stable ids, max 16) and `WeaponShaderSO` (per-zone look preset applied by property block); `WeaponSO.defaultShader`.
- Weapon Zone Editor: click UV islands to assign zones, hover outline, live preview, undo, mask load/save, preset preview, legacy RGBA conversion. `WeaponShaderSOEditor` with zone dropdown and duplicate warning.
- Inventory: `WeaponInstance`, `AmmoPoolSO`, `LoadoutSlotMask`, `StartingLoadoutSO`; `PlayerInventory` (carried weapons with a cap, shared ammo pools); `PlayerLoadout` (three weapon slots, slot rules, locks).
- Per-weapon slot override on `WeaponSO`; optional `ammoPool` on `WeaponArchetypeSO`; `WeaponDamageSource.Initialize` for pre-Awake instance injection.
- `AmmoPickup` routes to a shared pool or the held weapon, with Rounds / Magazines units and `consumeWhenUnused`.
- Armor: `ArmorSlotSO`, `ArmorSO`, `ArmorInstance`; armor slots on `PlayerLoadout` with equip, clear, locks and `ArmorSlotChanged`; carried armor with a per-slot cap; starting armor; Head, Arms, Chest, Legs and Trinket slot assets.
- `ModifierGrant` and `LoadoutModifierApplier`: one registration point for `InLoadout` / `InHands` modifiers on the player and loadout weapons.
- Perk system: `PerkSO` / `PerkRuntime` with listen scope, per-item state, timers and modifier grants; `PerkClock`; `WeaponPerks` and `ArmorPerks` hosts.
- Perk types: Stat, Effect, `TriggeredBuff`, `PrecisionRefund`, `HolsterReload`, `SwapSynergy`, `MagScaled`, `HealOnKill` and `LowHealth`.
- Perk columns, intrinsic perks and curated rolls on `WeaponSO` and `ArmorSO`; nested `PerkPoolSO`; default columns on `WeaponTypeSO` and `ArmorSlotSO`; roll mode on `StartingLoadoutSO`.
- Armor stat perks (Tough, Bounding, Steady Hands, Fast Hands) and pools (Survival, Mobility, Handling, Armor Traits, Defensive).
- `WeaponDamageSource.AddEffect` / `RemoveEffect`; `WeaponAmmo.AddToMagazine` / `RefillInstant`.
- Multi-source kill credit: each status entry records its applying source, and every live source gets a Kill.
- Stat Perk, Effect Perk and Perk Pool batch editor windows with tiers, folder rules, excludes and live validation.
- `CombatantHealth.HealthChanged`; `StatContainer.DebugDescribe`; debug keys for loadout, armor (Shift/Ctrl+F1-F5), perk reroll (F8), health (F9) and stat dumps (F10).
- `HealthBar`: fill over a delayed chip, segment and major ticks snapped to screen pixels, current/max text, low-health pulse, floating hit and heal numbers.
- `PerkTracker`: code-built tiles with stacks, timers and counters; stowed-weapon perks dimmed. `PerkStatus`, `PerkRuntime.AnyStatusChanged`, and icon and short name on `PerkSO`.
- Enemy melee (`EnemyMeleeAttack`): weighted moves with per-move clip frames, damage multiplier and hit parts; overlap from the enemy's own hitboxes during damage frames; one hit per swing; cooldown after recovery.
- `EnemyAttackStatKeys` (`attack_damage`, `attack_range`, `attack_cooldown`, `attack_speed`).
- `PlayerHurtbox` and the `DamageTaken` event; `playFeedback` toggle on `WeaponHitResolver`.
- GOAP `PlayerInAttackRange` key and sensor, and `AttackAction`.
- `DamageIndicatorRing` and `CameraBump`.
- Enemy Attack Animator Setup tool; alien zombie `Attack_0` state and parameters.
- GOAP Capability Builder: text authoring, validation before write, Create Missing, Add `GoapIds`.

### Changed
- `WeaponTypeSO.slotCategory` replaced with `allowedSlots`.
- `WeaponLoadout` spawns weapons from a runtime prefab per owned instance, caches and deactivates removed weapons, and stows then equips when the held slot is replaced.
- `WeaponAmmo` magazine backed by the instance; reserves by the instance or a shared pool.
- `WeaponHitResolver` falls back to runtime lookups for its hitmarker and audio references.
- Scene-placed player weapons replaced with the `WeaponRuntime` prefab and a `StartingLoadout` asset.
- Player max health change mode set to Proportional.
- `ModifierEntry` moved to the loadout namespace and shared with perks; `SetValue` and single-grant Remove on the applier.
- `WeaponSO.riderEffects` deprecated; riders moved to intrinsic Effect Perks.
- Armor bonuses moved to intrinsic Stat Perks; armor pieces inherit their slot's default columns.
- Zone masks moved from RGBA to id masks; pistol, rifle, SMG and shotgun base materials switched to `WeaponZones`; a shader preset assigned on every weapon.
- `ChaseAction` targets attack range; `AgentMoveBehaviour` holds position while swinging.
- Some rifle animations set to loop.
- HUD canvas set to Pixel Perfect.

### Fixed
- Nested stat resolves shared one scratch list, adding a derived source's modifiers to the dependent stat (`ads_time` resolved to 26.4s with handling modifiers).
- Health bar ticks sat off the fill under the default slider layout.

### Removed
- `UVIslandFinder` (superseded by the Weapon Zone Editor).
- `ArmorSO.modifiers` and the applier's armor registration path.

---

## [0.0.9] - 2026-10-17

Range, aim assist and status-transfer hardening.

### Added
- **Range stat** (`range`, 0-100) mapping into a per-archetype falloff start band; `rangeDelta` on `WeaponSO`.
- **Archetype falloff**: start band, end multiplier, damage floor, hip multiplier (can exceed 1) and an optional drop curve.
- **`ShotInfo`** carries the fire-time origin and ADS blend, so falloff reads where and how the shot was fired.
- **`IRangeFalloffSource`**: falloff applies to direct-hit base damage only; riders, splash and chains stay full value.
- **`WeaponFalloff`**: shared falloff math used by both runtime and debug tools.
- **`FalloffTestRange`**: an edit-previewable lane of dummies placed around the held weapon's band (reference, hip/ADS start, mid-drop, end, past end), with floor markers, distance labels and an F3 HUD comparing expected and actual damage.
- **Aim assist stat** (`aim_assist`, 0-100) mapping into a per-archetype magnetism cone band; `aimAssistDelta` on `WeaponSO`.
- **Archetype aim assist**: ADS cone band, hip multiplier, and reach as a multiple of falloff end.
- **Fire-time bullet magnetism** in `WeaponFireController`: near-misses bend toward the hitbox point nearest the ray, with a line-of-sight check; skipped when the ray already hits.
- **Hard 256-entry safety cap** for statuses left at `maxEntries` 0, with a one-time warning naming the status.

### Changed
- Pellet patterns recentre on the magnetised direction.
- Status import is batched per status: append all, trim to cap once in a single order-preserving pass, sync the stat modifier once.
- Status export is capped to the receiving pool's cap, keeping the strongest entries.
- `[Transfer]` logging moved behind `StatusLog` (compiled out without `STATUS_DEBUG`).
- Falloff tuned for sidearm, rifle, buckshot and slug archetypes.

### Fixed
- Runaway status transfer: per-entry eviction during import was O(n^2), and uncapped pools could grow exponentially through chained on-kill transfers (about two million entries on one victim, freezing the editor).

---

## [0.0.8] - 2026-09-28 to 2026-10-07

PSX viewmodel, ADS, crouch, weapon families and pellet delivery.

### Added
- **PSX Weapons pack** (Kuptchi) imported and configured: point filtering, no compression, mipmaps off; per-weapon and arms materials with the projectile slot remapped; Generic avatars for arms and guns, with animation FBXs copying from the matching avatar; static props (mags, shells, bullets) set to no rig.
- **`RetroWeaponClipRenamer`**: editor postprocessor renaming the pack's Blender-exported "Scene" clips to their FBX file name, scoped to the pack folder.
- **Uzi first-person model** imported with textures mapped. Credit: "UZI | First Person Animations (2026 Remake)" (https://skfb.ly/pN8EM) by 1Matzh, licensed under CC BY 4.0 (http://creativecommons.org/licenses/by/4.0/).
- **Viewmodel animation**: `AC_Arms_Base` (locomotion and action layers) and `AC_Gun_Base` controllers, with arms and gun override controllers per weapon (pistol, rifle, SMG, shotgun); viewmodel prefab with the arms offset aligned to the camera bone.
- **`ViewmodelRig`**: one shared arms rig, hand socket and base clip roles.
- **`WeaponViewmodelSO`**: per-weapon arms controller and gun prefab.
- **`GunModel`**: muzzle and sight transforms on each gun prefab; the muzzle is injected at spawn.
- **Per-shell reload** (`ReloadStyle.PerShell`): loads one shell per step, interruptible by the trigger, with a handling-based ready time.
- **ADS** (`PlayerAim`): stat-driven `ads_time` (derived from handling) and `ads_zoom` magnification; tan-space FOV zoom; FOV-matched sensitivity; move-speed penalty; suppressed while swapping, sprinting or reloading; pressing aim cancels a sprint.
- **Aim pose driven by ADS progress**: AimRaise state's Motion Time follows `AimProgress`, so tapping aim is as responsive as holding it.
- **Sight alignment**: sight offset measured from the aim pose, so sights land on screen centre at full ADS.
- **`ads_time` / `ads_zoom` stats**, archetype ADS fields and `adsZoomDelta` on `WeaponSO`.
- **`GameSettingsSO`**: FOV and FOV axis, viewmodel FOV, hip/ADS sensitivity modes, and hold/toggle/hybrid input modes.
- **Viewmodel layer and URP overlay camera**: no wall clipping and a fixed viewmodel FOV.
- **`ReticleCanvas`** (screen space, camera) so the gun draws over hitmarkers.
- **Crouch** (`PlayerCrouch`): shrinks the controller from the top, lowers the camera, applies a move-speed modifier, blocks sprint and refuses to stand under geometry; crouch arms states driven by the `Crouching` parameter; Aim and Crouch input actions.
- **Weapon families on the shared rig**:
  - Plinker 300 sidearm rebuilt on a fresh logic object (damage source, ammo, fire controller, animator, hit resolver, event bridge).
  - SMG type, spraying archetype, Sprayer 600 test weapon.
  - Rifle type, burst archetype, Dueler 390 test weapon.
  - Shotgun type, slug archetype, Slugger 120 test weapon (per-shell reload).
  - Gun prefabs with magazine meshes parented to their Magazine bones; viewmodel SOs, logic objects and loadout slots for each.
- **Pellet delivery** (`PelletDeliverySO` / `PelletDelivery`): wraps any delivery and fires N pellets in a fixed authored pattern scaled by the new `pellet_spread` stat; buckshot archetype on projectile pellets.
- **Pellet damage split**: `ShotInfo` carries pellet index/count and a damage scale; pellets split `weapon_damage` evenly on direct hits while splash and chains stay full value; crit rolls per pellet.
- **Pellet pattern editor**: real-metre preview at a chosen distance; drag, Shift+click add, right-click delete; presets and mirroring; preview `pellet_spread` slider; enemy prefab preview with a posed clip, aim height and facing; pellets raycast against the model's hitbox colliders for head/body/miss counts and expected damage; flat unlit preview material; settings stored per machine.

### Changed
- `WeaponAnimator` drives the shared arms and the spawned gun, reading clip lengths through override controllers.
- `WeaponLoadout` spawns gun models onto the hand socket and swaps the arms controller on equip.
- `WeaponFireController`: the trigger interrupts a per-shell reload.
- `PlayerMovement` exposes resolved walk speed and sprint multiplier; sprint gated by crouch.
- `WeaponArchetypeSO` / `WeaponSO` / `WeaponDamageSource`: reload style, viewmodel reference, equip time.
- Legacy gun model and capsule visuals removed from the player prefab.
- Crosshair fades out with ADS.
- Pellet feedback merges by `ShotId` within a short window rather than by call scope: one hitmarker and sound per target (upgraded on kill) and one summed damage number, which now also works for projectile pellets arriving on later frames.
- `PelletDelivery` no longer calls into the resolver directly.
- Arms Fire state is a 1D blend tree (hip Fire to Fire_Aiming) on `AimProgress`; Any State to Fire no longer branches on Aiming.
- Projectile pellet pattern tuned.

### Fixed
- Projectiles fired parallel from an offset muzzle missed thin hitboxes at angle-dependent distances; `ProjectileDelivery` now converges onto the crosshair aim point (nearest non-owner camera-ray hit, triggers ignored).
- Alien zombie head hitbox had no combatant assigned, so head hits were dropped.
- Enemies drifting and turning on their own: root motion off and clip root transforms baked into pose.
- Hitmarker rotating incorrectly on the camera-space canvas (now uses local rotation).
- Crosshair outline restored; CrossDot moved back to the overlay canvas so it stays above the viewmodel at hip and still fades with ADS.
- Un-aiming mid-shot finished the aimed fire clip and then snapped.
- Runaway status transfer growth: `maxEntries` set on the burn statuses (code-level guard followed in 0.0.9).

---

## [0.0.7] - 2026-08-10 to 2026-09-26

On-kill explosions, enemy pooling, status transfer and stat-modifier statuses.

### Added
- **On-kill explosion** (`ExplosionOnKillSO` / `ExplosionOnKillEffect`): a Reaction-phase rider that detonates around a killed enemy on the sibling-hit primitive.
  - Damage defaults to a percent of the wielder's `weapon_damage`, so blasts scale with the gun and its buffs; can be authored flat or off another stat.
  - An empty status field is a plain damage blast; a filled one also applies that status to every target.
  - Every target takes a full direct hit; blasts do not re-detonate on their own splash kills.
  - The dedup set is seeded with the victim, so a blast never hits its own corpse and simultaneous kills never hit each other's.
- **Blast VFX**: placeholder sphere (expands and fades) when no prefab is assigned, an editor radius gizmo, and real VFX scaled by radius over an authored reference size.
- **On-kill effects on statuses** (`StatusSO.onKillEffects`): appended to the tick's effect list once at pool build, so a lethal burn tick detonates exactly like a lethal shot.
- **Enemy spawner** (`EnemySpawner`): a pool per enemy type keyed by id; `Spawn` (one at a position) and `SpawnGroup` (N scattered in a radius) both return handles for future squad tracking.
- **`EnemyPool`** (grow-on-demand per prefab) and **`EnemyPoolHandle`** (caches `IPoolable` components); return resets then deactivates, reuse activates then resets.
- **`IPoolable`** (`OnSpawn` / `OnDespawn`) reset contract: health and `IsDying`, AI state and timers (re-requests WanderGoal), memory, nav path, wander target, status pools, stat modifiers and damage numbers all reset on reuse.
- **`GoapRuntimeInit`**: assigns the GOAP agent type in code by asset-name id, self-initialising for scene-placed enemies and called by the pool for spawned ones, guarded against double assignment.
- **`SpawnTestProbe`** (editor only): comma spawns one enemy, period spawns a group.
- **`CombatantHealth.OnDeath`** event; `StatContainer.ClearAllModifiers`.
- **`WanderMarker`**: forces a fresh wander target on every wander entry while still committing to a point mid-walk.
- **Status transfer on kill**: an explosion carries the victim's stacks, filtered to its `transferableStatuses` list, onto everyone it hits.
  - Transfer moves derivation inputs, not weights, so weight re-derives live on the new target; remaining duration, stack count and the original attacker carry over.
  - Transferred entries bypass `durationMode` and only decay, so a self-propagating fire chain is bounded by construction.
  - `transferResetsTickCadence` so a slow DOT doesn't wait a full interval before its first tick on a new target.
- **`StatusApplication` effect phase**, sorting before damage, so a lethal hit stamps its DOTs before the kill lands.
- **Stat-modifier statuses**: a status can apply a modifier (`modifierTargetStat`, `modifierBucket`, `modifierPerIntensity`) scaling with pool intensity; added on the first stack, synced as entries change, removed on expiry or teardown. `StatModifier.Value` is settable and `StatContainer.NotifyModifierChanged` re-resolves in place.
- **`SlowStatus`**: negative multiplicative `move_speed` modifier with no tick damage, the first non-damage status.
- **Alien zombie** (Mixamo Ch44): humanoid rig with looping idle/walk/run/sprint clips; locomotion blend tree on Speed (run at 1.3x playback at the sprint threshold); synced Arms override layer with an avatar mask for sprint arms; per-bone colliders via the Ragdoll Wizard with `EnemyHitbox` on layer 7; prefab variant of `EnemyRoot`.
- **`AlienZombieBaseLocomotionAnimator`**: drives Speed from the NavMeshAgent's horizontal velocity, damped, with a stop-threshold snap.
- **`HitLog`**: `HIT_DEBUG`-conditional hit tracing (calls and arguments compile out) with a runtime mute (`WeaponHitResolver.logHits`). Logs resolved outcomes, skipped resolutions, misses, no-hitbox and missing-combatant drops, pierce-dedup skips and collider-to-hitbox mapping, flagging inherited hitboxes.

### Changed
- `CombatantHealth` raises `OnDeath` instead of destroying; the pooled return is deferred to end of frame so the killing hit's Reaction phase reads the victim's live status pools first.
- `PooledEnemy`, `NavMeshAgentPoolReset` and `AccumulatorPoolReset` consolidated into `EnemyPoolAdapter`, running resets in an explicit order (generation bump, accumulator clear, nav warp).
- `StatusReceiver` clears pools on spawn as well as on disable; `CombatantStats` strips all modifiers on spawn.
- Damage-number accumulator stamps each number with the target's per-life generation and evicts stale ones before a new report.
- Enemy movement speed resolves live from stats each frame; `AgentMoveBehaviour` writes to the agent only on change.
- Agent type registration lives on a config child of the GOAP manager, independent of any enemy.
- Root whole-body hitbox removed from the alien zombie.
- Burn statuses given an on-kill explosion.

### Fixed
- Statuses applied from a pooled AoE/chain context ticked only once: the tick delegate closed over the context, which is recycled when the blast drains. It now binds to the target.
- Instantiated GOAP agents threw in Awake: a prefab can't reference the scene's `GoapBehaviour`, so `AgentTypeBehaviour` was removed from the enemy prefab.
- Damage numbers bled across lives of a pooled enemy, most visibly when a lethal DOT tick killed and the enemy respawned the same moment.
- Wander resumed the same target after any interruption.
- Chase and Investigate Speed Multiplier resolved to zero (Use Max enabled with a max of 0), freezing enemies once movement went stat-driven.
- One-shot kills died clean without applying their status riders.

---

## [0.0.6] - 2026-07-21 to 2026-07-26

Weapon event surface, performance pass, burst fire, loadout and the chain primitive.

### Added
- **Weapon event surface**: `WeaponEventBus` (per-owner, per-weapon routing with a depth guard), `WeaponEvents` (event enum and payload carrying the live `HitContext`), `IHitEffectContributor` for in-flight modification, `HitSourceMask`, and `WeaponEventBridge` forwarding the fire controller's events.
- **Ammo events** at every `WeaponAmmo` state transition, including the new `ReloadComplete` and `ReloadCancelled`.
- **Hit, PrecisionHit, Kill and PrecisionKill** events from `WeaponHitResolver`, with contributors queried before phase sorting.
- **`ProjectilePool`**: prefab-keyed, auto-creating; projectiles return instead of being destroyed.
- **`HitContext.Generation`**: events capture it at publish and validate on read, so a retained context fails loudly (editor / `STATUS_DEBUG`).
- **`StatusLog`** (compiled out without `STATUS_DEBUG`) and **`StatusPerfMonitor`** (pool integrity, weight caching, expiry cleanup, per-frame GC alloc).
- **Burst fire** (`BurstFireBehavior`): shots per burst and intra-burst delay authored, cooldown derived so RPM stays comparable across weapons; optional repeat while held; finishes once started.
- **`ShotInfo`** on every shot (shot id, burst index and count, charge level), stamped onto `HitContext`. `ShotId` is per weapon and monotonic for the controller's life; pierce shares one id.
- **`ProjectileSO`** owning the prefab and config, so projectile types are content.
- **Per-burst-index delivery overrides** (e.g. a different final round).
- **`WeaponLoadout`**: owns the player's weapons and the swap state machine, timed by each weapon's own stow and equip stats; double-tapping swap returns to the original weapon carrying progress; publishes Equip and Stow.
- **`handling` stat** with equip and stow time derived from it proportionally.
- **`IsActive` / `CanFire`** split between being in hand and being settled enough to shoot.
- **Reload refill point** (`refillPoint`): cancelling after it keeps the ammo.
- **Sprint, walk, ready and stow animation states**, with equip, stow and sprint-exit clips scaled to resolved durations.
- **Chain primitive**: `ChainResolver` (deferred queue with sibling and link entry points, drained at the outermost resolution), `ChainContextPool`, `NeighbourFinder` (allocation-free overlap sphere with faction filter, plus nearest-N), a shared already-hit set on `HitContext`, `ICombatant.TakeDamage`, and `ChainTestProbe` (editor only).

### Changed
- Status ticks allocate nothing: each pool builds its context, effect list and tick effect once and refills per tick.
- `HitContext`'s dedup set is lazy, removing an allocation from every hit.
- `StackEntry.Weight` is frame-stamped (one resolve per tick instead of five or more).
- Status removal is swap-back; each pool caches its owning receiver.
- One reused `HitContext` per in-flight projectile, refilled per hit; all per-flight state reset in `Init`.
- `ApplyStatusTickDamage` cached on `EnemyHitbox` instead of a closure per hit.
- Resolver sorts with a stable insertion sort instead of LINQ, and pools its effect list by nesting depth.
- Status ticks carry the originating damage source.
- `AmmoDisplay` subscribes instead of polling and follows the equipped weapon.
- Fire mode is a serialized class on the archetype instead of a `FireModeSO` asset; `DeliveryBuildContext` carries only scene-bound refs.
- Stowed weapons stay enabled and subscribed, with renderers and animator disabled; `Rebind` on equip.
- Input gated behind `IsActive`; swapping cancels an in-progress reload; auto-reload-on-empty gated behind `IsHeld`.
- `WeaponAmmo` re-resolves magazine size on access.
- Reloads are committed: firing waits instead of cancelling; only stowing or sprinting cancels. Reload input buffered briefly through a sprint cancel.
- Sprint cancels key on resolved sprint, not the sprint key; firing or reloading while sprinting drops the sprint first.
- `ContextResetAudit` classifies the new `HitContext` fields at every reuse site, including the chain-link site.
- StackPool and AI debug logging commented out.

### Fixed
- `WasCrit` was never reset (latent; would have surfaced under context reuse).
- A duplicate Player tag made `FindWithTag` nondeterministic.
- Trigger buffering left queued intent after release, letting a stray shot or an extra burst out.
- A burst that ran dry dry-fired its remainder.
- Holding fire and spamming reload produced free reload start/cancel events.
- A burst could freeze at the reload gate and fire its remaining shots seconds later.
- Magazine modifiers applied at runtime were ignored (size was cached at startup).
- The swap key did nothing on first press (both indices started at zero).

---

## [0.0.5] - 2026-07-08 to 2026-07-18

The stat system, and moving weapons, crit, health, movement and defense onto it.

### Added
- **Stat engine**: `StatDefinitionSO` (default, optional min/max, display hints), `StatRegistry` (deterministic id-sorted indices), `StatBootstrap`, `StatContainer`.
- **Bucket resolution**: nestable `StatBucketSO` with Sum/Multiply rules; ScaledBase `(base + flat) x (1 + additive) x mult` and Pool `(base + flat + additive) x mult` modes; each `(1 + sum)` floored at 0; stat clamp applied last.
- **Modifiers**: `ModifierHandle` (unique id plus owner), `StatModifier`, version-stamped caching, precise removal and `RemoveAllFromOwner`.
- **Derived modifiers**: value = coefficient x another resolved stat, optionally capped; per-stat cycle guard; lazy cross-stat invalidation via version stamps.
- **`CombatantStats`** with player-scope `crit_chance`, `crit_damage` and `global_damage`; attacker stats threaded through deliveries to the resolver.
- **Critical strikes**: one roll per resolution from the attacker's stats, multiplier `1 + crit_damage`; applies to hits and DOT ticks alike; independent of precision.
- **Health as a stat** (`max_health`) with `MaxHealthChangeMode` (ClampOnly, Proportional, Additive).
- **Keyed derivations**: Flat / PercentOfStat / PercentOfQuantity, owned by Attacker / Source / Target; `QuantityKind` (current, missing, fraction of health); `DerivationContext`.
- **Player movement stats**: `move_speed`, `sprint_multiplier`, `jump_force`, ground and air acceleration, `gravity`.
- **Defense stats**: per-type resistance keyed by each `DamageTypeSO`, and `damage_taken` vulnerability; `IsDebuffed` now real and drives debuff styling.
- **Enemy movement stats**: `move_speed` (shared with the player), `chase_speed_multiplier`, `investigate_speed_multiplier`; actions declare a `CurrentSpeedMultiplier`.

### Changed
- `StatBlock` retired; weapon stats live in a `StatContainer` built from archetype base plus weapon deltas, version-invalidated.
- Crit stats moved off weapons onto the player.
- Base damage is `PercentOfStat(weapon_damage, Source)` resolved live; status entry weights read live from their source and freeze if it is destroyed.
- `ITargetInfo` renamed `ICombatant`; `EnemyHealth` renamed `CombatantHealth` and shared by player and enemies.
- `CombatantStats` implements `ICombatant`; health is a plain sibling it delegates to.
- Damage numbers highlight on crit or headshot; accumulator numbers use normal type styling.
- `AgentMoveBehaviour` writes `nav.speed` only when it changes.

### Fixed
- Enemy FOV used a 3D angle, adding pitch from the raised eye and failing to re-acquire the player after investigating; now horizontal.
- Enemies halted inside stopping distance froze facing one direction; they now rotate toward a live target.

### Removed
- `DamageStats` snapshot, `DerivationTiming`, the `PercentOfCrit` derivation, and the static resistance array.

---

## [0.0.4] - 2026-07-05 to 2026-07-06

Weapon system phase 1.

### Added
- **Weapon hierarchy**: `WeaponTypeSO` / `WeaponArchetypeSO` / `WeaponSO` (type, frame stat profile, named weapon with intrinsic deltas and damage type).
- **Generated base damage** from resolved stats; the effect list is riders only.
- **Data-driven fire modes**: `IFireBehavior` (when) and `IDelivery` (how) as independent axes; `AutoFireBehavior` with buffered intent so auto and semi both reach the RPM cap; `WeaponFireController` coordinating input, behaviour and delivery.
- **Event-driven `WeaponAnimator`**: subscribes to `OnFired`, `OnReloadStarted` and `OnDryFire`; fire scales to RPM, reload to reload time, clip lengths read automatically.
- Ammo box animation.

### Changed
- Hitscan and projectile deliveries are plain classes built from `DeliverySO` data.
- Rider effects and identity/chain config moved onto `WeaponSO`; per-weapon audio.
- Fire burn rebalanced; NavMesh rebaked.

### Fixed
- Player root hierarchy.

### Removed
- `HandgunFire`, `HitscanStrategy`, `ProjectileStrategy`, `IFireStrategy`, `IProjectileSource`.

---

## [0.0.3] - 2026-06-03 to 2026-06-20

Combat core, damage types, damage numbers and status stacking.

### Added
- **Combat pipeline**: `IDamageSource` (stats, effects, chain config), swappable delivery, and `WeaponHitResolver` as the single chokepoint for damage and feedback.
- **ScriptableObject hit effects** (`HitEffectSO` factory with cached instances); `DamageEffectSO` with configurable derivation.
- **`HitContext`** carrying the stat snapshot, effect payload and inert chain fields.
- **Projectile delivery**: raycast-per-frame (no tunnelling); `ProjectileConfig` (speed, lifetime, distance, impact mode, pierce count); DestroyOnHit, Pierce with per-projectile dedup, Embed stubbed; environment-stop toggle; runtime hitscan/projectile swap.
- **Status system**: `StatusManager` central ticker with deferred add/remove; `StatusReceiver` per enemy with death cleanup; `StatusSO` / `BurnStatusSO`; `ApplyStatusHitEffect`.
- **Damage types as assets**: `DamageTypeSO` (id, display name, colour, sound) and `DamageTypeRegistry`; per-type resistance at the damage chokepoint.
- **`HitSource`** (Direct / StatusTick / Chain); status ticks resolve through the resolver and get hitmarkers, audio and kill feedback, with toggles for tick hitmarkers and audio.
- **Floating damage numbers**: pooled and billboarded, one spawn path for hits and ticks; relative log-space sizing so big hits pop and chip damage recedes; size, lifetime and rise scale with magnitude; formatting from single digits to m/b/t.
- **Per-type number styling**: font plus four gradients (normal, crit, debuffed, crit+debuffed), bold for crit and italic for debuff; font outlines.
- **Rolling accumulator numbers** per (target, status): climbing totals, per-target grouping, biggest-at-bottom ordering with smooth reordering, revive-on-reapply; per-effect `showFloatingNumber` / `feedsAccumulator` flags.
- **Type-driven hit sounds** with a default fallback.
- **Per-effect status stacking**: `EffectStackPool` per (target, status) holding a `StackEntry` per application; summed ticks on one cadence; first application ticks on apply.
- **Status axes on `StatusSO`**:
  - Duration: PerEntryIndependent, RefreshAll, ExtendShared (with `extensionCap`).
  - Stack cap and eviction: `maxEntries` with LowestWeight or ShortestRemaining.
  - Intensity: Magnitude, Rate (clamped to `minInterval`), Both.
  - Tick timer: SharedPoolTimer, PerEntryTimer.

### Changed
- Handgun firing routed through the new pipeline.
- Resistance composed on the target via `GetDamageMultiplier(type, bodyPart)`; inert body-part layer added; `TakeDamage` is a plain subtract.
- Hitmarker colour taken from the damage type.

### Fixed
- Damage numbers showed the pre-resistance value; every multiplier now applies before `DamageDealt` is written.

### Removed
- `DamageType` enum, `StatusInstance`, `StatusTickDamageEffect`.

---

## [0.0.2] - 2026-03-18 to 2026-05-21

GOAP enemy AI.

### Added
- **Enemy AI** on CrashKonijn GOAP v3 with wandering.
- **Chase** using FOV and raycast player detection.
- **Aware state** on losing line of sight, keeping the player's position briefly, then **Investigate** the last known position.
- **Look-around** action while investigating.
- **Enemy FOV/LOS visualizer** for debugging and balancing.

### Changed
- Detection triggers the moment the player is visible instead of waiting for the GOAP resolve cycle.
- Last known position sampled onto the NavMesh within 5 units vertically, so detection and investigation work mid-jump.
- Movement speed centralised on `AgentBrain` with chase and investigate multipliers.
- Wander, movement, behaviours and actions reworked for responsiveness.

---

## [0.0.1] - 2026-01-20 to 2026-02-25

Prototype.

### Added
- Capsule player with basic movement.
- Handgun with fire animation and sound; configurable RPM fire rate.
- Crosshair with a fire animation.
- Ammo counter, ammo pickups and raycast shooting.
- Velocity-based footsteps.
- Enemy damage with separate head and torso hitboxes for locational damage.
- Localised weapon audio.
- **Hitmarkers** for headshots, bodyshots and kills: colour, scale and rotation variation, fade-out animation, pitch and volume variation, a distinct kill sound, and larger recoloured kill markers.

### Changed
- Audio optimised and reworked onto a 2D/3D source attached to the player.
- Hierarchy tidied.
