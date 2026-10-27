using System;
using Halfling.Geometry;
using Halfling.Graphics;
using Halfling.Gui;
using Halfling.Gui.Components.Graphics;
using Cosmoteer.Localization;

namespace ZTX.HingesPistonsDocking
{
    internal sealed partial class LogicRulesTab
    {
        private enum ButtonRole { Add, Delete, Move, Misc, Group }

        private const float HeaderTextScale = 1.3f;
        private const int DefaultFontSize = 16;
        private const string StripAlpha = "38";

        private static Button MakeButton(string key, float width, ButtonRole role)
        {
            Button b = MakeButton(key, width);
            SetTextColour(b, RoleHex(role));
            return b;
        }

        private static string RoleHex(ButtonRole role)
        {
            switch (role)
            {
                case ButtonRole.Add: return LogicPalette.ActAdd;
                case ButtonRole.Delete: return LogicPalette.ActDelete;
                case ButtonRole.Move: return LogicPalette.ActMove;
                case ButtonRole.Group: return LogicPalette.GroupAdd;
                default: return LogicPalette.ActMisc;
            }
        }

        private static void SetTextColour(Button b, string hex)
        {
            Color c = LogicPalette.ToColor(hex);
            if (b.StateNormalTextRenderer.Color.Equals(c)) return;
            b.StateNormalTextRenderer.Color = c;
            b.StateHighlightedTextRenderer.Color = c;
            b.StatePressedTextRenderer.Color = c;
        }

        private static void SetTextColour(Label l, string hex)
        {
            Color c = LogicPalette.ToColor(hex);
            if (!l.TextRenderer.Color.Equals(c)) l.TextRenderer.Color = c;
        }

        private static void SetLit(Button b, bool lit, string litHex) => SetTextColour(b, lit ? litHex : LogicPalette.Dim);

        private static void ScaleText(Button b, float factor)
        {
            foreach (WidgetTextRenderer r in new[] { b.StateNormalTextRenderer, b.StateHighlightedTextRenderer, b.StatePressedTextRenderer, b.StateDisabledTextRenderer })
                r.FontSize = (int)Math.Round((r.FontSize > 0 ? r.FontSize : DefaultFontSize) * factor);
        }

        private static void ScaleText(Label l, float factor)
        {
            l.TextRenderer.FontSize = (int)Math.Round((l.TextRenderer.FontSize > 0 ? l.TextRenderer.FontSize : DefaultFontSize) * factor);
        }

        private static void TintFrame(LayoutBox box, string hex)
        {
            Color c = LogicPalette.ToColor(hex);
            WidgetNineSlice ns = box.NineSlice;
            if (ns.TopSprite.VertexColor.Equals(c)) return;
            foreach (GuiSprite s in new[] { ns.TopLeftSprite, ns.TopSprite, ns.TopRightSprite, ns.LeftSprite, ns.RightSprite, ns.BottomLeftSprite, ns.BottomSprite, ns.BottomRightSprite })
                s.VertexColor = c;
        }

        private static void TintStrip(LayoutBox row, string hex)
        {
            if (hex == null)
            {
                if (row.NineSlice.Flags != NineSliceFlags.None) row.NineSlice.Flags = NineSliceFlags.None;
                return;
            }
            row.NineSlice.CenterSprite.CopyFrom(new Sprite((Texture)null, LogicPalette.ToColor(hex + StripAlpha), Vector2.Zero, Rect.Zero));
            row.NineSlice.Flags = NineSliceFlags.Center;
        }

        private string FrameHex(RuleBlock b, LogicRuleDef def)
        {
            if (b.Rule == _flashRule && _frame < _flashUntil) return LogicPalette.Flash;
            if (def.Joined && _engine.RuleExprError(b.Rule, out _) != ExprError.None) return LogicPalette.FrameError;
            if (_engine.RulePaused(b.Rule)) return LogicPalette.FramePaused;
            return _engine.RuleResult(b.Rule) ? LogicPalette.FrameOn : LogicPalette.FrameOff;
        }

        private void RefreshHeaderStyle(RuleBlock b, LogicRuleDef def, bool commandable)
        {
            if (b.Position != b.LastPosition)
            {
                b.LastPosition = b.Position;
                b.Number.Text = "#" + b.Position;
            }
            int colour = _engine.RuleColour(b.Rule);
            if (colour != b.LastColour)
            {
                b.LastColour = colour;
                string hex = LogicPalette.RuleColour(colour);
                SetTextColour(b.Name.Display, hex ?? LogicPalette.LitValue);
                SetTextColour(b.Number, hex ?? LogicPalette.LitValue);
                TintStrip(b.Header, hex);
            }
            TintFrame(b.Box, FrameHex(b, def));
        }
    }
}
