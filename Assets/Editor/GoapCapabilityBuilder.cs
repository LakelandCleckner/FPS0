using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEditor;
using UnityEngine;

// Text authoring for GOAP capability assets: validate classes and wiring before writing, scaffold missing classes.
public class GoapCapabilityBuilder : EditorWindow
{
    private enum Kind { Goal, Action, WorldKey, TargetKey, WorldSensor, TargetSensor, MultiSensor }

    private static readonly Dictionary<Kind, string> SubFolder = new Dictionary<Kind, string>
    {
        { Kind.Goal, "Goals" }, { Kind.Action, "Actions" }, { Kind.WorldKey, "WorldKeys" },
        { Kind.TargetKey, "TargetKeys" }, { Kind.WorldSensor, "Sensors" }, { Kind.TargetSensor, "Sensors" },
        { Kind.MultiSensor, "Sensors" }
    };

    private static readonly Dictionary<Kind, Type> KindInterface = new Dictionary<Kind, Type>
    {
        { Kind.Goal, typeof(IGoal) }, { Kind.Action, typeof(IAction) }, { Kind.WorldKey, typeof(IWorldKey) },
        { Kind.TargetKey, typeof(ITargetKey) }, { Kind.WorldSensor, typeof(IWorldSensor) },
        { Kind.TargetSensor, typeof(ITargetSensor) }, { Kind.MultiSensor, typeof(IMultiSensor) }
    };

    // ------------------------------------------------------------------ parsed model

    private class Cond { public string Key; public Comparison Op; public int Amount; }
    private class Eff { public string Key; public bool Increase; }

    private class GoalLine { public int Line; public string Name; public float Cost = 1f; public List<Cond> Conds = new List<Cond>(); }

    private class ActionLine
    {
        public int Line; public string Name; public string Target;
        public float Cost = 1f; public float Range = 0.1f; public ActionMoveMode Move = ActionMoveMode.MoveBeforePerforming;
        public bool ValidateTarget = true; public bool RequiresTarget = true; public bool ValidateConditions = true;
        public List<Cond> Conds = new List<Cond>(); public List<Eff> Effs = new List<Eff>();
    }

    private class SensorLine { public int Line; public Kind Kind; public string Name; public string Key; }

    private class Parsed
    {
        public List<GoalLine> Goals = new List<GoalLine>();
        public List<ActionLine> Actions = new List<ActionLine>();
        public List<SensorLine> Sensors = new List<SensorLine>();
    }

    private class Issue { public int Line; public MessageType Type; public string Text; }

    // ------------------------------------------------------------------ window state

    [SerializeField] private CapabilityConfigScriptable capability;
    [SerializeField] private AgentTypeScriptable agentType;
    [SerializeField] private string text = "";
    [SerializeField] private bool showSyntax;

    private readonly List<Issue> issues = new List<Issue>();
    private readonly Dictionary<string, Kind> missing = new Dictionary<string, Kind>();
    private readonly List<Script> noId = new List<Script>();
    private bool validated;
    private bool hasErrors;
    private Vector2 textScroll, issueScroll;
    private GUIStyle codeStyle;

    [MenuItem("Tools/GOAP Capability Builder")]
    private static void Open() => GetWindow<GoapCapabilityBuilder>("GOAP Capabilities");

    private void OnGUI()
    {
        if (codeStyle == null)
        {
            codeStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = false };
            var mono = EditorGUIUtility.Load("Fonts/RobotoMono/RobotoMono-Regular.ttf") as Font;
            if (mono != null) codeStyle.font = mono;
        }

        EditorGUI.BeginChangeCheck();
        capability = (CapabilityConfigScriptable)EditorGUILayout.ObjectField("Capability", capability, typeof(CapabilityConfigScriptable), false);
        if (EditorGUI.EndChangeCheck()) { LoadFromAsset(); ClearResults(); }

