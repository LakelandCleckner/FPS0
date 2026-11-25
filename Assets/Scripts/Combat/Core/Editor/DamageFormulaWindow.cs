using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Combat.Events;
using Combat.Stats;

namespace Combat.Core.EditorTools
{
    // Edit damage formulas side by side and preview a test hit through each, written out in full.
    public class DamageFormulaWindow : EditorWindow
    {
        private const string PlayerPath = "Assets/GameData/Damage/PlayerDamageFormula.asset";
        private const string EnemyPath = "Assets/GameData/Damage/EnemyDamageFormula.asset";
        private const string SidePref = "FPS0.DamageFormulaWindow.SideBySide";

        private DamageFormulaSO left, right;
        private ReorderableList leftList, rightList;
        private bool sideBySide = true;
        private Vector2 scroll;
        private readonly Preview preview = new Preview();

        [MenuItem("FPS0/Stats/Damage Formula Editor", priority = 652)]
        private static void Open() => GetWindow<DamageFormulaWindow>("Damage Formula");

        private void OnEnable()
        {
            sideBySide = EditorPrefs.GetBool(SidePref, true);
            if (left == null) left = AssetDatabase.LoadAssetAtPath<DamageFormulaSO>(PlayerPath);
            if (right == null) right = AssetDatabase.LoadAssetAtPath<DamageFormulaSO>(EnemyPath);
            Undo.undoRedoPerformed += Repaint;
        }

        private void OnDisable() => Undo.undoRedoPerformed -= Repaint;

        private void OnGUI()
        {
            Toolbar();
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.BeginHorizontal();
            Column(ref left, ref leftList, sideBySide ? right : null);
            if (sideBySide)
            {
                GUILayout.Space(8f);
                Column(ref right, ref rightList, left);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10f);
            preview.Draw(left, sideBySide ? right : null);

            EditorGUILayout.EndScrollView();
        }

