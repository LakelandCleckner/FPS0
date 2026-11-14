using UnityEngine;
using Combat.AI;

namespace Combat.Encounters
{
    public enum WaveStart
    {
        // With the encounter (plus delay)
        OnEncounterStart,
        // Once the wave before has finished filing in (plus delay)
        AfterPreviousWave,
        // Once enemies left from earlier waves drop to Remaining (plus delay)
        WhenPreviousDownTo
    }

    // One squad brought in through a door.
    [System.Serializable]
    public class EncounterWave
    {
        public EnemySquadSO squad;
        [Tooltip("Door index in the encounter's list; -1 = next door in turn.")]
        public int door = -1;
        public WaveStart start = WaveStart.WhenPreviousDownTo;
        [Tooltip("WhenPreviousDownTo: enemies left from earlier waves.")]
        [Min(0)] public int remaining = 1;
        [Tooltip("Seconds after the start condition.")]
        [Min(0f)] public float delay = 1.5f;
    }
}
