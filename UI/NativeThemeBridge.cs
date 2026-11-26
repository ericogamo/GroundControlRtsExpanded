using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace GroundControlRtsExpanded;

/// <summary>
/// Null-safe wrapper around GroundControlRts.CommanderUiTheme, CommanderUiScale, and CommanderWindowScale
/// so all addon windows, buttons, bars, fonts, scaling, and resize/zoom chrome match Ground Control natively.
/// </summary>
internal static class NativeThemeBridge
{
    internal static float UiWidth
    {
        get
        {
            if (GroundControlReflection.UiScaleWidthGetter?.Invoke(null, null) is float w && w > 64f)
            {
                return w;
            }

            return Screen.width;
        }
    }

    internal static float UiHeight
    {
        get
        {
            if (GroundControlReflection.UiScaleHeightGetter?.Invoke(null, null) is float h && h > 64f)
            {
                return h;
            }

            return Screen.height;
        }
    }

    internal static Vector2 ScreenToGui(Vector2 screenPoint)
    {
        if (GroundControlReflection.UiScaleScreenToGuiMethod?.Invoke(null, new object[] { screenPoint }) is Vector2 pt)
        {
            return pt;
        }

        return new Vector2(screenPoint.x, Screen.height - screenPoint.y);
    }

    internal static void BeginScaledUi(out bool hasScale, out Matrix4x4 prevMatrix, out GUISkin? prevSkin)
    {
        hasScale = false;
        prevMatrix = GUI.matrix;
        prevSkin = GUI.skin;

        if (GroundControlReflection.UiScaleBeginMethod?.Invoke(null, null) is Matrix4x4 m)
        {
            prevMatrix = m;
            hasScale = true;
        }

        GroundControlReflection.UiThemeEnsureMethod?.Invoke(null, null);
        if (GroundControlReflection.UiThemeSkinProp?.GetValue(null) is GUISkin skin && skin != null)
        {
            GUI.skin = skin;
        }
    }

    internal static void EndScaledUi(bool hasScale, Matrix4x4 prevMatrix, GUISkin? prevSkin)
    {
        if (prevSkin != null)
        {
            GUI.skin = prevSkin;
        }

        if (hasScale)
        {
            GroundControlReflection.UiScaleEndMethod?.Invoke(null, new object[] { prevMatrix });
        }
        else
        {
            GUI.matrix = prevMatrix;
        }
    }

    internal static GUIStyle Window => GetStyle(GroundControlReflection.UiThemeWindowProp, GUI.skin.window);
    internal static GUIStyle Panel => GetStyle(GroundControlReflection.UiThemePanelProp, GUI.skin.box);
    internal static GUIStyle Inset => GetStyle(GroundControlReflection.UiThemeInsetProp, GUI.skin.box);
    internal static GUIStyle Button => GetStyle(GroundControlReflection.UiThemeButtonProp, GUI.skin.button);
    internal static GUIStyle PrimaryButton => GetStyle(GroundControlReflection.UiThemePrimaryButtonProp, Button);
    internal static GUIStyle DangerButton => GetStyle(GroundControlReflection.UiThemeDangerButtonProp, Button);
    internal static GUIStyle SelectedButton => GetStyle(GroundControlReflection.UiThemeSelectedButtonProp, Button);
    internal static GUIStyle ChromeButton => GetStyle(GroundControlReflection.UiThemeChromeButtonProp, Button);
    internal static GUIStyle Label => GetStyle(GroundControlReflection.UiThemeLabelProp, GUI.skin.label);
    internal static GUIStyle MutedLabel => GetStyle(GroundControlReflection.UiThemeMutedLabelProp, Label);
    internal static GUIStyle WarningLabel => GetStyle(GroundControlReflection.UiThemeWarningLabelProp, Label);
    internal static GUIStyle Header => GetStyle(GroundControlReflection.UiThemeHeaderProp, Label);
    internal static GUIStyle Caption => GetStyle(GroundControlReflection.UiThemeCaptionProp, Label);
    internal static GUIStyle Figure => GetStyle(GroundControlReflection.UiThemeFigureProp, Header);
    internal static GUIStyle Toggle => GetStyle(GroundControlReflection.UiThemeToggleProp, GUI.skin.toggle);
    internal static GUIStyle Alert => GetStyle(GroundControlReflection.UiThemeAlertProp, Panel);
    internal static GUIStyle Toast => GetStyle(GroundControlReflection.UiThemeToastProp, Panel);

    internal static Color Accent
    {
        get
        {
            if (GroundControlReflection.UiThemeAccentProp?.GetValue(null) is Color c)
            {
                return c;
            }

            return new Color(0.32f, 0.85f, 0.68f, 1f);
        }
    }

    internal static Rect ClampWindowRect(int windowId, Rect rect)
    {
        if (GroundControlReflection.WindowScaleClampMethod?.Invoke(null, new object[] { windowId, rect }) is Rect clamped)
        {
            return clamped;
        }

        float maxX = Mathf.Max(12f, UiWidth - rect.width - 12f);
        float maxY = Mathf.Max(48f, UiHeight - rect.height - 12f);
        rect.x = Mathf.Clamp(rect.x, 12f, maxX);
        rect.y = Mathf.Clamp(rect.y, 48f, maxY);
        return rect;
    }

