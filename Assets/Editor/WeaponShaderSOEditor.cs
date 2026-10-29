using System.Collections.Generic;
using Combat.Weapons;
using UnityEditor;
using UnityEngine;

// Zone looks picked by name from the preset's zone set.
[CustomEditor(typeof(WeaponShaderSO))]
public class WeaponShaderSOEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var setProp = serializedObject.FindProperty("zoneSet");
        EditorGUILayout.PropertyField(setProp);
        var set = setProp.objectReferenceValue as WeaponZoneSetSO;
        if (set == null)
            EditorGUILayout.HelpBox("Assign a Zone Set to pick zones by name.", MessageType.Info);

        var list = serializedObject.FindProperty("zones");
        var seen = new HashSet<int>();
        var dupes = new HashSet<int>();
        int remove = -1;

        for (int i = 0; i < list.arraySize; i++)
        {
            var e = list.GetArrayElementAtIndex(i);
            var idProp = e.FindPropertyRelative("zoneId");
            if (!seen.Add(idProp.intValue)) dupes.Add(idProp.intValue);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    idProp.intValue = ZonePopup(set, idProp.intValue);
                    if (GUILayout.Button("Remove", GUILayout.Width(64))) remove = i;
                }
                EditorGUILayout.PropertyField(e.FindPropertyRelative("color"));
                EditorGUILayout.PropertyField(e.FindPropertyRelative("recolor"));
                EditorGUILayout.PropertyField(e.FindPropertyRelative("brightness"));
                EditorGUILayout.PropertyField(e.FindPropertyRelative("metallic"));
                EditorGUILayout.PropertyField(e.FindPropertyRelative("smoothness"));
                EditorGUILayout.PropertyField(e.FindPropertyRelative("emission"));
            }
        }

        if (remove >= 0) list.DeleteArrayElementAtIndex(remove);

        foreach (int id in dupes)
            EditorGUILayout.HelpBox($"{ZoneName(set, id)} is listed more than once; the last entry wins.", MessageType.Warning);

        if (GUILayout.Button("Add Zone Look")) AddLook(list, set, seen);

        serializedObject.ApplyModifiedProperties();
    }

    private static void AddLook(SerializedProperty list, WeaponZoneSetSO set, HashSet<int> used)
    {
        int id = 1;
        if (set != null)
        {
            foreach (var z in set.zones)
            {
                if (z == null || z.id == 0 || used.Contains(z.id)) continue;
                id = z.id;
                break;
            }
        }

        list.arraySize++;
        var e = list.GetArrayElementAtIndex(list.arraySize - 1);
        e.FindPropertyRelative("zoneId").intValue = id;
        e.FindPropertyRelative("color").colorValue = Color.white;
        e.FindPropertyRelative("recolor").floatValue = 1f;
        e.FindPropertyRelative("brightness").floatValue = 1f;
        e.FindPropertyRelative("metallic").floatValue = 0f;
        e.FindPropertyRelative("smoothness").floatValue = 0f;
        e.FindPropertyRelative("emission").colorValue = Color.black;
    }

    private static int ZonePopup(WeaponZoneSetSO set, int id)
    {
        if (set == null) return EditorGUILayout.IntSlider("Zone Id", id, 1, WeaponZoneSetSO.MaxZones);

        var names = new List<string>();
        var ids = new List<int>();
        foreach (var z in set.zones)
        {
            if (z == null || z.id == 0) continue;
            names.Add(z.name);
            ids.Add(z.id);
        }
        if (!ids.Contains(id))
        {
            names.Add($"(missing zone {id})");
            ids.Add(id);
        }
        return EditorGUILayout.IntPopup("Zone", id, names.ToArray(), ids.ToArray());
    }

    private static string ZoneName(WeaponZoneSetSO set, int id)
    {
        var z = set != null ? set.Find(id) : null;
        return z != null ? $"'{z.name}'" : $"Zone {id}";
    }
}
