using System.Collections.Generic;
using UnityEngine;
using Combat.Enemies;
using Combat.Spawning;

namespace Combat.AI
{
    public enum SquadRole { None, Leader, Suppressor, Flanker }

    // Enemies that fight together: they share sightings and split into roles.
    // The highest rank leads; losing the leader shakes the rest.
    public class EnemySquad
    {
        private const float ShareInterval = 0.3f;

        private readonly List<EnemyBrain> members = new List<EnemyBrain>();
        private float nextShare;

        public IReadOnlyList<EnemyBrain> Members => members;
        public EnemyBrain Leader { get; private set; }
        // Leads while alive (set by a squad asset); otherwise the highest rank leads
        public EnemyBrain PreferredLeader { get; private set; }
        public string Label { get; private set; } = "Squad";

        // Every live squad, for tools
        public static readonly List<EnemySquad> All = new List<EnemySquad>();
        public bool LeaderLost { get; private set; }

        // A squad from these brains; null when fewer than two
        public static EnemySquad Form(IEnumerable<EnemyBrain> brains, EnemyBrain preferredLeader = null, string label = null)
        {
            var squad = new EnemySquad { PreferredLeader = preferredLeader };
            if (!string.IsNullOrEmpty(label)) squad.Label = label;
            foreach (var b in brains)
                if (b != null && !squad.members.Contains(b)) { b.LeaveSquad(); squad.members.Add(b); }
            if (squad.members.Count < 2) return null;
            foreach (var b in squad.members) b.JoinSquad(squad);
            squad.AssignRoles();
            All.Add(squad);
            return squad;
        }

        // A squad from freshly spawned enemies
        public static EnemySquad Form(List<EnemyPoolHandle> handles)
        {
            var brains = new List<EnemyBrain>(handles.Count);
            foreach (var h in handles)
                if (h != null && h.GameObject != null) brains.Add(h.GameObject.GetComponent<EnemyBrain>());
            return Form(brains);
        }

        public void Remove(EnemyBrain b)
        {
            if (!members.Remove(b)) return;
            if (members.Count < 2)
            {
                // One left: it fights alone
                foreach (var m in members) m.ClearSquad();
                members.Clear();
                Leader = null;
                All.Remove(this);
                return;
            }
            AssignRoles();
        }

        // Highest rank leads; a third of the rest flank (one at least from three members), the others suppress
        private void AssignRoles()
        {
            Leader = PreferredLeader != null && members.Contains(PreferredLeader) ? PreferredLeader : null;
            if (Leader == null)
                foreach (var b in members)
                    if (Leader == null || (int)b.Rank > (int)Leader.Rank) Leader = b;

            int flankers = members.Count >= 3 ? Mathf.Max(1, (members.Count - 1) / 3) : 0;
            int side = 1;
            foreach (var b in members)
            {
                if (b == Leader) b.SetRole(SquadRole.Leader, 0);
                else if (flankers > 0) { b.SetRole(SquadRole.Flanker, side); side = -side; flankers--; }
                else b.SetRole(SquadRole.Suppressor, 0);
            }
        }

        // A member saw, heard or was hit by the target: everyone else engages toward that spot
        public void ShareContact(EnemyBrain from, Vector3 position)
        {
            if (Time.time < nextShare) return;
            nextShare = Time.time + ShareInterval;
            foreach (var m in members)
                if (m != from && m.Perception != null) m.Perception.ShareContact(position);
        }

        public void MemberDied(EnemyBrain b)
        {
            if (b != Leader || LeaderLost) return;
            LeaderLost = true;
            foreach (var m in members)
                if (m != b) m.OnLeaderLost();
        }

        // Average position of the members
        public Vector3 Center
        {
            get
            {
                if (members.Count == 0) return Vector3.zero;
                Vector3 sum = Vector3.zero;
                foreach (var m in members) sum += m.transform.position;
                return sum / members.Count;
            }
        }
    }
}
