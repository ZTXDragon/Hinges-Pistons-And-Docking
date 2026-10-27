using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Ships.AI.StrategyModules;
using Cosmoteer.Ships.AI.TargetingModules;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Weapons;
using Halfling;
using Halfling.Pooling;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class AssemblyAi
    {
        public static float AssemblyFirepower(float[] memberFirepower)
        {
            float sum = 0f;
            if (memberFirepower != null)
                foreach (float f in memberFirepower) sum += f;
            return sum;
        }

        public static ulong PickAnchor(ulong self, ulong groupAnchor, ulong[] assembly)
        {
            if (assembly == null || assembly.Length == 0) return self;
            return Array.IndexOf(assembly, groupAnchor) >= 0 ? groupAnchor : assembly[0];
        }

        public static Ship[] SegmentsOf(Ship ship)
        {
            if (ship == null || HingeJointManager.ActivePinCount == 0) return null;
            Ship[] m = AssemblyIndex.FreshAssemblyOf(ship);
            if (m == null || m.Length < 2) return null;
            int owner = ship.Metadata.PlayerIndex;
            Ship[] same = m.Where(x => x != null && ReferenceEquals(x.Sim, ship.Sim) && x.Metadata.PlayerIndex == owner).ToArray();
            return same.Length < 2 ? null : same;
        }

        public static Ship AnchorFor(Ship chosen)
        {
            Ship[] m = SegmentsOf(chosen);
            if (m == null) return chosen;
            Ship group = AssemblyIndex.FreshGroupAnchorOf(chosen);
            ulong pick = PickAnchor(AssemblyTopology.Id(chosen), group == null ? 0UL : AssemblyTopology.Id(group), m.Select(AssemblyTopology.Id).ToArray());
            foreach (Ship s in m)
                if (AssemblyTopology.Id(s) == pick) return s;
            return chosen;
        }

        public static float FirepowerOf(AIAggroEnemiesModule module, Ship candidate)
        {
            Ship[] m = SegmentsOf(candidate);
            if (m == null) return module.GetShipFirepowerRating(candidate);
            var each = new float[m.Length];
            for (int i = 0; i < m.Length; i++) each[i] = module.GetShipFirepowerRating(m[i]);
            return AssemblyFirepower(each);
        }

        public static IEnumerable<Weapon> ShipWideOfShip(WeaponManager weapons, bool confirmed)
        {
            IEnumerable<Weapon> own = weapons.GetShipWideTargetableWeapons(confirmed);
            try
            {
                Ship[] m = SegmentsOf(weapons.Ship);
                if (m == null) return own;
                var list = new List<Weapon>(own);
                foreach (Ship s in m)
                    if (!ReferenceEquals(s, weapons.Ship) && s.Weapons != null)
                        list.AddRange(s.Weapons.GetShipWideTargetableWeapons(confirmed));
                return list;
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("AssemblyAi.ShipWideOfShip", ex);
                return own;
            }
        }
    }

    [HarmonyPatch(typeof(ShipAI), "FixedUpdate")]
    internal static class ShipAiWeaponsSitePatch
    {
        public const int EXPECTED = 1;
        public static int Patched;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);
            MethodInfo from = AccessTools.Method(typeof(WeaponManager), nameof(WeaponManager.GetShipWideTargetableWeapons));
            MethodInfo to = AccessTools.Method(typeof(AssemblyAi), nameof(AssemblyAi.ShipWideOfShip));
            int hits = list.Count(ci => (ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && Equals(ci.operand, from));
            if (from == null || to == null || hits != EXPECTED)
            {
                Patched = 0;
                Log.Error($"AssemblyAi: found {hits} ship-wide weapon reads in ShipAI.FixedUpdate, expected {EXPECTED} -- left vanilla.");
                return list;
            }
            foreach (CodeInstruction ci in list)
                if ((ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && Equals(ci.operand, from))
                {
                    ci.opcode = OpCodes.Call;
                    ci.operand = to;
                }
            Patched = hits;
            return list;
        }
    }

    [HarmonyPatch(typeof(AIAggroEnemiesModule), "FindBestTarget")]
    internal static class AggroWholeShipPatch
    {
        public const int EXPECTED = 1;
        public static int Patched;

        public static string Summary =>
            $"AssemblyAi: {ShipAiWeaponsSitePatch.Patched}/{ShipAiWeaponsSitePatch.EXPECTED} AI weapon-list read, {Patched}/{EXPECTED} aggro firepower read patched";

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);
            MethodInfo from = AccessTools.Method(typeof(AIAggroEnemiesModule), "GetShipFirepowerRating");
            MethodInfo to = AccessTools.Method(typeof(AssemblyAi), nameof(AssemblyAi.FirepowerOf));
            int hits = list.Count(ci => (ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && Equals(ci.operand, from));
            if (from == null || to == null || hits != EXPECTED)
            {
                Patched = 0;
                Log.Error($"AssemblyAi: found {hits} firepower reads in AIAggroEnemiesModule.FindBestTarget, expected {EXPECTED} -- left vanilla.");
                return list;
            }
            MethodInfo enemies = AccessTools.Method(typeof(Ship), nameof(Ship.IsEnemiesWith), new[] { typeof(Ship) });
            MethodInfo enemiesShim = AccessTools.Method(typeof(AssemblyJobs), nameof(AssemblyJobs.IsEnemiesWith));
            foreach (CodeInstruction ci in list)
            {
                if (ci.opcode != OpCodes.Call && ci.opcode != OpCodes.Callvirt) continue;
                if (Equals(ci.operand, from))
                {
                    ci.opcode = OpCodes.Call;
                    ci.operand = to;
                }
                else if (enemies != null && enemiesShim != null && Equals(ci.operand, enemies))
                {
                    ci.opcode = OpCodes.Call;
                    ci.operand = enemiesShim;
                }
            }
            Patched = hits;
            return list;
        }

        static void Postfix(ref Ship __result)
        {
            try
            {
                if (__result != null) __result = AssemblyAi.AnchorFor(__result);
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("AggroWholeShipPatch", ex);
            }
        }
    }

    [HarmonyPatch(typeof(AIValueTargeterModule), nameof(AIValueTargeterModule.TargetWeapons))]
    internal static class ValueTargeterWholeShipPatch
    {
        static bool Prefix(AIValueTargeterModule __instance, Ship ship, ShipAI ai, List<Weapon> weapons)
        {
            try
            {
                Ship target = (ship.Commands.CurConfirmedCommand as AttackCommand)?.Target;
                Ship[] targetSegments = target == null ? null : AssemblyAi.SegmentsOf(target);
                bool ownSegments = false;
                foreach (Weapon w in weapons)
                    if (w?.Ship != null && !ReferenceEquals(w.Ship, ship)) { ownSegments = true; break; }
                if (targetSegments == null && !ownSegments) return true;
                Ship[] targets = targetSegments ?? (target == null ? null : new[] { target });

                if (target?.Physics.Body != null)
                {
                    Part current = __instance._partTarget;
                    if (current != null && (current.Ship == null || Array.IndexOf(targets, current.Ship) < 0))
                        __instance._partTarget = null;
                    __instance._partTarget = BestPartAcross(__instance, ship, target, targets);
                }
                else
                {
                    __instance._partTarget = null;
                }
                Part part = __instance._partTarget;
                if (part == null) return false;
                for (int i = weapons.Count - 1; i >= 0; i--)
                {
                    Weapon weapon = weapons[i];
                    if (weapon.Rules.IsShipWideExplicitTargetingSuppressed(weapon.Ship, part)) continue;
                    if (weapon.ExplicitTargets.CurConfirmedTarget?.Target != part)
                    {
                        WeaponManager owner = weapon.Ship?.Weapons ?? ship.Weapons;
                        var wt = new WeaponTarget(weapon, part, owner.AllocGlobalTargetID());
                        wt.PlayerTimeGiven = App.Clock.Time;
                        weapon.ExplicitTargets.EnqueueTarget(wt, append: false, confirmed: true);
                    }
                    weapons.RemoveAt(i);
                }
                return false;
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("ValueTargeterWholeShipPatch", ex);
                return true;
            }
        }

        private static Part BestPartAcross(AIValueTargeterModule m, Ship ourShip, Ship target, Ship[] targets)
        {
            using TempList<Part> valued = TempList<Part>.Alloc();
            using TempList<Part> rest = TempList<Part>.Alloc();
            foreach (Ship s in targets)
            {
                if (s?.Physics?.Body == null) continue;
                for (int i = 0; i < s.Parts.Count; i++)
                {
                    Part p = s.Parts[i];
                    if (p == m._partTarget) continue;
                    if (p.Rules.AIValueFactor > 0f) valued.Add(p);
                    else rest.Add(p);
                }
            }
            return m.FindBestPartTarget(ourShip, target, valued) ?? (rest.Count > 0 ? ourShip.Rand.SelectElement(rest) : null);
        }
    }
}
