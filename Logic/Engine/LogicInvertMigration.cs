using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;

namespace ZTX.HingesPistonsDocking
{
    internal static class LogicInvertMigration
    {
        public static readonly ID<PartToggleGuiRules> InvertId = new ID<PartToggleGuiRules>("ztx_logic_invert");

        private sealed class Draft
        {
            public ZtxRuleEngineComponent Engine;
            public Dictionary<int, LogicLine> Lines = new Dictionary<int, LogicLine>();
            public HashSet<int> Joined = new HashSet<int>();

            public Draft Clone() => new Draft { Engine = Engine, Lines = new Dictionary<int, LogicLine>(Lines), Joined = new HashSet<int>(Joined) };

            public LogicLine Line(int k) => Lines.TryGetValue(k, out LogicLine l) ? l : Engine.Line(k);

            public int FreeLine()
            {
                for (int k = 1; k <= Engine.LineCount; k++) if (!Line(k).IsUsed) return k;
                return 0;
            }
        }

        public static List<(Part Part, ID<PartToggleGuiRules> Id, int Value)> Plan(Ship ship)
        {
            var writes = new List<(Part, ID<PartToggleGuiRules>, int)>();
            LogicShipManager manager = LogicShipManager.TryGet(ship);
            if (manager == null) return writes;
            var drafts = new List<Draft>();
            foreach (ZtxRuleEngineComponent e in manager.Engines()) drafts.Add(new Draft { Engine = e });
            if (drafts.Count == 0) return writes;
            var sensors = new List<Part>();
            foreach (Draft d in drafts)
            {
                foreach (Part p in d.Engine.Members)
                {
                    if (LogicNet.KindOf(p) != LogicKind.Sensor || !LogicSource.IsOn(p, InvertId) || sensors.Contains(p)) continue;
                    sensors.Add(p);
                }
            }
            if (sensors.Count == 0) return writes;
            foreach (Part s in sensors)
            {
                List<Draft> trial = drafts.ConvertAll(d => d.Clone());
                string why = null;
                int changed = 0;
                foreach (Draft d in trial)
                {
                    why = ConvertIn(d, s, ref changed);
                    if (why != null) break;
                }
                if (why != null)
                {
                    Log.Info("logic: sensor " + LogicNet.PartText(s, false) + " keeps its old Invert (still applied): " + why + ".");
                    continue;
                }
                drafts = trial;
                writes.Add((s, InvertId, 0));
                Log.Info("logic: sensor " + LogicNet.PartText(s, false) + " invert moved into the consoles (" + changed + " line(s) rewritten or added).");
            }
            foreach (Draft d in drafts)
            {
                Part console = d.Engine.Part;
                foreach (KeyValuePair<int, LogicLine> kv in d.Lines)
                {
                    LogicLine l = kv.Value;
                    writes.Add((console, d.Engine.LineSlot(kv.Key, ZtxRuleEngineComponent.LINE_A), l.PackA()));
                    writes.Add((console, d.Engine.LineSlot(kv.Key, ZtxRuleEngineComponent.LINE_B), l.PackB()));
                    writes.Add((console, d.Engine.LineSlot(kv.Key, ZtxRuleEngineComponent.LINE_M), l.PackMisc()));
                }
                foreach (int r in d.Joined)
                {
                    LogicRuleDef def = d.Engine.Rule(r);
                    def.Joined = true;
                    writes.Add((console, d.Engine.RuleSlot(r, ZtxRuleEngineComponent.RULE_M), def.PackMisc()));
                }
            }
            return writes;
        }

        private static string ConvertIn(Draft d, Part s, ref int changed)
        {
            for (int k = 1; k <= d.Engine.LineCount; k++)
            {
                LogicLine l = d.Line(k);
                if (!l.IsUsed || l.Op == LogicOp.None || LogicRuleEval.IsCompare(l.Op)) continue;
                bool a = Reads(d.Engine, l.A, s);
                bool b = LogicOpCatalog.RoleOfB(l.Op) != BRole.None && Reads(d.Engine, l.B, s);
                if (!a && !b) continue;
                if (a && !b && (l.Op == LogicOp.Pass || l.Op == LogicOp.Not))
                    l.Op = l.Op == LogicOp.Pass ? LogicOp.Not : LogicOp.Pass;
                else
                {
                    if (a) l.NotA = !l.NotA;
                    if (b) l.NotB = !l.NotB;
                }
                d.Lines[k] = l;
                changed++;
            }
            return null;
        }

        private static bool Reads(ZtxRuleEngineComponent e, LogicRef r, Part s)
        {
            return r.IsPart && ReferenceEquals(LogicNet.Resolve(e.Part?.Ship, e.Members, r), s);
        }
    }
}
