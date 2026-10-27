using System;
using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Thrusters;
using Cosmoteer.Ships.Parts.UI;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class DockThrust
    {
        public static readonly ID<PartToggleGuiRules> ThrustToggleId = new ID<PartToggleGuiRules>("ztx_dock_thrust");
        public static readonly ID<PartToggleGuiRules> MemberId = new ID<PartToggleGuiRules>("ztx_dock_member");

        private static readonly Dictionary<object, List<Thruster>> s_contrib = new Dictionary<object, List<Thruster>>();
        private static readonly Dictionary<Thruster, int> s_count = new Dictionary<Thruster, int>();
        private static readonly List<Thruster> s_changed = new List<Thruster>();

        public static void Reset()
        {
            s_contrib.Clear();
            s_count.Clear();
        }

        public static bool IsQuiet(Thruster t) => t != null && s_count.ContainsKey(t);

        public static bool ThrustOn(Part dockPart)
        {
            PartUIToggle t = dockPart != null ? PartUIToggle.GetOnPart(dockPart, ThrustToggleId) : null;
            if (t != null) return t.Mode != 0;
            if (ZtxBayTerminalComponent.ForPart(dockPart) != null) return BayValues.Get(dockPart, ThrustToggleId, 0) != 0;
            return true;
        }

        public static Ship Carrier(Ship a, Ship b)
        {
            float ma = a?.Physics?.Body?.Mass ?? 0f, mb = b?.Physics?.Body?.Mass ?? 0f;
            return DockThrustMath.CarrierSide(ma, mb, a.UniqueID.CompareTo(b.UniqueID)) == 0 ? a : b;
        }

        public static void UpdateWelded(object dock, Part dockPart, Ship carrier, Ship docked)
        {
            if (dockPart == null || carrier == null || docked == null || ReferenceEquals(carrier, docked) || ThrustOn(dockPart))
            {
                Clear(dock);
                return;
            }
            var list = new List<Thruster>();
            var seen = new HashSet<Thruster>();
            foreach (Ship s in HingeJointManager.JointSideShips(docked, carrier))
                Collect(s, list, seen, 0);
            Set(dock, dockPart, list);
        }

        public static void UpdateMerged(object dock, Part dockPart)
        {
            if (dockPart?.Ship == null || ThrustOn(dockPart))
            {
                Clear(dock);
                return;
            }
            var list = new List<Thruster>();
            var seen = new HashSet<Thruster>();
            int key = KeyOf(dockPart);
            Ship ship = dockPart.Ship;
            Collect(ship, list, seen, key);
            foreach (Ship arm in HingeJointManager.ArmsFrom(ship, p => TypedSettings.Stored(p, MemberId) == key))
                Collect(arm, list, seen, 0);
            Set(dock, dockPart, list);
        }

        public static void Clear(object dock) => Set(dock, null, null);

        public static int KeyOf(Part dockPart) => DockThrustMath.EncodeCell(dockPart.Location.X, dockPart.Location.Y);

        public static void RecordMembers(Ship survivor, Part governingDock, List<Halfling.Geometry.IntVector2> cells)
        {
            if (survivor == null || governingDock == null || cells == null) return;
            int key = KeyOf(governingDock);
            int n = 0;
            foreach (var c in cells)
            {
                Part p = survivor.Parts[c, PartRectType.Normal];
                if (p == null) continue;
                TypedSettings.Store(p, MemberId, key);
                n++;
            }
            Log.Info($"Dock thrust: {n} parts on ship {survivor.UniqueID} recorded as docked through the {governingDock.Rules.ID} at {governingDock.Location}.");
        }

        public static List<Halfling.Geometry.IntVector2> MemberCells(Ship ship, GridTransform xf)
        {
            var cells = new List<Halfling.Geometry.IntVector2>();
            if (ship == null) return cells;
            foreach (Part p in ship.Parts)
            {
                foreach (var c in p.GetCells(PartRectType.Normal))
                {
                    cells.Add(xf.TransformCell(c));
                    break;
                }
            }
            return cells;
        }

        public static void ClearMembers(Ship ship, HashSet<int> keys)
        {
            if (ship == null || keys == null || keys.Count == 0) return;
            foreach (Part p in ship.Parts)
                if (keys.Contains(TypedSettings.Stored(p, MemberId))) TypedSettings.Store(p, MemberId, 0);
        }

        private static void Collect(Ship ship, List<Thruster> into, HashSet<Thruster> seen, int memberKey)
        {
            if (ship == null) return;
            foreach (Part p in ship.Parts)
            {
                if (memberKey != 0 && TypedSettings.Stored(p, MemberId) != memberKey) continue;
                foreach (PartComponent c in p.Components)
                    if (c is Thruster t && seen.Add(t)) into.Add(t);
            }
        }

        private static void Set(object dock, Part dockPart, List<Thruster> list)
        {
            s_contrib.TryGetValue(dock, out List<Thruster> old);
            bool none = list == null || list.Count == 0;
            if (none && old == null) return;
            if (none) s_contrib.Remove(dock);
            else s_contrib[dock] = list;
            s_changed.Clear();
            if (!none)
                foreach (Thruster t in list)
                {
                    s_count.TryGetValue(t, out int c);
                    s_count[t] = c + 1;
                    if (c == 0) s_changed.Add(t);
                }
            if (old != null)
                foreach (Thruster t in old)
                {
                    if (!s_count.TryGetValue(t, out int c)) continue;
                    if (c <= 1) { s_count.Remove(t); s_changed.Add(t); }
                    else s_count[t] = c - 1;
                }
            foreach (Thruster t in s_changed) Reevaluate(t);
            int was = old?.Count ?? 0, now = none ? 0 : list.Count;
            if (was != now)
                Log.Info($"Dock thrust: dock at {dockPart?.Location.ToString() ?? "(gone)"} holds {now} thrusters quiet (was {was}).");
        }

        private static void Reevaluate(Thruster t)
        {
            try
            {
                if (t?.Part == null || t.Part.IsDestroyed) return;
                bool own = t._softOperationalToggle?.IsToggleOn ?? true;
                t.IsSoftOperational = t.IsOperational && own && !IsQuiet(t);
            }
            catch (Exception ex)
            {
                Log.Exception("DockThrust.Reevaluate", ex);
            }
        }
    }

    [HarmonyPatch(typeof(Thruster), "IsSoftOperational", MethodType.Setter)]
    internal static class ThrusterQuietPatch
    {
        static void Prefix(Thruster __instance, ref bool value)
        {
            if (value && DockThrust.IsQuiet(__instance)) value = false;
        }
    }
}
