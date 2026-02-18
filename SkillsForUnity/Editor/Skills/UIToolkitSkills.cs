using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace UnitySkills
{
    /// <summary>
    /// UI Toolkit query skills - query runtime VisualElement tree layout and style data.
    /// Requires Play Mode (UI is dynamically built from code).
    /// </summary>
    public static class UIToolkitSkills
    {
        // ─────────────────────────────────────────────────────────────
        // Skill 1: ui_toolkit_list_documents
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// List all UIDocument components in the scene (requires Play Mode).
        /// </summary>
        [UnitySkill("ui_toolkit_list_documents", "List all UIDocument components in the current scene (Play Mode required).")]
        public static object ListDocuments()
        {
            // Must be in Play Mode, UI elements only exist at runtime
            if (!EditorApplication.isPlaying)
                return new { error = "Play Mode required. UI Toolkit elements are dynamically created at runtime." };

            var documents = Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None);

            if (documents.Length == 0)
                return new { documents = new object[0], message = "No UIDocument components found in the scene" };

            var result = documents
                .OrderBy(d => d.sortingOrder)
                .Select((doc, index) => new
                {
                    index,
                    name = doc.gameObject.name,
                    gameObjectPath = GetGameObjectPath(doc.gameObject),
                    sortingOrder = doc.sortingOrder,
                    enabled = doc.enabled,
                    hasRootVisualElement = doc.rootVisualElement != null,
                    rootChildCount = doc.rootVisualElement?.childCount ?? 0
                })
                .ToList();

            return new { documents = result };
        }

        // ─────────────────────────────────────────────────────────────
        // Skill 2: ui_toolkit_query
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Query elements by USS selector, returning style and layout data (requires Play Mode).
        /// selector: starts with "." for class, "#" for name, otherwise searches by name.
        /// </summary>
        [UnitySkill("ui_toolkit_query", "Query UI Toolkit elements by USS selector and return layout/style data (Play Mode required). selector: '.class', '#name', or 'name'. depth: child levels to include (default 1). documentIndex: which UIDocument to use (default 0).")]
        public static object Query(string selector, int depth = 1, int documentIndex = 0)
        {
            // Must be in Play Mode
            if (!EditorApplication.isPlaying)
                return new { error = "Play Mode required. UI Toolkit elements are dynamically created at runtime." };

            // Get the target UIDocument
            var doc = GetDocumentByIndex(documentIndex);
            if (doc == null)
                return new { error = $"UIDocument at index {documentIndex} not found. Call ui_toolkit_list_documents first." };

            var root = doc.rootVisualElement;
            if (root == null)
                return new { error = $"UIDocument '{doc.gameObject.name}' rootVisualElement is null" };

            // Parse selector and find element
            VisualElement element = FindElement(root, selector);

            if (element == null)
                return new { error = $"No element matching selector '{selector}' found", selector, documentIndex };

            return SerializeElement(element, depth);
        }

        // ─────────────────────────────────────────────────────────────
        // Skill 3: ui_toolkit_tree
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Get a text representation of the VisualElement tree (requires Play Mode).
        /// </summary>
        [UnitySkill("ui_toolkit_tree", "Get UI Toolkit visual element hierarchy as a text tree (Play Mode required). depth: levels to show (default 3). rootSelector: optional '.class', '#name', or 'name' to use as root. documentIndex: which UIDocument (default 0).")]
        public static object Tree(int depth = 3, string rootSelector = null, int documentIndex = 0)
        {
            // Must be in Play Mode
            if (!EditorApplication.isPlaying)
                return new { error = "Play Mode required. UI Toolkit elements are dynamically created at runtime." };

            // Get the target UIDocument
            var doc = GetDocumentByIndex(documentIndex);
            if (doc == null)
                return new { error = $"UIDocument at index {documentIndex} not found. Call ui_toolkit_list_documents first." };

            var root = doc.rootVisualElement;
            if (root == null)
                return new { error = $"UIDocument '{doc.gameObject.name}' rootVisualElement is null" };

            // If rootSelector is specified, start from that element
            VisualElement startElement = root;
            if (!string.IsNullOrEmpty(rootSelector))
            {
                startElement = FindElement(root, rootSelector);
                if (startElement == null)
                    return new { error = $"No element matching selector '{rootSelector}' found", rootSelector };
            }

            // Build tree text
            var sb = new StringBuilder();
            BuildTreeText(sb, startElement, depth, "", true);

            return new
            {
                tree = sb.ToString(),
                documentName = doc.gameObject.name,
                rootSelector = rootSelector ?? "(root)",
                maxDepth = depth
            };
        }

        // ─────────────────────────────────────────────────────────────
        // Private helper methods
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Get a UIDocument by index (sorted by sortingOrder, then take the index-th one).
        /// </summary>
        private static UIDocument GetDocumentByIndex(int index)
        {
            var documents = Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
            var sorted = documents.OrderBy(d => d.sortingOrder).ToArray();
            if (index < 0 || index >= sorted.Length)
                return null;
            return sorted[index];
        }

        /// <summary>
        /// Find a VisualElement using USS selector syntax.
        /// - "#name" -> search by name
        /// - ".class" -> search by class
        /// - other -> search by name first, then fall back to class
        /// </summary>
        private static VisualElement FindElement(VisualElement root, string selector)
        {
            if (string.IsNullOrEmpty(selector))
                return root;

            if (selector.StartsWith("#"))
            {
                // Name selector
                return root.Q(name: selector.Substring(1));
            }
            else if (selector.StartsWith("."))
            {
                // Class selector
                return root.Q(className: selector.Substring(1));
            }
            else
            {
                // Try name first, fall back to class
                var byName = root.Q(name: selector);
                if (byName != null) return byName;
                return root.Q(className: selector);
            }
        }

        /// <summary>
        /// Serialize a VisualElement to a dictionary (including style, layout, and children).
        /// </summary>
        private static object SerializeElement(VisualElement el, int depth)
        {
            var result = new Dictionary<string, object>
            {
                ["name"] = el.name,
                ["type"] = el.GetType().Name,
                ["classes"] = el.GetClasses().ToList()
            };

            // If it's a text element, include the text content
            if (el is TextElement textEl && !string.IsNullOrEmpty(textEl.text))
                result["text"] = textEl.text;

            // World-space bounding box
            var wb = el.worldBound;
            result["worldBound"] = new
            {
                x = wb.x,
                y = wb.y,
                width = wb.width,
                height = wb.height
            };

            // resolvedStyle - only output meaningful non-default values
            result["resolvedStyle"] = SerializeResolvedStyle(el);

            // Children info
            result["childCount"] = el.childCount;

            if (depth > 0 && el.childCount > 0)
            {
                result["children"] = el.Children()
                    .Select(c => SerializeElement(c, depth - 1))
                    .ToList();
            }

            return result;
        }

        /// <summary>
        /// Serialize resolvedStyle, skipping NaN and meaningless default values to reduce data size.
        /// </summary>
        private static object SerializeResolvedStyle(VisualElement el)
        {
            var s = el.resolvedStyle;
            var style = new Dictionary<string, object>();

            // Colors (only output non-transparent colors)
            AddColorIfVisible(style, "color", s.color);
            AddColorIfVisible(style, "backgroundColor", s.backgroundColor);
            AddColorIfVisible(style, "borderTopColor", s.borderTopColor);
            AddColorIfVisible(style, "borderBottomColor", s.borderBottomColor);
            AddColorIfVisible(style, "borderLeftColor", s.borderLeftColor);
            AddColorIfVisible(style, "borderRightColor", s.borderRightColor);

            // Dimensions (skip NaN)
            AddFloatIfValid(style, "width", s.width);
            AddFloatIfValid(style, "height", s.height);

            // Padding
            AddFloatIfNonZero(style, "paddingTop", s.paddingTop);
            AddFloatIfNonZero(style, "paddingRight", s.paddingRight);
            AddFloatIfNonZero(style, "paddingBottom", s.paddingBottom);
            AddFloatIfNonZero(style, "paddingLeft", s.paddingLeft);

            // Margin
            AddFloatIfNonZero(style, "marginTop", s.marginTop);
            AddFloatIfNonZero(style, "marginRight", s.marginRight);
            AddFloatIfNonZero(style, "marginBottom", s.marginBottom);
            AddFloatIfNonZero(style, "marginLeft", s.marginLeft);

            // Border width
            AddFloatIfNonZero(style, "borderTopWidth", s.borderTopWidth);
            AddFloatIfNonZero(style, "borderRightWidth", s.borderRightWidth);
            AddFloatIfNonZero(style, "borderBottomWidth", s.borderBottomWidth);
            AddFloatIfNonZero(style, "borderLeftWidth", s.borderLeftWidth);

            // Border radius
            AddFloatIfNonZero(style, "borderTopLeftRadius", s.borderTopLeftRadius);
            AddFloatIfNonZero(style, "borderTopRightRadius", s.borderTopRightRadius);
            AddFloatIfNonZero(style, "borderBottomLeftRadius", s.borderBottomLeftRadius);
            AddFloatIfNonZero(style, "borderBottomRightRadius", s.borderBottomRightRadius);

            // Font
            AddFloatIfValid(style, "fontSize", s.fontSize);
            style["unityFontStyleAndWeight"] = s.unityFontStyleAndWeight.ToString();

            // Layout mode
            style["display"] = s.display.ToString();
            style["flexDirection"] = s.flexDirection.ToString();
            style["alignItems"] = s.alignItems.ToString();
            style["justifyContent"] = s.justifyContent.ToString();

            // Flex properties (only output non-default values)
            AddFloatIfNonZero(style, "flexGrow", s.flexGrow);
            if (s.flexShrink != 1f && !float.IsNaN(s.flexShrink))
                style["flexShrink"] = s.flexShrink;

            // Opacity (only output non-fully-opaque values)
            if (!float.IsNaN(s.opacity) && s.opacity < 1f)
                style["opacity"] = s.opacity;

            return style;
        }

        /// <summary>
        /// Add color to dictionary (only when alpha > 0).
        /// </summary>
        private static void AddColorIfVisible(Dictionary<string, object> dict, string key, Color c)
        {
            if (c.a > 0f)
                dict[key] = ColorToString(c);
        }

        /// <summary>
        /// Add float value to dictionary (skip NaN).
        /// </summary>
        private static void AddFloatIfValid(Dictionary<string, object> dict, string key, float value)
        {
            if (!float.IsNaN(value))
                dict[key] = value;
        }

        /// <summary>
        /// Add float value to dictionary (skip NaN and 0).
        /// </summary>
        private static void AddFloatIfNonZero(Dictionary<string, object> dict, string key, float value)
        {
            if (!float.IsNaN(value) && value != 0f)
                dict[key] = value;
        }

        /// <summary>
        /// Convert Color to "rgba(r,g,b,a)" format string, RGB as 0-255 integers, A as 0.00-1.00.
        /// </summary>
        private static string ColorToString(Color c)
        {
            return $"rgba({Mathf.RoundToInt(c.r * 255)},{Mathf.RoundToInt(c.g * 255)},{Mathf.RoundToInt(c.b * 255)},{c.a:F2})";
        }

        /// <summary>
        /// Get the full path of a GameObject in the scene hierarchy (for locating purposes).
        /// </summary>
        private static string GetGameObjectPath(GameObject go)
        {
            var path = go.name;
            var parent = go.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }

        /// <summary>
        /// Recursively build tree text representation using "├─" / "└─" connectors.
        /// Format: name (Type) .class1.class2 "text" [widthxheight]
        /// </summary>
        private static void BuildTreeText(StringBuilder sb, VisualElement el, int remainingDepth, string prefix, bool isLast)
        {
            // Build connector prefix for the current node
            string connector = isLast ? "└─ " : "├─ ";
            string line = prefix + (prefix.Length == 0 ? "" : connector);

            // Node description
            sb.Append(line);
            sb.Append(string.IsNullOrEmpty(el.name) ? "(unnamed)" : el.name);
            sb.Append($" ({el.GetType().Name})");

            // Class list
            var classes = el.GetClasses().ToList();
            if (classes.Count > 0)
                sb.Append(" " + string.Join("", classes.Select(c => "." + c)));

            // Text content
            if (el is TextElement textEl && !string.IsNullOrEmpty(textEl.text))
                sb.Append($" \"{textEl.text}\"");

            // Layout dimensions
            var wb = el.worldBound;
            if (!float.IsNaN(wb.width) && !float.IsNaN(wb.height))
                sb.Append($" [{Mathf.RoundToInt(wb.width)}x{Mathf.RoundToInt(wb.height)}]");

            sb.AppendLine();

            // Recurse into children
            if (remainingDepth <= 0)
            {
                if (el.childCount > 0)
                {
                    // Show ellipsis when max depth is exceeded
                    string childPrefix = prefix + (prefix.Length == 0 ? "" : (isLast ? "   " : "│  "));
                    sb.AppendLine(childPrefix + $"└─ ... ({el.childCount} children)");
                }
                return;
            }

            var children = el.Children().ToList();
            string newPrefix = prefix + (prefix.Length == 0 ? "" : (isLast ? "   " : "│  "));

            for (int i = 0; i < children.Count; i++)
            {
                bool childIsLast = (i == children.Count - 1);
                BuildTreeText(sb, children[i], remainingDepth - 1, newPrefix, childIsLast);
            }
        }
    }
}
