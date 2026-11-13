using UnityEngine;
using Combat.Spawning;

namespace Combat.AI
{
    // Squad in the scene: spawns the squad asset here on start, or, with none set, makes every enemy under this object one squad.
    public class EnemySquadGroup : MonoBehaviour
    {
        [Tooltip("Spawned at this object on start. Empty: the enemies under this object form the squad.")]
        [SerializeField] private EnemySquadSO squad;
        [Tooltip("Empty = the first EnemySpawner in the scene.")]
        [SerializeField] private EnemySpawner spawner;

        public EnemySquadSO SquadAsset => squad;

        private void Start()
        {
            if (squad == null)
            {
                EnemySquad.Form(GetComponentsInChildren<EnemyBrain>());
                return;
            }
            if (spawner == null) spawner = FindFirstObjectByType<EnemySpawner>();
            if (spawner == null) { Debug.LogError($"[EnemySquadGroup] {name}: no EnemySpawner in the scene.", this); return; }
            spawner.SpawnSquad(squad, transform.position);
        }

        private void OnDrawGizmos()
        {
            if (squad == null) return;
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, Mathf.Max(0.5f, squad.spread));
        }
    }
}
