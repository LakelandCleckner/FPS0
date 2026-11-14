using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using Combat.AI;

namespace Combat.Encounters
{
    // Builds a seeded blockout encounter room: floor, walls with a player entrance and spawn doors with hidden
    // alcoves, half and full cover, ammo, an Encounter wired to the doors, and waves picked from the budget.
    public static class EncounterRoomGenerator
    {
        private const float FloorThickness = 0.2f;
        private const float FloorTop = 0.05f;
        private const float DoorLeafThickness = 0.2f;
        private const float EntryInset = 3f;
        private const float DoorEdgeMargin = 2.5f;
        private const float OpeningGap = 2f;
        private const int PlaceAttempts = 30;

        // Wall sides: north, east, south (entrance), west
        private enum Side { North, East, South, West }

        private struct Opening
        {
            public float Along;   // from the wall's start corner
            public float Width;
            public float Height;
            public bool Door;
        }

        private struct Blocker
        {
            public Vector3 Pos;
            public float Radius;
        }

        public static GameObject Generate(EncounterRoomSettingsSO s, int seed, Vector3 origin, Transform parent = null)
        {
            var rng = new System.Random(seed);
            float w = Range(rng, s.widthRange);
            float d = Range(rng, s.depthRange);

            var root = new GameObject($"Encounter Room {seed}");
            root.transform.SetParent(parent, false);
            root.transform.position = origin;
            var room = root.AddComponent<EncounterRoom>();
            room.settings = s;
            room.seed = seed;

            var geo = new GameObject("Geometry").transform;
            geo.SetParent(root.transform, false);

            // Floor reaches under the alcoves
            float pad = (s.wallThickness + s.alcoveDepth + s.wallThickness) * 2f;
            var floor = Box(geo, "Floor", new Vector3(0f, FloorTop - FloorThickness * 0.5f, 0f), new Vector3(w + pad, FloorThickness, d + pad), Quaternion.identity, s.floorMaterial);
            int ground = LayerMask.NameToLayer("Ground");
            if (ground >= 0) floor.layer = ground;

            // Openings: the entrance mid-south, doors spread over the other walls
            var openings = new Dictionary<Side, List<Opening>>();
            foreach (Side side in System.Enum.GetValues(typeof(Side))) openings[side] = new List<Opening>();
            openings[Side.South].Add(new Opening { Along = WallLength(Side.South, w, d) * 0.5f, Width = s.entranceWidth, Height = s.doorHeight });

            int doorCount = rng.Next(Mathf.Min(s.minDoors, s.maxDoors), Mathf.Max(s.minDoors, s.maxDoors) + 1);
            var sides = new List<Side> { Side.North, Side.East, Side.West };
            Shuffle(sides, rng);
            for (int i = 0; i < doorCount; i++)
            {
                Side side = i < sides.Count ? sides[i] : (Side)rng.Next(4);
                if (TryPlaceOpening(rng, openings[side], WallLength(side, w, d), s.doorWidth, out float along))
                    openings[side].Add(new Opening { Along = along, Width = s.doorWidth, Height = s.doorHeight, Door = true });
            }

            var blockers = new List<Blocker>();
            var doors = new List<SpawnDoor>();
            foreach (Side side in System.Enum.GetValues(typeof(Side)))
            {
                SideFrame(side, w, d, out Vector3 a, out Vector3 dir, out Vector3 outward);
                var list = openings[side];
                list.Sort((x, y) => x.Along.CompareTo(y.Along));
                BuildWall(geo, s, a, dir, outward, WallLength(side, w, d), list);

                foreach (var o in list)
                {
                    Vector3 inner = a + dir * o.Along;
                    blockers.Add(new Blocker { Pos = inner - outward * (s.doorClearance * 0.5f), Radius = s.doorClearance });
                    if (o.Door) doors.Add(BuildDoor(root.transform, geo, s, inner, dir, outward, doors.Count));
                }
            }

            PlaceCover(geo, s, rng, w, d, blockers);
            PlaceAmmo(root.transform, s, rng, w, d, openings);

            var encounter = root.AddComponent<Encounter>();
            encounter.Configure(new Vector3(0f, s.wallHeight * 0.5f, 0f), new Vector3(w - 0.5f, s.wallHeight, d - 0.5f), doors, GenerateWaves(s, rng, doors.Count));
            return root;
        }

        // ---- waves ----

