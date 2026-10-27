using System;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.UI;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch(typeof(PartTriggerButton), "OnClicked")]
    internal static class LogicConsoleOpenPatch
    {
        public static readonly ID<PartTriggerGuiRules> PanelTriggerId =
            new ID<PartTriggerGuiRules>("ztx_logic_panel");

        static bool Prefix(PartTriggerButton __instance)
        {
            try
            {
                if (!__instance.Rules.TriggerID.Equals(PanelTriggerId)) return true;
                Part target = null;
                foreach (Part p in __instance.Sim.PlayerInput.SelectedParts)
                {
                    if (PartUITrigger.GetOnPart(p, PanelTriggerId) != null)
                    {
                        target = p;
                        break;
                    }
                }
                if (target == null)
                {
                    Log.Info("console: button clicked with no console part selected.");
                    return false;
                }
                LogicConsoleWindow.Open(__instance.Game, target);
            }
            catch (Exception ex)
            {
                Log.Exception("LogicConsoleOpenPatch.Prefix", ex);
            }
            return false;
        }
    }
}
