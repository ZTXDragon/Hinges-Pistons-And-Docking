using System;
using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Doors;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.UI;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal static class DockToggles
    {
        public const int CONNECT = 0, THRUST = 1;

        private static readonly ID<PartToggleGuiRules> s_dockActiveId = new ID<PartToggleGuiRules>("ztx_dock_active");

        public static void Effective(int kind, bool set, int code, bool on, out int first, out int second)
        {
            if (!set)
            {
                first = on ? 1 : 2;
                second = 0;
                return;
            }
            first = code % 3;
            second = (code / 3) % 3;
        }

        public static int Code(int first, int second) => Math.Max(0, Math.Min(2, first)) + 3 * Math.Max(0, Math.Min(2, second));

        public static List<(int, int)> PlanDo(int kind, bool set, int code, bool on)
        {
            var w = new List<(int, int)>();
            if (kind != LogicAction.KIND_DOCK) return w;
            Effective(kind, set, code, on, out int first, out int second);
            if (first == 1 || first == 2) w.Add((CONNECT, first == 1 ? 1 : 0));
            if (second == 1 || second == 2) w.Add((THRUST, second == 1 ? 1 : 0));
            return w;
        }

        public static List<(int, int)> PlanStep(int kind, int lockField, int mergeField)
        {
            var w = new List<(int, int)>();
            if (kind != LogicAction.KIND_DOCK) return w;
            if (lockField == 1 || lockField == 2) w.Add((CONNECT, lockField == 1 ? 1 : 0));
            if (mergeField == 1 || mergeField == 2) w.Add((THRUST, mergeField == 1 ? 1 : 0));
            return w;
        }

        public static int Settle(int kind, int lockField, int mergeField, bool done)
        {
            bool waits = lockField != 0;
            if (!waits) return SeqRunner.DONE;
            return done ? SeqRunner.DONE : SeqRunner.RUNNING;
        }

        public static string Apply(Part part, List<(int Kind, int Value)> plan)
        {
            if (part == null || part.IsDestroyed || part.Ship == null || plan == null) return "";
            string changed = "";
            foreach (var (kind, value) in plan)
            {
                ID<PartToggleGuiRules> id;
                string name;
                switch (kind)
                {
                    case CONNECT:
                        id = PartUIToggle.GetOnPart(part, DockShipManager.PadActiveId) != null ? DockShipManager.PadActiveId : s_dockActiveId;
                        name = "connect";
                        break;
                    case THRUST: id = DockThrust.ThrustToggleId; name = "docked thrust"; break;
                    default: continue;
                }
                PartUIToggle t = PartUIToggle.GetOnPart(part, id);
                if (t == null || t.Mode == value) continue;
                t.Mode = value;
                t.Part.MatchingBlueprintPart?.OnPartUIToggleStatesChanged();
                changed += (changed.Length > 0 ? ", " : "") + name + (value != 0 ? " on" : " off");
            }
            return changed;
        }

        public static string ApplyDo(Part part, int kind, LogicAction a, bool on) =>
            Apply(part, a.Sided ? PlanDo(kind, a.OnSet, a.OnPose, !a.WhenOff) : PlanDo(kind, on ? a.OnSet : a.OffSet, on ? a.OnPose : a.OffPose, on));

        public static string ApplyStep(Part part, int kind, SeqStep s) => Apply(part, PlanStep(kind, s.Lock, s.Merge));

        public static int SettleStep(Part part, int kind, SeqStep s, out string why)
        {
            bool done = LogicActuatorState.IsDone(part, out why);
            int r = Settle(kind, s.Lock, s.Merge, done);
            if (r == SeqRunner.DONE) why = "";
            return r;
        }

        public static Ship HeldShipOf(Part p)
        {
            if (p == null || p.IsDestroyed || p.Ship == null) return null;
            ZtxBayTerminalComponent terminal = ZtxBayTerminalComponent.ForPart(p);
            if (terminal != null) return terminal.Bay.Held;
            ZtxDockClampComponent clamp = ZtxDockClampComponent.ForPart(p);
            if (clamp != null)
            {
                Ship other = clamp.PairedClamp?.Part?.Ship;
                bool held = other != null && !ReferenceEquals(other, p.Ship)
                            && (HingeJointManager.HasPinWithTag(clamp) || HingeJointManager.HasPinWithTag(clamp.PairedClamp));
                return held ? other : null;
            }
            ZtxDockPadComponent pad = ZtxDockPadComponent.ForPart(p);
            if (pad != null && pad.DockedShip != null && HingeJointManager.HasPinWithTag(pad)) return pad.DockedShip;
            return null;
        }

        public static bool HeldCrewed(Part p)
        {
            ZtxBayTerminalComponent t = ZtxBayTerminalComponent.ForPart(p);
            if (t != null) return t.Bay.Held != null && BayRelease.Facts(t.Bay.Built, t.Bay.Crewed, t.Bay.Supplied, t.Bay.Repaired, t.Bay.Charged)[BayRelease.CREWED];
            Ship held = HeldShipOf(p);
            return held?.Sim != null && DockCrew.Crewed(held);
        }

        public static bool HeldSupplied(Part p)
        {
            ZtxBayTerminalComponent t = ZtxBayTerminalComponent.ForPart(p);
            if (t != null) return t.Bay.Held != null && BayRelease.Facts(t.Bay.Built, t.Bay.Crewed, t.Bay.Supplied, t.Bay.Repaired, t.Bay.Charged)[BayRelease.SUPPLIED];
            Ship held = HeldShipOf(p);
            return held?.Sim != null && DockSupply.Supplied(held, out _);
        }

        public static bool HeldRepaired(Part p)
        {
            ZtxBayTerminalComponent t = ZtxBayTerminalComponent.ForPart(p);
            if (t != null) return t.Bay.Held != null && t.Bay.Built && t.Bay.Repaired;
            Ship held = HeldShipOf(p);
            return held?.Sim != null && ShipBuilt(held) && ShipRepaired(held);
        }

        public static bool HeldCharged(Part p)
        {
            ZtxBayTerminalComponent t = ZtxBayTerminalComponent.ForPart(p);
            if (t != null) return t.Bay.Held != null && t.Bay.Charged;
            Ship held = HeldShipOf(p);
            return held?.Sim != null && DockSupply.Supplied(held, out _, DockSupply.ENERGY);
        }

        public static bool HeldBuilt(Part p)
        {
            ZtxBayTerminalComponent t = ZtxBayTerminalComponent.ForPart(p);
            if (t != null) return t.Bay.Held != null && t.Bay.Built;
            Ship held = HeldShipOf(p);
            return held?.Sim != null && ShipBuilt(held);
        }

        public static bool ShipBuilt(Ship ship)
        {
            if (ship == null) return false;
            foreach (Part part in ship.Parts)
                if (part.IsUnderConstruction) return false;
            foreach (Door door in ship.Doors)
                if (door.IsUnderConstruction) return false;
            return ship.BlueprintParts.UnmatchedParts.Count == 0 && ship.BlueprintDoors.UnmatchedDoors.Count == 0;
        }

        public static bool ShipRepaired(Ship ship)
        {
            if (ship == null) return false;
            foreach (Part part in ship.Parts)
                if (part.Health < part.Rules.MaxHealth) return false;
            return true;
        }

        public static bool IsDocked(Part p)
        {
            if (p == null || p.IsDestroyed || p.Ship == null) return false;
            ZtxDockClampComponent clamp = ZtxDockClampComponent.ForPart(p);
            if (clamp != null)
                return clamp.PairedClamp != null && (HingeJointManager.HasPinWithTag(clamp) || HingeJointManager.HasPinWithTag(clamp.PairedClamp)
                                                     || ReferenceEquals(clamp.PairedClamp.Part?.Ship, p.Ship));
            ZtxDockPadComponent pad = ZtxDockPadComponent.ForPart(p);
            if (pad != null) return pad.DockedShip != null && HingeJointManager.HasPinWithTag(pad);
            ZtxBayTerminalComponent terminal = ZtxBayTerminalComponent.ForPart(p);
            if (terminal != null) return terminal.Bay.Held != null;
            return false;
        }
    }
}
