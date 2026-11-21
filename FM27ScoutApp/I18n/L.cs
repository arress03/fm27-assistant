// Langue de l'interface : le texte français du code sert de clé ; les tables Strings.*.cs donnent l'anglais et le portugais (Portugal).

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace FM27Scout
{
    internal static partial class L
    {
        public static readonly (string code, string label)[] Languages = { ("fr", "Français"), ("en", "English"), ("pt", "Português") };

        /// <summary>Langue en cours : "fr", "en" ou "pt".</summary>
        public static string Lang { get; private set; } = "fr";

        static Dictionary<string, string> s_map = new();

        /// <summary>Langue des réglages, anglais par défaut.</summary>
        public static void Init()
        {
            string lang = Settings.Cur.Lang;
            if (Array.FindIndex(Languages, l => l.code == lang) < 0) lang = "en";
            Lang = lang;
            s_map = new();
            if (lang == "fr") return;
            foreach (var table in Tables)
                foreach (var (fr, en, pt) in table)
                    s_map.TryAdd(fr, lang == "en" ? en : pt);
        }

        /// <summary>Texte traduit (le texte français lui-même s'il n'a pas de traduction).</summary>
        public static string T(string fr) => fr != null && s_map.TryGetValue(fr, out var t) ? t : fr;

        /// <summary>Format traduit ({0}, {1:N0}…) puis rempli avec les valeurs.</summary>
        public static string F(string fr, params object[] args) => string.Format(T(fr), args);

        /// <summary>Traduit les textes fixes d'un écran XAML (TextBlock, contenus, en-têtes, infobulles).</summary>
        public static void Apply(DependencyObject root)
        {
            if (Lang == "fr" || root == null) return;
            switch (root)
            {
                case TextBlock tb when tb.Inlines.Count <= 1: tb.Text = T(tb.Text); break;
                case HeaderedContentControl hc when hc.Header is string h: hc.Header = T(h); break;
                case HeaderedItemsControl hi when hi.Header is string h: hi.Header = T(h); break;
            }
            if (root is ContentControl cc && cc.Content is string c) cc.Content = T(c);
            if (root is FrameworkElement fe && fe.ToolTip is string tip) fe.ToolTip = T(tip);
            foreach (var child in LogicalTreeHelper.GetChildren(root))
                if (child is DependencyObject d) Apply(d);
        }

        /// <summary>Toutes les tables de traduction : (français, anglais, portugais). Propriété : les tables sont dans d'autres fichiers.</summary>
        static (string fr, string en, string pt)[][] Tables => new[]
        {
            SharedStrings, MainWindowStrings, ShellStrings, FeatureStrings, TacticStrings, TrainingStrings, CoreStrings,
        };
    }
}
