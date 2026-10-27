using System;
using System.Collections.Generic;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Rendering;
using Halfling.Graphics;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch(typeof(ShipRenderer), "DrawStage", new Type[] {
        typeof(List<ShipRenderLayerRules>), typeof(RenderTarget), typeof(RenderTarget),
        typeof(RenderTarget), typeof(RenderTarget), typeof(RenderTarget),
        typeof(float), typeof(bool), typeof(Color?) })]
    internal static class RoofDecalsStagePatch
    {
        private static bool s_failed;

        static void Prefix(ShipRenderer __instance, List<ShipRenderLayerRules> layers,
            ref RenderTarget roofDecalsTarget, float roofOpacity, Color? overrideColorFactor)
        {
            if (s_failed || roofDecalsTarget != null || overrideColorFactor.HasValue || roofOpacity <= 0f)
                return;
            try
            {
                if (!ReferenceEquals(layers, __instance.Rules.HighestRenderLayers)) return;
                if (!HasRoofQuads(__instance, layers)) return;
                roofDecalsTarget = __instance.Sim?.Rendering.RoofDecalsTarget;
            }
            catch (Exception ex)
            {
                s_failed = true;
                Log.Exception("RoofDecalsStagePatch", ex);
            }
        }

        private static bool HasRoofQuads(ShipRenderer renderer, List<ShipRenderLayerRules> layers)
        {
            for (int i = 0; i < layers.Count; i++)
            {
                ShipRenderLayerRules srlr = layers[i];
                if (srlr.IsRoof && renderer._layersByRules.TryGetValue(srlr, out AtlasQuadManager aqm) && aqm.Count > 0)
                    return true;
            }
            return false;
        }
    }
}
