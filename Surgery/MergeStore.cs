using System;
using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Parts;

namespace ZTX.HingesPistonsDocking
{
    internal static class MergeStore
    {
        private static readonly Dictionary<string, ID<PartToggleGuiRules>> s_ids = new Dictionary<string, ID<PartToggleGuiRules>>();

        private static ID<PartToggleGuiRules> Id(string name)
        {
            lock (s_ids)
            {
                if (!s_ids.TryGetValue(name, out ID<PartToggleGuiRules> id))
                    s_ids[name] = id = new ID<PartToggleGuiRules>(name);
                return id;
            }
        }

        public static MergeRecord Read(Part owner)
        {
            var meta = owner?.Ship?.Metadata;
            if (meta == null) return null;
            var psd = new PartSaveData(owner);
            return MergeRecord.ReadFrom(name => meta.PartUIToggleStates.Get((psd, Id(name))));
        }

        public static MergeRecord ReadAt(Cosmoteer.Ships.Ship ship, PartSaveData psd)
        {
            var meta = ship?.Metadata;
            if (meta == null) return null;
            return MergeRecord.ReadFrom(name => meta.PartUIToggleStates.Get((psd, Id(name))));
        }

        public static MergeState ReadStateAt(Cosmoteer.Ships.Ship ship, PartSaveData psd)
        {
            var meta = ship?.Metadata;
            if (meta == null) return MergeState.None;
            return MergeRecord.ReadState(name => meta.PartUIToggleStates.Get((psd, Id(name))));
        }

        public static MergeState ReadState(Part owner)
        {
            var meta = owner?.Ship?.Metadata;
            if (meta == null) return MergeState.None;
            var psd = new PartSaveData(owner);
            return MergeRecord.ReadState(name => meta.PartUIToggleStates.Get((psd, Id(name))));
        }

        public static int ReadSlide(Part owner)
        {
            var meta = owner?.Ship?.Metadata;
            if (meta == null) return 0;
            return meta.PartUIToggleStates.Get((new PartSaveData(owner), Id(MergeRecord.PREFIX + "k"))) ?? 0;
        }

        public static void WriteAt(Cosmoteer.Ships.Ship ship, PartSaveData psd, MergeRecord r)
        {
            var meta = ship?.Metadata;
            if (meta == null || r == null) return;
            WriteCore(meta, psd, r);
        }

        public static void Write(Part owner, MergeRecord r)
        {
            var meta = owner?.Ship?.Metadata;
            if (meta == null || r == null) return;
            WriteCore(meta, new PartSaveData(owner), r);
        }

        private static void WriteCore(Cosmoteer.Ships.ShipMetadata meta, PartSaveData psd, MergeRecord r)
        {
            int oldMembers = meta.PartUIToggleStates.Get((psd, Id(MergeRecord.PREFIX + "n"))) ?? 0;
            int oldSettings = meta.PartUIToggleStates.Get((psd, Id(MergeRecord.PREFIX + "tn"))) ?? 0;
            r.WriteTo((name, value) =>
            {
                meta.PartUIToggleStates.Set((psd, Id(name)), value);
                meta.PartUIToggleStates.Set((psd, Id(name)), value, blueprintMode: true);
            });
            r.ClearStale(name =>
            {
                meta.PartUIToggleStates.Set((psd, Id(name)), null);
                meta.PartUIToggleStates.Set((psd, Id(name)), null, blueprintMode: true);
            }, oldMembers, oldSettings);
        }
    }
}
