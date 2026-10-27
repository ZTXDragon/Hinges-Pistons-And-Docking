using System;
using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.UI;

namespace ZTX.HingesPistonsDocking
{
    internal static class RailToggles
    {
        public const int MODE = 0, TARGET = 1, SPEED = 2, DWELL = 3, SWITCH = 4, MERGE = 5;
        public const int GRIP = 6;
        public const int POWER = 7;
        private const int MODE_MAX = RailCartRules.MODE_HOLD;

        private static readonly ID<PartToggleGuiRules> s_modeId = new ID<PartToggleGuiRules>("ztx_rail_mode");
        private static readonly ID<PartToggleGuiRules> s_target1Id = new ID<PartToggleGuiRules>("ztx_rail_tgt_1");
        private static readonly ID<PartToggleGuiRules> s_target2Id = new ID<PartToggleGuiRules>("ztx_rail_tgt_2");
        public static readonly ID<PartToggleGuiRules> SpeedValueId = new ID<PartToggleGuiRules>("ztx_cdrv_speed");
        public static readonly ID<PartToggleGuiRules> PowerValueId = new ID<PartToggleGuiRules>("ztx_cdrv_power");
        public static readonly ID<PartToggleGuiRules> DwellValueId = new ID<PartToggleGuiRules>("ztx_cdrv_dwell");
        public const string VALUE_PREFIX = "ztx_cdrv_";
        public static readonly ID<PartToggleGuiRules> GapValueId = new ID<PartToggleGuiRules>("ztx_cdrv_gap");
        public static readonly ID<PartToggleGuiRules> ShipHoldValueId = new ID<PartToggleGuiRules>("ztx_cdrv_shiphold");
        public static readonly ID<PartToggleGuiRules> GlobalId = new ID<PartToggleGuiRules>("ztx_cdrv_global");
        private static readonly ID<PartToggleGuiRules> s_switchId = new ID<PartToggleGuiRules>("ztx_rail_switch");
        public static readonly ID<PartToggleGuiRules> MergeId = new ID<PartToggleGuiRules>("ztx_rail_merge");
        public static readonly ID<PartToggleGuiRules> GripId = new ID<PartToggleGuiRules>("ztx_rail_grip");

        public static List<(int, int)> PlanCartDo(LogicAction a, bool on)
        {
            var w = new List<(int, int)>();
            LogicRef station = on ? a.OnStation : a.OffStation;
            if (!station.IsNone) w.Add((TARGET, station.Pack()));
            int mode = on ? a.OnPose : a.OffPose;
            if (mode > 0) w.Add((MODE, Math.Min(MODE_MAX, mode - 1)));
            AddDrive(w, a.Drive, a.Speed, a.Strength);
            if (a.MergeAt == 1 || a.MergeAt == 2) w.Add((MERGE, a.MergeAt == 1 ? 1 : 0));
            AddGrip(w, a.Grip);
            return w;
        }

        public static List<(int, int)> PlanCartStep(SeqStep s)
        {
            var w = new List<(int, int)>();
            if (!s.Station.IsNone) w.Add((TARGET, s.Station.Pack()));
            if (s.Pose > 0) w.Add((MODE, Math.Min(MODE_MAX, s.Pose - 1)));
            AddDrive(w, s.Drive, s.Speed, s.Strength);
            if (s.Merge == 1 || s.Merge == 2) w.Add((MERGE, s.Merge == 1 ? 1 : 0));
            AddGrip(w, s.Grip);
            return w;
        }

        private static void AddGrip(List<(int, int)> w, int grip)
        {
            if (grip == LogicAction.GRIP_ATTACH || grip == LogicAction.GRIP_RELEASE) w.Add((GRIP, grip));
            else if (grip == LogicAction.GRIP_SET_OFF) w.Add((GRIP, 0));
        }

        private static void AddDrive(List<(int, int)> w, int drive, int legacySpeed, int legacyDwell)
        {
            if (drive != 0)
            {
                RailCartRules.UnpackDrive(drive, out float? speed, out int? power, out float? dwell);
                if (speed.HasValue) w.Add((SPEED, RailCartRules.EncodeSpeed(speed.Value)));
                if (power.HasValue) w.Add((POWER, RailCartRules.EncodePower(power.Value)));
                if (dwell.HasValue) w.Add((DWELL, RailCartRules.EncodeDwell(dwell.Value)));
                return;
            }
            if (legacySpeed > 0) w.Add((SPEED, RailCartRules.EncodeSpeed(RailCartRules.OLD_SPEEDS[Math.Min(RailCartRules.OLD_SPEEDS.Length - 1, legacySpeed - 1)])));
            if (legacyDwell > 0) w.Add((DWELL, RailCartRules.EncodeDwell(RailCartRules.OLD_DWELLS[Math.Min(RailCartRules.OLD_DWELLS.Length - 1, legacyDwell - 1)])));
        }

        public static List<(int, int)> PlanSwitchDo(bool on) => new List<(int, int)> { (SWITCH, on ? 1 : 0) };

