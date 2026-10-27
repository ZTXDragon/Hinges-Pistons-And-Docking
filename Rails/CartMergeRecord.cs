using System;
using System.Collections.Generic;
using Cosmoteer.Ships.Parts;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal sealed class CartMergeRecord
    {
        public const string PREFIX = "ztx_cm_";
        private const int MAX_MEMBERS = 4096;

        public bool Away;
        public int K;
        public IntVector2 T;
        public IntVector2 Home;
        public int MergeRot = -1;
        private readonly List<(IntVector2 Rel, uint Hash)> _members = new List<(IntVector2, uint)>();
        private readonly List<(int Kind, IntVector2 Cell, int RotFlip)> _frames = new List<(int, IntVector2, int)>();

        public int MemberCount => _members.Count;
        public (IntVector2 Rel, uint Hash) Member(int i) => _members[i];
        public void AddMember(IntVector2 rel, uint hash) => _members.Add((rel, hash));
        public int FrameCount => _frames.Count;
        public (int Kind, IntVector2 Cell, int RotFlip) Frame(int i) => _frames[i];
        public void AddFrame(int kind, IntVector2 cell, int rotFlip) => _frames.Add((kind, cell, rotFlip));

        public GridTransform Xf => new GridTransform(K, T);

        public static GridTransform Drift(IntVector2 mergedAt, int rotAtMerge, IntVector2 now, int rotNow)
        {
            int dk = ((rotNow - rotAtMerge) % 4 + 4) % 4;
            return new GridTransform(dk, now - mergedAt.Rotate90(dk));
        }

        public GridTransform DriftOf(Part cartPart)
        {
            if (cartPart == null) return GridTransform.Identity;
            IntVector2 mergedAt = Xf.TransformCell(Home);
            return Drift(mergedAt, MergeRot >= 0 ? MergeRot : cartPart.Rotation, cartPart.Location, cartPart.Rotation);
        }

        public GridTransform XfNow(Part cartPart) => Xf.Then(DriftOf(cartPart));

        public CartMergeRecord RebasedTo(Part cartPart)
        {
            GridTransform drift = DriftOf(cartPart);
            GridTransform now = Xf.Then(drift);
            var r = new CartMergeRecord { Away = Away, K = now.K, T = now.T, Home = Home, MergeRot = cartPart?.Rotation ?? MergeRot };
            r._members.AddRange(_members);
            foreach ((int kind, IntVector2 cell, int rotFlip) in _frames)
                r._frames.Add((kind, drift.TransformCell(cell), (((rotFlip & 3) + drift.K) & 3) | (rotFlip & 4)));
            return r;
        }

        public void WriteTo(Action<string, int> set) => WriteClearing(set, 0);

        public void WriteClearing(Action<string, int> set, int clearMembersUpTo, int clearFramesUpTo = 0)
        {
            set(PREFIX + "s", Away ? 1 : 0);
            set(PREFIX + "k", Away ? (K & 3) + 1 : 0);
            set(PREFIX + "t", Away ? MergeRecord.PackCell(T) : 0);
            set(PREFIX + "h", Away ? MergeRecord.PackCell(Home) : 0);
            set(PREFIX + "o", Away && MergeRot >= 0 ? (MergeRot & 3) + 1 : 0);
            set(PREFIX + "n", Away ? _members.Count : 0);
            int n = Away ? _members.Count : 0;
            for (int i = 0; i < Math.Max(n, clearMembersUpTo); i++)
            {
                set(PREFIX + "r" + i, i < n ? MergeRecord.PackCell(_members[i].Rel) : 0);
                set(PREFIX + "q" + i, i < n ? unchecked((int)_members[i].Hash) : 0);
            }
            int nf = Away ? _frames.Count : 0;
            set(PREFIX + "fn", nf);
            for (int i = 0; i < Math.Max(nf, clearFramesUpTo); i++)
            {
                set(PREFIX + "fc" + i, i < nf ? MergeRecord.PackCell(_frames[i].Cell) : 0);
                set(PREFIX + "fk" + i, i < nf ? (_frames[i].Kind + 1) | ((_frames[i].RotFlip & 7) << 8) : 0);
            }
        }

        public static CartMergeRecord ReadFrom(Func<string, int?> get)
        {
            if ((get(PREFIX + "s") ?? 0) == 0) return null;
            var r = new CartMergeRecord
            {
                Away = true,
                K = Math.Max(0, (get(PREFIX + "k") ?? 1) - 1) & 3,
                T = MergeRecord.UnpackCell(get(PREFIX + "t") ?? 0),
                Home = MergeRecord.UnpackCell(get(PREFIX + "h") ?? 0),
                MergeRot = (get(PREFIX + "o") ?? 0) - 1,
            };
            int n = Math.Min(MAX_MEMBERS, Math.Max(0, get(PREFIX + "n") ?? 0));
            for (int i = 0; i < n; i++)
                r._members.Add((MergeRecord.UnpackCell(get(PREFIX + "r" + i) ?? 0), unchecked((uint)(get(PREFIX + "q" + i) ?? 0))));
            int nf = Math.Min(MAX_MEMBERS, Math.Max(0, get(PREFIX + "fn") ?? 0));
            for (int i = 0; i < nf; i++)
            {
                int k = get(PREFIX + "fk" + i) ?? 0;
                if ((k & 0xFF) == 0) continue;
                r._frames.Add(((k & 0xFF) - 1, MergeRecord.UnpackCell(get(PREFIX + "fc" + i) ?? 0), (k >> 8) & 7));
            }
            return r;
        }

        public static bool IsAway(Func<string, int?> get) => (get(PREFIX + "s") ?? 0) != 0;
    }
}
