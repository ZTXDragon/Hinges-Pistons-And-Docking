using System;
using Cosmoteer.Game;
using Cosmoteer.Simulation;
using Halfling;
using Halfling.Input;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch(typeof(GameRoot), "Input", new Type[] { typeof(Action) })]
    public static class DebugJointHotkeyPatch
    {
        public const bool HOTKEYS = false;

        static void Postfix(GameRoot __instance)
        {
            if (!HOTKEYS) return;
            try
            {
                bool overlayToggle = App.Keyboard.HotkeyPressed(ViKey.Shift, ViKey.K);
                bool presetCycle = !overlayToggle && App.Keyboard.HotkeyPressed(ViKey.Control, ViKey.K);
                if (!overlayToggle && !presetCycle) return;

                SimRoot sim = __instance.Sim;
                if (sim == null) return;

                if (presetCycle)
                {
                    if (__instance.NetManager?.IsMultiplayer ?? false)
                    {
                        Log.Info("Ctrl+K (flight preset) is single-player only: in multiplayer every machine must fly alike.");
                        return;
                    }
                    AssemblyFlightFeatures.Toggle();
                    return;
                }
                SkeletonOverlay.Toggle(sim);
            }
            catch (Exception ex)
            {
                Log.Exception("DebugJointHotkeyPatch.Postfix", ex);
            }
        }
    }

    [HarmonyPatch(typeof(SimRoot), "Dispose", new Type[0])]
    public static class SimRootDisposeResetPatch
    {
        static void Postfix(SimRoot __instance)
        {
            try
            {
                HingeJointManager.ResetSim(__instance);
            }
            catch (Exception ex)
            {
                Log.Exception("SimRootDisposeResetPatch.ResetSim", ex);
            }
            try
            {
                SensorAreaOverlay.Reset(__instance);
                RemoteLinkWindow.Reset();
                RemoteLinks.Reset();
                LogThrottle.Reset();
                FlightProbe.Reset();
                PistonGripWindow.Reset();
                MotorAngleWindow.Reset();
                HingeWindow.Reset();
                DockWindow.Reset();
                BayTerminalWindow.Reset();
                BayStationWindow.Reset();
                DockThrust.Reset();
                AssemblyIndex.Reset(__instance);
                AssemblyFlightFeatures.Reset();
                SkeletonOverlay.Reset();
                PistonRodRenderer.Reset();
                LogicConsoleWindow.Reset();
                RailCartWindow.Reset();
                RailStopWindow.Reset();
                RailTerminalWindow.Reset();
                AreaSensorWindow.Reset();
                ProximitySensorWindow.Reset();
                ShipSensorWindow.Reset();
                AdapterWindow.Reset();
                LampWindow.Reset();
                ConsoleHelpWindow.Reset();
                LogicConsoleHighlight.Reset();
                RailShipManager.ResetStatics();
                RailRenderOrder.Reset();
                RailPlacementPatch.Reset();
                CorridorHinge.Reset();
                DrivePool.Reset();
                HingeJointManager.ResetQuietReleases();
                AssemblySupply.Reset();
            }
            catch (Exception ex)
            {
                Log.Exception("SimRootDisposeResetPatch.Postfix", ex);
            }
        }
    }
}
