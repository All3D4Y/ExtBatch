using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace CmOxeProvisioner.Services
{
    public class ThemeOption
    {
        public string Key { get; set; }
        public string Label { get; set; }

        public ThemeOption(string key, string label)
        {
            Key = key;
            Label = label;
        }

        public override string ToString()
        {
            return Label;
        }
    }

    public static class ThemeService
    {
        public static readonly List<ThemeOption> Themes = new List<ThemeOption>
        {
            new ThemeOption("system", "System (OS 자동)"),
            new ThemeOption("antigravity-dark", "Antigravity Dark"),
            new ThemeOption("antigravity-light", "Antigravity Light (Default)"),
            new ThemeOption("legacy-dark", "Legacy Dark"),
            new ThemeOption("nord", "Nord"),
            new ThemeOption("tokyo-night", "Tokyo Night"),
            new ThemeOption("tokyo-night-storm", "Tokyo Night Storm"),
            new ThemeOption("tokyo-night-light", "Tokyo Night Light"),
            new ThemeOption("solarized-dark", "Solarized Dark"),
            new ThemeOption("solarized-light", "Solarized Light"),
            new ThemeOption("high-contrast-dark", "High Contrast Dark")
        };

        public static string ResolveThemeKey(string key)
        {
            if (key == "system")
            {
                return IsWindowsLightTheme() ? "antigravity-light" : "antigravity-dark";
            }
            return string.IsNullOrEmpty(key) ? "antigravity-light" : key;
        }

        private static bool IsWindowsLightTheme()
        {
            try
            {
                object val = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\Themes\Personalize",
                    "AppsUseLightTheme",
                    0);
                if (val is int) return ((int)val) == 1;
            }
            catch
            {
            }
            return false;
        }

        private static Dictionary<string, string> GetPalette(string resolvedKey)
        {
            var p = new Dictionary<string, string>();
            switch (resolvedKey)
            {
                case "antigravity-light":
                    p["AppBg"] = "#F3F6FA"; p["HeaderBg"] = "#FFFFFF"; p["Surface"] = "#FFFFFF"; p["SurfaceAlt"] = "#F8FAFC";
                    p["InputBg"] = "#FFFFFF"; p["Text"] = "#0F172A"; p["Muted"] = "#475569"; p["Border"] = "#CBD5E1";
                    p["Primary"] = "#2563EB"; p["PrimaryFg"] = "#FFFFFF"; p["Oxe"] = "#0F766E"; p["OxeFg"] = "#FFFFFF";
                    p["ChipBg"] = "#EFF6FF"; p["ChipFg"] = "#1D4ED8";
                    p["OkBg"] = "#ECFDF5"; p["OkFg"] = "#047857"; p["OkBorder"] = "#A7F3D0";
                    p["WarnBg"] = "#FFFBEB"; p["WarnFg"] = "#B45309"; p["WarnBorder"] = "#FDE68A";
                    p["ErrBg"] = "#FFF1F2"; p["ErrFg"] = "#BE123C"; p["ErrBorder"] = "#FECDD3";
                    p["DangerBg"] = "#C26B76"; p["DangerFg"] = "#FFFFFF"; p["DangerBorder"] = "#B05B66";
                    p["ConsoleBg"] = "#0F172A"; p["ConsoleFg"] = "#E2E8F0";
                    p["TableAltRow"] = "#F7F9FC"; p["TableGridLine"] = "#E2E8F0";
                    break;

                case "legacy-dark":
                    p["AppBg"] = "#1E1E1E"; p["HeaderBg"] = "#181818"; p["Surface"] = "#252526"; p["SurfaceAlt"] = "#2D2D30";
                    p["InputBg"] = "#1A1A1B"; p["Text"] = "#D4D4D4"; p["Muted"] = "#9CA3AF"; p["Border"] = "#3E3E42";
                    p["Primary"] = "#007ACC"; p["PrimaryFg"] = "#FFFFFF"; p["Oxe"] = "#16825D"; p["OxeFg"] = "#FFFFFF";
                    p["ChipBg"] = "#2D2D30"; p["ChipFg"] = "#75BEFF";
                    p["OkBg"] = "#123322"; p["OkFg"] = "#6EE7B7"; p["OkBorder"] = "#256D4B";
                    p["WarnBg"] = "#36290E"; p["WarnFg"] = "#FBBF24"; p["WarnBorder"] = "#785A18";
                    p["ErrBg"] = "#3B161B"; p["ErrFg"] = "#FCA5A5"; p["ErrBorder"] = "#7F1D1D";
                    p["DangerBg"] = "#AD5D68"; p["DangerFg"] = "#FFFFFF"; p["DangerBorder"] = "#994F59";
                    p["ConsoleBg"] = "#111111"; p["ConsoleFg"] = "#D4D4D4";
                    p["TableAltRow"] = "#2A2A2E"; p["TableGridLine"] = "#343438";
                    break;

                case "nord":
                    p["AppBg"] = "#2E3440"; p["HeaderBg"] = "#242933"; p["Surface"] = "#3B4252"; p["SurfaceAlt"] = "#434C5E";
                    p["InputBg"] = "#292E39"; p["Text"] = "#ECEFF4"; p["Muted"] = "#D8DEE9"; p["Border"] = "#4C566A";
                    p["Primary"] = "#88C0D0"; p["PrimaryFg"] = "#1B222C"; p["Oxe"] = "#8FBCBB"; p["OxeFg"] = "#1B222C";
                    p["ChipBg"] = "#434C5E"; p["ChipFg"] = "#88C0D0";
                    p["OkBg"] = "#293A34"; p["OkFg"] = "#A3BE8C"; p["OkBorder"] = "#4F6B54";
                    p["WarnBg"] = "#3E3529"; p["WarnFg"] = "#EBCB8B"; p["WarnBorder"] = "#7B623B";
                    p["ErrBg"] = "#3F292E"; p["ErrFg"] = "#E5989B"; p["ErrBorder"] = "#BF616A";
                    p["DangerBg"] = "#A95A63"; p["DangerFg"] = "#FFFFFF"; p["DangerBorder"] = "#954B54";
                    p["ConsoleBg"] = "#20252E"; p["ConsoleFg"] = "#E5E9F0";
                    p["TableAltRow"] = "#353B4A"; p["TableGridLine"] = "#465063";
                    break;

                case "tokyo-night":
                    p["AppBg"] = "#1A1B26"; p["HeaderBg"] = "#16161E"; p["Surface"] = "#24283B"; p["SurfaceAlt"] = "#292E42";
                    p["InputBg"] = "#16161E"; p["Text"] = "#C0CAF5"; p["Muted"] = "#9AA5CE"; p["Border"] = "#3B4261";
                    p["Primary"] = "#7AA2F7"; p["PrimaryFg"] = "#15161E"; p["Oxe"] = "#73DACA"; p["OxeFg"] = "#132020";
                    p["ChipBg"] = "#292E42"; p["ChipFg"] = "#7AA2F7";
                    p["OkBg"] = "#1A2F2C"; p["OkFg"] = "#9ECE6A"; p["OkBorder"] = "#396250";
                    p["WarnBg"] = "#33291A"; p["WarnFg"] = "#E0AF68"; p["WarnBorder"] = "#7C5C2C";
                    p["ErrBg"] = "#361B26"; p["ErrFg"] = "#F7768E"; p["ErrBorder"] = "#9E3B52";
                    p["DangerBg"] = "#B55E70"; p["DangerFg"] = "#FFFFFF"; p["DangerBorder"] = "#A04E5F";
                    p["ConsoleBg"] = "#12131A"; p["ConsoleFg"] = "#C0CAF5";
                    p["TableAltRow"] = "#1F2335"; p["TableGridLine"] = "#2F354F";
                    break;

                case "tokyo-night-storm":
                    p["AppBg"] = "#24283B"; p["HeaderBg"] = "#1F2335"; p["Surface"] = "#2D334A"; p["SurfaceAlt"] = "#343B54";
                    p["InputBg"] = "#1F2335"; p["Text"] = "#C0CAF5"; p["Muted"] = "#A9B1D6"; p["Border"] = "#414868";
                    p["Primary"] = "#7AA2F7"; p["PrimaryFg"] = "#151824"; p["Oxe"] = "#73DACA"; p["OxeFg"] = "#132020";
                    p["ChipBg"] = "#343B54"; p["ChipFg"] = "#7DCFFF";
                    p["OkBg"] = "#1E3330"; p["OkFg"] = "#9ECE6A"; p["OkBorder"] = "#3B6454";
                    p["WarnBg"] = "#382E20"; p["WarnFg"] = "#E0AF68"; p["WarnBorder"] = "#7E5E31";
                    p["ErrBg"] = "#3B202C"; p["ErrFg"] = "#F7768E"; p["ErrBorder"] = "#9E3B52";
                    p["DangerBg"] = "#B55E70"; p["DangerFg"] = "#FFFFFF"; p["DangerBorder"] = "#A04E5F";
                    p["ConsoleBg"] = "#191C2B"; p["ConsoleFg"] = "#C0CAF5";
                    p["TableAltRow"] = "#282D42"; p["TableGridLine"] = "#39405E";
                    break;

                case "tokyo-night-light":
                    p["AppBg"] = "#E1E2E7"; p["HeaderBg"] = "#D5D6DB"; p["Surface"] = "#F5F6F8"; p["SurfaceAlt"] = "#ECEEF2";
                    p["InputBg"] = "#FFFFFF"; p["Text"] = "#343B58"; p["Muted"] = "#5A6282"; p["Border"] = "#B4B8C7";
                    p["Primary"] = "#34548A"; p["PrimaryFg"] = "#FFFFFF"; p["Oxe"] = "#0F6E7A"; p["OxeFg"] = "#FFFFFF";
                    p["ChipBg"] = "#E1E6F2"; p["ChipFg"] = "#34548A";
                    p["OkBg"] = "#E6F4EE"; p["OkFg"] = "#2A6F4E"; p["OkBorder"] = "#A6D7BE";
                    p["WarnBg"] = "#F9F0DD"; p["WarnFg"] = "#8F5E15"; p["WarnBorder"] = "#E2C58A";
                    p["ErrBg"] = "#FCE8EC"; p["ErrFg"] = "#8C2B3D"; p["ErrBorder"] = "#E6A9B5";
                    p["DangerBg"] = "#B35E6C"; p["DangerFg"] = "#FFFFFF"; p["DangerBorder"] = "#9E4E5C";
                    p["ConsoleBg"] = "#1A1B26"; p["ConsoleFg"] = "#C0CAF5";
                    p["TableAltRow"] = "#ECEFF5"; p["TableGridLine"] = "#CFD3E0";
                    break;

                case "solarized-dark":
                    p["AppBg"] = "#002B36"; p["HeaderBg"] = "#00212B"; p["Surface"] = "#073642"; p["SurfaceAlt"] = "#0B414F";
                    p["InputBg"] = "#00212B"; p["Text"] = "#EEE8D5"; p["Muted"] = "#93A1A1"; p["Border"] = "#1A5260";
                    p["Primary"] = "#268BD2"; p["PrimaryFg"] = "#FFFFFF"; p["Oxe"] = "#1D7E76"; p["OxeFg"] = "#FDF6E3";
                    p["ChipBg"] = "#0B414F"; p["ChipFg"] = "#7CC4F2";
                    p["OkBg"] = "#083C34"; p["OkFg"] = "#859900"; p["OkBorder"] = "#386641";
                    p["WarnBg"] = "#3D2E05"; p["WarnFg"] = "#B58900"; p["WarnBorder"] = "#785B08";
                    p["ErrBg"] = "#421417"; p["ErrFg"] = "#F87171"; p["ErrBorder"] = "#991B1B";
                    p["DangerBg"] = "#B55D5B"; p["DangerFg"] = "#FFFFFF"; p["DangerBorder"] = "#9E4E4C";
                    p["ConsoleBg"] = "#001B22"; p["ConsoleFg"] = "#EEE8D5";
                    p["TableAltRow"] = "#052E39"; p["TableGridLine"] = "#124350";
                    break;

                case "solarized-light":
                    p["AppBg"] = "#FDF6E3"; p["HeaderBg"] = "#EAE3CE"; p["Surface"] = "#EEE8D5"; p["SurfaceAlt"] = "#F8F1DF";
                    p["InputBg"] = "#FDF6E3"; p["Text"] = "#073642"; p["Muted"] = "#586E75"; p["Border"] = "#CFC7B2";
                    p["Primary"] = "#268BD2"; p["PrimaryFg"] = "#FFFFFF"; p["Oxe"] = "#1D7E76"; p["OxeFg"] = "#FFFFFF";
                    p["ChipBg"] = "#E4DEC8"; p["ChipFg"] = "#0D5E97";
                    p["OkBg"] = "#E8F1DC"; p["OkFg"] = "#4D6600"; p["OkBorder"] = "#BDCE8E";
                    p["WarnBg"] = "#F7EBCC"; p["WarnFg"] = "#8F6800"; p["WarnBorder"] = "#DFC378";
                    p["ErrBg"] = "#FAE4E0"; p["ErrFg"] = "#B91C1C"; p["ErrBorder"] = "#EAA79D";
                    p["DangerBg"] = "#B55D5B"; p["DangerFg"] = "#FFFFFF"; p["DangerBorder"] = "#9E4E4C";
                    p["ConsoleBg"] = "#002B36"; p["ConsoleFg"] = "#EEE8D5";
                    p["TableAltRow"] = "#F7F0DF"; p["TableGridLine"] = "#DBD3BF";
                    break;

                case "high-contrast-dark":
                    p["AppBg"] = "#000000"; p["HeaderBg"] = "#000000"; p["Surface"] = "#0A0A0A"; p["SurfaceAlt"] = "#141414";
                    p["InputBg"] = "#000000"; p["Text"] = "#FFFFFF"; p["Muted"] = "#E2E8F0"; p["Border"] = "#6FC3DF";
                    p["Primary"] = "#38BDF8"; p["PrimaryFg"] = "#000000"; p["Oxe"] = "#34D399"; p["OxeFg"] = "#000000";
                    p["ChipBg"] = "#141414"; p["ChipFg"] = "#FACC15";
                    p["OkBg"] = "#022C22"; p["OkFg"] = "#6EE7B7"; p["OkBorder"] = "#34D399";
                    p["WarnBg"] = "#2E1A00"; p["WarnFg"] = "#FDE047"; p["WarnBorder"] = "#FACC15";
                    p["ErrBg"] = "#2D060D"; p["ErrFg"] = "#FDA4AF"; p["ErrBorder"] = "#FB7185";
                    p["DangerBg"] = "#B84A58"; p["DangerFg"] = "#FFFFFF"; p["DangerBorder"] = "#E11D48";
                    p["ConsoleBg"] = "#000000"; p["ConsoleFg"] = "#FFFFFF";
                    p["TableAltRow"] = "#121212"; p["TableGridLine"] = "#355E75";
                    break;

                default: // antigravity-dark
                    p["AppBg"] = "#0F1117"; p["HeaderBg"] = "#131620"; p["Surface"] = "#171A23"; p["SurfaceAlt"] = "#1E2330";
                    p["InputBg"] = "#0D1017"; p["Text"] = "#E6EDF3"; p["Muted"] = "#94A3B8"; p["Border"] = "#2D3343";
                    p["Primary"] = "#3B82F6"; p["PrimaryFg"] = "#FFFFFF"; p["Oxe"] = "#0D9488"; p["OxeFg"] = "#FFFFFF";
                    p["ChipBg"] = "#1E293B"; p["ChipFg"] = "#93C5FD";
                    p["OkBg"] = "#052E24"; p["OkFg"] = "#6EE7B7"; p["OkBorder"] = "#065F46";
                    p["WarnBg"] = "#332206"; p["WarnFg"] = "#FCD34D"; p["WarnBorder"] = "#92400E";
                    p["ErrBg"] = "#331018"; p["ErrFg"] = "#FDA4AF"; p["ErrBorder"] = "#9F1239";
                    p["DangerBg"] = "#B25F6B"; p["DangerFg"] = "#FFFFFF"; p["DangerBorder"] = "#9E4E5A";
                    p["ConsoleBg"] = "#080A0F"; p["ConsoleFg"] = "#CBD5E1";
                    p["TableAltRow"] = "#131620"; p["TableGridLine"] = "#252B3B";
                    break;
            }
            return p;
        }

        public static void ApplyTheme(string selectedKey)
        {
            if (Application.Current == null) return;
            string resolved = ResolveThemeKey(selectedKey);
            Dictionary<string, string> pal = GetPalette(resolved);
            ResourceDictionary res = Application.Current.Resources;

            foreach (KeyValuePair<string, string> kv in pal)
            {
                Color c = (Color)ColorConverter.ConvertFromString(kv.Value);
                SolidColorBrush brush = new SolidColorBrush(c);
                brush.Freeze();
                res[kv.Key + "Brush"] = brush;
            }
        }
    }
}