        public static List<(int, int)> PlanSwitchLine(int code)
        {
            var w = new List<(int, int)>();
            if (code == 1 || code == 2) w.Add((SWITCH, code == 1 ? 1 : 0));
            return w;
        }

        public static List<(int, int)> PlanSwitchStep(SeqStep s)
        {
            var w = new List<(int, int)>();
            if (s.Lock == 1 || s.Lock == 2) w.Add((SWITCH, s.Lock == 1 ? 1 : 0));
            return w;
        }

        public static string Apply(Part part, List<(int Kind, int Value)> plan)
        {
            if (part == null || part.IsDestroyed || part.Ship == null || plan == null) return "";
            string changed = "";
            foreach (var (kind, value) in plan)
            {
                switch (kind)
                {
                    case TARGET:
                    {
                        LogicRef station = LogicRef.Unpack(value);
                        string key = RailShipManager.StationKey(part, station);
                        if (key == null) { changed += Sep(changed) + "target " + station + " not on its rail (left)"; continue; }
                        int n1 = LogicRuleDef.PackName(key, 0), n2 = LogicRuleDef.PackName(key, 5);
                        bool a = Set(part, s_target1Id, n1), b = Set(part, s_target2Id, n2);
                        if (a || b) changed += Sep(changed) + "target " + (key.Length == 0 ? "HOME" : key);
                        break;
                    }
                    case MODE:
                        if (Set(part, s_modeId, value)) changed += Sep(changed) + "mode " + value;
                        break;
                    case SPEED:
                        if (SetValue(part, SpeedValueId, value)) changed += Sep(changed) + "speed " + ((value - 1) / 10f).ToString("0.#") + " cells/s";
                        break;
                    case POWER:
                        if (SetValue(part, PowerValueId, value)) changed += Sep(changed) + "power " + (value - 1) + " %";
                        break;
                    case DWELL:
                        if (SetValue(part, DwellValueId, value)) changed += Sep(changed) + "dwell " + ((value - 1) / 10f).ToString("0.#") + " s";
                        break;
                    case SWITCH:
                        if (Set(part, s_switchId, value)) changed += Sep(changed) + (value != 0 ? "open" : "closed");
                        break;
                    case MERGE:
                        if (Set(part, MergeId, value)) changed += Sep(changed) + "merge " + (value != 0 ? "on" : "off");
                        break;
                    case GRIP:
                        if (Set(part, GripId, value)) changed += Sep(changed) + "grip " + (value == 1 ? "attach" : value == 2 ? "release" : "off");
                        break;
                }
            }
            return changed;
        }

        private static string Sep(string s) => s.Length > 0 ? ", " : "";

        private static bool SetValue(Part part, ID<PartToggleGuiRules> id, int value)
        {
            var meta = part?.Ship?.Metadata;
            if (meta == null) return false;
            var key = (new Cosmoteer.Ships.Blueprints.PartSaveData(part), id);
            if ((meta.PartUIToggleStates.Get(key) ?? 0) == value) return false;
            meta.PartUIToggleStates.Set(key, value);
            meta.PartUIToggleStates.Set(key, value, blueprintMode: true);
            return true;
        }

        private static bool Set(Part part, ID<PartToggleGuiRules> id, int value)
        {
            PartUIToggle t = PartUIToggle.GetOnPart(part, id);
            if (t == null || t.Mode == value) return false;
            t.Mode = value;
            t.Part.MatchingBlueprintPart?.OnPartUIToggleStatesChanged();
            return true;
        }

        public static string ApplyDo(Part part, int kind, LogicAction a, bool on)
        {
            if (kind == LogicAction.KIND_CART) return Apply(part, PlanCartDo(a, on));
            if (kind == LogicAction.KIND_SWITCH) return Apply(part, a.Sided ? PlanSwitchLine(a.OnPose) : PlanSwitchDo(on));
            return "";
        }

        public static string ApplyStep(Part part, int kind, SeqStep s)
        {
            if (kind == LogicAction.KIND_CART) return Apply(part, PlanCartStep(s));
            if (kind == LogicAction.KIND_SWITCH) return Apply(part, PlanSwitchStep(s));
            return "";
        }

        public static int SettleStep(Part part, int kind, SeqStep s, int startTick, out string why)
        {
            why = "";
            if (kind != LogicAction.KIND_CART) return SeqRunner.DONE;
            if (s.Station.IsPart && RailShipManager.StationKey(part, s.Station) == null) { why = "station not on its rail"; return SeqRunner.REFUSED; }
            RailShipManager.CartRest(part, out bool merged, out bool moving, out string resting);
            string target = RailNetwork.Key(RailShipManager.TargetName(part));
            int r = RailCartRules.Settle(RailShipManager.Mode(part), target.Length == 0, merged, moving, resting, target, RailShipManager.MergeOn(part), out why);
            if (r != SeqRunner.DONE) return r;
            ZtxRailCartComponent cart = ZtxRailCartComponent.ForPart(part);
            return RailCartRules.GripSettle(s.Grip, GripService.IsCartHolding(part), GripService.RefusedSince(cart, startTick, out _), out why);
        }
    }
}
