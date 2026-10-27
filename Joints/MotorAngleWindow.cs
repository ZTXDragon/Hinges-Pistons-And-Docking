using System;
using System.Globalization;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Gui;
using Cosmoteer.Localization;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.UI;
using Halfling.Events;
using Halfling.Geometry;
using Halfling.Gui;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch(typeof(PartTriggerButton), "OnClicked")]
    internal static class MotorPanelOpenPatch
    {
        public static readonly ID<PartTriggerGuiRules> TriggerId = new ID<PartTriggerGuiRules>("ztx_motor_panel");

        static bool Prefix(PartTriggerButton __instance)
        {
            if (!__instance.Rules.TriggerID.Equals(TriggerId)) return true;
            try
            {
                foreach (Part p in __instance.Sim.PlayerInput.SelectedParts)
                {
                    if (PartUITrigger.GetOnPart(p, TriggerId) == null) continue;
                    MotorAngleWindow.Open(__instance.Game, p);
                    break;
                }
            }
            catch (Exception ex)
            {
                Log.Exception("MotorPanelOpenPatch.Prefix", ex);
            }
            return false;
        }
    }

    internal sealed class MotorAngleWindow : RailPartWindow
    {
        private static MotorAngleWindow s_instance;

        private readonly TextEditField _angle;
        private readonly Label _status;
        private int _lastTenths = -1;

        public static void Open(GameRoot game, Part motor)
        {
            if (game == null || motor == null) return;
            if (s_instance == null || !ReferenceEquals(s_instance._game, game))
            {
                s_instance = new MotorAngleWindow(game);
                game.Gui.FloatingWindows.AddChild(s_instance);
            }
            s_instance.Bind(motor);
            s_instance.SelfActive = true;
            game.Gui.FloatingWindows.Children.RaiseToFront(s_instance);
        }

        public static void Reset()
        {
            s_instance = null;
        }

        private MotorAngleWindow(GameRoot game) : base(game, "ZtxMotorPanel/Title", new Vector2(420f, 170f))
        {
            AddToggleRow("ZtxPartPanel/Engage", "ZtxPartPanel/TipEngage", JointPower.OnOffId, "ZtxMotorPanel/EngageOff", "ZtxMotorPanel/EngageOn");
            LayoutBox row = Row();
            row.DockedChildren.Add(RowLabel("ZtxMotorPanel/Angle", "ZtxMotorPanel/TipAngle"));
            _angle = LogicGui.Typing(new TextEditField(TextEditField.Style.SingleLine) { DockMode = DockMode.Left, Width = 80f });
            _angle.ToolTips.TextProvider = Strings.KeyString("ZtxMotorPanel/TipAngle");
            _angle.TextChanged += OnAngleChanged;
            row.DockedChildren.Add(_angle);
            AddChild(row);
            AddTypedRow("ZtxPartPanel/Speed", "ZtxPartPanel/UnitDegS", "ZtxPartPanel/TipMotorSpeed", ReadSpeed, WriteSpeed);
            AddToggleRow("ZtxPartPanel/Strength", "ZtxPartPanel/TipStrength", new ID<PartToggleGuiRules>("ztx_motor_strength"));
            AddToggleRow("ZtxPartPanel/Accel", "ZtxPartPanel/TipAccel", new ID<PartToggleGuiRules>("ztx_drive_accel"));
            _status = new Label { Height = RowHeight };
            AddChild(_status);
        }

        private static readonly ID<PartToggleGuiRules> s_speedId = new ID<PartToggleGuiRules>("ztx_motor_speed");
        private const int SPEED_DEFAULT_MODE = 4;

        private static float? ReadSpeed(Part p)
        {
            float[] table = ArmToggles.MotorSpeeds(p);
            if (table == null || PartUIToggle.GetOnPart(p, s_speedId) == null) return null;
            return TypedSettings.MotorSpeed(p, table, LogicSource.Mode(p, s_speedId, SPEED_DEFAULT_MODE));
        }

        private void WriteSpeed(float typed)
        {
            float[] table = ArmToggles.MotorSpeeds(_part);
            if (table == null) return;
            float v = Math.Max(TypedSettings.MOTOR_SPEED_MIN, Math.Min(TypedSettings.MOTOR_SPEED_MAX, typed));
            Write((s_speedId, PistonToggles.NearestSpeed(table, v)));
            WriteValues((TypedSettings.MotorSpeedId, TypedSettings.Encode(v, TypedSettings.MOTOR_SPEED_SCALE, TypedSettings.MOTOR_SPEED_MIN, TypedSettings.MOTOR_SPEED_MAX)));
        }

        protected override bool SameKind(Part p) => PartUITrigger.GetOnPart(p, MotorPanelOpenPatch.TriggerId) != null;

        protected override void Rebind(Part p) => Bind(p);

        private void Bind(Part motor)
        {
            _part = motor;
            _refreshing = true;
            try
            {
                int tenths = (int)Math.Round(ArmToggles.MotorDegrees(motor) * 10f);
                _lastTenths = tenths;
                LogicGui.SetFieldText(_angle, (tenths / 10f).ToString("0.#", CultureInfo.InvariantCulture));
            }
            finally { _refreshing = false; }
        }

        protected override void OnUpdatingUIState(object sender, EventArgs e)
        {
            try
            {
                if (PartGone()) { SelfActive = false; return; }
                RefreshTargets();
                FitToggleRows();
                RefreshTypedRows();
                int tenths = (int)Math.Round(ArmToggles.MotorDegrees(_part) * 10f);
                if (tenths != _lastTenths)
                {
                    _refreshing = true;
                    try
                    {
                        _lastTenths = tenths;
                        LogicGui.SetFieldText(_angle, (tenths / 10f).ToString("0.#", CultureInfo.InvariantCulture));
                    }
                    finally { _refreshing = false; }
                }
                LogicActuatorState.IsDone(_part, out string why);
                string status = Strings.FormatText("ZtxMotorPanel/StatusFmt", why);
                ShowText(_status, status);
            }
            catch (Exception ex)
            {
                Log.Exception("MotorAngleWindow.OnUpdatingUIState", ex);
                SelfActive = false;
            }
        }

        private void OnAngleChanged(object sender, ValueChangedEventArgs<string> e)
        {
            if (_refreshing || _part?.Ship == null) return;
            try
            {
                string t = (_angle.Text ?? "").Trim().Replace(',', '.');
                if (!float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out float deg)) return;
                int tenths = Math.Max(0, Math.Min(ArmToggles.ANGLE_MAX_TENTHS, (int)Math.Round(deg * 10f)));
                if (tenths == _lastTenths) return;
                _lastTenths = tenths;
                WriteValues((ArmToggles.AngleId, ArmToggles.EncodeAngle(tenths)));
                Log.Info("motor at " + _part.Location + ": angle -> " + (tenths / 10f).ToString("0.#", CultureInfo.InvariantCulture) + " deg.");
            }
            catch (Exception ex) { Log.Exception("MotorAngleWindow.OnAngleChanged", ex); }
        }
    }
}
