using UnityEditor;
using UnityEngine;

namespace Combat.Core.EditorTools
{
    [CustomEditor(typeof(DamageFormulaSO))]
    public class DamageFormulaSOEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var formula = (DamageFormulaSO)target;
            var problems = formula.Problems();
            if (problems.Count == 0) EditorGUILayout.HelpBox("Every layer is listed once.", MessageType.Info);
            foreach (var p in problems) EditorGUILayout.HelpBox(p, MessageType.Warning);

            EditorGUILayout.Space();
            DrawDefaultInspector();

            EditorGUILayout.Space();
            if (GUILayout.Button("Reset to default layers"))
            {
                Undo.RecordObject(formula, "Reset damage formula");
                formula.layers = DamageFormulaSO.DefaultLayers();
                EditorUtility.SetDirty(formula);
            }
        }
    }
}