        // Splits the budget over the waves, heavier later; each wave is one or two squads that fit its share
        public static List<EncounterWave> GenerateWaves(EncounterRoomSettingsSO s, System.Random rng, int doorCount)
        {
            var waves = new List<EncounterWave>();
            var pool = s.squadPool.FindAll(q => q != null && q.Cost > 0);
            if (pool.Count == 0 || doorCount == 0) return waves;
            pool.Sort((x, y) => x.Cost.CompareTo(y.Cost));

            int count = rng.Next(Mathf.Max(1, s.waveCount.x), Mathf.Max(s.waveCount.x, s.waveCount.y) + 1);
            int weightSum = count * (count + 1) / 2;
            for (int i = 0; i < count; i++)
            {
                int share = Mathf.Max(pool[0].Cost, s.budget * (i + 1) / weightSum);
                for (int n = 0; n < 2 && share >= pool[0].Cost; n++)
                {
                    var squad = PickSquad(pool, share, rng);
                    share -= squad.Cost;
                    bool first = n == 0;
                    waves.Add(new EncounterWave
                    {
                        squad = squad,
                        door = -1,
                        start = !first ? WaveStart.AfterPreviousWave : i == 0 ? WaveStart.OnEncounterStart : WaveStart.WhenPreviousDownTo,
                        remaining = s.remainingForNextWave,
                        delay = !first ? 0f : i == 0 ? 1f : s.waveDelay
                    });
                }
            }
            return waves;
        }

        // A random squad that fits; the cheapest when none do
        private static EnemySquadSO PickSquad(List<EnemySquadSO> pool, int share, System.Random rng)
        {
            var fits = pool.FindAll(q => q.Cost <= share);
            return fits.Count > 0 ? fits[rng.Next(fits.Count)] : pool[0];
        }

        // ---- walls and doors ----

        private static float WallLength(Side side, float w, float d) => side == Side.North || side == Side.South ? w : d;

        // Start corner, direction along the wall, and outward normal
        private static void SideFrame(Side side, float w, float d, out Vector3 a, out Vector3 dir, out Vector3 outward)
        {
            switch (side)
            {
                case Side.North: a = new Vector3(-w / 2f, 0f, d / 2f); dir = Vector3.right; outward = Vector3.forward; break;
                case Side.East: a = new Vector3(w / 2f, 0f, d / 2f); dir = Vector3.back; outward = Vector3.right; break;
                case Side.South: a = new Vector3(w / 2f, 0f, -d / 2f); dir = Vector3.left; outward = Vector3.back; break;
                default: a = new Vector3(-w / 2f, 0f, -d / 2f); dir = Vector3.forward; outward = Vector3.left; break;
            }
        }

        private static bool TryPlaceOpening(System.Random rng, List<Opening> existing, float length, float width, out float along)
        {
            float min = width * 0.5f + DoorEdgeMargin, max = length - width * 0.5f - DoorEdgeMargin;
            along = 0f;
            if (max <= min) return false;
            for (int t = 0; t < PlaceAttempts; t++)
            {
                along = Mathf.Lerp(min, max, (float)rng.NextDouble());
                bool clear = true;
                foreach (var o in existing)
                    if (Mathf.Abs(o.Along - along) < (o.Width + width) * 0.5f + OpeningGap) { clear = false; break; }
                if (clear) return true;
            }
            return false;
        }

        // Solid runs between openings, with a lintel over each
        private static void BuildWall(Transform geo, EncounterRoomSettingsSO s, Vector3 a, Vector3 dir, Vector3 outward, float length, List<Opening> openings)
        {
            float t = s.wallThickness;
            Quaternion rot = Quaternion.LookRotation(outward);
            Vector3 offset = outward * (t * 0.5f);
            float cursor = -t;
            foreach (var o in openings)
            {
                float start = o.Along - o.Width * 0.5f;
                Segment(geo, s, a, dir, offset, rot, cursor, start, 0f, s.wallHeight);
                Segment(geo, s, a, dir, offset, rot, start, start + o.Width, o.Height, s.wallHeight);
                cursor = start + o.Width;
            }
            Segment(geo, s, a, dir, offset, rot, cursor, length + t, 0f, s.wallHeight);
        }

        private static void Segment(Transform geo, EncounterRoomSettingsSO s, Vector3 a, Vector3 dir, Vector3 offset, Quaternion rot,
                                    float from, float to, float bottom, float top)
        {
            if (to - from < 0.01f || top - bottom < 0.01f) return;
            Vector3 c = a + dir * ((from + to) * 0.5f) + offset + Vector3.up * (FloorTop + (bottom + top) * 0.5f);
            Box(geo, "Wall", c, new Vector3(to - from, top - bottom, s.wallThickness), rot, s.wallMaterial);
        }

