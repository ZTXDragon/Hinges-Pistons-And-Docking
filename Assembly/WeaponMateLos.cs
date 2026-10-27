using System;
using Cosmoteer;
using Cosmoteer.Bullets;
using Cosmoteer.Bullets.Physics;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Weapons;
using Halfling.Geometry;
using Halfling.Physics2D.Dynamics;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class WeaponMateLos
    {
        public static bool BlocksShot(bool structure, bool low, bool high, bool ignoreStructure, bool ignoreLow, bool ignoreHigh)
        {
            if (low && ignoreLow) return false;
            if (high && ignoreHigh) return false;
            if (structure) return !ignoreStructure;
            return true;
        }

        public static float ShotRadius(float emitted, float circle, float boxWidth, float boxHeight)
        {
            float r = 0.001f;
            if (emitted > r) r = emitted;
            if (circle > r) r = circle;
            if (boxWidth > 0f && boxHeight > 0f)
            {
                float half = 0.5f * Math.Min(boxWidth, boxHeight);
                if (half > r) r = half;
            }
            return r;
        }

        public static float ShotRadiusOf(IEmitter emitter)
        {
            float emitted = emitter?.GetEmittedRadius() ?? float.NaN;
            IEmitter e = emitter;
            for (int hop = 0; hop < 4 && e != null && !(e is BulletEmitter); hop++)
            {
                if (e is PartToggleEmitterSelector sel) e = sel.Emitter;
                else if (e is EmitterProxy proxy) e = proxy._proxy?.ProxiedComponent;
                else e = null;
            }
            float circle = float.NaN, boxW = float.NaN, boxH = float.NaN;
            if (e is BulletEmitter be && be.Rules?.Bullet?.Components != null)
                foreach (BulletComponentRules c in be.Rules.Bullet.Components)
                {
                    if (c is BulletCirclePhysicsRules cr) circle = cr.Radius;
                    else if (c is BulletBoxPhysicsRules br) { boxW = br.Size.X; boxH = br.Size.Y; }
                }
            return ShotRadius(emitted, circle, boxW, boxH);
        }

        public static void DiagBlock(Weapon weapon, Ship other, Fixture f, Vector2 at)
        {
            try
            {
                Part part = other?.Physics?.GetContactPart(f, at, out _);
                string id = part?.Rules?.ID.ToString() ?? "no part";
                LogThrottle.Info($"matelos|{weapon?.Part?.Location}|{id}",
                    $"Weapon at {weapon?.Part?.Location} on ship {weapon?.Ship?.UniqueID} holds fire: its barrel's current line to the target is blocked by {id} at {part?.Location} on assembly mate {other?.UniqueID} (fixture categories {(int)f.CollisionCategories:X}).");
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("WeaponMateLos.DiagBlock", ex);
            }
        }

        public static bool IsMate(Ship ship, Ship other)
        {
            if (ship == null || other == null || ReferenceEquals(ship, other)) return false;
            Ship[] members = AssemblyIndex.FreshAssemblyOf(ship);
            if (members == null) return false;
            for (int i = 0; i < members.Length; i++)
                if (ReferenceEquals(members[i], other)) return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(FixedWeapon), "GetEnemyPartInSights")]
    internal static class WeaponMateLineOfFirePatch
    {
        static void Postfix(FixedWeapon __instance, ref Part __result, ref Vector2 worldPoint)
        {
            try
            {
                if (__result == null) return;
                Ship ship = __instance?.Ship;
                Ship target = __result.Ship;
                if (ship?.Sim == null || target == null || ReferenceEquals(target, ship) || WeaponMateLos.IsMate(ship, target)) return;
                Ship[] members = AssemblyIndex.FreshAssemblyOf(ship);
                if (members == null || members.Length < 2) return;
                Segment line = __instance.GetWorldFireSegment(null, allowStretchedRange: false);
                Vector2 dir = (line.Point2 - line.Point1).Normalize();
                float reach = Math.Min(line.Point1.DistanceTo(in line.Point2), line.Point1.DistanceTo(in worldPoint));
                if (!(reach > 0f)) return;
                Vector2 end = line.Point1 + dir * reach;
                float radius = WeaponMateLos.ShotRadiusOf(__instance.GetTargetingEmitter());
                LogThrottle.Info($"matelos-radius|{__instance.Part?.Rules?.ID}", $"Weapon {__instance.Part?.Rules?.ID} on an assembly member: its line check sweeps the shot's half-width {radius:F3} (emitter {__instance.GetTargetingEmitter()?.GetType().Name}).");
                WeaponRules r = __instance.Rules;
                bool ignoreStructure = r.IgnoreFriendlyStructureLOSChecks, ignoreLow = r.IgnoreFriendlyShipLowLOSChecks, ignoreHigh = r.IgnoreFriendlyShipHighLOSChecks;
                Fixture hit = ship.Sim.PhysicsWorld.RayCastFirst(line.Point1, end, out Vector2 hitAt, out _, Category.Cat1, radius, (f, p) =>
                {
                    if (!(f.Body.UserData is Ship s) || ReferenceEquals(s, ship) || Array.IndexOf(members, s) < 0) return false;
                    Category c = f.CollisionCategories;
                    return WeaponMateLos.BlocksShot((c & Category.Cat3) != Category.None, (c & Category.Cat4) != Category.None,
                        (c & Category.Cat5) != Category.None, ignoreStructure, ignoreLow, ignoreHigh);
                });
                if (hit == null) return;
                WeaponMateLos.DiagBlock(__instance, hit.Body.UserData as Ship, hit, hitAt);
                __result = null;
                worldPoint = default(Vector2);
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("WeaponMateLineOfFirePatch", ex);
            }
        }
    }
}
