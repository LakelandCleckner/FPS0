using System.Collections.Generic;
using UnityEngine;
using Combat.AI;

namespace Combat.Encounters
{
    // How procedural encounter rooms are built: size, doors, cover, pickups and the wave budget.
    [CreateAssetMenu(fileName = "RoomSettings", menuName = "Combat/Encounters/Room Settings")]
    public class EncounterRoomSettingsSO : ScriptableObject
    {
        [Header("Room")]
        public Vector2 widthRange = new Vector2(24f, 34f);
        public Vector2 depthRange = new Vector2(24f, 34f);
        public float wallHeight = 5f;
        public float wallThickness = 0.5f;
        [Tooltip("Player opening in the south wall, no door.")]
        public float entranceWidth = 3f;

        [Header("Spawn doors")]
        [Range(1, 4)] public int minDoors = 2;
        [Range(1, 4)] public int maxDoors = 3;
        public float doorWidth = 3f;
        public float doorHeight = 3.2f;
        [Tooltip("Depth of the hidden alcove behind each door.")]
        public float alcoveDepth = 4f;

        [Header("Cover")]
        [Tooltip("Cover pieces per 100 m2 of floor.")]
        public float coverDensity = 2.2f;
        [Range(0f, 1f)] public float halfCoverChance = 0.6f;
        public float halfHeight = 1.1f;
        public float fullHeight = 2.4f;
        public Vector2 coverLength = new Vector2(1.2f, 3f);
        public float coverDepth = 1f;
        [Tooltip("Gap kept between cover pieces (m).")]
        public float coverSpacing = 2.5f;
        [Tooltip("Clear radius inside doors and the entrance (m).")]
        public float doorClearance = 4.5f;
        public float wallClearance = 1.5f;

        [Header("Pickups")]
        public GameObject ammoPrefab;
        [Min(0)] public int ammoCount = 2;

        [Header("Waves")]
        [Tooltip("Squads waves are picked from.")]
        public List<EnemySquadSO> squadPool = new List<EnemySquadSO>();
        [Tooltip("Total squad cost across all waves (Minor 1, Major 2, Elite 3, Miniboss 5 per member).")]
        [Min(1)] public int budget = 24;
        public Vector2Int waveCount = new Vector2Int(2, 4);
        [Tooltip("Later waves come in when this many enemies are left.")]
        [Min(0)] public int remainingForNextWave = 1;
        public float waveDelay = 2f;

        [Header("Look (optional)")]
        public Material floorMaterial;
        public Material wallMaterial;
        public Material coverMaterial;
        public Material doorMaterial;
    }
}
