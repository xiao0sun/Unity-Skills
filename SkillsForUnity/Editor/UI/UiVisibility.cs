using UnityEngine.UIElements;

namespace UnitySkills
{
    /// <summary>
    /// Single shared mechanism for the common "show/hide this element" toggle that used to be
    /// scattered across controllers as either a bespoke <c>SetDisplay(VisualElement, bool)</c>
    /// helper (duplicated verbatim in more than one controller) or an inline
    /// <c>element.style.display = DisplayStyle.Flex/None</c> assignment. Both forms bypass USS
    /// entirely, so any stylesheet rule for that element can never win. Routing every site
    /// through the shared "is-hidden" USS class keeps exactly one mechanism in control of
    /// visibility and lets the stylesheet own the value.
    /// </summary>
    public static class UiVisibility
    {
        /// <summary>The shared utility class; declared once per stylesheet as
        /// <c>.is-hidden { display: none; }</c> (see UnitySkillsWindow.uss).</summary>
        public const string HiddenClass = "is-hidden";

        /// <summary>Shows or hides <paramref name="element"/> by toggling <see cref="HiddenClass"/>.</summary>
        public static void SetVisible(this VisualElement element, bool visible)
        {
            if (element == null) return;
            element.EnableInClassList(HiddenClass, !visible);
        }

        /// <summary>
        /// True when the element is not hidden via <see cref="HiddenClass"/>. Does not detect
        /// visibility withheld through some other mechanism (a different modifier class, an
        /// ancestor's display, etc.) — only useful for elements exclusively toggled through
        /// <see cref="SetVisible"/>.
        /// </summary>
        public static bool IsVisible(this VisualElement element)
        {
            return element != null && !element.ClassListContains(HiddenClass);
        }
    }
}

// Producer:Betsy
