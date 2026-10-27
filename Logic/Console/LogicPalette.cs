using System;
using System.Collections.Generic;
using System.Globalization;
using Halfling.Graphics;

namespace ZTX.HingesPistonsDocking
{
    internal static class LogicPalette
    {
        public const string KindButton = "FFD84A";
        public const string KindSensor = "4AD9FF";
        public const string KindActuator = "FFA85C";
        public const string KindConsole = "7CFF7C";
        public const string KindLink = "C89BFF";

        public const string Neutral = "A0A0A0";
        public const string RuleRef = "E0E0E0";
        public const string LineRef = "C8E6FF";
        public const string Heading = "808080";
        public const string Error = "FF6060";
        public const string Warn = "FFB84A";
        public const string On = "7CFF7C";
        public const string Off = "A0A0A0";
        public const string Set = "FFA85C";

        public const string ActAdd = "7CFF7C";
        public const string ActDelete = "FF6060";
        public const string ActMove = "6FA8FF";
        public const string ActMisc = "A0A0A0";
        public const string Dim = "8C8C8C";
        public const string LitNot = "FFB84A";
        public const string LitBracket = "C89BFF";
        public const string LitValue = "FFFFFF";
        public const string FrameOn = "7CFF7C";
        public const string FrameOff = "D0D0D0";
        public const string FramePaused = "8C8C8C";
        public const string FrameError = "FF6060";
        public const string Flash = "4AD9FF";
        public const string Divider = "5A5A5A";
        public const string GroupAdd = "4AD9FF";
        public const int CUSTOM_COLOUR = 1 << 24;
        public const string DividerHot = "8C8C8C";
        public static readonly string[] RuleColours = { "FF6B6B", "FFA85C", "FFD84A", "7CFF7C", "4AD9FF", "6FA8FF", "C89BFF", "FF8AD8" };

        public static string Wrap(string hex, string text) => "<color hex=\"" + hex + "\">" + text + "</color>";

        private static readonly Dictionary<string, Color> s_colours = new Dictionary<string, Color>();

        public static Color ToColor(string hex)
        {
            if (!s_colours.TryGetValue(hex, out Color c)) s_colours[hex] = c = Color.FromHex(hex);
            return c;
        }

        public static string RuleColour(int index)
        {
            if ((index & CUSTOM_COLOUR) != 0) return (index & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture);
            return index >= 1 && index <= RuleColours.Length ? RuleColours[index - 1] : null;
        }

        public static int CustomColour(int rgb) => CUSTOM_COLOUR | (rgb & 0xFFFFFF);

        public static int PickerColour(float u, float v)
        {
            float h = Math.Max(0f, Math.Min(1f, u)) * 6f;
            int i = (int)Math.Floor(h);
            float f = h - i;
            float r, g, b;
            switch (((i % 6) + 6) % 6)
            {
                case 0: r = 1f; g = f; b = 0f; break;
                case 1: r = 1f - f; g = 1f; b = 0f; break;
                case 2: r = 0f; g = 1f; b = f; break;
                case 3: r = 0f; g = 1f - f; b = 1f; break;
                case 4: r = f; g = 0f; b = 1f; break;
                default: r = 1f; g = 0f; b = 1f - f; break;
            }
            float t = Math.Max(0f, Math.Min(1f, v));
            if (t < 0.5f)
            {
                float k = t * 2f;
                r = 1f - k * (1f - r); g = 1f - k * (1f - g); b = 1f - k * (1f - b);
            }
            else
            {
                float k = (t - 0.5f) * 2f;
                r *= 1f - k; g *= 1f - k; b *= 1f - k;
            }
            int Byte(float c) => (int)Math.Round(Math.Max(0f, Math.Min(1f, c)) * 255f);
            return (Byte(r) << 16) | (Byte(g) << 8) | Byte(b);
        }

        public static int ParseHex(string text)
        {
            string s = (text ?? "").Trim().TrimStart('#');
            if (s.Length != 6) return -1;
            return int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v) ? v : -1;
        }
    }
}
