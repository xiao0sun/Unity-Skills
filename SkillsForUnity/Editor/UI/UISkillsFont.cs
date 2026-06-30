using System;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;

namespace UnitySkills
{
    /// <summary>
    /// Forces the UnitySkills editor windows to render text with a bundled CJK font
    /// instead of the editor's shared default font.
    ///
    /// Why: on macOS, the Unity editor's default UI Toolkit text path rasterizes CJK
    /// glyphs on demand into a single *shared* dynamic font atlas. When that atlas has
    /// to grow/repack, individual glyphs can come back with a stale/blank UV rect and
    /// render as empty advances — so a handful of common characters (e.g. 局/更/卸/定)
    /// silently disappear while everything else looks fine. It is glyph-specific and
    /// stable-per-session, not a style/bold/truncation issue.
    ///
    /// Fix: bind our bundled, subsetted Maple Mono CN (OFL 1.1) TTF to the window
    /// root via <c>unityFont</c>, which is an inherited property, so every label in
    /// the window picks it up. Avoid constructing a runtime TextCore FontAsset here:
    /// in Unity 2022 UI Toolkit can retain stale/null material references from those
    /// generated assets and fail inside UIRStylePainter.DrawTextInfo.
    /// </summary>
    internal static class UISkillsFont
    {
        private const string TtfPath =
            "Packages/com.besty.unity-skills/Editor/UI/Fonts/UnitySkillsCN-Regular.ttf";

        private static Font _cjkFont;
        private static bool _attempted;

        private static Font GetFont()
        {
            if (_attempted) return _cjkFont;
            _attempted = true;

            try
            {
                _cjkFont = AssetDatabase.LoadAssetAtPath<Font>(TtfPath);
                if (_cjkFont == null)
                {
                    // Missing font is non-fatal: fall back to the editor default so the
                    // window still works (just with the original macOS glyph-drop quirk).
                    Debug.LogWarning($"[UnitySkills] CJK font not found, using editor default: {TtfPath}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnitySkills] Failed to load CJK font: {ex.Message}");
                _cjkFont = null;
            }

            return _cjkFont;
        }

        /// <summary>
        /// Apply the bundled CJK font to a window's root element. Safe to call on every
        /// window; the font asset is loaded once and shared. No-op if the font is missing.
        /// </summary>
        public static void Apply(VisualElement root)
        {
            if (root == null) return;
            var font = GetFont();
            if (font == null) return;
            root.style.unityFontDefinition = StyleKeyword.Null;
            root.style.unityFont = font;
        }
    }
}
