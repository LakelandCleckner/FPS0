using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Combat.AI;
using Combat.Spawning;

namespace Combat.Encounters
{
    // A door enemies come in through: opens, spawns a squad behind it out of sight, walks them in one by one
    // to the entry point, then closes once they're all out in the room. While closed the leaf carves the NavMesh,
    // so nothing can path back into the spawn room.
    public class SpawnDoor : MonoBehaviour
    {
        [SerializeField] private Transform spawnPoint;
        [SerializeField] private Transform entryPoint;
        [Tooltip("Moving door leaf; slides by Open Height to open (negative = down into the floor). Optional.")]
        [SerializeField] private Transform door;
        [SerializeField] private float openHeight = 3.5f;
        [SerializeField] private float doorSpeed = 5f;
        [Tooltip("Seconds between members coming through.")]
        [SerializeField] private float fileInterval = 0.5f;
        [Tooltip("Longest a member walks toward the entry point once inside the room before it's left to fight (s).")]
        [SerializeField] private float entryTimeout = 8f;
        [SerializeField] private float closeDelay = 1.5f;

        private const float OpenLead = 0.4f;
        private const float EntryArrive = 0.8f;
        private const float EntrySpacing = 1.6f;
        private const float SpawnSpacing = 0.7f;
        // A member counts as through once this far into the room from the door
        private const float ClearOfDoor = 2f;

        private Vector3 closedPos;
        private NavMeshObstacle blocker;
        private bool open;
        private int walking;

        public Transform SpawnPoint => spawnPoint;
        public Transform EntryPoint => entryPoint;

        // Set up by the room generator
        public void Configure(Transform spawn, Transform entry, Transform doorLeaf, float leafTravel)
        {
            spawnPoint = spawn;
            entryPoint = entry;
            door = doorLeaf;
            openHeight = leafTravel;
            if (door != null) closedPos = door.localPosition;
        }

        private void Awake()
        {
            if (door == null) return;
            closedPos = door.localPosition;
            blocker = door.GetComponent<NavMeshObstacle>();
            if (blocker == null) blocker = door.gameObject.AddComponent<NavMeshObstacle>();
            blocker.shape = NavMeshObstacleShape.Box;
            var box = door.GetComponent<BoxCollider>();
            blocker.center = box != null ? box.center : Vector3.zero;
            blocker.size = box != null ? box.size : Vector3.one;
            blocker.carving = true;
            blocker.carveOnlyStationary = false;
        }

        private void Update()
        {
            if (door == null) return;
            Vector3 target = closedPos + (open ? Vector3.up * openHeight : Vector3.zero);
            door.localPosition = Vector3.MoveTowards(door.localPosition, target, doorSpeed * Time.deltaTime);
            // Carve only once fully shut; opening frees the doorway straight away
            if (blocker != null) blocker.enabled = !open && (door.localPosition - closedPos).sqrMagnitude < 0.0001f;
        }

        public void Close() { StopAllCoroutines(); open = false; walking = 0; }

        public void Deploy(EnemySquadSO squad, EnemySpawner spawner, Encounter encounter, Action done)
            => StartCoroutine(DeployRoutine(squad, spawner, encounter, done));

        private IEnumerator DeployRoutine(EnemySquadSO squad, EnemySpawner spawner, Encounter encounter, Action done)
        {
            open = true;
            yield return new WaitForSeconds(OpenLead);

            var brains = new List<EnemyBrain>();
            EnemyBrain leader = null;
            Quaternion facing = Facing();
            int k = 0;
            foreach (var m in squad.members)
            {
                if (!m.IsSet) continue;
                var def = m.Def;
                for (int i = 0; i < Mathf.Max(1, m.count); i++)
                {
                    var h = spawner.Spawn(def, SpawnSpot(k), facing);
                    if (h == null) continue;
                    var b = h.GameObject.GetComponent<EnemyBrain>();
                    if (b != null)
                    {
                        brains.Add(b);
                        if (m.leader && leader == null) leader = b;
                        encounter.Register(b);
                        StartCoroutine(WalkIn(b, EntrySpot(k), encounter));
                    }
                    k++;
                    yield return new WaitForSeconds(fileInterval);
                }
            }

            EnemySquad.Form(brains, leader, squad.Label);
            done?.Invoke();

            yield return new WaitForSeconds(closeDelay);
            while (walking > 0) yield return null;
            open = false;
        }

        // Steer to the entry point; never let go before the member is out in the room. Once through, stops at the
        // entry point, on sight of the target, or when out of time.
        private IEnumerator WalkIn(EnemyBrain b, Vector3 spot, Encounter encounter)
        {
            walking++;
            var mover = b.GetComponent<EnemyMover>();
            var perception = b.Perception;
            if (mover != null) mover.RequestPoint(this, spot);
            float until = Time.time + entryTimeout;
            while (mover != null && b != null && b.isActiveAndEnabled)
            {
                if (Through(b.transform.position, encounter)
                    && (mover.AtPoint(spot, EntryArrive) || Time.time >= until || (perception != null && perception.TargetVisible)))
                    break;
                yield return null;
            }
            if (mover != null) mover.ReleasePoint(this);
            walking--;
        }

        private bool Through(Vector3 p, Encounter encounter)
        {
            Vector3 off = p - transform.position;
            off.y = 0f;
            return encounter.Contains(p) && off.sqrMagnitude >= ClearOfDoor * ClearOfDoor;
        }

        private Quaternion Facing()
        {
            if (spawnPoint == null || entryPoint == null) return transform.rotation;
            Vector3 d = entryPoint.position - spawnPoint.position;
            d.y = 0f;
            return d.sqrMagnitude > 0.01f ? Quaternion.LookRotation(d) : transform.rotation;
        }

        // Two abreast behind the door
        private Vector3 SpawnSpot(int k)
        {
            Transform t = spawnPoint != null ? spawnPoint : transform;
            Vector3 p = t.position + t.right * ((k % 2) * 2 - 1) * SpawnSpacing * 0.5f;
            return NavMesh.SamplePosition(p, out var hit, 2f, NavMesh.AllAreas) ? hit.position : t.position;
        }

        // Rows of three just inside the room
        private Vector3 EntrySpot(int k)
        {
            Transform t = entryPoint != null ? entryPoint : transform;
            Vector3 p = t.position + t.right * ((k % 3) - 1) * EntrySpacing + t.forward * (k / 3) * EntrySpacing;
            return NavMesh.SamplePosition(p, out var hit, 2f, NavMesh.AllAreas) ? hit.position : t.position;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.9f);
            if (spawnPoint != null) Gizmos.DrawWireSphere(spawnPoint.position, 0.5f);
            if (entryPoint != null)
            {
                Gizmos.DrawSphere(entryPoint.position, 0.3f);
                if (spawnPoint != null) Gizmos.DrawLine(spawnPoint.position, entryPoint.position);
            }
        }
    }
}
