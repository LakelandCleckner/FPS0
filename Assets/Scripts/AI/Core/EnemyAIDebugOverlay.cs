using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using Combat.Enemies;

namespace Combat.AI
{
    // In-game AI debug view per enemy (state, plan, aim, points, decisions, setup problems); Debug/ToggleAIDebug cycles Off > Focus > All.
    public class EnemyAIDebugOverlay : MonoBehaviour
    {
        public enum Mode { Off, Focus, All }

        private const float DecisionShowTime = 3f;
        private const float FocusScreenRadius = 0.2f;

        public static EnemyAIDebugOverlay Instance { get; private set; }
        public Mode CurrentMode { get; private set; }

        private InputAction toggle;
        private Material lineMaterial;
        private GUIStyle label;
        private GUIStyle header;
        private Camera cam;
        private EnemyBrain focused;
        private readonly StringBuilder sb = new StringBuilder(256);
        private readonly List<string> issues = new List<string>();

#if UNITY_EDITOR || STATUS_DEBUG
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (Instance != null) return;
            var go = new GameObject("[EnemyAIDebug]");
            DontDestroyOnLoad(go);
            go.AddComponent<EnemyAIDebugOverlay>();
        }
#endif

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            EnemyBrain.RecordPoints = false;
            if (lineMaterial != null) Destroy(lineMaterial);
        }

        // ---- API (debug panel buttons) ----

        public void CycleMode() => SetMode((Mode)(((int)CurrentMode + 1) % 3));

        public void SetMode(Mode m)
        {
            CurrentMode = m;
            EnemyBrain.RecordPoints = m != Mode.Off;
        }

        private void Update()
        {
            if (toggle == null)
            {
                var router = PlayerInputRouter.Local;
                if (router != null) toggle = router.Inputs.asset.FindAction("Debug/ToggleAIDebug");
            }
            if (toggle != null && toggle.enabled && toggle.WasPressedThisFrame()) CycleMode();
            if (CurrentMode == Mode.Off) return;

            cam = Camera.main;
            if (cam != null) focused = PickFocus();
        }

        // Enemy nearest the screen centre, else the nearest one
        private EnemyBrain PickFocus()
        {
            EnemyBrain best = null;
            float bestScore = float.MaxValue;
            var center = new Vector2(0.5f, 0.5f);
            foreach (var b in EnemyBrain.Active)
            {
                if (b == null) continue;
                Vector3 vp = cam.WorldToViewportPoint(b.transform.position + Vector3.up * 1.2f);
                float score = vp.z > 0f ? Vector2.Distance(center, vp) : float.MaxValue;
                if (score > FocusScreenRadius) score = 10f + Vector3.Distance(cam.transform.position, b.transform.position);
                if (score < bestScore) { bestScore = score; best = b; }
            }
            return best;
        }

        // ---- drawing ----

        private void OnGUI()
        {
            if (CurrentMode == Mode.Off || cam == null) return;
            EnsureStyles();

            GUI.Label(new Rect(10f, 10f, 600f, 22f),
                $"AI debug: {CurrentMode}   enemies {EnemyBrain.Active.Count}   rays this frame {PositionScorer.RaysThisFrame}", header);

            if (Event.current.type == EventType.Repaint)
            {
                lineMaterial.SetPass(0);
                GL.PushMatrix();
                GL.LoadPixelMatrix();
                GL.Begin(GL.LINES);
                foreach (var b in EnemyBrain.Active)
                    if (Shown(b)) DrawLines(b);
                GL.End();
                GL.PopMatrix();
            }

            foreach (var b in EnemyBrain.Active)
                if (Shown(b)) DrawLabel(b);
        }

        private bool Shown(EnemyBrain b) => b != null && (CurrentMode == Mode.All || b == focused);

        private void DrawLines(EnemyBrain b)
        {
            var p = b.Perception;
            Vector3 feet = b.transform.position + Vector3.up * 0.05f;

            if (p.HasTarget || p.HasLastKnown)
            {
                Vector3 believed = p.BelievedPosition + Vector3.up * 1.2f;
                Line(p.EyePosition, believed, p.TargetVisible ? Color.yellow : new Color(1f, 0.5f, 0f));
                if (!p.TargetVisible && p.Target != null)
                {
                    Cross(believed, 0.3f, new Color(1f, 0.5f, 0f));
                    Line(believed, AITargets.FeetOf(p.Target) + Vector3.up * 1.2f, new Color(1f, 1f, 1f, 0.35f));
                }
            }

            if (b.HasFiringPoint)
            {
                var c = b.FiringPointValid ? Color.cyan : Color.magenta;
                Line(feet, b.FiringPoint + Vector3.up * 0.05f, c);
                Cross(b.FiringPoint + Vector3.up * 0.05f, 0.35f, c);
            }

            // Cover: light blue half cover, dark blue full cover with a line to its peek spot
            if (b.HasCombatCover)
            {
                var cs = b.CombatCover;
                var c = cs.Half ? new Color(0.35f, 0.65f, 1f) : new Color(0.25f, 0.3f, 1f);
                Vector3 hide = cs.Point + Vector3.up * 0.05f;
                Line(feet, hide, c);
                Cross(hide, 0.35f, c);
                if (!cs.Half)
                {
                    Line(hide, cs.Peek + Vector3.up * 0.05f, c);
                    Cross(cs.Peek + Vector3.up * 0.05f, 0.2f, c);
                }
            }

            var r = b.Ranged;
            if (r != null && r.CurrentPhase == EnemyRangedAttack.Phase.Bursting && r.Muzzle != null)
                Line(r.Muzzle.position, r.LastAimPoint, Color.red);

            // Candidates: grey rejected, red to green by score
            if (b == focused && b.DebugCandidates != null && b.DebugCandidates.Count > 0)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var s in b.DebugCandidates)
                    if (!s.Rejected) { lo = Mathf.Min(lo, s.Score); hi = Mathf.Max(hi, s.Score); }
                foreach (var s in b.DebugCandidates)
                {
                    Color c = s.Rejected ? new Color(0.5f, 0.5f, 0.5f, 0.5f)
                        : Color.Lerp(Color.red, Color.green, hi > lo ? (s.Score - lo) / (hi - lo) : 1f);
                    Cross(s.Position + Vector3.up * 0.05f, 0.15f, c);
                }
            }
        }

        private void DrawLabel(EnemyBrain b)
        {
            Vector3 sp = cam.WorldToScreenPoint(b.transform.position + Vector3.up * 2.4f);
            if (sp.z <= 0f) return;

            var p = b.Perception;
            sb.Clear();
            sb.Append(b.name).Append("  [").Append(p.Status).Append(']').Append('\n');
            sb.Append(b.Band).Append(' ').Append(b.Defensiveness.ToString("0.00"));
            if (b.Squad != null)
            {
                sb.Append("  ").Append(b.Role);
                if (b.Role == SquadRole.Flanker) sb.Append(b.FlankSide > 0 ? " R" : " L");
                sb.Append(" (").Append(b.Squad.Members.Count).Append(')');
            }

            var action = b.Agent != null ? b.Agent.ActionState.Action : null;
            var plan = b.Provider != null ? b.Provider.CurrentPlan : null;
            var goal = plan != null ? plan.Goal : null;
            sb.Append("   ").Append(goal != null ? goal.GetType().Name : "-")
              .Append(" > ").Append(action != null ? action.GetType().Name : "-").Append('\n');

            var r = b.Ranged;
            if (b.IsRangedProfile && r != null)
            {
                sb.Append(r.CurrentPhase).Append("  cone ").Append(r.AimCone.ToString("0.0"))
                  .Append("  settle ").Append(Mathf.RoundToInt(r.SettleProgress * 100f)).Append('%');
                if (r.MagazineSize > 0) sb.Append("  ammo ").Append(r.Rounds).Append('/').Append(r.MagazineSize);
                sb.Append(b.HasRangedToken ? "  [R]" : "");
            }
            if (b.HasToken) sb.Append("  [M]");
            sb.Append('\n');

            for (int i = 0; i < b.RecentDecisionCount; i++)
            {
                var d = b.RecentDecision(i);
                float age = Time.time - d.Time;
                if (age > DecisionShowTime) break;
                sb.Append("> ").Append(d.Decision).Append(' ').Append(age.ToString("0.0")).Append("s\n");
            }

            issues.Clear();
            b.CollectIssues(issues);
            var anim = b.GetComponentInChildren<EnemyShooterAnimator>();
            if (anim != null) anim.CollectIssues(issues);
            foreach (var s in issues) sb.Append("! ").Append(s).Append('\n');

            string text = sb.ToString();
            Vector2 size = label.CalcSize(new GUIContent(text));
            var rect = new Rect(sp.x - size.x * 0.5f, Screen.height - sp.y - size.y, size.x + 8f, size.y + 4f);
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = issues.Count > 0 ? new Color(1f, 0.6f, 0.6f) : Color.white;
            GUI.Label(new Rect(rect.x + 4f, rect.y + 2f, size.x, size.y), text, label);
            GUI.color = Color.white;
        }

        // Screen-space line between two world points (skipped behind the camera); y flipped because GL inside OnGUI is top-left origin
        private void Line(Vector3 a, Vector3 b, Color c)
        {
            Vector3 sa = cam.WorldToScreenPoint(a);
            Vector3 sb2 = cam.WorldToScreenPoint(b);
            if (sa.z <= 0f || sb2.z <= 0f) return;
            GL.Color(c);
            GL.Vertex3(sa.x, Screen.height - sa.y, 0f);
            GL.Vertex3(sb2.x, Screen.height - sb2.y, 0f);
        }

        private void Cross(Vector3 p, float size, Color c)
        {
            Line(p + new Vector3(-size, 0f, -size), p + new Vector3(size, 0f, size), c);
            Line(p + new Vector3(-size, 0f, size), p + new Vector3(size, 0f, -size), c);
        }

        private void EnsureStyles()
        {
            if (lineMaterial == null)
            {
                lineMaterial = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
                lineMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            }
            if (label == null) label = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = false, wordWrap = false };
            if (header == null) header = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
        }
    }
}