        agentType = (AgentTypeScriptable)EditorGUILayout.ObjectField(
            new GUIContent("Agent Type", "Used for cross-capability checks. Empty = every agent type using this capability."),
            agentType, typeof(AgentTypeScriptable), false);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("New Capability...")) CreateCapability();
        GUI.enabled = capability != null;
        if (GUILayout.Button("Reload From Asset")) { LoadFromAsset(); ClearResults(); }
        GUI.enabled = true;
        if (GUILayout.Button("Check Whole Agent Type")) CheckAgentType();
        EditorGUILayout.EndHorizontal();

        showSyntax = EditorGUILayout.Foldout(showSyntax, "Syntax", true);
        if (showSyntax)
            EditorGUILayout.HelpBox(
                "goal   Name [cost=1] | Key >= 1, Key2 < 3\n" +
                "action Name -> TargetKey [range=0.1 move=before|while cost=1 validateTarget=true requiresTarget=true validateConditions=true] | conditions | effects\n" +
                "world  SensorName -> WorldKey\n" +
                "target SensorName -> TargetKey\n" +
                "multi  SensorName\n\n" +
                "Conditions: Key >= 1   (ops: < <= > >=)   Effects: Key+  or  Key-   Empty list: -\n" +
                "Lines starting with # are comments.", MessageType.None);

        EditorGUI.BeginChangeCheck();
        textScroll = EditorGUILayout.BeginScrollView(textScroll, GUILayout.MinHeight(220), GUILayout.MaxHeight(420));
        text = EditorGUILayout.TextArea(text, codeStyle, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
        if (EditorGUI.EndChangeCheck()) validated = false;

        EditorGUILayout.BeginHorizontal();
        GUI.enabled = capability != null;
        if (GUILayout.Button("Validate", GUILayout.Height(26))) Validate();
        GUI.enabled = capability != null && missing.Count > 0;
        if (GUILayout.Button($"Create Missing ({missing.Count})", GUILayout.Height(26))) CreateMissing();
        GUI.enabled = capability != null && noId.Count > 0;
        if (GUILayout.Button($"Add GoapIds ({noId.Count})", GUILayout.Height(26))) AddGoapIds();
        GUI.enabled = capability != null && validated && !hasErrors;
        if (GUILayout.Button("Apply", GUILayout.Height(26))) Apply();
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        if (validated && !hasErrors && issues.Count == 0)
            EditorGUILayout.HelpBox("All good.", MessageType.Info);

        issueScroll = EditorGUILayout.BeginScrollView(issueScroll);
        foreach (var i in issues)
            EditorGUILayout.HelpBox(i.Line > 0 ? $"Line {i.Line}: {i.Text}" : i.Text, i.Type);
        EditorGUILayout.EndScrollView();
    }

    private void ClearResults()
    {
        issues.Clear(); missing.Clear(); noId.Clear();
        validated = false; hasErrors = false;
    }

    // ------------------------------------------------------------------ generator / scan

    private GeneratorScriptable Generator()
    {
        if (capability != null && capability.generatorScriptable != null) return capability.generatorScriptable;
        if (capability != null)
        {
            var g = ClassScanner.GetGenerator(capability);
            if (g != null) return g;
        }
        return ClassScanner.GetGenerators().FirstOrDefault();
    }

    private static Script[] ScriptsOf(Scripts s, Kind k)
    {
        Script[] r;
        switch (k)
        {
            case Kind.Goal: r = s.goals; break;
            case Kind.Action: r = s.actions; break;
            case Kind.WorldKey: r = s.worldKeys; break;
            case Kind.TargetKey: r = s.targetKeys; break;
            case Kind.WorldSensor: r = s.worldSensors; break;
            case Kind.TargetSensor: r = s.targetSensors; break;
            default: r = s.multiSensors; break;
        }
        return r ?? new Script[0];
    }

    private static Script Find(Scripts s, Kind k, string name) => ScriptsOf(s, k).FirstOrDefault(x => x.Type != null && x.Type.Name == name);

    // ------------------------------------------------------------------ text <-> asset

    private void LoadFromAsset()
    {
        text = capability != null ? ToText(FromAsset(capability)) : "";
        GUI.FocusControl(null);
    }

    private static Parsed FromAsset(CapabilityConfigScriptable cap)
    {
        var p = new Parsed();
        foreach (var g in cap.goals)
        {
            var gl = new GoalLine { Name = g.goal.Name, Cost = g.baseCost };
            foreach (var c in g.conditions) gl.Conds.Add(new Cond { Key = c.worldKey.Name, Op = c.comparison, Amount = c.amount });
            p.Goals.Add(gl);
        }
        foreach (var a in cap.actions)
        {
            var al = new ActionLine
            {
                Name = a.action.Name, Target = a.target.Name, Cost = a.baseCost, Range = a.stoppingDistance, Move = a.moveMode,
                ValidateTarget = a.validateTarget, RequiresTarget = a.requiresTarget, ValidateConditions = a.validateConditions
            };
            foreach (var c in a.conditions) al.Conds.Add(new Cond { Key = c.worldKey.Name, Op = c.comparison, Amount = c.amount });
            foreach (var e in a.effects) al.Effs.Add(new Eff { Key = e.worldKey.Name, Increase = e.effect == EffectType.Increase });
            p.Actions.Add(al);
        }
        foreach (var s in cap.worldSensors) p.Sensors.Add(new SensorLine { Kind = Kind.WorldSensor, Name = s.sensor.Name, Key = s.worldKey.Name });
        foreach (var s in cap.targetSensors) p.Sensors.Add(new SensorLine { Kind = Kind.TargetSensor, Name = s.sensor.Name, Key = s.targetKey.Name });
        foreach (var s in cap.multiSensors) p.Sensors.Add(new SensorLine { Kind = Kind.MultiSensor, Name = s.sensor.Name });
        return p;
    }

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Conds(List<Cond> c) => c.Count == 0 ? "-" : string.Join(", ", c.Select(x => $"{x.Key} {x.Op.ToName()} {x.Amount}"));
    private static string Effs(List<Eff> e) => e.Count == 0 ? "-" : string.Join(", ", e.Select(x => x.Key + (x.Increase ? "+" : "-")));

    private static string ToText(Parsed p)
    {
        var sb = new StringBuilder();
        if (p.Goals.Count > 0) sb.AppendLine("# goals");
        foreach (var g in p.Goals)
            sb.AppendLine($"goal {g.Name}{(g.Cost != 1f ? " cost=" + F(g.Cost) : "")} | {Conds(g.Conds)}");

        if (p.Actions.Count > 0) sb.AppendLine().AppendLine("# actions");
        foreach (var a in p.Actions)
        {
            var o = new StringBuilder();
            o.Append(" range=").Append(F(a.Range));
            o.Append(" move=").Append(a.Move == ActionMoveMode.PerformWhileMoving ? "while" : "before");
            if (a.Cost != 1f) o.Append(" cost=").Append(F(a.Cost));
            if (!a.ValidateTarget) o.Append(" validateTarget=false");
            if (!a.RequiresTarget) o.Append(" requiresTarget=false");
            if (!a.ValidateConditions) o.Append(" validateConditions=false");
            sb.AppendLine($"action {a.Name} -> {a.Target}{o} | {Conds(a.Conds)} | {Effs(a.Effs)}");
        }

        if (p.Sensors.Count > 0) sb.AppendLine().AppendLine("# sensors");
        foreach (var s in p.Sensors)
        {
            if (s.Kind == Kind.WorldSensor) sb.AppendLine($"world {s.Name} -> {s.Key}");
            else if (s.Kind == Kind.TargetSensor) sb.AppendLine($"target {s.Name} -> {s.Key}");
            else sb.AppendLine($"multi {s.Name}");
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ parsing

    private static readonly Regex HeadRx = new Regex(@"^(\w+)\s+(\w+)(?:\s*->\s*(\w+))?((?:\s+\w+=\S+)*)\s*$");
    private static readonly Regex CondRx = new Regex(@"^(\w+)\s*(<=|>=|<|>)\s*(-?\d+)$");
    private static readonly Regex EffRx = new Regex(@"^(\w+)\s*(\+\+|\+|--|-)$");

    private Parsed Parse(string src, List<Issue> into)
    {
        var p = new Parsed();
        var lines = src.Replace("\r", "").Split('\n');
        for (int n = 0; n < lines.Length; n++)
        {
            int ln = n + 1;
            string line = lines[n].Trim();
            if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//")) continue;

            var segs = line.Split('|').Select(s => s.Trim()).ToArray();
            var m = HeadRx.Match(segs[0]);
            if (!m.Success) { Err(into, ln, "Can't read this line. Expected 'kind Name [-> Key] [options]'."); continue; }

            string kw = m.Groups[1].Value.ToLowerInvariant();
            string name = m.Groups[2].Value;
            string key = m.Groups[3].Success && m.Groups[3].Value.Length > 0 ? m.Groups[3].Value : null;
            var opts = ParseOptions(m.Groups[4].Value, ln, into);

            switch (kw)
            {
                case "goal":
                {
                    if (key != null) Err(into, ln, "Goals don't take '-> Key'.");
                    if (segs.Length > 2) Err(into, ln, "Goal format: goal Name [cost=1] | conditions");
                    var g = new GoalLine { Line = ln, Name = name };
                    foreach (var kv in opts)
                    {
                        if (kv.Key == "cost") g.Cost = ParseFloat(kv.Value, ln, into);
                        else Err(into, ln, $"Unknown goal option '{kv.Key}'.");
                    }
                    if (segs.Length > 1) g.Conds = ParseConds(segs[1], ln, into);
                    p.Goals.Add(g);
                    break;
                }
                case "action":
                {
                    if (key == null) Err(into, ln, "Actions need a target: action Name -> TargetKey.");
                    if (segs.Length > 3) Err(into, ln, "Action format: action Name -> Target [options] | conditions | effects");
                    var a = new ActionLine { Line = ln, Name = name, Target = key };
                    foreach (var kv in opts)
                    {
                        switch (kv.Key)
                        {
                            case "cost": a.Cost = ParseFloat(kv.Value, ln, into); break;
                            case "range": a.Range = ParseFloat(kv.Value, ln, into); break;
                            case "move":
                                if (kv.Value == "while") a.Move = ActionMoveMode.PerformWhileMoving;
                                else if (kv.Value == "before") a.Move = ActionMoveMode.MoveBeforePerforming;
                                else Err(into, ln, "move must be 'while' or 'before'.");
                                break;
                            case "validatetarget": a.ValidateTarget = ParseBool(kv.Value, ln, into); break;
                            case "requirestarget": a.RequiresTarget = ParseBool(kv.Value, ln, into); break;
                            case "validateconditions": a.ValidateConditions = ParseBool(kv.Value, ln, into); break;
                            default: Err(into, ln, $"Unknown action option '{kv.Key}'."); break;
                        }
                    }
                    if (segs.Length > 1) a.Conds = ParseConds(segs[1], ln, into);
                    if (segs.Length > 2) a.Effs = ParseEffs(segs[2], ln, into);
                    if (a.Effs.Count == 0) Warn(into, ln, $"{name} has no effects, so the planner can never choose it.");
                    p.Actions.Add(a);
                    break;
                }
                case "world":
                case "target":
                    if (key == null) { Err(into, ln, $"Sensor needs a key: {kw} {name} -> Key."); break; }
                    if (segs.Length > 1 || opts.Count > 0) Err(into, ln, "Sensors take no options or lists.");
                    p.Sensors.Add(new SensorLine { Line = ln, Kind = kw == "world" ? Kind.WorldSensor : Kind.TargetSensor, Name = name, Key = key });
                    break;
                case "multi":
                    if (key != null || segs.Length > 1 || opts.Count > 0) Err(into, ln, "Multi sensor format: multi Name");
                    p.Sensors.Add(new SensorLine { Line = ln, Kind = Kind.MultiSensor, Name = name });
                    break;
                default:
                    Err(into, ln, $"Unknown kind '{kw}'. Use goal, action, world, target or multi.");
                    break;
            }
        }
        return p;
    }

    private static List<KeyValuePair<string, string>> ParseOptions(string s, int ln, List<Issue> into)
    {
        var r = new List<KeyValuePair<string, string>>();
        foreach (var tok in s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = tok.IndexOf('=');
            r.Add(new KeyValuePair<string, string>(tok.Substring(0, eq).ToLowerInvariant(), tok.Substring(eq + 1).ToLowerInvariant()));
        }
        return r;
    }

    private static List<Cond> ParseConds(string s, int ln, List<Issue> into)
    {
        var r = new List<Cond>();
        if (s.Length == 0 || s == "-") return r;
        foreach (var part in s.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0))
        {
            var m = CondRx.Match(part);
            if (!m.Success) { Err(into, ln, $"Bad condition '{part}'. Use 'Key >= 1'."); continue; }
            r.Add(new Cond { Key = m.Groups[1].Value, Op = m.Groups[2].Value.FromName(), Amount = int.Parse(m.Groups[3].Value) });
        }
        return r;
    }

    private static List<Eff> ParseEffs(string s, int ln, List<Issue> into)
    {
        var r = new List<Eff>();
        if (s.Length == 0 || s == "-") return r;
        foreach (var part in s.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0))
        {
            var m = EffRx.Match(part);
            if (!m.Success) { Err(into, ln, $"Bad effect '{part}'. Use 'Key+' or 'Key-'."); continue; }
            r.Add(new Eff { Key = m.Groups[1].Value, Increase = m.Groups[2].Value.StartsWith("+") });
        }
        return r;
    }

    private static float ParseFloat(string v, int ln, List<Issue> into)
    {
        if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) return f;
        Err(into, ln, $"'{v}' is not a number."); return 0f;
    }

    private static bool ParseBool(string v, int ln, List<Issue> into)
    {
        if (v == "true" || v == "1") return true;
        if (v == "false" || v == "0") return false;
        Err(into, ln, $"'{v}' is not true/false."); return true;
    }

    private static void Err(List<Issue> l, int ln, string t) => l.Add(new Issue { Line = ln, Type = MessageType.Error, Text = t });
    private static void Warn(List<Issue> l, int ln, string t) => l.Add(new Issue { Line = ln, Type = MessageType.Warning, Text = t });
    private static void Info(List<Issue> l, int ln, string t) => l.Add(new Issue { Line = ln, Type = MessageType.Info, Text = t });

    // ------------------------------------------------------------------ validation

    private void Validate()
    {
        ClearResults();
        var gen = Generator();
        if (gen == null) { Err(issues, 0, "No GOAP Generator asset found."); Finish(); return; }

        var parsed = Parse(text, issues);
        var scripts = gen.GetClasses();
        CheckClasses(parsed, scripts, gen, issues, missing, noId);
        CheckDuplicates(parsed, issues);

        // Cross checks against the agent type(s) this capability belongs to, using the edited text for this one.
        var types = AgentTypesFor(capability);
        if (types.Count == 0)
        {
            Info(issues, 0, "Capability isn't in any agent type; sensor/reachability checks only see this capability.");
            CheckWiring(parsed, new List<Parsed> { parsed }, issues);
        }
        foreach (var t in types)
        {
            var all = new List<Parsed> { parsed };
            foreach (var c in t.capabilities.OfType<CapabilityConfigScriptable>())
                if (c != capability) all.Add(FromAsset(c));
            var before = issues.Count;
            CheckWiring(parsed, all, issues);
            if (types.Count > 1)
                for (int i = before; i < issues.Count; i++) issues[i].Text = $"[{t.name}] " + issues[i].Text;
        }
        Finish();
    }

    private void Finish()
    {
        validated = true;
        hasErrors = issues.Any(i => i.Type == MessageType.Error);
        issues.Sort((a, b) => a.Line != b.Line ? a.Line.CompareTo(b.Line) : a.Type.CompareTo(b.Type));
        Repaint();
    }

    private List<AgentTypeScriptable> AgentTypesFor(CapabilityConfigScriptable cap)
    {
        if (agentType != null) return new List<AgentTypeScriptable> { agentType };
        return AssetDatabase.FindAssets("t:AgentTypeScriptable")
            .Select(g => AssetDatabase.LoadAssetAtPath<AgentTypeScriptable>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(t => t != null && t.capabilities.Contains(cap)).ToList();
    }

    private static void CheckClasses(Parsed p, Scripts s, GeneratorScriptable gen, List<Issue> into,
                                     Dictionary<string, Kind> missingOut, List<Script> noIdOut)
    {
        foreach (var g in p.Goals)
        {
            CheckClass(g.Name, Kind.Goal, g.Line, s, gen, into, missingOut, noIdOut);
            foreach (var c in g.Conds) CheckClass(c.Key, Kind.WorldKey, g.Line, s, gen, into, missingOut, noIdOut);
        }
        foreach (var a in p.Actions)
        {
            CheckClass(a.Name, Kind.Action, a.Line, s, gen, into, missingOut, noIdOut);
            if (a.Target != null) CheckClass(a.Target, Kind.TargetKey, a.Line, s, gen, into, missingOut, noIdOut);
            foreach (var c in a.Conds) CheckClass(c.Key, Kind.WorldKey, a.Line, s, gen, into, missingOut, noIdOut);
            foreach (var e in a.Effs) CheckClass(e.Key, Kind.WorldKey, a.Line, s, gen, into, missingOut, noIdOut);
        }
        foreach (var x in p.Sensors)
        {
            CheckClass(x.Name, x.Kind, x.Line, s, gen, into, missingOut, noIdOut);
            if (x.Kind == Kind.WorldSensor) CheckClass(x.Key, Kind.WorldKey, x.Line, s, gen, into, missingOut, noIdOut);
            if (x.Kind == Kind.TargetSensor) CheckClass(x.Key, Kind.TargetKey, x.Line, s, gen, into, missingOut, noIdOut);
        }
    }

    private static void CheckClass(string name, Kind kind, int ln, Scripts s, GeneratorScriptable gen, List<Issue> into,
                                   Dictionary<string, Kind> missingOut, List<Script> noIdOut)
    {
        var hit = Find(s, kind, name);
        if (hit != null)
        {
            if (string.IsNullOrEmpty(hit.Id) && !noIdOut.Contains(hit))
            {
                noIdOut.Add(hit);
                Warn(into, ln, $"{name} has no [GoapId]; the asset links it by name only. Use Add GoapIds.");
            }
            return;
        }

        foreach (Kind other in Enum.GetValues(typeof(Kind)))
            if (other != kind && Find(s, other, name) != null)
            {
                Err(into, ln, $"{name} is a {Label(other)}, but is used here as a {Label(kind)}.");
                return;
            }

        var stray = TypeCache.GetTypesDerivedFrom(KindInterface[kind]).FirstOrDefault(t => t.Name == name && !t.IsAbstract);
        if (stray != null)
        {
            string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(gen)).Replace('\\', '/');
            Err(into, ln, $"{name} exists ({stray.FullName}) but the generator can't see it. It must be under '{folder}', " +
                          $"in a namespace starting with '{gen.nameSpace}', in a file named {name}.cs.");
            return;
        }

        if (missingOut.TryGetValue(name, out var prev) && prev != kind)
        {
            Err(into, ln, $"{name} is used as both a {Label(prev)} and a {Label(kind)}.");
            return;
        }
        if (!missingOut.ContainsKey(name))
        {
            missingOut[name] = kind;
            Err(into, ln, $"{Label(kind)} {name} doesn't exist yet. Use Create Missing.");
        }
    }

    private static void CheckDuplicates(Parsed p, List<Issue> into)
    {
        foreach (var d in p.Goals.GroupBy(g => g.Name).Where(x => x.Count() > 1))
            Err(into, d.Last().Line, $"Goal {d.Key} is listed twice.");
        foreach (var d in p.Actions.GroupBy(a => a.Name).Where(x => x.Count() > 1))
            Err(into, d.Last().Line, $"Action {d.Key} is listed twice.");
        foreach (var d in p.Sensors.GroupBy(x => x.Name).Where(x => x.Count() > 1))
            Err(into, d.Last().Line, $"Sensor {d.Key} is listed twice.");
    }

    // Sensors present, targets sensed, goals reachable -- across every capability of the agent type.
    private static void CheckWiring(Parsed mine, List<Parsed> all, List<Issue> into)
    {
        var worldSensed = new HashSet<string>(all.SelectMany(p => p.Sensors).Where(x => x.Kind == Kind.WorldSensor).Select(x => x.Key));
        var targetSensed = new HashSet<string>(all.SelectMany(p => p.Sensors).Where(x => x.Kind == Kind.TargetSensor).Select(x => x.Key));
        bool hasMulti = all.SelectMany(p => p.Sensors).Any(x => x.Kind == Kind.MultiSensor);
        string multiNote = hasMulti ? " (unless a multi sensor sets it)" : "";

        var worldOwners = all.SelectMany(p => p.Sensors).Where(x => x.Kind == Kind.WorldSensor)
            .GroupBy(x => x.Key).Where(g => g.Select(x => x.Name).Distinct().Count() > 1);
        foreach (var g in worldOwners)
        {
            var mineLine = mine.Sensors.FirstOrDefault(x => x.Kind == Kind.WorldSensor && x.Key == g.Key);
            if (mineLine != null) Warn(into, mineLine.Line, $"{g.Key} is set by more than one world sensor ({string.Join(", ", g.Select(x => x.Name).Distinct())}).");
        }

        var allActions = all.SelectMany(p => p.Actions).ToList();

        foreach (var a in mine.Actions)
        {
            if (a.Target != null && a.RequiresTarget && !targetSensed.Contains(a.Target))
                Warn(into, a.Line, $"No target sensor provides {a.Target}, so {a.Name} never has a target{multiNote}.");
            foreach (var c in a.Conds)
                if (!worldSensed.Contains(c.Key) && !Produced(allActions, c))
                    Warn(into, a.Line, $"Nothing senses or produces {c.Key}; it always reads 0{multiNote}.");
        }

        foreach (var g in mine.Goals)
            foreach (var c in g.Conds)
            {
                if (Produced(allActions, c)) continue;
                if (worldSensed.Contains(c.Key))
                    Info(into, g.Line, $"No action produces {c.Key}; {g.Name} only completes when its sensor says so.");
                else
                    Warn(into, g.Line, $"{g.Name} is unreachable: no action changes {c.Key} the right way and nothing senses it{multiNote}.");
            }
    }

    private static bool Produced(List<ActionLine> actions, Cond c)
    {
        bool wantUp = c.Op == Comparison.GreaterThan || c.Op == Comparison.GreaterThanOrEqual;
        return actions.Any(a => a.Effs.Any(e => e.Key == c.Key && e.Increase == wantUp));
    }

    private static string Label(Kind k)
    {
        switch (k)
        {
            case Kind.WorldKey: return "world key";
            case Kind.TargetKey: return "target key";
            case Kind.WorldSensor: return "world sensor";
            case Kind.TargetSensor: return "target sensor";
            case Kind.MultiSensor: return "multi sensor";
            default: return k.ToString().ToLowerInvariant();
        }
    }

    private void CheckAgentType()
    {
        ClearResults();
        var types = agentType != null ? new List<AgentTypeScriptable> { agentType }
                  : capability != null ? AgentTypesFor(capability) : new List<AgentTypeScriptable>();
        if (types.Count == 0) { Err(issues, 0, "Pick an Agent Type (or a capability that belongs to one)."); Finish(); return; }

        var gen = Generator();
        if (gen == null) { Err(issues, 0, "No GOAP Generator asset found."); Finish(); return; }
        var scripts = gen.GetClasses();

        foreach (var t in types)
        {
            var caps = t.capabilities.OfType<CapabilityConfigScriptable>().ToList();
            var all = caps.Select(FromAsset).ToList();
            for (int i = 0; i < caps.Count; i++)
            {
                var local = new List<Issue>();
                CheckClasses(all[i], scripts, gen, local, new Dictionary<string, Kind>(), new List<Script>());
                CheckDuplicates(all[i], local);
                CheckWiring(all[i], all, local);
                foreach (var x in local) { x.Text = $"[{t.name} / {caps[i].name}] {x.Text}"; x.Line = 0; }
                issues.AddRange(local);
            }
            var dupGoals = all.SelectMany(p => p.Goals).GroupBy(g => g.Name).Where(g => g.Count() > 1);
            foreach (var d in dupGoals) Err(issues, 0, $"[{t.name}] Goal {d.Key} is in more than one capability.");
            var dupActions = all.SelectMany(p => p.Actions).GroupBy(a => a.Name).Where(g => g.Count() > 1);
            foreach (var d in dupActions) Err(issues, 0, $"[{t.name}] Action {d.Key} is in more than one capability.");
        }
        if (!issues.Any(i => i.Type == MessageType.Error || i.Type == MessageType.Warning))
            Info(issues, 0, $"Agent type check: no problems in {string.Join(", ", types.Select(t => $"{t.name} ({t.capabilities.Count} capabilities)"))}.");
        Finish();
        validated = false;   // agent-wide check doesn't validate the text box
    }

    // ------------------------------------------------------------------ apply

    private void Apply()
    {
        Validate();
        if (hasErrors) return;

        var gen = Generator();
        var s = gen.GetClasses();
        var parsed = Parse(text, new List<Issue>());

        Undo.RecordObject(capability, "Apply GOAP Capability");

        var oldProps = new Dictionary<string, IActionProperties>();
        foreach (var a in capability.actions)
            if (a.action != null && a.action.Name != null && a.properties != null) oldProps[a.action.Name] = a.properties;

        capability.goals = parsed.Goals.Select(g => new CapabilityGoal
        {
            goal = Ref(s, Kind.Goal, g.Name),
            baseCost = g.Cost,
            conditions = g.Conds.Select(c => ToCond(s, c)).ToList()
        }).ToList();

        capability.actions = parsed.Actions.Select(a => new CapabilityAction
        {
            action = Ref(s, Kind.Action, a.Name),
            target = Ref(s, Kind.TargetKey, a.Target),
            properties = oldProps.TryGetValue(a.Name, out var props) ? props : new EmptyActionProperties(),
            baseCost = a.Cost,
            stoppingDistance = a.Range,
            validateTarget = a.ValidateTarget,
            requiresTarget = a.RequiresTarget,
            validateConditions = a.ValidateConditions,
            moveMode = a.Move,
            conditions = a.Conds.Select(c => ToCond(s, c)).ToList(),
            effects = a.Effs.Select(e => new CapabilityEffect
            {
                worldKey = Ref(s, Kind.WorldKey, e.Key),
                effect = e.Increase ? EffectType.Increase : EffectType.Decrease
            }).ToList()
        }).ToList();

        capability.worldSensors = parsed.Sensors.Where(x => x.Kind == Kind.WorldSensor).Select(x => new CapabilityWorldSensor
        { sensor = Ref(s, Kind.WorldSensor, x.Name), worldKey = Ref(s, Kind.WorldKey, x.Key) }).ToList();

        capability.targetSensors = parsed.Sensors.Where(x => x.Kind == Kind.TargetSensor).Select(x => new CapabilityTargetSensor
        { sensor = Ref(s, Kind.TargetSensor, x.Name), targetKey = Ref(s, Kind.TargetKey, x.Key) }).ToList();

        capability.multiSensors = parsed.Sensors.Where(x => x.Kind == Kind.MultiSensor).Select(x => new CapabilityMultiSensor
        { sensor = Ref(s, Kind.MultiSensor, x.Name) }).ToList();

        if (capability.generatorScriptable == null) capability.generatorScriptable = gen;

        EditorUtility.SetDirty(capability);
        EditorUtility.SetDirty(gen);   // class cache refreshed by GetClasses, so builds see new classes
        AssetDatabase.SaveAssets();

        LoadFromAsset();
        Debug.Log($"[GOAP Builder] Applied '{capability.name}': {capability.goals.Count} goals, {capability.actions.Count} actions, " +
                  $"{capability.worldSensors.Count + capability.targetSensors.Count + capability.multiSensors.Count} sensors.");
        Validate();
    }

    private static ClassRef Ref(Scripts s, Kind k, string name)
    {
        var hit = Find(s, k, name);
        return new ClassRef { Name = name, Id = hit != null ? hit.Id : null };
    }

    private static CapabilityCondition ToCond(Scripts s, Cond c)
        => new CapabilityCondition { worldKey = Ref(s, Kind.WorldKey, c.Key), comparison = c.Op, amount = c.Amount };

    // ------------------------------------------------------------------ scaffolding

    private void CreateCapability()
    {
        var gen = Generator();
        string start = gen != null ? Path.GetDirectoryName(AssetDatabase.GetAssetPath(gen)).Replace('\\', '/') + "/Capabilities" : "Assets";
        if (!AssetDatabase.IsValidFolder(start)) start = "Assets";

        string path = EditorUtility.SaveFilePanelInProject("New Capability", "NewCapability", "asset", "Create a GOAP capability", start);
        if (string.IsNullOrEmpty(path)) return;

        var cap = CreateInstance<CapabilityConfigScriptable>();
        cap.generatorScriptable = gen;
        AssetDatabase.CreateAsset(cap, path);

        if (agentType != null && EditorUtility.DisplayDialog("Add to agent type",
                $"Add {cap.name} to agent type {agentType.name}?", "Add", "Skip"))
        {
            Undo.RecordObject(agentType, "Add Capability");
            agentType.capabilities.Add(cap);
            EditorUtility.SetDirty(agentType);
        }
        AssetDatabase.SaveAssets();

        capability = cap;
        LoadFromAsset();
        ClearResults();
    }

    private void CreateMissing()
    {
        var gen = Generator();
        if (gen == null) return;
        string root = Path.GetDirectoryName(AssetDatabase.GetAssetPath(gen)).Replace('\\', '/');
        var made = new List<string>();

        foreach (var kv in missing)
        {
            if (kv.Value == Kind.MultiSensor) { Debug.LogWarning($"[GOAP Builder] Multi sensor {kv.Key} not scaffolded; write it by hand."); continue; }

            string dir = root + "/" + SubFolder[kv.Value];
            Directory.CreateDirectory(dir);
            string file = dir + "/" + kv.Key + ".cs";
            if (File.Exists(file)) { Debug.LogWarning($"[GOAP Builder] {file} already exists; skipped."); continue; }

            string ns = gen.nameSpace + "." + SubFolder[kv.Value];
            File.WriteAllText(file, Stub(kv.Value, kv.Key, ns, Guid.NewGuid().ToString()));
            made.Add(file);
        }

        AssetDatabase.Refresh();
        Debug.Log($"[GOAP Builder] Created {made.Count} class(es):\n" + string.Join("\n", made));
        ClearResults();
        Info(issues, 0, $"Created {made.Count} class(es). Wait for the recompile, then Validate again.");
        Finish();
        validated = false;
    }

    private static string Stub(Kind k, string name, string ns, string id)
    {
        string head = $"namespace {ns}\n{{\n    [GoapId(\"{id}\")]\n";
        switch (k)
        {
            case Kind.Goal:
                return "using CrashKonijn.Goap.Runtime;\n\n" + head +
                       $"    public class {name} : GoalBase {{ }}\n}}\n";
            case Kind.WorldKey:
                return "using CrashKonijn.Goap.Runtime;\n\n" + head +
                       $"    public class {name} : WorldKey\n    {{\n        public {name}() : base(\"{name}\") {{ }}\n    }}\n}}\n";
            case Kind.TargetKey:
                return "using CrashKonijn.Goap.Runtime;\n\n" + head +
                       $"    public class {name} : TargetKey\n    {{\n        public {name}() : base(\"{name}\") {{ }}\n    }}\n}}\n";
            case Kind.WorldSensor:
                return "using CrashKonijn.Agent.Core;\nusing CrashKonijn.Goap.Core;\nusing CrashKonijn.Goap.Runtime;\nusing UnityEngine;\n\n" + head +
                       $"    public class {name} : LocalWorldSensorBase\n    {{\n" +
                       "        public override void Created() { }\n\n" +
                       "        public override void Update() { }\n\n" +
                       "        public override SenseValue Sense(IActionReceiver agent, IComponentReference references)\n" +
                       "        {\n            // TODO: return the key's current value for this agent\n            return new SenseValue(0);\n        }\n    }\n}\n";
            case Kind.TargetSensor:
                return "using CrashKonijn.Agent.Core;\nusing CrashKonijn.Goap.Core;\nusing CrashKonijn.Goap.Runtime;\nusing UnityEngine;\n\n" + head +
                       $"    public class {name} : LocalTargetSensorBase\n    {{\n" +
                       "        public override void Created() { }\n\n" +
                       "        public override void Update() { }\n\n" +
                       "        public override ITarget Sense(IActionReceiver agent, IComponentReference references, ITarget existingTarget)\n" +
                       "        {\n            // TODO: return a TransformTarget / PositionTarget for this agent\n            return existingTarget;\n        }\n    }\n}\n";
            case Kind.Action:
                return "using CrashKonijn.Agent.Core;\nusing CrashKonijn.Goap.Runtime;\nusing UnityEngine;\n\n" + head +
                       $"    public class {name} : GoapActionBase<{name}.Data>\n    {{\n" +
                       "        public override void Start(IMonoAgent agent, Data data) { }\n\n" +
                       "        public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)\n" +
                       "        {\n            // TODO: action logic; Completed when done, Continue to hold the plan\n            return ActionRunState.ContinueOrResolve;\n        }\n\n" +
                       "        public override void Stop(IMonoAgent agent, Data data) { }\n\n" +
                       "        public class Data : IActionData\n        {\n            public ITarget Target { get; set; }\n        }\n    }\n}\n";
            default:
                return "";
        }
    }

    private void AddGoapIds()
    {
        int done = 0;
        foreach (var s in noId)
        {
            if (string.IsNullOrEmpty(s.Path) || !File.Exists(s.Path)) continue;
            string src = File.ReadAllText(s.Path);
            var rx = new Regex(@"(^[ \t]*)((?:public|internal)\s+(?:sealed\s+|partial\s+)*class\s+" + Regex.Escape(s.Type.Name) + @"\b)", RegexOptions.Multiline);
            var m = rx.Match(src);
            if (!m.Success) { Debug.LogWarning($"[GOAP Builder] Couldn't find class {s.Type.Name} in {s.Path}."); continue; }

            string attr = $"{m.Groups[1].Value}[GoapId(\"{Guid.NewGuid()}\")]\n";
            src = src.Insert(m.Index, attr);
            if (!src.Contains("using CrashKonijn.Goap.Runtime;")) src = "using CrashKonijn.Goap.Runtime;\n" + src;
            File.WriteAllText(s.Path, src);
            done++;
        }
        AssetDatabase.Refresh();
        ClearResults();
        Info(issues, 0, $"Added [GoapId] to {done} class(es). Wait for the recompile, then Validate again.");
        Finish();
        validated = false;
    }
}
