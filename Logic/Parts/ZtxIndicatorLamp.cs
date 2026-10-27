using System;
using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Logic;
using Cosmoteer.Ships.Parts.UI;
using Halfling.Geometry;
using Halfling.Graphics;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.HingesPistonsDocking
{
    internal static class LampToggles
    {
        public static readonly ID<PartToggleGuiRules> StateId = new ID<PartToggleGuiRules>("ztx_lamp_state");
        public static readonly ID<PartToggleGuiRules> MessageId = new ID<PartToggleGuiRules>("ztx_lamp_message");
        public static readonly ID<PartToggleGuiRules> Name1Id = new ID<PartToggleGuiRules>("ztx_lamp_name_1");
        public static readonly ID<PartToggleGuiRules> Name2Id = new ID<PartToggleGuiRules>("ztx_lamp_name_2");
        public static readonly ID<PartToggleGuiRules> SizeId = new ID<PartToggleGuiRules>("ztx_lamp_size");
        public static readonly ID<PartToggleGuiRules> RgbId = new ID<PartToggleGuiRules>("ztx_lamp_rgb");
        public static readonly ID<PartToggleGuiRules> OldColourId = new ID<PartToggleGuiRules>("ztx_lamp_colour");
        private const int RGB_SET = 1 << 24;
        public const int STATE_OFF = 0, STATE_ON = 1, STATE_BLINK = 2;
        public const int LIGHT_LEAVE = 0, SIZES = 3;

        public static readonly Color[] Presets =
        {
            new Color(1f, 0.275f, 0.235f), new Color(1f, 0.706f, 0.157f), new Color(0.275f, 0.902f, 0.431f), new Color(0.275f, 0.667f, 1f), new Color(0.961f, 0.961f, 0.961f),
        };

        public static int Code(int light) => Math.Max(0, Math.Min(3, light));

        public static void PlanDo(int code, out int? state)
        {
            int l = code & 7;
            state = l >= 1 && l <= 3 ? l - 1 : (int?)null;
        }

        public static int ConvertColour(int? old, bool rgbSet) =>
            !rgbSet && old.HasValue && old.Value >= 1 && old.Value < Presets.Length ? old.Value : -1;

        public static int PackRgb(Color c)
        {
            int Byte(float v) => Math.Max(0, Math.Min(255, (int)Math.Round(v * 255f)));
            return RGB_SET | (Byte(c.R) << 16) | (Byte(c.G) << 8) | Byte(c.B);
        }

        public static Color? UnpackRgb(int v) =>
            (v & RGB_SET) == 0 ? (Color?)null : new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f);

        public static bool AnnounceDue(int state, bool wasLit, bool messageOn) =>
            messageOn && (state == STATE_BLINK || (state == STATE_ON && !wasLit));

        public static bool Lit(int state, bool phase) => state == STATE_ON || (state == STATE_BLINK && phase);

        public static string StoredName(Part lamp)
        {
            if (lamp == null) return "";
            return (LogicRuleDef.UnpackName(LogicSource.Mode(lamp, Name1Id, 0)) + LogicRuleDef.UnpackName(LogicSource.Mode(lamp, Name2Id, 0))).TrimEnd();
        }

        public static Color ColourOf(Part lamp) => UnpackRgb(LogicSource.Mode(lamp, RgbId, 0)) ?? Presets[0];

        public static string ApplyDo(Part part, LogicAction a)
        {
            if (part == null || part.IsDestroyed || part.Ship == null || !a.Sided || !a.OnSet) return "";
            PlanDo(a.OnPose, out int? state);
            return state.HasValue && WriteToggle(part, StateId, state.Value) ? "light " + state.Value : "";
        }

        private static bool WriteToggle(Part part, ID<PartToggleGuiRules> id, int value)
        {
            PartUIToggle t = PartUIToggle.GetOnPart(part, id);
            if (t == null || t.Mode == value) return false;
            t.Mode = value;
            t.Part.MatchingBlueprintPart?.OnPartUIToggleStatesChanged();
            return true;
        }
    }

    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxIndicatorLamp")]
    internal class ZtxIndicatorLampRules : PartComponentRules
    {
        public override PartComponent CreateComponent()
        {
            return new ZtxIndicatorLampComponent(this);
        }
    }

    internal class ZtxIndicatorLampComponent : ZtxLogicTickerComponent
    {
        private readonly List<ZtxLampGlowComponent> _glows = new List<ZtxLampGlowComponent>();
        private bool _wasLit;
        private bool _converted;

        public ZtxIndicatorLampComponent(ZtxIndicatorLampRules rules) : base(rules) { }

        public override void Tick()
        {
            Part me = base.Part;
            if (me == null || me.Ship == null) return;
            if (!_converted) ConvertOldColour(me);
            int state = LogicSource.Mode(me, LampToggles.StateId, LampToggles.STATE_OFF);
            int now = LogicShipManager.TryGet(me.Ship)?.TickCount ?? 0;
            int half = Math.Max(1, (int)Math.Round(0.5f / Math.Max(0.001f, LogicShipManager.TickSeconds)));
            bool phase = (now / half) % 2 == 0;
            bool lit = state == LampToggles.STATE_ON || state == LampToggles.STATE_BLINK;
            SetToggle(lit);
            _glows.Clear();
            foreach (PartComponent c in me.Components)
                if (c is ZtxLampGlowComponent g) _glows.Add(g);
            foreach (ZtxLampGlowComponent g in _glows) g.Set(LampToggles.Lit(state, phase));
            if (LampToggles.AnnounceDue(state, _wasLit, LogicSource.Mode(me, LampToggles.MessageId, 0) == 1)) Announce(me);
            _wasLit = lit;
        }

        private void ConvertOldColour(Part me)
        {
            _converted = true;
            var psd = new PartSaveData(me);
            ShipMetadata meta = me.Ship.Metadata;
            int? old = meta.PartUIToggleStates.Get((psd, LampToggles.OldColourId));
            if (!old.HasValue) return;
            bool rgbSet = LampToggles.UnpackRgb(LogicSource.Mode(me, LampToggles.RgbId, 0)).HasValue;
            int preset = LampToggles.ConvertColour(old, rgbSet);
            PartUIToggle rgb = PartUIToggle.GetOnPart(me, LampToggles.RgbId);
            if (preset >= 0 && rgb != null)
            {
                rgb.Mode = LampToggles.PackRgb(LampToggles.Presets[preset]);
                Log.Info($"indicator lamp at {me.Location}: old colour {preset} kept as its colour.");
            }
            meta.PartUIToggleStates.Set((psd, LampToggles.OldColourId), null);
        }

        private static void Announce(Part me)
        {
            string name = LampToggles.StoredName(me);
            if (name.Length == 0) return;
            Ship ship = me.Ship;
            IntRect r = me.Rect;
            float scale = ship.Scale.X > 0f ? ship.Scale.X : 1f;
            Vector2 world = ship.Physics?.Body != null
                ? ship.Physics.Body.GetWorldPoint(new Vector2((r.X + r.Width * 0.5f) * scale, (r.Y + r.Height * 0.5f) * scale))
                : ship.Sim.WorldMouseLoc;
            BayText.ShowAt(ship, "Lamp|" + me.Location, name, LampToggles.ColourOf(me), world);
        }
    }

    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxLampGlow")]
    internal class ZtxLampGlowRules : PartComponentRules
    {
        public override PartComponent CreateComponent()
        {
            return new ZtxLampGlowComponent(this);
        }
    }

    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxLampColour")]
    internal class ZtxLampColourRules : PartComponentRules
    {
        public override PartComponent CreateComponent()
        {
            return new ZtxLampColourComponent(this);
        }
    }

    internal class ZtxLampColourComponent : PartComponent, Cosmoteer.Ships.Parts.Graphics.IPartComponentColor
    {
        private PartUIToggle _rgb;
        private Color _colour = LampToggles.Presets[0];

        public ZtxLampColourComponent(ZtxLampColourRules rules) : base(rules) { }

        public Color Color => _colour;
        public event EventHandler<EventArgs> ColorChanged;

        public override void OnPartAttached()
        {
            base.OnPartAttached();
            _rgb = PartUIToggle.GetOnPart(base.Part, LampToggles.RgbId);
            if (_rgb != null) _rgb.ModeChanged += OnRgbChanged;
            Refresh();
        }

        public override void OnPartDetaching()
        {
            if (_rgb != null) _rgb.ModeChanged -= OnRgbChanged;
            _rgb = null;
            base.OnPartDetaching();
        }

        private void OnRgbChanged(object sender, EventArgs e) => Refresh();

        private void Refresh()
        {
            Color c = LampToggles.UnpackRgb(_rgb?.Mode ?? 0) ?? LampToggles.Presets[0];
            if (c.Equals(_colour)) return;
            _colour = c;
            ColorChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal class ZtxLampGlowComponent : PartComponent, IPartComponentToggle
    {
        private bool _on;

        public ZtxLampGlowComponent(ZtxLampGlowRules rules) : base(rules) { }

        public bool IsToggleOn => _on;
        public bool IsToggleOnAnticipated => _on;
        public event EventHandler<EventArgs> ToggleChanged;

        public void Set(bool on)
        {
            if (on == _on) return;
            _on = on;
            ToggleChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
