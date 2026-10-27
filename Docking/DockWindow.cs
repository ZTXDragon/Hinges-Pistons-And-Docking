using System;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Localization;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.UI;
using Halfling.Geometry;
using Halfling.Gui;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch(typeof(PartTriggerButton), "OnClicked")]
    internal static class DockPanelOpenPatch
    {
        public static readonly ID<PartTriggerGuiRules> ClampTriggerId = new ID<PartTriggerGuiRules>("ztx_clamp_panel");
        public static readonly ID<PartTriggerGuiRules> PadTriggerId = new ID<PartTriggerGuiRules>("ztx_pad_panel");

        static bool Prefix(PartTriggerButton __instance)
        {
            ID<PartTriggerGuiRules> id = __instance.Rules.TriggerID;
            if (!id.Equals(ClampTriggerId) && !id.Equals(PadTriggerId)) return true;
            try
            {
                foreach (Part p in __instance.Sim.PlayerInput.SelectedParts)
                {
                    if (PartUITrigger.GetOnPart(p, id) == null) continue;
                    DockWindow.Open(__instance.Game, p);
                    break;
                }
            }
            catch (Exception ex)
            {
                Log.Exception("DockPanelOpenPatch.Prefix", ex);
            }
            return false;
        }
    }

    internal sealed class DockWindow : RailPartWindow
    {
        private static DockWindow s_instance;
        private readonly Label _status;
        private readonly Label _service;

        public static void Open(GameRoot game, Part dock)
        {
            if (game == null || dock == null) return;
            if (s_instance == null || !ReferenceEquals(s_instance._game, game))
            {
                s_instance = new DockWindow(game);
                game.Gui.FloatingWindows.AddChild(s_instance);
            }
            s_instance._part = dock;
            s_instance.SelfActive = true;
            game.Gui.FloatingWindows.Children.RaiseToFront(s_instance);
        }

        public static void Reset()
        {
            s_instance = null;
        }

        private DockWindow(GameRoot game) : base(game, "ZtxDockPanel/Title", new Vector2(460f, 160f))
        {
            AddToggleRow("ZtxDockPanel/Connect", "ZtxDockPanel/TipConnect", new ID<PartToggleGuiRules>("ztx_dock_active"), "ZtxDockPanel/Off", "ZtxDockPanel/On");
            AddToggleRow("ZtxDockPanel/Connect", "ZtxDockPanel/TipConnect", DockShipManager.PadActiveId, "ZtxDockPanel/Off", "ZtxDockPanel/On");
            AddToggleRow("ZtxDockPanel/Thrust", "ZtxDockPanel/TipThrust", DockThrust.ThrustToggleId, "ZtxDockPanel/Off", "ZtxDockPanel/On");
            AddToggleRow("ZtxDockPanel/Join", "ZtxDockPanel/TipJoin", DockJoin.JoinToggleId, "ZtxDockPanel/JoinOff", "ZtxDockPanel/JoinOn", "ZtxDockPanel/JoinMerge");
            AddToggleRow("ZtxDockPanel/Crew", "ZtxDockPanel/TipCrew", HatchLinks.ClampCrewId, "ZtxDockPanel/CrewNone", "ZtxDockPanel/CrewGive", "ZtxDockPanel/CrewReceive");
            AddToggleRow("ZtxDockPanel/Supplies", "ZtxDockPanel/TipSupplies", DockShipManager.ClampResupplyId, "ZtxDockPanel/SupplyNone", "ZtxDockPanel/SupplyGive", "ZtxDockPanel/SupplyReceive");
            _service = new Label { Height = RowHeight };
            AddChild(_service);
            _status = new Label { Height = RowHeight };
            AddChild(_status);
        }

        protected override void OnUpdatingUIState(object sender, EventArgs e)
        {
            try
            {
                if (PartGone()) { SelfActive = false; return; }
                RefreshTargets();
                FitToggleRows();
                LogicActuatorState.IsDone(_part, out string why);
                ShowText(_service, ZtxDockClampComponent.ForPart(_part)?.ServiceStatus ?? "");
                ShowText(_status, Strings.FormatText("ZtxDockPanel/StatusFmt", why));
            }
            catch (Exception ex)
            {
                Log.Exception("DockWindow.OnUpdatingUIState", ex);
                SelfActive = false;
            }
        }
    }
}
