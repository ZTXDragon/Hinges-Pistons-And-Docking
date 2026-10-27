using System;
using System.Collections.Generic;
using Cosmoteer;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Rendering;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch]
    internal static class AssemblyIconPatch
    {
        private static readonly Type s_infoType = AccessTools.Inner(typeof(ShipRenderer), "IconInfo");
        private static readonly AccessTools.FieldRef<object, RenderTarget> s_iconF =
            AccessTools.FieldRefAccess<RenderTarget>(s_infoType, "Icon");
        private static readonly AccessTools.FieldRef<object, uint> s_countF =
            AccessTools.FieldRefAccess<uint>(s_infoType, "ChangeCount");
        private static readonly AccessTools.FieldRef<object, Halfling.Timing.Time> s_timeF =
            AccessTools.FieldRefAccess<Halfling.Timing.Time>(s_infoType, "LastChangeTime");

        static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(ShipRenderer), "RefreshIcon");
        }

        private static uint LayerChangeCount(Ship ship)
        {
            uint n = 0u;
            var renderer = ship.Renderer;
            foreach (var layer in ship.Rules.RenderLayers.Values)
            {
                if (layer.GhostMaterial != null && renderer._layersByRules.TryGetValue(layer, out var aqm))
                    n += aqm.ChangeCount;
            }
            return n;
        }

        static bool Prefix(ShipRenderer __instance, object info, IntVector2 resolution)
        {
            try
            {
                Ship root = __instance.Ship;
                if (root == null || !ReferenceEquals(AssemblyFacade.RootOf(root), root)) return true;
                var members = new List<Ship>();
                HingeJointManager.GetAssemblyShips(root, members, rigidOnly: false, identity: true);
                if (members.Count <= 1) return true;

                Matrix rootInv = root.WorldTransform.Invert();
                Rect rect = (Rect)(root.Parts.BoundingRect ?? IntRect.Zero);
                uint key = LayerChangeCount(root);
                var rels = new List<(Ship Ship, Matrix Rel)>();
                foreach (Ship m in members)
                {
                    if (ReferenceEquals(m, root) || m?.Renderer == null || m.Parts.Count == 0) continue;
                    Matrix rel = rootInv * m.WorldTransform;
                    rels.Add((m, rel));
                    Rect mr = (Rect)(m.Parts.BoundingRect ?? IntRect.Zero);
                    Vector2 a = rel.TransformPoint(new Vector2(mr.Left, mr.Top));
                    Vector2 b = rel.TransformPoint(new Vector2(mr.Right, mr.Top));
                    Vector2 c = rel.TransformPoint(new Vector2(mr.Right, mr.Bottom));
                    Vector2 d = rel.TransformPoint(new Vector2(mr.Left, mr.Bottom));
                    float minX = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X));
                    float maxX = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X));
                    float minY = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
                    float maxY = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));
                    rect = Rect.BoundRects(in rect, Rect.FromEdges(minX, minY, maxX, maxY));

                    Vector2 o = rel.TransformPoint(Vector2.Zero);
                    Vector2 ax = rel.TransformPoint(new Vector2(1f, 0f));
                    int qx = (int)Math.Round(o.X), qy = (int)Math.Round(o.Y);
                    int qa = (int)Math.Round(Math.Atan2(ax.Y - o.Y, ax.X - o.X) * 6.0);
                    unchecked
                    {
                        key = key * 31u + LayerChangeCount(m);
                        key = key * 31u + (uint)qx * 7u + (uint)qy * 13u + (uint)qa * 17u;
                    }
                }
                if (rels.Count == 0) return true;

                var oi = GameApp.Rules.Game.GameGui.ObjectIndicators;
                Vector2 resF = (Vector2)resolution / oi.ShipIconGlowShipScale * 2f;
                IntVector2 res = (IntVector2)resF;
                RenderTarget icon = s_iconF(info);
                if (icon != null && icon.ActualSize == res &&
                    (key == s_countF(info) || App.Clock.Time < s_timeF(info) + 3f))
                    return false;
                s_countF(info) = key;
                s_timeF(info) = App.Clock.Time;
                if (icon == null || icon.ActualSize != res)
                {
                    icon?.Dispose();
                    icon = App.Graphics.CreateRenderTarget(res, depthStencil: false, 0);
                    icon.SampleMode = TextureSampleMode.Linear;
                    s_iconF(info) = icon;
                }

                float k = 2f / Math.Max(rect.Width, rect.Height) * oi.ShipIconGlowShipScale;
                Vector2 center = rect.Center;
                Matrix baseXf = Matrix.Scaling(k, 0f - k) * Matrix.Translation(-center);

                using (App.Graphics.PushClipper(null))
                using (App.Graphics.PushRenderTarget(icon))
                using (App.Graphics.PushViewport(null))
                {
                    StencilBufferMode sbm = App.Graphics.StencilBufferMode;
                    App.Graphics.StencilBufferMode = StencilBufferMode.Disabled;
                    ChannelMask cwm = App.Graphics.ChannelWriteMask;
                    App.Graphics.ChannelWriteMask = ChannelMask.All;
                    using (App.Graphics.PushTransform(baseXf))
                    {
                        App.Graphics.Clear(Color.TransparentWhite, 1f, 0);
                        DrawAll(__instance, rels);
                    }
                    using (App.Graphics.PushTransform(Matrix.ProjectionOrthographic(1f, 1f)))
                    {
                        RenderTarget scratch = RenderTargetCache.Alloc(res);
                        App.Graphics.SetToMaterial(scratch);
                        App.Graphics.Shader = oi.ShipIconGlowShader;
                        App.Graphics.TargetBlendMode = TargetBlendMode.ReplaceNoBlend;
                        Quad primitive = new Quad(new Rect(0f, 0f, 1f, 1f));
                        int passes = (int)Math.Ceiling(oi.ShipIconGlowIterations * (float)Math.Max(res.X, res.Y));
                        for (int i = 0; i < passes; i++)
                        {
                            icon.CopyTo(scratch);
                            App.Graphics.SetVector2("_direction", new Vector2(oi.ShipIconGlowDisplacement / (float)res.X, 0f));
                            App.Graphics.Draw(in primitive);
                            icon.CopyTo(scratch);
                            App.Graphics.SetVector2("_direction", new Vector2(0f, oi.ShipIconGlowDisplacement / (float)res.Y));
                            App.Graphics.Draw(in primitive);
                        }
                        App.Graphics.Texture = null;
                        RenderTargetCache.Release(scratch);
                    }
                    using (App.Graphics.PushTransform(baseXf))
                    {
                        DrawAll(__instance, rels);
                    }
                    App.Graphics.StencilBufferMode = sbm;
                    App.Graphics.ChannelWriteMask = cwm;
                }
                icon.GenerateMipLevels();
                return false;
            }
            catch (Exception ex)
            {
                Log.Exception("AssemblyIconPatch.Prefix", ex);
                return true;
            }
        }

        private static void DrawAll(ShipRenderer rootRenderer, List<(Ship Ship, Matrix Rel)> rels)
        {
            rootRenderer.DrawLocalShipGhost(Color.White);
            foreach (var (m, rel) in rels)
            {
                using (App.Graphics.PushTransformMultiply(rel))
                    m.Renderer.DrawLocalShipGhost(Color.White);
            }
        }
    }
}
