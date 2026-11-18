using UnityEngine;

namespace Combat.Effects
{
    // What a shot looks like in flight. Archetypes pick one; a weapon can override it with its own.
    [CreateAssetMenu(fileName = "Round", menuName = "Combat/Weapons/Round")]
    public class RoundSO : ScriptableObject
    {
        [Header("Model")]
        [Tooltip("Mesh to fly. Empty = Model + Mesh Name, or a low-poly ball when both are empty.")]
        public Mesh mesh;
        [Tooltip("Imported model holding the mesh, read by child name.")]
        public GameObject model;
        public string meshName = "";
        public Material material;
        [Tooltip("Multiplied into the material colour. White = untouched.")]
        public Color tint = Color.white;
        [Tooltip("Size along the mesh's own axes.")]
        public Vector3 scale = Vector3.one;
        [Tooltip("Turns the mesh so its tip faces the flight direction.")]
        public Vector3 rotation = new Vector3(0f, 90f, 0f);
        [Tooltip("Metres from the muzzle before the round, streak and element effect are drawn, so the muzzle stays clear.")]
        [Min(0f)] public float hideNearMuzzle = 0.2f;

        [Header("Element")]
        [Tooltip("Element rounds get a glowing shell in the element colour.")]
        public bool elementGlow = true;
        [Min(1f)] public float glowSize = 1.3f;
        [Range(0f, 1f)] public float glowAlpha = 0.35f;
        [Tooltip("Size of the element's flight effect riding on this round, times the type's tracer scale. 0 = none.")]
        [Min(0f)] public float elementBodyScale = 0.5f;

        [Header("Streak")]
        [Min(0f)] public float streakLength = 1f;
        [Min(0f)] public float streakWidth = 1f;
        [Tooltip("Element particle trail density multiplier. Lower for multi-pellet shots.")]
        [Min(0f)] public float trailDensity = 1f;

        [System.NonSerialized] private Mesh resolved;
        private static Mesh ball;

        public Mesh Mesh
        {
            get
            {
                if (resolved != null) return resolved;
                if (mesh != null) return resolved = mesh;
                if (model != null && !string.IsNullOrEmpty(meshName))
                {
                    var child = Find(model.transform, meshName);
                    var filter = child != null ? child.GetComponent<MeshFilter>() : null;
                    if (filter != null && filter.sharedMesh != null) return resolved = filter.sharedMesh;
                    Debug.LogWarning($"[{name}] No mesh named '{meshName}' in {model.name}; using a ball.");
                }
                return resolved = Ball();
            }
        }

        private void OnValidate() => resolved = null;

        private static Transform Find(Transform t, string n)
        {
            if (t.name == n) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var f = Find(t.GetChild(i), n);
                if (f != null) return f;
            }
            return null;
        }

        // Low-poly unit-diameter ball for pellets
        public static Mesh Ball()
        {
            if (ball != null) return ball;
            const int seg = 6, rings = 4;
            var verts = new Vector3[(seg + 1) * (rings + 1)];
            var uvs = new Vector2[verts.Length];
            int v = 0;
            for (int r = 0; r <= rings; r++)
            {
                float phi = Mathf.PI * r / rings;
                for (int s = 0; s <= seg; s++, v++)
                {
                    float th = 2f * Mathf.PI * s / seg;
                    verts[v] = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th)) * 0.5f;
                    uvs[v] = new Vector2((float)s / seg, (float)r / rings);
                }
            }
            var tris = new int[seg * rings * 6];
            int t = 0;
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    int a = r * (seg + 1) + s, b = a + seg + 1;
                    tris[t++] = a; tris[t++] = a + 1; tris[t++] = b;
                    tris[t++] = a + 1; tris[t++] = b + 1; tris[t++] = b;
                }
            ball = new Mesh { name = "RoundBall", vertices = verts, uv = uvs, triangles = tris };
            ball.RecalculateNormals();
            ball.RecalculateBounds();
            return ball;
        }
    }
}
