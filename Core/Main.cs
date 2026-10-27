using System;
using System.Reflection;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    public static class Main
    {
        private static bool s_initialized;

        public static void AssemblyLoadInitializer() => RunInitOnce();
        public static void InitializePatches() => RunInitOnce();

        private static void RunInitOnce()
        {
            if (s_initialized) return;
            s_initialized = true;
            try
            {
                var harmony = new Harmony("ztx.hinges_pistons_docking");
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                Log.Info(AssemblyWeaponReadPatch.Summary);
                Log.Info(AssemblyHaltSitesPatch.Summary);
                Log.Info(AssemblyShipWideSitesPatch.Summary);
                Log.Info(AssemblyCommandSelectionPatch.Summary);
                Log.Info(UiFanoutButtonSitesPatch.Summary);
                Log.Info(UiFanoutSameTypePatch.Summary);
                Log.Info(SelectionCallShimsPatch.Summary);
                Log.Info(AssemblyJobsSitesPatch.Summary);
                Log.Info(SalvageOrderUiPatch.Summary);
                Log.Info(AssemblyReachSitesPatch.Summary);
                Log.Info(AssemblyCrewSitePatch.Summary);
                Log.Info(OverlayFanoutPatch.Summary);
                Log.Info(AggroWholeShipPatch.Summary);
                Log.Info(ModeSitesPatch.Summary);
                Log.Info(ClaimCanApplySitesPatch.Summary);
                Log.Info(RecenterButtonPatch.Summary);
                Log.Info(JumpAnimInDelayPatch.Summary);
                Log.Info(AssemblyCrewGatePatch.Summary);
                Log.Info(CorridorExitPatch.Summary);
                Log.Info(CorridorRoutePatch.Summary);
                Log.Info(SeamContigPatch.Summary);
                Log.Info(SeamSourcePatch.Summary);
                try { SeamReach.TryApply(harmony); }
                catch (Exception ex) { Log.Exception("SeamReach.TryApply", ex); }
                Log.Info(SeamReach.Summary);
                Log.Info(CorridorEntryPatch.Summary);
                Log.Info(CorridorNoHatchPatch.Summary);
                Log.Info(AssemblyCrewPoolPatch.Summary);
                Log.Info(CrewTargetsPickerPatch.Summary);
                ClaimOverlay.Register();
                PerfProbe.Install(harmony);
                try
                {
                    RailRenderOrder.TryApply(harmony);
                }
                catch (Exception ex)
                {
                    Log.Exception("RailRenderOrder.TryApply", ex);
                }
                Log.Info("init OK. " + Log.VERSION
                         + " | .NET " + Environment.Version
                         + " | OS " + Environment.OSVersion.VersionString);
                int catalogFails = LogicOpCatalog.SelfTest(out string catalogFirst);
                Log.Info("logic catalog self-test: " + (catalogFails == 0 ? "ok" : catalogFails + " failed, first: " + catalogFirst));
                int exprFails = LogicExpr.SelfTest(out string exprFirst);
                Log.Info("logic expression self-test: " + (exprFails == 0 ? "ok" : exprFails + " failed, first: " + exprFirst));
                int actFails = LogicAction.SelfTest(out string actFirst);
                Log.Info("logic actuator self-test: " + (actFails == 0 ? "ok" : actFails + " failed, first: " + actFirst));
                int storeFails = LogicStore.SelfTest(out string storeFirst);
                Log.Info("logic store self-test: " + (storeFails == 0 ? "ok" : storeFails + " failed, first: " + storeFirst));
                int readerFails = LogicStoreReader.SelfTest(out string readerFirst);
                Log.Info("logic store reader self-test: " + (readerFails == 0 ? "ok" : readerFails + " failed, first: " + readerFirst));
            }
            catch (Exception ex)
            {
                Log.Exception("Main.RunInitOnce", ex);
            }
        }
    }
}
