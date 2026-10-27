using System;
using System.Collections.Generic;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Rendering;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch(typeof(ShipRenderer), nameof(ShipRenderer.CaptureScreenShot), new Type[] { typeof(RenderTarget), typeof(bool) })]
    internal static class AssemblyScreenshotPatch
    {
        static bool Prefix(ShipRenderer __instance, RenderTarget rt, bool showRoof)
        {
            try
            {
                Ship ship = __instance.Ship;
                if (rt == null || ship == null || !AssemblyFacade.Enabled || HingeJointManager.ActivePinCount == 0) return true;
                Ship[] segs = AssemblyAi.SegmentsOf(ship);
                if (segs == null) return true;
                var renderers = new List<ShipRenderer> { __instance };
                foreach (Ship m in segs)
                    if (m != null && !ReferenceEquals(m, ship) && m.Renderer != null && m.Parts.Count > 0) renderers.Add(m.Renderer);
                if (renderers.Count < 2) return true;
                Capture(renderers, rt, showRoof);
                return false;
            }
            catch (Exception ex)
            {
                Log.Exception("AssemblyScreenshotPatch (the image shows the main ship alone)", ex);
                return true;
            }
        }

        private static void Capture(List<ShipRenderer> renderers, RenderTarget rt, bool showRoof)
        {
            ShipRenderer main = renderers[0];
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (ShipRenderer r in renderers)
            {
                r.RefreshGeometry();
                IntRect? b = r.Ship.Parts.BoundingRect;
                if (!b.HasValue) continue;
                minX = Math.Min(minX, b.Value.Left);
                minY = Math.Min(minY, b.Value.Top);
                maxX = Math.Max(maxX, b.Value.Right);
                maxY = Math.Max(maxY, b.Value.Bottom);
            }
            IntRect union = minX == int.MaxValue ? IntRect.Zero : new IntRect(minX, minY, maxX - minX, maxY - minY);
            int x = rt.SourceSize.X;
            int y = rt.SourceSize.Y;
            Rect rect = ((Rect)union).Inflate(3f, 3f);
            float num = Mathx.Min((float)x / rect.Width, (float)y / rect.Height);
            using (App.Graphics.BeginDraw())
            {
                IntRect? viewport = App.Graphics.Viewport;
                using (App.Graphics.PushTransform())
                {
                    using (App.Graphics.PushClipper())
                    {
                        using (App.Graphics.PushRenderTarget())
                        {
                            App.Graphics.Transform = Matrix.Scaling(2f / (float)x * num, -2f / (float)y * num) * Matrix.Translation(-rect.Center);
                            App.Graphics.Viewport = new IntRect(0, 0, x, y);
                            App.Graphics.Clipper = null;
                            App.Graphics.SetMatrix(Cosmoteer.ShaderConstantIDs.InvShipRotMatrix, Matrix.Identity);
                            using RenderTarget stencil = App.Graphics.CreateRenderTarget(rt.SourceSize);
                            App.Graphics.RenderTarget = stencil;
                            App.Graphics.Clear(Color.Zero, 1f, 0);
                            using RenderTarget normals = App.Graphics.CreateRenderTarget(rt.SourceSize);
                            App.Graphics.RenderTarget = normals;
                            App.Graphics.Clear(new Color(0.5f, 0.5f, 0.5f, 0f), 1f, 0);
                            using RenderTarget diffuse = App.Graphics.CreateRenderTarget(rt.SourceSize);
                            App.Graphics.RenderTarget = diffuse;
                            App.Graphics.Clear();
                            using RenderTarget light = App.Graphics.CreateRenderTarget(rt.SourceSize);
                            App.Graphics.RenderTarget = light;
                            App.Graphics.Clear();
                            using RenderTarget decals = App.Graphics.CreateRenderTarget(rt.SourceSize);
                            App.Graphics.RenderTarget = decals;
                            App.Graphics.Clear(Color.Zero, 1f, 0);
                            App.Graphics.RenderTarget = rt;
                            App.Graphics.Clear(Color.Zero, 1f, 0);
                            App.Graphics.SetTexture(Cosmoteer.ShaderConstantIDs.StencilTarget, stencil);
                            App.Graphics.SetTexture(Cosmoteer.ShaderConstantIDs.NormalsTarget, normals);
                            App.Graphics.SetTexture(Cosmoteer.ShaderConstantIDs.DiffuseTarget, diffuse);
                            App.Graphics.SetTexture(Cosmoteer.ShaderConstantIDs.RoofDecalsTarget, decals);
                            float roofOpacity = showRoof ? 1 : 0;
                            foreach (ShipRenderer r in renderers)
                                r.DrawStage(main.Rules.LowestRenderLayers, stencil, normals, diffuse, light, null, roofOpacity, suppressIndicators: true, Color.White);
                            foreach (ShipRenderer r in renderers)
                                r.DrawStage(main.Rules.LowRenderLayers, stencil, normals, diffuse, light, decals, roofOpacity, suppressIndicators: true, Color.White);
                            foreach (ShipRenderer r in renderers)
                                r.DrawStage(main.Rules.MiddleRenderLayers, stencil, normals, diffuse, light, decals, roofOpacity, suppressIndicators: true, Color.White);
                            foreach (ShipRenderer r in renderers)
                                r.DrawCrew(Color.White);
                            foreach (ShipRenderer r in renderers)
                                r.DrawStage(main.Rules.HighRenderLayers, stencil, normals, diffuse, light, showRoof ? decals : null, roofOpacity, suppressIndicators: true, Color.White);
                            foreach (ShipRenderer r in renderers)
                                r.DrawStage(main.Rules.HighestRenderLayers, stencil, normals, diffuse, light, null, roofOpacity, suppressIndicators: true, Color.White);
                            App.Graphics.SetToMaterial(light);
                            App.Graphics.TargetBlendMode = new TargetBlendMode(BlendFactor.InverseDestColor, BlendFactor.One, BlendOperator.Add, BlendFactor.Zero, BlendFactor.One, BlendOperator.Add);
                            App.Graphics.Shader = Cosmoteer.GameApp.Rules.LightBufferShader;
                            using (App.Graphics.PushTransform(Matrix.ProjectionOrthographic(1f, 1f)))
                            {
                                App.Graphics.Draw(new Quad(new Rect(0f, 0f, 1f, 1f)));
                            }
                            App.Graphics.Texture = null;
                            App.Graphics.SetTexture(Cosmoteer.ShaderConstantIDs.StencilTarget, null);
                            App.Graphics.SetTexture(Cosmoteer.ShaderConstantIDs.RoofDecalsTarget, null);
                        }
                    }
                }
                App.Graphics.Viewport = viewport;
            }
            Log.Info($"Ship file image: ship {main.Ship.UniqueID} drawn with {renderers.Count - 1} segment(s) at home.");
        }
    }
}
