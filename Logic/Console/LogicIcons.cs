using System;
using System.Collections.Generic;
using System.IO;
using Cosmoteer;
using Cosmoteer.Localization;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics;
using Halfling.Gui;

namespace ZTX.HingesPistonsDocking
{
    internal static class LogicIcons
    {
        public const string PopOut = "gui/widgets_resizer_trbl.png";
        public const string Dock = "gui/icon_back.png";
        public const string Close = "gui/icon_close.png";
        public const string Collapse = "gui/widgets_menu_up.png";
        public const string Expand = "gui/widgets_menu_down.png";
        public const string Filter = "mod:gui/console/filter.png";
        public const string Help = "gui/icon_codex.png";
        public const string Size = "gui/icon_settings.png";
        public const string Copy = "gui/game/designer/tool_copy.png";
        public const string Paint = "gui/game/designer/hud_paint.png";
        public const string ArrowIcon = "gui/arrow.png";
        public const string TextSize = "mod:gui/console/text_size.png";
        private const string ModPrefix = "mod:";

        private static readonly Dictionary<string, Texture> s_cache = new Dictionary<string, Texture>();
        private static string s_root;

        public static Texture Get(string relative)
        {
            if (string.IsNullOrEmpty(relative)) return null;
            if (s_cache.TryGetValue(relative, out Texture cached)) return cached;
            Texture texture = null;
            try
            {
                s_root ??= Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(GameApp).Assembly.Location) ?? "", "..", "Data"));
                string path = relative.StartsWith(ModPrefix, StringComparison.Ordinal)
                    ? Path.Combine(Path.GetDirectoryName(typeof(LogicIcons).Assembly.Location) ?? "", relative.Substring(ModPrefix.Length).Replace('/', Path.DirectorySeparatorChar))
                    : Path.Combine(s_root, relative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path)) texture = App.Graphics.Textures.Load(path, mipLevels: 2, useExisting: true);
                if (texture == null) Log.Info("console: icon not found: " + path + " (text button used).");
            }
            catch (Exception ex)
            {
                Log.Exception("LogicIcons.Get", ex);
            }
            s_cache[relative] = texture;
            return texture;
        }

        public static Button Make(string icon, string labelKey, float width, string tipKey = null)
        {
            Texture t = Get(icon);
            Button b;
            if (t != null) b = new ImageButton(t);
            else
            {
                b = new Button();
                b.TextProvider = Strings.KeyString(labelKey);
            }
            b.DockMode = DockMode.Left;
            b.AutoSize.AutoWidthMode = AutoSizeMode.Disable;
            b.AutoSize.AutoHeightMode = AutoSizeMode.Disable;
            b.Width = width;
            b.ToolTips.TextProvider = Strings.KeyString(tipKey ?? labelKey);
            return b;
        }

        public static Button Arrow(bool up, string labelKey, float width, string tipKey)
        {
            Button b = Make(ArrowIcon, labelKey, width, tipKey);
            if (b is ImageButton ib) ib.ImageSprite.Rotation = up ? Direction.Up : Direction.Down;
            return b;
        }

        public static void Set(Button b, string icon, string labelKey)
        {
            if (b is ImageButton ib)
            {
                Texture t = Get(icon);
                if (t != null && !ReferenceEquals(ib.ImageSprite.Texture, t)) ib.ImageSprite.Texture = t;
            }
            else b.TextProvider = Strings.KeyString(labelKey);
        }
    }
}
