using System.Runtime.CompilerServices;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.UI;

namespace ZTX.HingesPistonsDocking
{
    internal static class JointPower
    {
        public static readonly ID<PartToggleGuiRules> OnOffId = new ID<PartToggleGuiRules>("on_off");
        private static readonly ID<PartToggleGuiRules> s_oldId = new ID<PartToggleGuiRules>("ztx_hinge_active");
        private static readonly ConditionalWeakTable<Part, object> s_carried = new ConditionalWeakTable<Part, object>();
        private static readonly object s_mark = new object();

        public static bool IsOnFrom(int? old, int mode) => (old ?? mode) != 0;

        public static int CarriedMode(int? old, int mode) => old ?? mode;

        public static bool IsOn(Part p)
        {
            if (p == null) return false;
            PartUIToggle t = PartUIToggle.GetOnPart(p, OnOffId);
            return t != null && IsOnFrom(Old(p), t.Mode);
        }

        public static void CarryOver(ZtxHingeBaseComponent b)
        {
            if (b == null || b.PowerCarried) return;
            Part p = b.Part;
            if (p?.Ship == null) return;
            b.PowerCarried = true;
            int? old = Old(p);
            s_carried.AddOrUpdate(p, s_mark);
            if (!old.HasValue) return;
            PartUIToggle t = PartUIToggle.GetOnPart(p, OnOffId);
            if (t != null) t.Mode = CarriedMode(old, t.Mode);
            p.Ship.Metadata.PartUIToggleStates.Set((new PartSaveData(p), s_oldId), null);
            Log.Info($"joint at {p.Location}: old engage setting {old.Value} moved to the on/off toggle.");
        }

        private static int? Old(Part p)
        {
            if (s_carried.TryGetValue(p, out _)) return null;
            ShipMetadata meta = p.Ship?.Metadata;
            return meta?.PartUIToggleStates.Get((new PartSaveData(p), s_oldId));
        }
    }
}
