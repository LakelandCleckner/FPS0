using System.IO;
using UnityEditor;
using UnityEngine;

// Blender FBX exports name every clip "Scene". Renames each to its FBX file name on import.
// Scoped to the RetroWeaponsPack so other assets are untouched. Import settings (.meta) are not modified.
public class RetroWeaponClipRenamer : AssetPostprocessor
{
    private const string PackPath = "RetroWeaponsPack";
    private const string BlenderTakeName = "Scene";

    private void OnPostprocessAnimation(GameObject root, AnimationClip clip)
    {
        if (!assetPath.Contains(PackPath) || clip.name != BlenderTakeName) return;

        clip.name = Path.GetFileNameWithoutExtension(assetPath);
    }
}
