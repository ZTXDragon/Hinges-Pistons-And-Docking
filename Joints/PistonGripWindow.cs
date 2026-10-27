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
    internal static class GripPanelOpenPatch
    {
        public static readonly ID<PartTriggerGuiRules> TriggerId = new ID<PartTriggerGuiRules>("ztx_grip_panel");

        static bool Prefix(PartTriggerButton __instance)
        {
            ID<PartTriggerGuiRules> id = __instance.Rules.TriggerID;
            if (!id.Equals(TriggerId)) return true;
            try
            {
                foreach (Part p in __instance.Sim.PlayerInput.SelectedParts)
                {
                    if (PartUITrigger.GetOnPart(p, id) == null) continue;
                    PistonGripWindow.Open(__instance.Game, p);
                    break;
                }
            }
            catch (Exception ex)
            {
                Log.Exception("GripPanelOpenPatch.Prefix", ex);
            }
            return false;
        }
    }

    internal sealed class PistonGripWindow : RailPartWindow
    {
        private static PistonGripWindow s_instance;

        private readonly Label _status;

        public static Part Current => s_instance != null && s_instance.IsActive ? s_instance._part : null;

        public static void Open(GameRoot game, Part piston)
        {
            if (game == null || piston == null) return;
            if (s_instance == null || !ReferenceEquals(s_instance._game, game))
            {
                s_instance = new PistonGripWindow(game);
                game.Gui.FloatingWindows.AddChild(s_instance);
            }
            s_instance._part = piston;
            s_instance.SelfActive = true;
            game.Gui.FloatingWindows.Children.RaiseToFront(s_instance);
            SensorAreaOverlay.Attach(game.Sim);
        }

        public static void Reset()
        {
            s_instance = null;
        }

        private PistonGripWindow(GameRoot game) : base(game, "ZtxPistonPanel/Title", new Vector2(460f, 250f))
        {
            AddToggleRow("ZtxPartPanel/Engage", "ZtxPartPanel/TipEngage", JointPower.OnOffId, "ZtxPistonPanel/EngageOff", "ZtxPistonPanel/EngageOn");
            AddTypedRow("ZtxPartPanel/Extension", "ZtxPartPanel/UnitCells", "ZtxPartPanel/TipExtension", ReadExtension, WriteExtension);
            AddTypedRow("ZtxPartPanel/Speed", "ZtxPartPanel/UnitCellsS", "ZtxPartPanel/TipPistonSpeed", ReadSpeed, WriteSpeed);
            AddToggleRow("ZtxPartPanel/Strength", "ZtxPartPanel/TipStrength", new ID<PartToggleGuiRules>("ztx_motor_strength"));
            AddToggleRow("ZtxPartPanel/Accel", "ZtxPartPanel/TipAccel", new ID<PartToggleGuiRules>("ztx_drive_accel"));
            AddToggleRow("ZtxPartPanel/Merge", "ZtxPistonPanel/TipMerge", new ID<PartToggleGuiRules>("ztx_piston_merge"), "ZtxRailPanel/MergeOff", "ZtxRailPanel/MergeOn");
            AddToggleRow("ZtxPartPanel/Grip", "ZtxPistonPanel/TipGrip", new ID<PartToggleGuiRules>("ztx_piston_grip"), "ZtxRailPanel/GripOff", "ZtxRailPanel/GripAttach", "ZtxRailPanel/GripRelease");
            AddGripArea();
            _status = new Label { Height = RowHeight };
            AddChild(_status);
        }

        private static readonly ID<PartToggleGuiRules> s_extId = new ID<PartToggleGuiRules>("ztx_piston_extension");
        private static readonly ID<PartToggleGuiRules> s_speedId = new ID<PartToggleGuiRules>("ztx_piston_speed");
        private const int SPEED_DEFAULT_MODE = 3;

        private static float? ReadExtension(Part p)
        {
            PartUIToggle t = PartUIToggle.GetOnPart(p, s_extId);
            return t != null ? (float?)t.Mode : null;
        }

        private void WriteExtension(float typed)
        {
            PistonToggles.Limits(GripService.PistonOf(_part), out _, out int maxCells);
            int cells = Math.Max(0, Math.Min(maxCells, (int)Math.Round(typed, MidpointRounding.AwayFromZero)));
            Write((s_extId, cells));
        }

        private static float? ReadSpeed(Part p)
        {
            ZtxPistonBaseComponent piston = GripService.PistonOf(p);
            float[] table = piston?.Rules.SpeedModes;
            if (table == null || PartUIToggle.GetOnPart(p, s_speedId) == null) return null;
            return TypedSettings.PistonSpeed(p, table, LogicSource.Mode(p, s_speedId, SPEED_DEFAULT_MODE));
        }

        private void WriteSpeed(float typed)
        {
            float[] table = GripService.PistonOf(_part)?.Rules.SpeedModes;
            if (table == null) return;
            float v = Math.Max(TypedSettings.PISTON_SPEED_MIN, Math.Min(TypedSettings.PISTON_SPEED_MAX, typed));
            Write((s_speedId, PistonToggles.NearestSpeed(table, v)));
            WriteValues((TypedSettings.PistonSpeedId, TypedSettings.Encode(v, TypedSettings.PISTON_SPEED_SCALE, TypedSettings.PISTON_SPEED_MIN, TypedSettings.PISTON_SPEED_MAX)));
        }

        protected override bool SameKind(Part p) => PartUITrigger.GetOnPart(p, GripPanelOpenPatch.TriggerId) != null;

        protected override void OnUpdatingUIState(object sender, EventArgs e)
        {
            try
            {
                if (PartGone()) { SelfActive = false; return; }
                RefreshTargets();
                FitToggleRows();
                RefreshTypedRows();
                RefreshGripArea();
                ShowText(_status, Strings.FormatText("ZtxGripPanel/StatusFmt", GripService.StatusOf(_part)));
            }
            catch (Exception ex)
            {
                Log.Exception("PistonGripWindow.OnUpdatingUIState", ex);
                SelfActive = false;
            }
        }
    }
}
