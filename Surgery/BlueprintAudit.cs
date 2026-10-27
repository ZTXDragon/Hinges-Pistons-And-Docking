using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Parts;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal static class BlueprintAudit
    {
        public static readonly bool ENABLED = false;

        private const int MAX_LISTED = 12;

        private static readonly ConditionalWeakTable<Ship, StrongBox<string>> s_last = new ConditionalWeakTable<Ship, StrongBox<string>>();

        public static string Describe(PartRules rules, IntVector2 loc, int rot, bool flip) =>
            (rules?.ID.ToString() ?? "?") + "@" + loc.X + "," + loc.Y + " r" + rot + (flip ? " f" : "");

        public static void Report(Ship ship, string when)
        {
            if (!ENABLED || ship?.BlueprintParts == null || ship.Parts == null) return;
            try
            {
                var unbuilt = new List<string>();
                foreach (BlueprintPart b in ship.BlueprintParts.UnmatchedParts)
                    if (b?.Rules != null) unbuilt.Add(Describe(b.Rules, b.Location, b.Rotation, b.FlipX));
                var unplanned = new List<string>();
                foreach (Part p in ship.Parts.UnmatchedParts)
                    if (p?.Rules != null) unplanned.Add(Describe(p.Rules, p.Location, p.Rotation, p.FlipX));
                unbuilt.Sort(StringComparer.Ordinal);
                unplanned.Sort(StringComparer.Ordinal);
                string summary = unbuilt.Count + " unbuilt [" + Listed(unbuilt) + "]; " + unplanned.Count + " built without a blueprint [" + Listed(unplanned) + "]";
                StrongBox<string> last = s_last.GetValue(ship, _ => new StrongBox<string>("0 unbuilt []; 0 built without a blueprint []"));
                if (last.Value == summary) return;
                last.Value = summary;
                Log.Info("Blueprint audit " + when + ", ship " + ship.UniqueID + ": " + summary + ".");
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("BlueprintAudit.Report", ex);
            }
        }

        public static void Blocked(Ship ship, List<PartInfo> removals, List<PartInfo> adds)
        {
            if (!ENABLED || ship == null || (removals.Count == 0 && adds.Count == 0)) return;
            var r = new List<string>();
            foreach (PartInfo pi in removals) r.Add(Describe(pi.Rules, pi.Location, pi.Rotation, pi.FlipX));
            var a = new List<string>();
            foreach (PartInfo pi in adds) a.Add(Describe(pi.Rules, pi.Location, pi.Rotation, pi.FlipX));
            Log.Info("Blueprint audit, ship " + ship.UniqueID + ": the engine's blueprint match wanted to remove " + removals.Count + " [" + Listed(r)
                + "] and add " + adds.Count + " [" + Listed(a) + "] that the surgery filters kept as they were.");
        }

        public static void Dropped(Part basePart, List<(PartRules Rules, IntVector2 Cell, int Rot, bool Flip)> members)
        {
            if (!ENABLED || basePart?.Ship == null || members.Count == 0) return;
            Ship ship = basePart.Ship;
            var lines = new List<string>();
            foreach (var m in members)
            {
                Part built = ship.Parts[m.Cell, PartRectType.Normal];
                BlueprintPart planned = ship.BlueprintParts?[m.Cell, PartRectType.Normal];
                lines.Add(Describe(m.Rules, m.Cell, m.Rot, m.Flip) + " -> built "
                    + (built != null ? Describe(built.Rules, built.Location, built.Rotation, built.FlipX) : "none")
                    + ", planned " + (planned != null ? Describe(planned.Rules, planned.Location, planned.Rotation, planned.FlipX) : "none"));
            }
            Log.Info("Blueprint audit, base at " + basePart.Location + ": dropped members expected vs found [" + Listed(lines) + "].");
        }

        private static string Listed(List<string> items)
        {
            if (items.Count <= MAX_LISTED) return string.Join("; ", items);
            return string.Join("; ", items.GetRange(0, MAX_LISTED)) + "; ... " + (items.Count - MAX_LISTED) + " more";
        }
    }
}
