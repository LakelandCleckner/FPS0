using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Batch import settings for Mixamo clip FBXs (selected files or folders)
public static class MixamoClipSetup
{
    // Name fragments that mark a one-shot clip
    internal static readonly string[] OneShot = { "jump up", "jump down", "death", "turn", "kneel", " to ", "hitreaction", "hit reaction" };

    [MenuItem("Tools/Mixamo Clip Setup")]
    private static void Run()
    {
        int changed = 0, skipped = 0;

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var path in SelectedModelPaths())
            {
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) continue;

                var clips = importer.clipAnimations;
                if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
                if (clips == null || clips.Length == 0) { skipped++; continue; }

                string file = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                bool loop = !OneShot.Any(file.Contains);

                bool dirty = false;
                foreach (var c in clips) dirty |= Apply(c, loop);
                if (!dirty) { skipped++; continue; }

                importer.clipAnimations = clips;
                importer.SaveAndReimport();
                changed++;
                Debug.Log($"[MixamoClipSetup] {path} (loop {(loop ? "on" : "off")})");
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        Debug.Log($"[MixamoClipSetup] {changed} changed, {skipped} skipped (already set or no clips).");
    }

    [MenuItem("Tools/Mixamo Clip Setup", true)]
    private static bool Validate() => Selection.objects.Length > 0;

    // Returns true if anything changed
    internal static bool Apply(ModelImporterClipAnimation c, bool loop)
    {
        bool same = c.lockRootRotation && !c.keepOriginalOrientation
                    && c.lockRootHeightY && c.keepOriginalPositionY
                    && !c.lockRootPositionXZ
                    && c.loopTime == loop && c.loopPose == loop;
        if (same) return false;

        c.lockRootRotation = true;          // bake rotation
        c.keepOriginalOrientation = false;  // based upon body orientation
        c.lockRootHeightY = true;           // bake Y
        c.keepOriginalPositionY = true;     // based upon original
        c.lockRootPositionXZ = false;       // XZ stays unbaked
        c.loopTime = loop;
        c.loopPose = loop;
        return true;
    }

    // Selected model files, plus every model inside selected folders
    private static IEnumerable<string> SelectedModelPaths()
    {
        var paths = new HashSet<string>();
        foreach (var obj in Selection.objects)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path)) continue;

            if (AssetDatabase.IsValidFolder(path))
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { path }))
                    paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }
            else if (AssetImporter.GetAtPath(path) is ModelImporter)
            {
                paths.Add(path);
            }
        }
        return paths;
    }
}
