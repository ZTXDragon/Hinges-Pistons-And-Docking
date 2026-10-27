using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Parts.UI;

namespace ZTX.HingesPistonsDocking
{
    internal static class LogicStoreReader
    {
        public const int PROBE_MARGIN = 32;

        private sealed class ShipIndex
        {
            public int Tick = int.MinValue;
            public readonly Dictionary<PartSaveData, List<(string Name, int Value)>> ByPart = new Dictionary<PartSaveData, List<(string, int)>>();
        }

        private static readonly ConditionalWeakTable<Ship, ShipIndex> s_index = new ConditionalWeakTable<Ship, ShipIndex>();

        public static StoreImage ReadFull(Ship ship, PartSaveData psd, int tick)
        {
            ShipIndex ix = s_index.GetOrCreateValue(ship);
            if (ix.Tick != tick)
            {
                ix.Tick = tick;
                ix.ByPart.Clear();
                foreach (KeyValuePair<(PartSaveData Part, ID<PartToggleGuiRules> ComponentID), (int? State, int? BlueprintState)> kv in ship.Metadata.PartUIToggleStates)
                {
                    if (!kv.Value.State.HasValue) continue;
                    string name = kv.Key.ComponentID.ToString();
                    if (!name.StartsWith(LogicStore.PREFIX, StringComparison.Ordinal)) continue;
                    if (!ix.ByPart.TryGetValue(kv.Key.Part, out List<(string, int)> list)) ix.ByPart[kv.Key.Part] = list = new List<(string, int)>();
                    list.Add((name, kv.Value.State.Value));
                }
            }
            var img = new StoreImage();
            if (ix.ByPart.TryGetValue(psd, out List<(string Name, int Value)> mine))
                foreach (var (name, value) in mine) LogicStore.Add(img, name, value);
            return img;
        }

        public static StoreImage ReadProbe(Ship ship, PartSaveData psd, int knownLines, int knownRules, int knownActs, int knownSteps = 0, int knownSeqs = 0)
            => ReadProbe(ship.Metadata.PartUIToggleStates, psd, knownLines, knownRules, knownActs, knownSteps, knownSeqs);

        private static StoreImage ReadProbe(PartStateDictionary<(PartSaveData, ID<PartToggleGuiRules>), int> states, PartSaveData psd,
            int knownLines, int knownRules, int knownActs, int knownSteps = 0, int knownSeqs = 0)
        {
            var img = new StoreImage();
            Probe(states, psd, img, knownLines, LogicStore.LINE_FIELDS, LogicStore.LineName);
            Probe(states, psd, img, knownRules, LogicStore.RULE_FIELDS, LogicStore.RuleName);
            Probe(states, psd, img, knownActs, LogicStore.ACT_FIELDS, LogicStore.ActName);
            Probe(states, psd, img, knownSteps, LogicStore.STEP_FIELDS, LogicStore.StepName);
            Probe(states, psd, img, knownSeqs, LogicStore.SEQ_FIELDS, LogicStore.SeqName);
            return img;
        }

        public static int SelfTest(out string first)
        {
            var fails = new List<string>();
            try
            {
                var states = new PartStateDictionary<(PartSaveData, ID<PartToggleGuiRules>), int>();
                var me = new PartSaveData(new ID<Cosmoteer.Ships.Parts.PartRules>("ztx_test_console"), new Halfling.Geometry.IntVector2(1, 2), 0, false);
                var other = new PartSaveData(new ID<Cosmoteer.Ships.Parts.PartRules>("ztx_test_console"), new Halfling.Geometry.IntVector2(5, 5), 0, false);
                var expect = new StoreImage();
                void Put(PartSaveData p, string n, int v)
                {
                    states.Set((p, new ID<PartToggleGuiRules>(n)), v);
                    if (p.Equals(me)) LogicStore.Add(expect, n, v);
                }
                for (int k = 1; k <= 53; k++) for (int f = 0; f < LogicStore.LINE_FIELDS; f++) Put(me, LogicStore.LineName(k, f), 100 * k + f + 1);
                Put(me, LogicStore.RuleName(1, 1), 7);
                Put(me, LogicStore.RuleName(1, LogicStore.RULE_C), 3);
                Put(me, LogicStore.ActName(20, 0), 9);
                Put(other, LogicStore.LineName(1, 0), 999);
                Put(me, LogicStore.VersionId.ToString(), 42);
                StoreImage got = ReadProbe(states, me, 3, 1, 0);
                Check(fails, got.MaxLine == 53 && expect.MaxLine == 53, "50 new lines past the known 3");
                Check(fails, got.Lines.Count == expect.Lines.Count && got.Lines[53][2] == expect.Lines[53][2] && got.Lines[1][0] == 101, "line values");
                Check(fails, got.Rules.Count == 1 && got.Rules[1][LogicStore.RULE_C] == 3, "rule with colour");
                Check(fails, got.Acts.Count == 1 && got.MaxAct == 20, "actuator inside the margin");
            }
            catch (Exception ex) { fails.Add("exception " + ex.GetType().Name + ": " + ex.Message); }
            first = fails.Count > 0 ? fails[0] : "";
            return fails.Count;
        }

        private static void Check(List<string> fails, bool ok, string what)
        {
            if (!ok) fails.Add(what);
        }

        private static void Probe(PartStateDictionary<(PartSaveData, ID<PartToggleGuiRules>), int> states, PartSaveData psd,
            StoreImage img, int known, int fields, Func<int, int, string> name)
        {
            int hi = 0;
            for (int k = 1; k <= Math.Max(known, hi) + PROBE_MARGIN; k++)
            {
                for (int f = 0; f < fields; f++)
                {
                    string n = name(k, f);
                    int? v = states.Get((psd, new ID<PartToggleGuiRules>(n)));
                    if (v.HasValue && v.Value != 0 && LogicStore.Add(img, n, v.Value)) hi = k;
                }
            }
        }
    }
}
