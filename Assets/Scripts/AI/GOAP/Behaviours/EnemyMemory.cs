using UnityEngine;
namespace GOAPGettingStarted.Behaviours
{
    public class EnemyMemory : MonoBehaviour
    {
        public Vector3 LastKnownPlayerPosition { get; set; }
        public bool HasLastKnownPosition { get; set; }
    }
}