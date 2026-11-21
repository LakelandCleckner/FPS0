using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Unity.AI.Navigation;
using Combat.Core;
using Combat.Effects;
using Combat.Enemies;
using Combat.Events;
using Combat.Perks;
using Combat.Sources;
using Combat.Status;
using Combat.Weapons;

// Play-mode effects lineup for the enemy gallery: one row per status and body, one enemy per stack level. Driven by the Gallery window.
public class EnemyEffectsGallery : MonoBehaviour
{
    public class Row
    {
        public StatusSO Status;
        public GameObject Prefab;
        public EnemyVariantSO Variant;
        public EnemyArchetypeSO Archetype;
        public string Body;
        public readonly List<Slot> Slots = new List<Slot>();

        public string Label => StatusName(Status) + " - " + Body;
    }

    public class Slot
    {
        public Row Row;
        public int Stacks;
        public Vector3 Local;
        public GameObject Go;
        public CombatantHealth Health;
        public StatusReceiver Receiver;
        public EnemyHitbox Torso;
        public bool Killing;
        public float RespawnAt;
        public string Label;
    }

    [Header("Layout")]
    public float Spacing = 2.5f;
    public float RowSpacing = 4f;

    [Header("Triggers")]
    public float ShotDistance = 6f;
    public float BlastRadius = 3f;
    public float RespawnDelay = 3f;
    public ExplosionOnKillSO KillExplosion;
    [Tooltip("Layers added to every enemy in the lineup, e.g. a shield.")]
    public List<EnemyModifierSO> ExtraLayers = new List<EnemyModifierSO>();

    [Header("Loop")]
    public bool HoldStatuses = true;
    public bool Loop;
    public float LoopInterval = 1f;
    public bool LoopFire = true;
    public bool LoopBlast;

    public bool ShowLabels = true;

    public readonly List<Row> Rows = new List<Row>();
    public WeaponSO Weapon { get; private set; }
    public DamageTypeSO Element => source != null ? source.BaseDamageType : null;

    // AI, attacks and loot off; animation, movement (for knockback), hit reactions and ragdoll stay on.
    private static readonly HashSet<string> Frozen = new HashSet<string>
    {
        "EnemyBrain", "EnemyPerception", "EnemyMeleeAttack", "EnemyRangedAttack", "EnemyLootDropper", "EnemyFovGizmo"
    };

    private GameObject gun;
    private WeaponDamageSource source;
    private WeaponHitResolver resolver;
    private float nextHold;
    private float nextLoop;
    private int shotId = 1000000;
    private GUIStyle labelStyle;
    private GUIStyle rowStyle;

    public static string StatusName(StatusSO s)
    {
        if (s == null) return "None";
        if (s.keyword != null && !string.IsNullOrEmpty(s.keyword.displayName)) return s.keyword.displayName;
        return s.name.EndsWith("Status") ? s.name.Substring(0, s.name.Length - 6) : s.name;
    }

    private void Awake()
    {
        foreach (var s in FindObjectsByType<NavMeshSurface>(FindObjectsSortMode.None))
            if (s.navMeshData == null) s.BuildNavMesh();
    }

    private void OnDestroy() => Clear();

    // ------------------------------------------------------------------ lineup

