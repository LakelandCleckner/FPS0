using UnityEngine;
using UnityEngine.AI;
using Combat.Enemies;
using GOAPGettingStarted.Behaviours;

// Temporary: prints what one enemy's movement and attack logic see, twice a second
public class EnemyDebugProbe : MonoBehaviour
{
    [SerializeField] private float interval = 0.5f;

    private NavMeshAgent nav;
    private AgentBrain brain;
    private EnemyMeleeAttack melee;
    private Transform player;
    private float next;

    private void Start()
    {
        nav = GetComponent<NavMeshAgent>();
        brain = GetComponent<AgentBrain>();
        melee = GetComponent<EnemyMeleeAttack>();
        var p = GameObject.FindWithTag("Player");
        player = p != null ? p.transform : null;
    }

    private void Update()
    {
        if (Time.time < next || player == null || nav == null) return;
        next = Time.time + interval;

        Vector3 to = player.position - transform.position; to.y = 0f;
        Vector3 toDest = nav.destination - player.position; toDest.y = 0f;
        float angle = Vector3.Angle(new Vector3(transform.forward.x, 0f, transform.forward.z), to);

        Debug.Log($"[Probe] {name} state={(brain != null ? brain.State.ToString() : "-")} " +
                  $"dist={to.magnitude:F2} range={(melee != null ? melee.Range : -1f):F2} angle={angle:F0} " +
                  $"swing={(melee != null && melee.IsSwinging)} cd={(melee != null && melee.IsCoolingDown)} " +
                  $"speed={nav.velocity.magnitude:F2} navSpeed={nav.speed:F2} stopDist={nav.stoppingDistance:F2} " +
                  $"destFromPlayer={toDest.magnitude:F2} remaining={nav.remainingDistance:F2} " +
                  $"stopped={nav.isStopped} pending={nav.pathPending} status={nav.pathStatus} " +
                  $"updRot={nav.updateRotation} prio={nav.avoidancePriority}", this);
    }
}