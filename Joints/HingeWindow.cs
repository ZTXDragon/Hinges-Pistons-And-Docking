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
    internal static class HingePanelOpenPatch
    {
        public static readonly ID<PartTriggerGuiRules> TriggerId = new ID<PartTriggerGuiRules>("ztx_hinge_panel");

        static bool Prefix(PartTriggerButton __instance)
        {
            if (!__instance.Rules.TriggerID.Equals(TriggerId)) return true;
            try
            {
                foreach (Part p in __instance.Sim.PlayerInput.SelectedParts)
                {
                    if (PartUITrigger.GetOnPart(p, TriggerId) == null) continue;
                    HingeWindow.Open(__instance.Game, p);
                    break;
                }
            }
            catch (Exception ex)
            {
                Log.Exception("HingePanelOpenPatch.Prefix", ex);
            }
            return false;
        }
    }

    internal sealed class HingeWindow : RailPartWindow
    {
        private static HingeWindow s_instance;
        private readonly Label _status;

        public static void Open(GameRoot game, Part hinge)
        {
            if (game == null || hinge == null) return;
            if (s_instance == null || !ReferenceEquals(s_instance._game, game))
            {
                s_instance = new HingeWindow(game);
                game.Gui.FloatingWindows.AddChild(s_instance);
            }
            s_instance._part = hinge;
            s_instance.SelfActive = true;
            game.Gui.FloatingWindows.Children.RaiseToFront(s_instance);
        }

        public static void Reset()
        {
            s_instance = null;
        }

        private HingeWindow(GameRoot game) : base(game, "ZtxHingePanel/Title", new Vector2(420f, 260f))
        {
            AddToggleRow("ZtxPartPanel/Engage", "ZtxPartPanel/TipEngage", JointPower.OnOffId, "ZtxHingePanel/EngageOff", "ZtxHingePanel/EngageOn");
            AddTypedRow("ZtxPartPanel/LeftAngle", "ZtxPartPanel/UnitDeg", "ZtxPartPanel/TipLeftLimit",
                p => ReadLimit(p, s_leftId, true), v => WriteLimit(s_leftId, TypedSettings.LeftLimitId, v));
            AddTypedRow("ZtxPartPanel/RightAngle", "ZtxPartPanel/UnitDeg", "ZtxPartPanel/TipRightLimit",
                p => ReadLimit(p, s_rightId, false), v => WriteLimit(s_rightId, TypedSettings.RightLimitId, v));
            _status = new Label { Height = RowHeight };
            AddChild(_status);
        }

        private static readonly ID<PartToggleGuiRules> s_leftId = new ID<PartToggleGuiRules>("ztx_hinge_angle_left");
        private static readonly ID<PartToggleGuiRules> s_rightId = new ID<PartToggleGuiRules>("ztx_hinge_angle_right");

        private static float? ReadLimit(Part p, ID<PartToggleGuiRules> id, bool left)
        {
            PartUIToggle t = PartUIToggle.GetOnPart(p, id);
            if (t == null) return null;
            return left ? TypedSettings.LeftLimit(p, t.Mode) : TypedSettings.RightLimit(p, t.Mode);
        }

        private void WriteLimit(ID<PartToggleGuiRules> toggleId, ID<PartToggleGuiRules> typedId, float typed)
        {
            float v = Math.Max(TypedSettings.LIMIT_MIN, Math.Min(TypedSettings.LIMIT_MAX, typed));
            Write((toggleId, TypedSettings.LimitMode(v)));
            WriteValues((typedId, TypedSettings.Encode(v, TypedSettings.LIMIT_SCALE, TypedSettings.LIMIT_MIN, TypedSettings.LIMIT_MAX)));
        }

        protected override bool SameKind(Part p) => PartUITrigger.GetOnPart(p, HingePanelOpenPatch.TriggerId) != null;

        protected override void OnUpdatingUIState(object sender, EventArgs e)
        {
            try
            {
                if (PartGone()) { SelfActive = false; return; }
                RefreshTargets();
                FitToggleRows();
                RefreshTypedRows();
                LogicActuatorState.IsDone(_part, out string why);
                string status = Strings.FormatText("ZtxHingePanel/StatusFmt", why);
                ShowText(_status, status);
            }
            catch (Exception ex)
            {
                Log.Exception("HingeWindow.OnUpdatingUIState", ex);
                SelfActive = false;
            }
        }
    }
}