        // Alcove walls behind the opening, a sliding leaf in it, and the SpawnDoor with spawn and entry points
        private static SpawnDoor BuildDoor(Transform root, Transform geo, EncounterRoomSettingsSO s, Vector3 inner, Vector3 dir, Vector3 outward, int index)
        {
            float t = s.wallThickness;
            Quaternion rot = Quaternion.LookRotation(outward);
            float h = s.wallHeight;
            Vector3 up = Vector3.up * (FloorTop + h * 0.5f);

            for (int sign = -1; sign <= 1; sign += 2)
            {
                Vector3 c = inner + outward * (t + s.alcoveDepth * 0.5f) + dir * sign * (s.doorWidth * 0.5f + t * 0.5f) + up;
                Box(geo, "Alcove", c, new Vector3(t, h, s.alcoveDepth), rot, s.wallMaterial);
            }
            Box(geo, "Alcove", inner + outward * (t + s.alcoveDepth + t * 0.5f) + up, new Vector3(s.doorWidth + t * 2f, h, t), rot, s.wallMaterial);

            var doorObj = new GameObject($"Spawn Door {index}");
            doorObj.transform.SetParent(root, false);
            doorObj.transform.localPosition = inner;
            doorObj.transform.localRotation = Quaternion.LookRotation(-outward);

            var leaf = Box(doorObj.transform, "Leaf", Vector3.zero, new Vector3(s.doorWidth, s.doorHeight, DoorLeafThickness), Quaternion.identity, s.doorMaterial);
            leaf.transform.position = root.TransformPoint(inner - outward * (-t * 0.5f) + Vector3.up * (FloorTop + s.doorHeight * 0.5f));
            leaf.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

            var spawn = new GameObject("Spawn Point").transform;
            spawn.SetParent(doorObj.transform, false);
            spawn.position = root.TransformPoint(inner + outward * (t + s.alcoveDepth * 0.55f));
            spawn.rotation = doorObj.transform.rotation;

            var entry = new GameObject("Entry Point").transform;
            entry.SetParent(doorObj.transform, false);
            entry.position = root.TransformPoint(inner - outward * EntryInset);
            entry.rotation = doorObj.transform.rotation;

            var door = doorObj.AddComponent<SpawnDoor>();
            // Sinks into the floor to open, so it never shows above the wall
            door.Configure(spawn, entry, leaf.transform, -(s.doorHeight + 0.1f));
            return door;
        }

        // ---- cover and pickups ----

        private static void PlaceCover(Transform geo, EncounterRoomSettingsSO s, System.Random rng, float w, float d, List<Blocker> blockers)
        {
            int count = Mathf.RoundToInt(s.coverDensity * w * d / 100f);
            float hx = w * 0.5f - s.wallClearance, hz = d * 0.5f - s.wallClearance;
            for (int i = 0; i < count; i++)
            {
                for (int t = 0; t < PlaceAttempts; t++)
                {
                    float len = Range(rng, s.coverLength);
                    float radius = Mathf.Max(len, s.coverDepth) * 0.5f;
                    var p = new Vector3(Mathf.Lerp(-hx + radius, hx - radius, (float)rng.NextDouble()), 0f,
                                        Mathf.Lerp(-hz + radius, hz - radius, (float)rng.NextDouble()));
                    if (!Clear(blockers, p, radius + s.coverSpacing * 0.5f)) continue;

                    bool half = rng.NextDouble() < s.halfCoverChance;
                    float height = half ? s.halfHeight : s.fullHeight;
                    var rot = Quaternion.Euler(0f, rng.Next(2) * 90f, 0f);
                    Box(geo, half ? "Cover Half" : "Cover Full", p + Vector3.up * (FloorTop + height * 0.5f),
                        new Vector3(len, height, s.coverDepth), rot, s.coverMaterial);
                    blockers.Add(new Blocker { Pos = p, Radius = radius + s.coverSpacing * 0.5f });
                    break;
                }
            }
        }

        // Against the walls, clear of every opening
        private static void PlaceAmmo(Transform root, EncounterRoomSettingsSO s, System.Random rng, float w, float d, Dictionary<Side, List<Opening>> openings)
        {
            if (s.ammoPrefab == null || s.ammoCount <= 0) return;
            var placed = new List<Vector3>();
            for (int i = 0; i < s.ammoCount; i++)
            {
                for (int t = 0; t < PlaceAttempts; t++)
                {
                    var side = (Side)rng.Next(4);
                    SideFrame(side, w, d, out Vector3 a, out Vector3 dir, out Vector3 outward);
                    float length = WallLength(side, w, d);
                    float along = Mathf.Lerp(1.5f, length - 1.5f, (float)rng.NextDouble());
                    bool clear = true;
                    foreach (var o in openings[side])
                        if (Mathf.Abs(o.Along - along) < o.Width * 0.5f + s.doorClearance) { clear = false; break; }
                    Vector3 p = a + dir * along - outward * 1f;
                    foreach (var q in placed)
                        if ((q - p).sqrMagnitude < 9f) { clear = false; break; }
                    if (!clear) continue;

                    var ammo = Object.Instantiate(s.ammoPrefab, root);
                    ammo.transform.localPosition = p + Vector3.up * FloorTop;
                    ammo.transform.localRotation = Quaternion.LookRotation(-outward);
                    // Pickups stay out of the NavMesh bake
                    ammo.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
                    placed.Add(p);
                    break;
                }
            }
        }

        private static bool Clear(List<Blocker> blockers, Vector3 p, float radius)
        {
            foreach (var b in blockers)
            {
                Vector3 d = b.Pos - p;
                d.y = 0f;
                if (d.magnitude < b.Radius + radius) return false;
            }
            return true;
        }

        // ---- helpers ----

        private static GameObject Box(Transform parent, string name, Vector3 localPos, Vector3 size, Quaternion localRot, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = size;
            if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        private static float Range(System.Random rng, Vector2 r) => Mathf.Lerp(r.x, r.y, (float)rng.NextDouble());

        private static void Shuffle<T>(List<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