    internal static Rect VisualRect(int windowId, Rect rect)
    {
        if (GroundControlReflection.WindowScaleVisualMethod?.Invoke(null, new object[] { windowId, rect }) is Rect visual)
        {
            return visual;
        }

        return rect;
    }

    internal static Rect DrawWindow(int windowId, Rect rect, GUI.WindowFunction draw, string title)
    {
        Rect clamped = ClampWindowRect(windowId, rect);
        if (GroundControlReflection.WindowScaleWindowMethod?.Invoke(null, new object[] { windowId, clamped, draw, title }) is Rect drawn)
        {
            return drawn;
        }

        return GUI.Window(windowId, clamped, draw, title, Window);
    }

    internal static float DrawSection(Rect rect, string title)
    {
        if (GroundControlReflection.UiThemeDrawSectionMethod?.Invoke(null, new object[] { rect, title }) is float nextY)
        {
            return nextY;
        }

        GUI.Label(rect, title, Caption);
        return rect.y + 22f;
    }

    internal static void DrawBar(Rect rect, float fraction, Color fill)
    {
        if (GroundControlReflection.UiThemeDrawBarMethod != null)
        {
            GroundControlReflection.UiThemeDrawBarMethod.Invoke(null, new object[] { rect, fraction, fill });
            return;
        }

        GUI.Box(rect, string.Empty, Inset);
    }

    internal static void DrawAccentLine(Rect rect)
    {
        if (GroundControlReflection.UiThemeDrawAccentLineMethod != null)
        {
            GroundControlReflection.UiThemeDrawAccentLineMethod.Invoke(null, new object[] { rect });
            return;
        }

        Color prev = GUI.color;
        GUI.color = Accent;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = prev;
    }

    internal static void DrawRule(Rect rect, float alpha = 0.3f)
    {
        if (GroundControlReflection.UiThemeDrawRuleMethod != null)
        {
            GroundControlReflection.UiThemeDrawRuleMethod.Invoke(null, new object[] { rect, alpha });
        }
    }

    internal static bool DrawHelpButton(float windowWidth, ref bool helpVisible)
    {
        if (GroundControlReflection.UiThemeDrawHelpButtonMethod != null)
        {
            object[] args = { windowWidth, helpVisible };
            bool res = GroundControlReflection.UiThemeDrawHelpButtonMethod.Invoke(null, args) is bool b && b;
            helpVisible = args[1] is bool updated && updated;
            return res;
        }

        if (GUI.Button(new Rect(windowWidth - 62f, 3f, 26f, 22f), "?", helpVisible ? SelectedButton : ChromeButton))
        {
            helpVisible = !helpVisible;
        }

        return helpVisible;
    }

    internal static float DrawHelpOverlay(Rect rect, string text)
    {
        if (GroundControlReflection.UiThemeDrawHelpOverlayMethod?.Invoke(null, new object[] { rect, text }) is float h)
        {
            return h;
        }

        GUI.Box(rect, text, Inset);
        return rect.height;
    }

    internal static bool DrawCheck(Rect rect, bool value, string text)
    {
        if (GroundControlReflection.UiThemeCheckMethod != null)
        {
            if (GroundControlReflection.UiThemeCheckMethod.Invoke(null, new object[] { rect, value, text, Toggle }) is bool toggled)
            {
                return toggled;
            }
        }

        return GUI.Toggle(rect, value, text, Toggle);
    }

    internal static void DrawWindowChrome(float windowWidth, ref bool helpVisible, out bool closeClicked)
    {
        DrawHelpButton(windowWidth, ref helpVisible);
        closeClicked = GUI.Button(new Rect(windowWidth - 34f, 3f, 26f, 22f), "X", ChromeButton);
    }

    internal static Color StatusColor(float fraction)
    {
        if (GroundControlReflection.UiThemeStatusColorMethod?.Invoke(null, new object[] { fraction }) is Color c)
        {
            return c;
        }

        return Accent;
    }

    internal static bool IsAnyAddonUiUnderScreenPoint(Vector2 screenPoint)
    {
        if (RtsCityUiService.Instance.ContainsScreenPoint(screenPoint)
            || EconomyTrackerUiService.Instance.ContainsScreenPoint(screenPoint)
            || AbmDefenseService.Instance.ContainsScreenPoint(screenPoint)
            || PioneerRoadService.Instance.ContainsScreenPoint(screenPoint)
            || MdsFlatbedHaulService.Instance.ContainsScreenPoint(screenPoint)
            || SingleplayerCheatService.Instance.ContainsScreenPoint(screenPoint)
            || PerfMetricsService.Instance.ContainsScreenPoint(screenPoint))
        {
            return true;
        }

        return MdsBridgeBootstrap.IsLogisticsWindowUnderScreenPoint?.Invoke(screenPoint) == true;
    }

    private static GUIStyle GetStyle(PropertyInfo? prop, GUIStyle fallback)
    {
        try
        {
            if (prop?.GetValue(null) is GUIStyle style && style != null)
            {
                return style;
            }
        }
        catch
        {
            // Ignore pre-init lookups and return fallback.
        }

        return fallback;
    }
}
