using System;
using System.Collections.Generic;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal static class BayDesign
    {
        public const int EMPTY = 0, STRUCTURE = 1, DOCK = 2, CARRIER = 3, BLUEPRINT = 4;

        public static List<List<T>> Sections<T>(IEnumerable<T> seeds, Func<T, IEnumerable<T>> neighbours, Func<T, bool> touchesCarrier)
        {
            var sections = new List<List<T>>();
            var seen = new HashSet<T>();
            foreach (T seed in seeds)
            {
                if (seed == null || !seen.Add(seed)) continue;
                var section = new List<T>();
                bool carrier = false;
                var queue = new Queue<T>();
                queue.Enqueue(seed);
                while (queue.Count > 0)
                {
                    T current = queue.Dequeue();
                    section.Add(current);
                    if (!carrier && touchesCarrier(current)) carrier = true;
                    foreach (T next in neighbours(current))
                        if (next != null && seen.Add(next)) queue.Enqueue(next);
                }
                if (!carrier) sections.Add(section);
            }
            return sections;
        }

        public static bool CanBuild(int blueprintPieces, int storedParts) => blueprintPieces <= 1 && storedParts > 0;

        public static bool StationsBuild(int constructionMode) => constructionMode != 0;

        public static bool EditsLocked(bool buildOn) => buildOn;

        public static bool TellDue(bool toldBefore, int lastMs, int nowMs, int repeatMs) => !toldBefore || unchecked(nowMs - lastMs) >= repeatMs;

        public const int CAPTURE_STORED = 0, CAPTURE_REPLACED = 1, CAPTURE_WARN = 2;

        public static int CaptureKind(int before, int now) =>
            before <= 0 ? CAPTURE_STORED : now < before || now <= 3 ? CAPTURE_WARN : CAPTURE_REPLACED;

        public static bool ShouldCapture(int blueprintPieces, int blueprints, bool holdingCopy) =>
            blueprintPieces == 1 && blueprints > 0 && !holdingCopy;

        public static HashSet<T> CarrierWork<T>(IEnumerable<T> frameAdjacent, Func<T, IEnumerable<T>> neighbours, Func<T, bool> carrierKind)
        {
            var work = new HashSet<T>();
            var queue = new Queue<T>();
            foreach (T t in frameAdjacent)
                if (t != null && carrierKind(t) && work.Add(t)) queue.Enqueue(t);
            while (queue.Count > 0)
            {
                T current = queue.Dequeue();
                foreach (T next in neighbours(current))
                    if (next != null && carrierKind(next) && work.Add(next)) queue.Enqueue(next);
            }
            return work;
        }

        public static bool FacesBack(int ax, int ay, int bx, int by) => (ax != 0 || ay != 0) && ax == -bx && ay == -by;

        public static bool IsDockMateId(string id) => id == "ztx.hinges_pistons_docking.dock_clamp";

        public static bool BlueprintsStale(int done, int doorsDone, int blueprintedDone, int blueprintedDoors, bool sameShip) =>
            !sameShip || done != blueprintedDone || doorsDone != blueprintedDoors;

        public static bool TouchesAny(IntRect rect, HashSet<IntVector2> cells)
        {
            if (cells == null || cells.Count == 0) return false;
            for (int y = rect.Y; y < rect.Y + rect.Height; y++)
                for (int x = rect.X; x < rect.X + rect.Width; x++)
                {
                    if (cells.Contains(new IntVector2(x + 1, y)) || cells.Contains(new IntVector2(x - 1, y)) ||
                        cells.Contains(new IntVector2(x, y + 1)) || cells.Contains(new IntVector2(x, y - 1))) return true;
                }
            return false;
        }

        public static int[] Classify(int[] kinds, int width, out int pieces)
        {
            pieces = 0;
            if (kinds == null || width <= 0) return new int[0];
            int height = kinds.Length / width;
            IEnumerable<int> Around(int i)
            {
                int x = i % width, y = i / width;
                if (x > 0) yield return i - 1;
                if (x < width - 1) yield return i + 1;
                if (y > 0) yield return i - width;
                if (y < height - 1) yield return i + width;
            }
            var seeds = new List<int>();
            for (int i = 0; i < kinds.Length; i++)
            {
                if (kinds[i] != BLUEPRINT) continue;
                foreach (int n in Around(i))
                    if (kinds[n] == STRUCTURE) { seeds.Add(i); break; }
            }
            List<List<int>> sections = Sections(seeds,
                i => FilterBlueprints(Around(i), kinds),
                i => { foreach (int n in Around(i)) if (kinds[n] == CARRIER) return true; return false; });
            pieces = sections.Count;
            var cells = new List<int>();
            foreach (List<int> s in sections) cells.AddRange(s);
            cells.Sort();
            return cells.ToArray();
        }

        private static IEnumerable<int> FilterBlueprints(IEnumerable<int> cells, int[] kinds)
        {
            foreach (int c in cells)
                if (kinds[c] == BLUEPRINT) yield return c;
        }
    }

    internal static class BayDesignCodec
    {
        private const uint MAGIC = 0x3144425A;

        public sealed class ToggleRec
        {
            public string Id;
            public int Value;

            public ToggleRec(string id, int value)
            {
                Id = id;
                Value = value;
            }

            public override string ToString() => Id + "=" + Value;
        }

        public sealed class PartRec
        {
            public string Id;
            public int X;
            public int Y;
            public int Rotation;
            public bool FlipX;
            public List<ToggleRec> Toggles = new List<ToggleRec>();
        }

        public sealed class DoorRec
        {
            public string Id;
            public int X;
            public int Y;
            public int Orientation;
        }

        public static byte[] Encode(List<PartRec> parts, List<DoorRec> doors)
        {
            using (var ms = new System.IO.MemoryStream())
            using (var w = new System.IO.BinaryWriter(ms, System.Text.Encoding.UTF8))
            {
                w.Write(MAGIC);
                w.Write(parts?.Count ?? 0);
                if (parts != null)
                    foreach (PartRec p in parts)
                    {
                        w.Write(p.Id ?? "");
                        w.Write(p.X);
                        w.Write(p.Y);
                        w.Write((byte)p.Rotation);
                        w.Write(p.FlipX);
                        w.Write(p.Toggles?.Count ?? 0);
                        if (p.Toggles != null)
                            foreach (ToggleRec t in p.Toggles)
                            {
                                w.Write(t.Id ?? "");
                                w.Write(t.Value);
                            }
                    }
                w.Write(doors?.Count ?? 0);
                if (doors != null)
                    foreach (DoorRec d in doors)
                    {
                        w.Write(d.Id ?? "");
                        w.Write(d.X);
                        w.Write(d.Y);
                        w.Write(d.Orientation);
                    }
                w.Flush();
                return ms.ToArray();
            }
        }

        public static bool Decode(byte[] bytes, out List<PartRec> parts, out List<DoorRec> doors)
        {
            parts = new List<PartRec>();
            doors = new List<DoorRec>();
            if (bytes == null || bytes.Length < 12) return false;
            try
            {
                using (var ms = new System.IO.MemoryStream(bytes))
                using (var r = new System.IO.BinaryReader(ms, System.Text.Encoding.UTF8))
                {
                    if (r.ReadUInt32() != MAGIC) return false;
                    int n = r.ReadInt32();
                    if (n < 0 || n > 100000) return false;
                    for (int i = 0; i < n; i++)
                    {
                        var p = new PartRec { Id = r.ReadString(), X = r.ReadInt32(), Y = r.ReadInt32(), Rotation = r.ReadByte(), FlipX = r.ReadBoolean() };
                        int tn = r.ReadInt32();
                        if (tn < 0 || tn > 10000) return false;
                        for (int j = 0; j < tn; j++) p.Toggles.Add(new ToggleRec(r.ReadString(), r.ReadInt32()));
                        parts.Add(p);
                    }
                    int dn = r.ReadInt32();
                    if (dn < 0 || dn > 100000) return false;
                    for (int i = 0; i < dn; i++)
                        doors.Add(new DoorRec { Id = r.ReadString(), X = r.ReadInt32(), Y = r.ReadInt32(), Orientation = r.ReadInt32() });
                    return true;
                }
            }
            catch (System.IO.EndOfStreamException) { return false; }
        }
    }

    internal static class BayRelease
    {
        public const int BUILT = 0, CREWED = 1, SUPPLIED = 2, REPAIRED = 3, CHARGED = 4, CONDITIONS = 5;

        public static bool[] Facts(bool built, bool crewed, bool supplied, bool repaired, bool charged) =>
            new[] { built, built && crewed, built && supplied, built && repaired, built && charged };

        public static int FlightDirectionOf(int mode) => mode >= 0 && mode < 8 ? (mode + 1) % 8 : 1;

        public static bool BoardingBlocks(bool boardMode, int homed, int aboard) => boardMode && aboard < homed;

        public static bool ShouldRelease(bool releaseOn, bool[] ticked, bool[] facts)
        {
            if (!releaseOn) return false;
            if (ticked == null) return true;
            for (int i = 0; i < ticked.Length; i++)
                if (ticked[i] && (facts == null || i >= facts.Length || !facts[i])) return false;
            return true;
        }
    }

    internal static class BayStation
    {
        public static int PickInReach(int[] states, float[] distances, bool[] taken, float reach)
        {
            if (states == null || distances == null) return -1;
            int best = -1;
            for (int pass = 0; pass < 2 && best < 0; pass++)
            {
                int want = pass == 0 ? (int)PipelineState.Building : (int)PipelineState.Spawnable;
                float bestD = float.MaxValue;
                for (int i = 0; i < states.Length && i < distances.Length; i++)
                {
                    if (states[i] != want || distances[i] > reach) continue;
                    if (taken != null && i < taken.Length && taken[i]) continue;
                    if (distances[i] < bestD) { bestD = distances[i]; best = i; }
                }
            }
            return best;
        }

        public static int PickOrHelp(int[] states, float[] distances, bool[] taken, float reach)
        {
            int pick = PickInReach(states, distances, taken, reach);
            if (pick >= 0 || states == null || distances == null) return pick;
            float bestD = float.MaxValue;
            for (int i = 0; i < states.Length && i < distances.Length; i++)
            {
                if (states[i] != (int)PipelineState.Building || distances[i] > reach) continue;
                if (distances[i] < bestD) { bestD = distances[i]; pick = i; }
            }
            return pick;
        }
    }

    internal static class BayGrid
    {
        public static bool OnGrid(float dx, float dy, float dAngle, float tolCells, float tolAngle)
        {
            if (Math.Abs(dx) > tolCells || Math.Abs(dy) > tolCells) return false;
            double a = dAngle % (2.0 * Math.PI);
            if (a > Math.PI) a -= 2.0 * Math.PI;
            if (a < -Math.PI) a += 2.0 * Math.PI;
            return Math.Abs(a) <= tolAngle;
        }
    }
}
