using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Combat.AI;
using Combat.Spawning;

namespace Combat.Encounters
{
    // A fight in a room: starts when the player walks into the zone, brings waves in through spawn doors,
    // keeps every spawned enemy fighting inside the zone, and fires Cleared when the last one dies.
    public class Encounter : MonoBehaviour
    {
        public enum State { Waiting, Running, Cleared }

        [Header("Zone")]
        [Tooltip("Room footprint, local to this object (rotation ignored). Spawned enemies fight inside it.")]
        [SerializeField] private Vector3 zoneCenter = new Vector3(0f, 2f, 0f);
        [SerializeField] private Vector3 zoneSize = new Vector3(20f, 4f, 20f);
        [SerializeField] private bool startOnEnter = true;

        [Header("Spawning")]
        [SerializeField] private List<SpawnDoor> doors = new List<SpawnDoor>();
        [SerializeField] private List<EncounterWave> waves = new List<EncounterWave>();
        [Tooltip("Empty = the first EnemySpawner in the scene.")]
        [SerializeField] private EnemySpawner spawner;

        [Header("Events")]
        public UnityEvent onStarted = new UnityEvent();
        public UnityEvent onCleared = new UnityEvent();

        private readonly HashSet<EnemyBrain> alive = new HashSet<EnemyBrain>();
        private int deploying;
        private int nextDoor;

        public State Current { get; private set; }
        // Last wave sent in (-1 before the first)
        public int WaveIndex { get; private set; } = -1;
        public int WaveCount => waves.Count;
        public List<SpawnDoor> Doors => doors;
        public List<EncounterWave> Waves => waves;

        public int Alive
        {
            get
            {
                alive.RemoveWhere(b => b == null || !b.isActiveAndEnabled);
                return alive.Count;
            }
        }

        public Bounds Zone => new Bounds(transform.position + zoneCenter, zoneSize);

        // Set up by the room generator
        public void Configure(Vector3 center, Vector3 size, List<SpawnDoor> roomDoors, List<EncounterWave> roomWaves)
        {
            zoneCenter = center;
            zoneSize = size;
            doors = roomDoors;
            waves = roomWaves;
        }

        private void OnEnable() => EnemyBrain.Killed += HandleKilled;
        private void OnDisable() => EnemyBrain.Killed -= HandleKilled;

        private void Update()
        {
            if (Current != State.Waiting || !startOnEnter) return;
            var player = AITargets.Player;
            if (player != null && PositionScorer.InZone(Zone, AITargets.FeetOf(player))) Begin();
        }

        public void Begin()
        {
            if (Current != State.Waiting) return;
            if (spawner == null) spawner = FindFirstObjectByType<EnemySpawner>();
            if (spawner == null) { Debug.LogError($"[Encounter] {name}: no EnemySpawner in the scene.", this); return; }
            Current = State.Running;
            onStarted.Invoke();
            StartCoroutine(Run());
        }

        // Back to waiting; enemies already spawned are left alone
        public void ResetEncounter()
        {
            StopAllCoroutines();
            foreach (var d in doors) if (d != null) d.Close();
            alive.Clear();
            deploying = 0;
            nextDoor = 0;
            WaveIndex = -1;
            Current = State.Waiting;
        }

        private IEnumerator Run()
        {
            for (int i = 0; i < waves.Count; i++)
            {
                yield return WaitFor(waves[i]);
                Deploy(i);
            }
            while (deploying > 0 || Alive > 0) yield return null;
            Current = State.Cleared;
            onCleared.Invoke();
        }

        private IEnumerator WaitFor(EncounterWave w)
        {
            if (w.start == WaveStart.AfterPreviousWave)
                while (deploying > 0) yield return null;
            else if (w.start == WaveStart.WhenPreviousDownTo)
                while (deploying > 0 || Alive > w.remaining) yield return null;
            if (w.delay > 0f) yield return new WaitForSeconds(w.delay);
        }

        private void Deploy(int index)
        {
            WaveIndex = index;
            var w = waves[index];
            var door = PickDoor(w);
            if (w.squad == null || door == null) return;
            deploying++;
            door.Deploy(w.squad, spawner, this, () => deploying--);
        }

        private SpawnDoor PickDoor(EncounterWave w)
        {
            if (doors.Count == 0) return null;
            if (w.door >= 0 && w.door < doors.Count && doors[w.door] != null) return doors[w.door];
            for (int i = 0; i < doors.Count; i++)
            {
                var d = doors[(nextDoor + i) % doors.Count];
                if (d == null) continue;
                nextDoor = (nextDoor + i + 1) % doors.Count;
                return d;
            }
            return null;
        }

        // A door spawned this enemy for us
        internal void Register(EnemyBrain b)
        {
            alive.Add(b);
            b.SetCombatZone(Zone);
        }

        internal bool Contains(Vector3 p) => PositionScorer.InZone(Zone, p);

        private void HandleKilled(EnemyBrain b) => alive.Remove(b);

        private void OnDrawGizmos()
        {
            var z = Zone;
            Gizmos.color = Current == State.Running ? new Color(1f, 0.3f, 0.2f, 0.9f) : new Color(1f, 0.6f, 0.1f, 0.6f);
            Gizmos.DrawWireCube(z.center, z.size);
        }
    }
}