    public void Build(WeaponSO weapon, WeaponStatKeys keys, List<Row> rows)
    {
        Clear();
        SetWeapon(weapon, keys);
        if (source == null) return;

        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row.Status == null || row.Prefab == null) continue;
            int max = row.Status.AuraMaxStacks;
            float x0 = -(max - 1) * Spacing * 0.5f;
            for (int k = 1; k <= max; k++)
            {
                var slot = new Slot
                {
                    Row = row,
                    Stacks = k,
                    Local = new Vector3(x0 + (k - 1) * Spacing, 0f, Rows.Count * RowSpacing),
                    Label = StatusName(row.Status) + " " + k + "/" + max
                };
                row.Slots.Add(slot);
                Spawn(slot);
            }
            Rows.Add(row);
        }
        nextLoop = Time.time + LoopInterval;
    }

    public void Clear()
    {
        foreach (var row in Rows)
            foreach (var s in row.Slots)
                if (s.Go != null) Destroy(s.Go);
        Rows.Clear();
        if (gun != null) Destroy(gun);
        gun = null;
        source = null;
        resolver = null;
    }

    // A live gun of the chosen weapon: its stats, element, round and hit pipeline.
    private void SetWeapon(WeaponSO w, WeaponStatKeys keys)
    {
        Weapon = w;
        if (w == null || keys == null) return;

        gun = new GameObject("Gallery Gun");
        gun.SetActive(false);
        gun.transform.SetParent(transform, false);
        gun.AddComponent<WeaponEventBus>();
        source = gun.AddComponent<WeaponDamageSource>();
        typeof(WeaponDamageSource).GetField("statKeys", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(source, keys);
        source.Initialize(new WeaponInstance(w, RollMode.Curated));
        resolver = gun.AddComponent<WeaponHitResolver>();
        gun.SetActive(true);
    }

    private void Spawn(Slot slot)
    {
        var row = slot.Row;
        var rot = transform.rotation * Quaternion.Euler(0f, 180f, 0f);
        var go = Instantiate(row.Prefab, transform.TransformPoint(slot.Local), rot, transform);
        go.name = slot.Label;

        var applier = go.GetComponent<EnemyDefinitionApplier>();
        if (applier != null)
            applier.SetDefinition(new EnemySpawnDef(row.Variant, row.Variant == null ? row.Archetype : null, ExtraLayers));
        foreach (var b in go.GetComponentsInChildren<Behaviour>(true))
            if (Frozen.Contains(b.GetType().Name)) b.enabled = false;

        slot.Go = go;
        slot.Health = go.GetComponentInChildren<CombatantHealth>();
        slot.Receiver = go.GetComponentInChildren<StatusReceiver>();
        slot.Torso = null;
        foreach (var hb in go.GetComponentsInChildren<EnemyHitbox>())
            if (slot.Torso == null || hb.bodyPart == BodyPart.Torso) { slot.Torso = hb; if (hb.bodyPart == BodyPart.Torso) break; }
        slot.Killing = false;
    }

    private void Respawn(Slot slot)
    {
        if (slot.Go != null) Destroy(slot.Go);
        Spawn(slot);
    }

    private static bool Alive(Slot s) => s.Go != null && !s.Killing && s.Health != null && !s.Health.IsDying;

    // ------------------------------------------------------------------ frame

    private void Update()
    {
        if (source == null) return;
        float now = Time.time;
        bool hold = now >= nextHold;
        if (hold) nextHold = now + 0.5f;

        foreach (var row in Rows)
            foreach (var s in row.Slots)
            {
                if (s.Go == null || s.Killing)
                {
                    if (!s.Killing || now >= s.RespawnAt) Respawn(s);
                    continue;
                }
                if (s.Health != null && s.Health.IsDying)
                {
                    s.Killing = true;
                    s.RespawnAt = now + RespawnDelay;
                    continue;
                }
                if (s.Health != null && !s.Health.IsDying && s.Health.CurrentHealth < s.Health.MaxHealth * 0.5f)
                    s.Health.Heal(s.Health.MaxHealth);
                if (!HoldStatuses || s.Receiver == null) continue;
                if (s.Receiver.Stacks(row.Status) < s.Stacks) ApplyStatus(s, row.Status);
                if (hold) s.Receiver.Hold(row.Status);
            }

        if (Loop && now >= nextLoop)
        {
            nextLoop = now + Mathf.Max(0.1f, LoopInterval);
            if (LoopFire) FireAll();
            if (LoopBlast) BlastAll();
        }
    }

    private void ApplyStatus(Slot s, StatusSO st)
    {
        if (s.Receiver == null || st == null) return;
        ICombatant victim = s.Torso != null ? (ICombatant)s.Torso.combatant : s.Go.GetComponent<ICombatant>();
        if (victim == null) return;
        var tickType = st.damageType != null ? st.damageType : source.BaseDamageType;
        s.Receiver.Apply(st, resolver, source.Attacker, source, tickType, source.Faction, 0, 1f,
                         dmg => victim.TakeDamage(dmg, BodyPart.Torso, tickType));
    }

    // ------------------------------------------------------------------ triggers

    // Tracer, round and trail into the torso, then a real hit: hit effect, numbers, sound, arcs.
    public void Fire(Slot s) => Shoot(s, null);

    public void Blast(Slot s)
    {
        var type = Element;
        if (!Alive(s) || type == null || type.blastVfx == null) return;
        float scale = type.blastReferenceRadius > 0f ? BlastRadius / type.blastReferenceRadius : 1f;
        VfxPool.Spawn(type.blastVfx, s.Go.transform.position + Vector3.up, Quaternion.identity, null, type.color, type.blastLifetime, scale, type.colorizeVfx);
    }

    // Pushes the status over its threshold (Fracture into Break, Chill into Freeze).
    public void Convert(Slot s)
    {
        var st = s.Row.Status;
        if (!Alive(s) || s.Receiver == null || st.convertsTo == null || st.convertAtStacks <= 0) return;
        for (int guard = 0; guard <= st.convertAtStacks && !s.Receiver.Has(st.convertsTo); guard++) ApplyStatus(s, st);
    }

    // Lethal hit with the kill explosion (if any), ragdoll, then a fresh enemy.
    public void Kill(Slot s)
    {
        if (!Alive(s)) return;
        var effects = new List<IHitEffect>(source.GetEffects());
        if (KillExplosion != null) effects.Add(KillExplosion.GetInstance());
        if (s.Health.CurrentHealth > 1f) s.Health.TakeDamage(s.Health.CurrentHealth - 1f, BodyPart.Torso, null);
        s.Killing = true;
        s.RespawnAt = Time.time + RespawnDelay;
        Shoot(s, effects, true);
        if (s.Go != null && s.Health != null && !s.Health.IsDying) s.Health.Kill();
    }

    // Arcs between neighbours, with a spark where each lands.
    public void Arc(Row row)
    {
        var type = Element;
        Color c = type != null ? type.color : Color.white;
        for (int i = 0; i + 1 < row.Slots.Count; i++)
        {
            var a = row.Slots[i];
            var b = row.Slots[i + 1];
            if (!Alive(a) || !Alive(b)) continue;
            Vector3 to = b.Go.transform.position + Vector3.up;
            ArcVfx.Draw(a.Go.transform.position + Vector3.up, to, c, type != null ? type.arcMaterial : null);
            if (type != null && type.hitVfx != null)
                VfxPool.Spawn(type.hitVfx, to, Quaternion.identity, null, c, type.hitVfxLifetime, type.hitVfxScale, type.colorizeVfx);
        }
    }

    public void FireAll() => ForEach(Fire);
    public void BlastAll() => ForEach(Blast);
    public void ConvertAll() => ForEach(Convert);
    public void KillAll() => ForEach(Kill);
    public void ArcAll() { foreach (var r in Rows) Arc(r); }

    public void FireRow(Row r) { foreach (var s in r.Slots) Fire(s); }
    public void BlastRow(Row r) { foreach (var s in r.Slots) Blast(s); }
    public void ConvertRow(Row r) { if (r.Slots.Count > 0) Convert(r.Slots[r.Slots.Count - 1]); }
    public void KillRow(Row r) { foreach (var s in r.Slots) Kill(s); }

    public void BreakShields() => ForEach(s => { var sh = Shield(s); if (sh != null) sh.ForceBreak(); });
    public void RestoreShields() => ForEach(s => { var sh = Shield(s); if (sh != null) sh.Restore(); });

    private static EnemyShield Shield(Slot s) => Alive(s) ? s.Go.GetComponentInChildren<EnemyShield>() : null;

    public void ResetStatuses()
    {
        foreach (var r in Rows)
            foreach (var s in r.Slots)
                if (s.Receiver != null) s.Receiver.ClearStatuses();
    }

    public void RespawnAll()
    {
        foreach (var r in Rows)
            foreach (var s in r.Slots) Respawn(s);
    }

    private void ForEach(System.Action<Slot> act)
    {
        foreach (var r in Rows)
            foreach (var s in r.Slots) act(s);
    }

    private void Shoot(Slot s, List<IHitEffect> effects, bool killing = false)
    {
        if (s.Go == null || s.Torso == null || source == null || s.Health == null || s.Health.IsDying) return;
        if (!killing && s.Killing) return;
        if (!killing) s.Health.Heal(s.Health.MaxHealth);

        var hb = s.Torso;
        Vector3 to = hb.transform.position;
        Vector3 from = to + transform.rotation * Vector3.back * ShotDistance + Vector3.up * 0.2f;
        TracerVfx.Fire(from, to, source.BaseDamageType, true, source.Round);

        var ctx = new HitContext
        {
            Target = hb.combatant,
            HitPoint = to,
            Source = HitSource.Direct,
            ChainDepth = 0,
            DamageSource = source,
            Attacker = source.Attacker,
            SourceFaction = source.Faction,
            DamageType = source.BaseDamageType,
            HitboxMultiplier = hb.damageMultiplier,
            BodyPartHit = hb.bodyPart,
            Shot = new ShotInfo(shotId++),
            ApplyStatusTickDamage = hb.ApplyTickDamage,
            Effects = effects ?? source.GetEffects(),
            MaxChainDepth = source.MaxChainDepth,
            ChainFalloff = source.ChainFalloff,
            ChainGrowth = source.ChainGrowth,
            DedupMode = source.DedupMode
        };
        resolver.ResolveHit(ctx);
    }

    // ------------------------------------------------------------------ camera and labels

    public void Frame(Row row)
    {
        var cam = Camera.main;
        if (cam == null || row == null || row.Slots.Count == 0) return;
        Vector3 a = transform.TransformPoint(row.Slots[0].Local);
        Vector3 b = transform.TransformPoint(row.Slots[row.Slots.Count - 1].Local);
        Vector3 centre = (a + b) * 0.5f + Vector3.up * 1.1f;
        float half = Vector3.Distance(a, b) * 0.5f + 1.5f;
        float dist = half / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(0.5f, cam.aspect) + 1f;
        cam.transform.position = centre + transform.rotation * Vector3.back * dist + Vector3.up * 0.5f;
        cam.transform.LookAt(centre);
    }

    private void OnGUI()
    {
        if (!ShowLabels || Rows.Count == 0) return;
        var cam = Camera.main;
        if (cam == null) return;
        labelStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
        rowStyle ??= new GUIStyle(labelStyle) { fontSize = 14, normal = { textColor = new Color(1f, 0.85f, 0.5f) } };

        foreach (var r in Rows)
        {
            if (r.Slots.Count == 0) continue;
            Label(cam, transform.TransformPoint(r.Slots[0].Local) + transform.rotation * Vector3.left * Spacing + Vector3.up, r.Label, rowStyle, 220f);
            foreach (var s in r.Slots)
                Label(cam, transform.TransformPoint(s.Local) + Vector3.up * 2.3f, s.Label, labelStyle, 140f);
        }
        if (Weapon != null)
            GUI.Label(new Rect(10f, 10f, 400f, 22f), Weapon.name + (Element != null ? " (" + Element.displayName + ")" : ""), rowStyle);
    }

    private static void Label(Camera cam, Vector3 world, string text, GUIStyle style, float width)
    {
        Vector3 p = cam.WorldToScreenPoint(world);
        if (p.z <= 0f) return;
        GUI.Label(new Rect(p.x - width * 0.5f, Screen.height - p.y - 11f, width, 22f), text, style);
    }
}
