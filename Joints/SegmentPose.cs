using System;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Ships.Parts.Defenses;
using Cosmoteer.Ships.Parts.Weapons;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class SegmentPose
    {
        public static float[] AtDestination(float ax, float ay, float aRot, float mx, float my, float mRot, float dx, float dy, float dRot)
        {
            float ox = mx - ax, oy = my - ay;
            float c = (float)Math.Cos(-aRot), s = (float)Math.Sin(-aRot);
            float lx = ox * c - oy * s, ly = ox * s + oy * c;
            float c2 = (float)Math.Cos(dRot), s2 = (float)Math.Sin(dRot);
            return new[] { dx + lx * c2 - ly * s2, dy + lx * s2 + ly * c2, dRot + (mRot - aRot) };
        }
    }

    [HarmonyPatch(typeof(Command), "DrawWeaponCoverage")]
    internal static class SegmentCoveragePatch
    {
        static void Postfix(Command __instance, Vector2 destLoc, Direction destRot, bool showWeapons, bool showShields, Circle? localTargetCircle)
        {
            try
            {
                Ship anchor = __instance.Ship;
                if (anchor == null || HingeJointManager.ActivePinCount == 0) return;
                Ship[] members = AssemblyIndex.AssemblyOf(anchor);
                if (members == null || members.Length < 2) return;
                float fade = __instance.AdjustingSince.HasValue
                    ? Mathx.InverseLerp(__instance.AdjustingSince.Value, __instance.AdjustingSince.Value + __instance.Rules.WeaponCoverageFadeInDuration, App.Clock.Time)
                    : 1f;
                Vector2 ac = anchor.DetWorldCenter;
                float aRot = (float)anchor.DetRotation;
                foreach (Ship m in members)
                {
                    if (m == null || ReferenceEquals(m, anchor) || m.Weapons == null) continue;
                    Vector2 mc = m.DetWorldCenter;
                    float[] p = SegmentPose.AtDestination(ac.X, ac.Y, aRot, mc.X, mc.Y, (float)m.DetRotation, destLoc.X, destLoc.Y, (float)destRot);
                    var loc = new Vector2(p[0], p[1]);
                    var rot = new Direction(p[2]);
                    if (showShields)
                    {
                        Color shieldColor = Cosmoteer.Settings.GetCurrentEnemyColor();
                        shieldColor.A *= fade;
                        using (App.Graphics.PushTransformMultiply(Matrix.Translation(in loc) * Matrix.RotationZ(rot) * Matrix.Scaling(m.WorldScale) * Matrix.Translation(-m.Parts.LocalCenter)))
                        {
                            foreach (ArcShield shield in ArcShield.GetAllOnShip(m))
                                shield.DrawCoverageArc(shieldColor);
                        }
                    }
                    if (!showWeapons) continue;
                    Circle? memberCircle = null;
                    if (localTargetCircle.HasValue)
                    {
                        Circle c = localTargetCircle.Value;
                        Vector2 centre = m.DetTransformPointFromWorld(anchor.DetTransformPointToWorld(c.Center));
                        memberCircle = new Circle(in centre, c.Radius * anchor.UniformScale / m.UniformScale);
                    }
                    foreach (Weapon w in m.Weapons)
                    {
                        if (!w.Rules.AlwaysShowCoverage && !w.ShouldEstimateFirepower) continue;
                        Color color = Cosmoteer.Settings.GetCurrentEnemyColor();
                        if (!memberCircle.HasValue || !w.IsInCoverage(memberCircle.Value, WeaponCoverageMode.Displayed, checkLOS: true, null))
                            color *= __instance.Rules.WeaponOutOfCoverageColorFactor;
                        color.A *= fade;
                        w.DrawCoverageArea(loc, rot, color);
                    }
                }
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("SegmentCoveragePatch", ex);
            }
        }
    }
}
