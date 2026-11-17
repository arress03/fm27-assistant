using System;
using System.Windows;
using System.Windows.Media;

namespace FM27Scout
{
    /// <summary>
    /// Thème de l'app : une base sombre légèrement teintée par la couleur principale du club, et un accent tiré de cette couleur.
    /// Chaque changement de club remplace les pinceaux dans les ressources : toute l'interface suit.
    /// </summary>
    internal static class Theme
    {
        // Les ressources de l'application sont figées par WPF : à chaque changement de club on crée de nouveaux
        // pinceaux et on remplace les ressources (DynamicResource suit tout seul), puis on redessine le code.
        public static SolidColorBrush Bg { get; private set; }
        public static SolidColorBrush Surface { get; private set; }
        public static SolidColorBrush Surface2 { get; private set; }
        public static SolidColorBrush Line { get; private set; }
        public static SolidColorBrush Text { get; private set; }
        public static SolidColorBrush Dim { get; private set; }
        public static SolidColorBrush Faint { get; private set; }
        public static SolidColorBrush Accent { get; private set; }
        public static SolidColorBrush AccentSoft { get; private set; }
        public static SolidColorBrush AccentText { get; private set; }
        public static SolidColorBrush Hover { get; private set; }
        public static LinearGradientBrush Backdrop { get; private set; }
        public static LinearGradientBrush Header { get; private set; }

        public static Color Primary { get; private set; } = (Color)ColorConverter.ConvertFromString("#4C8DFF");
        public static Color Secondary { get; private set; } = Colors.White;

        /// <summary>Après chaque changement de couleurs.</summary>
        public static event Action Changed;

        static ResourceDictionary s_res;

        static SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        /// <summary>Déclare les pinceaux dans les ressources de l'application (à appeler avant l'ouverture de la fenêtre).</summary>
        public static void Install(ResourceDictionary r)
        {
            s_res = r;
            Text = B((Color)ColorConverter.ConvertFromString("#ECEFF4"));
            Apply(ParseOr(Settings.Cur.ThemePrimary, Primary), ParseOr(Settings.Cur.ThemeSecondary, Secondary), false);
        }

        static Color ParseOr(string hex, Color def)
        {
            try { return string.IsNullOrEmpty(hex) ? def : (Color)ColorConverter.ConvertFromString(hex); } catch { return def; }
        }

        /// <summary>Applique les couleurs d'un club (principale, secondaire).</summary>
        public static void Apply(Color primary, Color secondary, bool save = true)
        {
            // un club en noir ou blanc : on prend l'autre couleur si elle est plus marquée
            if (Sat(primary) < 0.12 && Sat(secondary) >= 0.12) (primary, secondary) = (secondary, primary);
            Primary = primary; Secondary = secondary;
            ToHsl(primary, out double h, out double s, out _);
            bool neutral = s < 0.12;
            double ts = neutral ? 0.04 : Math.Min(0.32, s * 0.45);   // teinte du fond : discrète

            Bg = B(FromHsl(h, ts, 0.055));
            Surface = B(FromHsl(h, ts, 0.085));
            Surface2 = B(FromHsl(h, ts, 0.115));
            Hover = B(FromHsl(h, ts, 0.145));
            Line = B(FromHsl(h, ts * 0.8, 0.17));
            Dim = B(FromHsl(h, Math.Min(ts, 0.12), 0.62));
            Faint = B(FromHsl(h, Math.Min(ts, 0.10), 0.42));
            Text ??= B((Color)ColorConverter.ConvertFromString("#ECEFF4"));

            // accent : la couleur du club, éclaircie si besoin pour rester lisible sur le fond sombre
            var acc = neutral ? (Color)ColorConverter.ConvertFromString("#E8E8E8") : primary;
            ToHsl(acc, out double ah, out double asat, out double al);
            while (Luminance(FromHsl(ah, asat, al)) < 0.18 && al < 0.85) al += 0.03;
            acc = FromHsl(ah, Math.Min(1, asat), al);
            Accent = B(acc);
            AccentSoft = B(Color.FromArgb(0x2E, acc.R, acc.G, acc.B));
            AccentText = B(Luminance(acc) > 0.45 ? Color.FromRgb(0x10, 0x12, 0x16) : Colors.White);

            // fond : halo très léger de la couleur du club en haut à gauche
            var bg = Bg.Color;
            Backdrop = new LinearGradientBrush(new GradientStopCollection
            {
                new(Blend(bg, primary, neutral ? 0.03 : 0.16), 0), new(bg, 0.55), new(Blend(bg, Colors.Black, 0.25), 1),
            }, new Point(0, 0), new Point(1, 1));
            Backdrop.Freeze();
            Header = new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(0x55, primary.R, primary.G, primary.B), 0), new(Color.FromArgb(0x00, primary.R, primary.G, primary.B), 1),
            }, new Point(0, 0), new Point(1, 0));
            Header.Freeze();

            if (s_res != null)
            {
                s_res["Bg"] = Bg; s_res["Panel"] = Surface; s_res["Panel2"] = Surface2; s_res["Line"] = Line; s_res["Text"] = Text;
                s_res["Dim"] = Dim; s_res["Faint"] = Faint; s_res["Accent"] = Accent; s_res["AccentSoft"] = AccentSoft;
                s_res["AccentText"] = AccentText; s_res["Hover"] = Hover; s_res["Backdrop"] = Backdrop; s_res["HeaderGrad"] = Header;
            }
            Changed?.Invoke();

            if (save)
            {
                Settings.Cur.ThemePrimary = primary.ToString();
                Settings.Cur.ThemeSecondary = secondary.ToString();
                Settings.Save();
            }
        }

        /// <summary>« RGBA(32, 76, 156, 255) » (format de FM) → couleur.</summary>
        public static Color? ParseFm(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            int i = s.IndexOf('('), j = s.IndexOf(')');
            if (i < 0 || j < i) return null;
            var p = s[(i + 1)..j].Split(',');
            if (p.Length < 3) return null;
            try { return Color.FromRgb(byte.Parse(p[0].Trim()), byte.Parse(p[1].Trim()), byte.Parse(p[2].Trim())); } catch { return null; }
        }

        // ------------------------------------------------------------------ couleurs

        static Color Blend(Color a, Color b, double t) =>
            Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        static double Lin(byte c) { double v = c / 255.0; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        static double Luminance(Color c) => 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
        static double Sat(Color c) { ToHsl(c, out _, out double s, out double l); return l < 0.06 || l > 0.94 ? 0 : s; }

        static void ToHsl(Color c, out double h, out double s, out double l)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            l = (max + min) / 2; h = 0; s = 0;
            if (max == min) return;
            double d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
            h /= 6;
        }

        static Color FromHsl(double h, double s, double l)
        {
            double Hue(double p, double q, double t)
            {
                if (t < 0) t += 1; if (t > 1) t -= 1;
                if (t < 1.0 / 6) return p + (q - p) * 6 * t;
                if (t < 0.5) return q;
                if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
                return p;
            }
            double r, g, b;
            if (s == 0) r = g = b = l;
            else
            {
                double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
                r = Hue(p, q, h + 1.0 / 3); g = Hue(p, q, h); b = Hue(p, q, h - 1.0 / 3);
            }
            return Color.FromRgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
        }
    }
}
