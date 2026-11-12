using UnityEditor;
using UnityEngine;
using Combat.AI;

// Capability flags first, then only the tuning for capabilities that are on, with warnings for bad combinations.
[CustomEditor(typeof(EnemyBehaviourProfileSO))]
public class EnemyBehaviourProfileEditor : Editor
{
    private static readonly string[] Flags = { "melee", "kamikaze", "shieldCover", "ranged", "search", "idle" };
    private static readonly string[] Melee = { "holdDistance", "holdCircleSpeed" };
    private static readonly string[] Kamikaze = { "kamikazeTriggerRange", "kamikazeSpeedFactor" };
    private static readonly string[] Cover = { "coverSearchRadius", "coverMinTargetDistance", "retreatDistance" };
    private static readonly string[] Ranged =
    {
        "rangedMinRange", "rangedPreferredRange", "rangedBandShift",
        "firingPointSearchRadius", "firingPointRecheck", "firingPointTolerance", "allySpacing", "fireWeights",
        "advanceSpeedFactor", "faceWhileMoving",
        "aimErrorPerTargetSpeed", "burstSeparationJitter", "flinchDamageFraction", "flinchRecovery", "lowerAfter"
    };
    private static readonly string[] Defensive =
    {
        "defDamage", "defAimedAt", "defAllyDeath", "defAllyDeathRadius", "defProximity", "defOutranged", "defDecay",
        "aggressiveBelow", "defensiveAbove"
    };

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var p = (EnemyBehaviourProfileSO)target;

        Draw(Flags);
        if (p.melee) Draw(Melee);
        if (p.kamikaze) Draw(Kamikaze);
        if (p.shieldCover) Draw(Cover);
        if (p.ranged) { Draw(Ranged); Draw(Defensive); }

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Agent type id", p.AgentTypeId, EditorStyles.miniLabel);
        Warnings(p);
    }

    private void Draw(string[] names)
    {
        foreach (var n in names)
        {
            var prop = serializedObject.FindProperty(n);
            if (prop != null) EditorGUILayout.PropertyField(prop, true);
        }
    }

    private static void Warnings(EnemyBehaviourProfileSO p)
    {
        if (!p.melee && !p.kamikaze && !p.ranged)
            EditorGUILayout.HelpBox("No way to attack: turn on melee, kamikaze or ranged.", MessageType.Error);
        if (p.kamikaze && p.melee)
            EditorGUILayout.HelpBox("Kamikaze and melee are both on: whichever plan is cheaper wins each time.", MessageType.Warning);
        if (!p.search && !p.idle)
            EditorGUILayout.HelpBox("Search and idle are both off: the enemy stands still without a target.", MessageType.Warning);
        if (p.ranged)
        {
            if (p.rangedMinRange >= p.rangedPreferredRange)
                EditorGUILayout.HelpBox("Min range is at or past the preferred range.", MessageType.Warning);
            if (p.aggressiveBelow >= p.defensiveAbove)
                EditorGUILayout.HelpBox("Aggressive Below must be lower than Defensive Above.", MessageType.Error);
            if (p.firingPointTolerance <= 0f)
                EditorGUILayout.HelpBox("Firing Point Tolerance must be above 0.", MessageType.Error);
            if (!p.melee)
                EditorGUILayout.HelpBox("Ranged without melee: inside the min range it backs off instead of swinging.", MessageType.Info);
        }
    }
}