        private void Toolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            bool side = GUILayout.Toggle(sideBySide, "Side by side", EditorStyles.toolbarButton, GUILayout.Width(100f));
            if (side != sideBySide)
            {
                sideBySide = side;
                EditorPrefs.SetBool(SidePref, side);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        // ------------------------------------------------------------------ formula column

        private void Column(ref DamageFormulaSO formula, ref ReorderableList list, DamageFormulaSO other)
        {
            EditorGUILayout.BeginVertical(GUILayout.MinWidth(320f));

            var picked = (DamageFormulaSO)EditorGUILayout.ObjectField(formula, typeof(DamageFormulaSO), false);
            if (picked != formula) { formula = picked; list = null; }
            if (formula == null)
            {
                EditorGUILayout.HelpBox("Pick a formula. FPS0/Stats/Setup Damage Formula creates the player and enemy ones.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            var problems = formula.Problems();
            foreach (var p in problems) EditorGUILayout.HelpBox(p, MessageType.Warning);

            if (list == null || list.list != formula.layers) list = BuildList(formula);
            var f = formula;
            list.drawElementBackgroundCallback = (rect, i, active, focused) =>
            {
                if (i < 0 || i >= f.layers.Count) return;
                if (active) EditorGUI.DrawRect(rect, new Color(0.24f, 0.37f, 0.59f, 0.5f));
                else if (other != null && !Contains(other, f.layers[i])) EditorGUI.DrawRect(rect, new Color(1f, 0.75f, 0.2f, 0.12f));
            };
            list.DoLayoutList();

            if (GUILayout.Button("Reset to default layers"))
            {
                Undo.RecordObject(formula, "Reset damage formula");
                formula.layers = DamageFormulaSO.DefaultLayers();
                EditorUtility.SetDirty(formula);
                list = null;
            }
            if (other != null) EditorGUILayout.LabelField("Tinted rows differ from the other formula.", EditorStyles.miniLabel);

            EditorGUILayout.EndVertical();
        }

        private ReorderableList BuildList(DamageFormulaSO formula)
        {
            var list = new ReorderableList(formula.layers, typeof(DamageFormulaLayer), true, true, true, true);
            float line = EditorGUIUtility.singleLineHeight, gap = 2f;

            list.drawHeaderCallback = r => EditorGUI.LabelField(r, formula.name + "   (Direct / Tick / Chain)", EditorStyles.boldLabel);

            list.elementHeightCallback = i =>
            {
                var l = i < formula.layers.Count ? formula.layers[i] : null;
                int rows = l != null && l.layer == DamageLayer.Stat ? 5 : 2;
                return rows * (line + gap) + 4f;
            };

            list.drawElementCallback = (rect, i, active, focused) =>
            {
                var l = formula.layers[i];
                if (l == null) return;
                rect.y += 2f;
                var row = new Rect(rect.x, rect.y, rect.width, line);

                EditorGUI.BeginChangeCheck();
                var kind = (DamageLayer)EditorGUI.EnumPopup(new Rect(row.x, row.y, 130f, line), l.layer);
                float x = row.x + 138f;
                bool d = Flag(ref x, row.y, "D", l.appliesTo, HitSourceMask.Direct);
                bool t = Flag(ref x, row.y, "T", l.appliesTo, HitSourceMask.StatusTick);
                bool c = Flag(ref x, row.y, "C", l.appliesTo, HitSourceMask.Chain);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(formula, "Edit damage layer");
                    l.layer = kind;
                    l.appliesTo = (d ? HitSourceMask.Direct : 0) | (t ? HitSourceMask.StatusTick : 0) | (c ? HitSourceMask.Chain : 0);
                    EditorUtility.SetDirty(formula);
                }

                row.y += line + gap;
                EditorGUI.LabelField(row, Describe(l), EditorStyles.miniLabel);

                if (l.layer != DamageLayer.Stat) return;
                EditorGUI.BeginChangeCheck();
                row.y += line + gap;
                var label = EditorGUI.TextField(row, "Label", l.label);
                row.y += line + gap;
                var stat = (StatDefinitionSO)EditorGUI.ObjectField(row, "Stat", l.stat, typeof(StatDefinitionSO), false);
                row.y += line + gap;
                float w = row.width / 3f;
                var owner = (StatScope)EditorGUI.EnumPopup(new Rect(row.x, row.y, w - 4f, line), l.owner);
                var mode = (StatLayerMode)EditorGUI.EnumPopup(new Rect(row.x + w, row.y, w - 4f, line), l.mode);
                var only = (DamageTypeSO)EditorGUI.ObjectField(new Rect(row.x + 2f * w, row.y, w, line), l.onlyType, typeof(DamageTypeSO), false);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(formula, "Edit stat layer");
                    l.label = label; l.stat = stat; l.owner = owner; l.mode = mode; l.onlyType = only;
                    EditorUtility.SetDirty(formula);
                }
            };

            list.onAddDropdownCallback = (rect, _) =>
            {
                var menu = new GenericMenu();
                foreach (DamageLayer k in System.Enum.GetValues(typeof(DamageLayer)))
                {
                    if (k == DamageLayer.Stat) continue;
                    bool has = formula.layers.Exists(x => x != null && x.layer == k);
                    if (has) menu.AddDisabledItem(new GUIContent(k + " (listed)"));
                    else menu.AddItem(new GUIContent(k.ToString()), false, () => Add(formula, k));
                }
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Stat layer"), false, () => Add(formula, DamageLayer.Stat));
                menu.DropDown(rect);
            };

            list.onRemoveCallback = l =>
            {
                Undo.RecordObject(formula, "Remove damage layer");
                formula.layers.RemoveAt(l.index);
                EditorUtility.SetDirty(formula);
            };
            list.onReorderCallback = _ => EditorUtility.SetDirty(formula);
            return list;
        }

        private static bool Flag(ref float x, float y, string label, HitSourceMask mask, HitSourceMask bit)
        {
            EditorGUI.LabelField(new Rect(x, y, 12f, EditorGUIUtility.singleLineHeight), label);
            bool on = EditorGUI.Toggle(new Rect(x + 12f, y, 18f, EditorGUIUtility.singleLineHeight), (mask & bit) != 0);
            x += 36f;
            return on;
        }

        private void Add(DamageFormulaSO formula, DamageLayer kind)
        {
            Undo.RecordObject(formula, "Add damage layer");
            formula.layers.Add(new DamageFormulaLayer { layer = kind, appliesTo = HitSourceMask.All });
            EditorUtility.SetDirty(formula);
            Repaint();
        }

        // Same layer with the same hits (and stat) in the other formula.
        private static bool Contains(DamageFormulaSO other, DamageFormulaLayer l)
        {
            if (l == null) return true;
            foreach (var o in other.layers)
                if (o != null && o.layer == l.layer && o.appliesTo == l.appliesTo &&
                    (l.layer != DamageLayer.Stat || (o.stat == l.stat && o.owner == l.owner && o.mode == l.mode && o.onlyType == l.onlyType)))
                    return true;
            return false;
        }

        public static string Describe(DamageFormulaLayer l)
        {
            switch (l.layer)
            {
                case DamageLayer.Hitbox: return "x the struck hitbox's multiplier";
                case DamageLayer.Crit: return "x (1 + crit damage) when the hit crits";
                case DamageLayer.Condition: return "x (1 + summed condition bonuses) x multiplicative ones";
                case DamageLayer.PelletShare: return "x this pellet's share of the shot";
                case DamageLayer.ElementIdentity: return "x the damage type's direct-hit multiplier";
                case DamageLayer.Precision: return "x weapon precision (+ type bonus) on head hits";
                case DamageLayer.RangeFalloff: return "x weapon range falloff at the hit distance";
                case DamageLayer.ChainFalloff: return "x chain falloff, when the damage uses it";
                case DamageLayer.ElementDamage: return "x the attacker's damage stat for the element";
                case DamageLayer.Defense: return "x (1 - resist) x body part x (1 + damage taken)";
                case DamageLayer.Stat:
                    string what = l.mode == StatLayerMode.OnePlus ? "(1 + " + l.owner + " stat)" : l.owner + " stat";
                    return "x " + what + (l.onlyType != null ? ", " + l.onlyType.displayName + " hits only" : "");
                default: return "";
            }
        }

        // ------------------------------------------------------------------ preview

        private class Preview
        {
            private bool open = true;
            private HitSource kind = HitSource.Direct;
            private float raw = 100f;
            private float hitbox = 1f;
            private bool crit;
            private float critDamage = 0.5f;
            private float conditionBonus;
            private float conditionFactor = 1f;
            private float pelletShare = 1f;
            private float identity = 1f;
            private bool head;
            private float precision = 1.5f;
            private float falloff = 1f;
            private bool usesChainFalloff;
            private float chainMultiplier = 1f;
            private float elementDamage = 1f;
            private float resist;
            private float bodyPart = 1f;
            private float damageTaken;
            private readonly Dictionary<string, float> statValues = new Dictionary<string, float>();

            public void Draw(DamageFormulaSO a, DamageFormulaSO b)
            {
                open = EditorGUILayout.BeginFoldoutHeaderGroup(open, "Hit preview");
                if (open)
                {
                    Inputs(a, b);
                    EditorGUILayout.Space(6f);
                    if (a != null) Result(a);
                    if (b != null) Result(b);
                }
                EditorGUILayout.EndFoldoutHeaderGroup();
            }

            private void Inputs(DamageFormulaSO a, DamageFormulaSO b)
            {
                kind = (HitSource)EditorGUILayout.EnumPopup("Hit kind", kind);
                raw = EditorGUILayout.FloatField("Raw damage", raw);
                hitbox = EditorGUILayout.FloatField("Hitbox multiplier", hitbox);
                crit = EditorGUILayout.Toggle("Crit", crit);
                using (new EditorGUI.DisabledScope(!crit)) critDamage = EditorGUILayout.FloatField("  Crit damage", critDamage);
                conditionBonus = EditorGUILayout.FloatField("Condition bonuses (sum)", conditionBonus);
                conditionFactor = EditorGUILayout.FloatField("Condition multiplicative", conditionFactor);
                pelletShare = EditorGUILayout.FloatField("Pellet share", pelletShare);
                identity = EditorGUILayout.FloatField("Type direct multiplier", identity);
                head = EditorGUILayout.Toggle("Head hit", head);
                using (new EditorGUI.DisabledScope(!head)) precision = EditorGUILayout.FloatField("  Precision (+ type bonus)", precision);
                falloff = EditorGUILayout.FloatField("Range falloff", falloff);
                usesChainFalloff = EditorGUILayout.Toggle("Uses chain falloff", usesChainFalloff);
                using (new EditorGUI.DisabledScope(!usesChainFalloff)) chainMultiplier = EditorGUILayout.FloatField("  Chain multiplier", chainMultiplier);
                elementDamage = EditorGUILayout.FloatField("Element damage stat", elementDamage);
                resist = EditorGUILayout.FloatField("Target resist", resist);
                bodyPart = EditorGUILayout.FloatField("Body part multiplier", bodyPart);
                damageTaken = EditorGUILayout.FloatField("Target damage taken", damageTaken);

                foreach (var f in new[] { a, b })
                {
                    if (f == null) continue;
                    foreach (var l in f.layers)
                    {
                        if (l == null || l.layer != DamageLayer.Stat || l.stat == null) continue;
                        string key = Key(l);
                        if (!statValues.TryGetValue(key, out var v)) v = l.mode == StatLayerMode.OnePlus ? 0f : 1f;
                        statValues[key] = EditorGUILayout.FloatField(l.Name + " (" + l.owner + ")", v);
                    }
                }
            }

            private static string Key(DamageFormulaLayer l) => l.stat.GetInstanceID() + ":" + l.owner;

            private float Factor(DamageFormulaLayer l)
            {
                switch (l.layer)
                {
                    case DamageLayer.Hitbox: return hitbox;
                    case DamageLayer.Crit: return crit ? 1f + critDamage : 1f;
                    case DamageLayer.Condition: return (1f + conditionBonus) * conditionFactor;
                    case DamageLayer.PelletShare: return pelletShare;
                    case DamageLayer.ElementIdentity: return identity;
                    case DamageLayer.Precision: return head ? precision : 1f;
                    case DamageLayer.RangeFalloff: return falloff;
                    case DamageLayer.ChainFalloff: return usesChainFalloff ? chainMultiplier : 1f;
                    case DamageLayer.ElementDamage: return Mathf.Max(0f, elementDamage);
                    case DamageLayer.Defense: return Mathf.Max(0f, 1f - resist) * bodyPart * (1f + damageTaken);
                    case DamageLayer.Stat:
                        if (l.stat == null) return 1f;
                        return DamageFormulaSO.Mult(l.mode, statValues.TryGetValue(Key(l), out var v) ? v : (l.mode == StatLayerMode.OnePlus ? 0f : 1f));
                    default: return 1f;
                }
            }

            private void Result(DamageFormulaSO f)
            {
                var mask = kind.ToMask();
                var formula = new StringBuilder("final = ").Append(raw.ToString("0.##")).Append(" raw");
                var skipped = new StringBuilder();
                float value = raw;
                foreach (var l in f.layers)
                {
                    if (l == null) continue;
                    if ((l.appliesTo & mask) == 0)
                    {
                        if (skipped.Length > 0) skipped.Append(", ");
                        skipped.Append(l.Name);
                        continue;
                    }
                    float x = Factor(l);
                    value *= x;
                    formula.Append("\n   x ").Append(x.ToString("0.###")).Append("  ").Append(l.Name);
                }
                formula.Append("\n   = ").Append(value.ToString("0.##"));

                EditorGUILayout.LabelField(f.name + "  (" + kind + ")", EditorStyles.boldLabel);
                EditorGUILayout.SelectableLabel(formula.ToString(), EditorStyles.helpBox,
                    GUILayout.Height((f.layers.Count + 3) * EditorGUIUtility.singleLineHeight));
                if (skipped.Length > 0) EditorGUILayout.LabelField("Not applied to this hit: " + skipped, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space(4f);
            }
        }
    }
}
