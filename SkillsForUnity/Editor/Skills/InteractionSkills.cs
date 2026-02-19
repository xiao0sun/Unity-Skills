using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnitySkills
{
    /// <summary>
    /// Interaction skills - simulate UI clicks, input, keyboard events for automated testing.
    /// All skills require Play Mode (UI elements only exist at runtime).
    /// </summary>
    public static class InteractionSkills
    {
        // ─────────────────────────────────────────────────────────────
        //  Shared helpers
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Find a VisualElement by USS selector across all UIDocuments.
        /// Searches documents in sortingOrder. Returns first match.
        /// </summary>
        private static (VisualElement element, UIDocument document, string error) FindElementAcrossDocuments(string selector)
        {
            if (string.IsNullOrEmpty(selector))
                return (null, null, "selector is required");

            var documents = UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None)
                .OrderBy(d => d.sortingOrder)
                .ToArray();

            if (documents.Length == 0)
                return (null, null, "No UIDocument found in scene");

            foreach (var doc in documents)
            {
                var root = doc.rootVisualElement;
                if (root == null) continue;

                var element = FindElement(root, selector);
                if (element != null)
                    return (element, doc, null);
            }

            return (null, null, $"No element matching '{selector}' found in any UIDocument");
        }

        /// <summary>
        /// Find a VisualElement using USS selector syntax.
        /// "#name" -> by name, ".class" -> by class, other -> name first then class.
        /// </summary>
        private static VisualElement FindElement(VisualElement root, string selector)
        {
            if (string.IsNullOrEmpty(selector))
                return root;

            if (selector.StartsWith("#"))
                return root.Q(name: selector.Substring(1));
            else if (selector.StartsWith("."))
                return root.Q(className: selector.Substring(1));
            else
            {
                var byName = root.Q(name: selector);
                return byName ?? root.Q(className: selector);
            }
        }

        /// <summary>
        /// Build a summary object for a VisualElement (for return values).
        /// </summary>
        private static object ElementSummary(VisualElement el)
        {
            var wb = el.worldBound;
            var result = new Dictionary<string, object>
            {
                ["name"] = el.name ?? "",
                ["type"] = el.GetType().Name,
                ["classes"] = el.GetClasses().ToList(),
                ["worldBound"] = new { x = wb.x, y = wb.y, width = wb.width, height = wb.height },
                ["enabled"] = el.enabledSelf,
                ["visible"] = el.resolvedStyle.display != DisplayStyle.None
            };
            if (el is TextElement textEl && !string.IsNullOrEmpty(textEl.text))
                result["text"] = textEl.text;
            return result;
        }

        /// <summary>
        /// Set position fields on a PointerEventBase via reflection (fields are read-only in public API).
        /// In Unity 6, PointerEventBase properties like position, localPosition, button, pointerId
        /// are read-only. We must use reflection to set the underlying private fields.
        /// </summary>
        private static void SetPointerEventPosition<T>(PointerEventBase<T> evt, Vector2 position) where T : PointerEventBase<T>, new()
        {
            // Walk up the type hierarchy to find fields in PointerEventBase<T> or its base classes
            var type = evt.GetType();
            var bindingFlags = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public;

            // Try to set position - look for backing field in the hierarchy
            SetFieldInHierarchy(evt, type, "m_Position", (Vector3)position, bindingFlags);
            SetFieldInHierarchy(evt, type, "m_LocalPosition", (Vector3)position, bindingFlags);

            // Also try the property setter approach as fallback
            var posProperty = FindPropertyInHierarchy(type, "position", bindingFlags | BindingFlags.Public);
            if (posProperty != null && posProperty.CanWrite)
                posProperty.SetValue(evt, (Vector3)position);

            var localPosProperty = FindPropertyInHierarchy(type, "localPosition", bindingFlags | BindingFlags.Public);
            if (localPosProperty != null && localPosProperty.CanWrite)
                localPosProperty.SetValue(evt, (Vector3)position);

            // Set pointer button to left click (0)
            SetFieldInHierarchy(evt, type, "m_Button", 0, bindingFlags);

            // Set pointerId to default mouse
            SetFieldInHierarchy(evt, type, "m_PointerId", PointerId.mousePointerId, bindingFlags);
        }

        /// <summary>
        /// Search for a field by name up the type hierarchy and set its value.
        /// </summary>
        private static void SetFieldInHierarchy(object obj, Type type, string fieldName, object value, BindingFlags flags)
        {
            var currentType = type;
            while (currentType != null)
            {
                var field = currentType.GetField(fieldName, flags);
                if (field != null)
                {
                    field.SetValue(obj, value);
                    return;
                }
                currentType = currentType.BaseType;
            }
        }

        /// <summary>
        /// Search for a property by name up the type hierarchy.
        /// </summary>
        private static PropertyInfo FindPropertyInHierarchy(Type type, string propertyName, BindingFlags flags)
        {
            var currentType = type;
            while (currentType != null)
            {
                var prop = currentType.GetProperty(propertyName, flags);
                if (prop != null)
                    return prop;
                currentType = currentType.BaseType;
            }
            return null;
        }

        // ─────────────────────────────────────────────────────────────
        //  Skill 1: ui_click
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 模拟点击 UI 元素。
        /// Button 类型使用 Clickable.SimulateSingleClick 直接触发；
        /// 其他元素发送 PointerDown + PointerUp + ClickEvent 序列。
        /// </summary>
        [UnitySkill("ui_click", "Simulate a click on a UI Toolkit element (Play Mode required). selector: '#name', '.class', or 'name'.")]
        public static object UIClick(string selector)
        {
            if (!EditorApplication.isPlaying)
                return new { error = "Play Mode required. UI elements only exist at runtime." };

            var (element, doc, error) = FindElementAcrossDocuments(selector);
            if (error != null)
                return new { error, selector };

            // 获取元素中心的世界坐标
            var wb = element.worldBound;
            if (float.IsNaN(wb.x) || float.IsNaN(wb.y) || wb.width <= 0 || wb.height <= 0)
                return new { error = $"Element '{selector}' has invalid bounds (not visible or not laid out)", selector };

            var center = wb.center;
            string clickMethod;

            // Button 类型使用 NavigationSubmitEvent 触发点击，模拟键盘 Enter 行为
            if (element is Button)
            {
                using (var evt = NavigationSubmitEvent.GetPooled())
                {
                    evt.target = element;
                    element.SendEvent(evt);
                }
                clickMethod = "NavigationSubmitEvent";
            }
            else
            {
                // 非 Button 元素：发送完整的指针事件序列 + ClickEvent
                using (var downEvt = PointerDownEvent.GetPooled())
                {
                    downEvt.target = element;
                    SetPointerEventPosition(downEvt, center);
                    element.SendEvent(downEvt);
                }

                using (var upEvt = PointerUpEvent.GetPooled())
                {
                    upEvt.target = element;
                    SetPointerEventPosition(upEvt, center);
                    element.SendEvent(upEvt);
                }

                // 手动发送 ClickEvent 作为后备，确保 RegisterCallback<ClickEvent> 也能触发
                using (var clickEvt = ClickEvent.GetPooled())
                {
                    clickEvt.target = element;
                    element.SendEvent(clickEvt);
                }
                clickMethod = "PointerDown+PointerUp+ClickEvent";
            }

            return new
            {
                success = true,
                clicked = ElementSummary(element),
                position = new { x = center.x, y = center.y },
                document = doc.gameObject.name,
                clickMethod
            };
        }

        // ─────────────────────────────────────────────────────────────
        //  Skill 2: ui_set_value
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Set the value of a UI input element (TextField, Toggle, Slider, DropdownField).
        /// Triggers the appropriate ChangeEvent via the value property setter.
        /// </summary>
        [UnitySkill("ui_set_value", "Set the value of a UI input element (Play Mode required). selector: '#name', '.class', or 'name'. value: the value to set (string for text/dropdown, 'true'/'false' for toggle, number for slider).")]
        public static object UISetValue(string selector, string value)
        {
            if (!EditorApplication.isPlaying)
                return new { error = "Play Mode required." };

            if (string.IsNullOrEmpty(value))
                return new { error = "value parameter is required" };

            var (element, doc, error) = FindElementAcrossDocuments(selector);
            if (error != null)
                return new { error, selector };

            string elementType = element.GetType().Name;
            string previousValue = null;

            // Set value based on element type
            if (element is TextField textField)
            {
                previousValue = textField.value;
                textField.value = value;
            }
            else if (element is Toggle toggle)
            {
                previousValue = toggle.value.ToString();
                if (!bool.TryParse(value, out bool boolVal))
                    return new { error = $"Cannot parse '{value}' as bool for Toggle", selector };
                toggle.value = boolVal;
            }
            else if (element is Slider slider)
            {
                previousValue = slider.value.ToString("F2");
                if (!float.TryParse(value, out float floatVal))
                    return new { error = $"Cannot parse '{value}' as float for Slider", selector };
                floatVal = Mathf.Clamp(floatVal, slider.lowValue, slider.highValue);
                slider.value = floatVal;
            }
            else if (element is SliderInt sliderInt)
            {
                previousValue = sliderInt.value.ToString();
                if (!int.TryParse(value, out int intVal))
                    return new { error = $"Cannot parse '{value}' as int for SliderInt", selector };
                intVal = Mathf.Clamp(intVal, sliderInt.lowValue, sliderInt.highValue);
                sliderInt.value = intVal;
            }
            else if (element is DropdownField dropdown)
            {
                previousValue = dropdown.value;
                dropdown.value = value;
            }
            else
            {
                return new { error = $"Unsupported element type: {elementType}. Supported: TextField, Toggle, Slider, SliderInt, DropdownField", selector };
            }

            return new
            {
                success = true,
                elementType,
                previousValue,
                newValue = value,
                element = ElementSummary(element),
                document = doc.gameObject.name
            };
        }

        // ─────────────────────────────────────────────────────────────
        //  Skill 3: ui_submit
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Send a NavigationSubmitEvent to a UI element (simulates Enter key on focused element).
        /// </summary>
        [UnitySkill("ui_submit", "Send NavigationSubmitEvent to a UI element (Play Mode required). selector: '#name', '.class', or 'name'.")]
        public static object UISubmit(string selector)
        {
            if (!EditorApplication.isPlaying)
                return new { error = "Play Mode required." };

            var (element, doc, error) = FindElementAcrossDocuments(selector);
            if (error != null)
                return new { error, selector };

            using (var evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = element;
                element.SendEvent(evt);
            }

            return new
            {
                success = true,
                submitted = ElementSummary(element),
                document = doc.gameObject.name
            };
        }

        // ─────────────────────────────────────────────────────────────
        //  Skill 4: key_press
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Simulate a key press (KeyDown + KeyUp) on the first UIDocument's panel.
        /// keyCode: Unity KeyCode name (e.g. "Escape", "Return", "Space", "F1").
        /// modifiers: comma-separated list of modifiers ("shift", "ctrl", "alt", "command").
        /// </summary>
        [UnitySkill("key_press", "Simulate a keyboard key press (Play Mode required). keyCode: KeyCode name (e.g. 'Escape', 'Return', 'Space'). modifiers: optional comma-separated modifiers ('shift,ctrl,alt,command').")]
        public static object KeyPress(string keyCode, string modifiers = "")
        {
            if (!EditorApplication.isPlaying)
                return new { error = "Play Mode required." };

            if (string.IsNullOrEmpty(keyCode))
                return new { error = "keyCode is required" };

            // Parse KeyCode
            if (!Enum.TryParse<KeyCode>(keyCode, true, out var parsedKeyCode))
                return new { error = $"Invalid keyCode: '{keyCode}'. Use Unity KeyCode names (e.g. Escape, Return, Space, F1, A, Alpha1)." };

            // Parse modifiers
            EventModifiers eventModifiers = EventModifiers.None;
            if (!string.IsNullOrEmpty(modifiers))
            {
                foreach (var mod in modifiers.Split(',').Select(s => s.Trim().ToLower()))
                {
                    switch (mod)
                    {
                        case "shift": eventModifiers |= EventModifiers.Shift; break;
                        case "ctrl":
                        case "control": eventModifiers |= EventModifiers.Control; break;
                        case "alt": eventModifiers |= EventModifiers.Alt; break;
                        case "command":
                        case "cmd": eventModifiers |= EventModifiers.Command; break;
                        default:
                            return new { error = $"Unknown modifier: '{mod}'. Supported: shift, ctrl, alt, command." };
                    }
                }
            }

            // Find first UIDocument's panel root
            var documents = UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None)
                .OrderBy(d => d.sortingOrder)
                .ToArray();

            if (documents.Length == 0)
                return new { error = "No UIDocument found in scene" };

            var root = documents[0].rootVisualElement;
            if (root == null)
                return new { error = "UIDocument root is null" };

            var panel = root.panel;
            if (panel == null)
                return new { error = "Panel is null" };

            // Determine target: focused element or root
            var focusedElement = panel.focusController?.focusedElement as VisualElement;
            var target = focusedElement ?? root;

            // Build a UnityEngine.Event for KeyDown (Unity 6 requires Event-based GetPooled)
            var keyDownNativeEvent = new Event
            {
                type = EventType.KeyDown,
                keyCode = parsedKeyCode,
                character = '\0',
                modifiers = eventModifiers
            };

            using (var downEvt = KeyDownEvent.GetPooled(keyDownNativeEvent))
            {
                downEvt.target = target;
                target.SendEvent(downEvt);
            }

            // Build a UnityEngine.Event for KeyUp
            var keyUpNativeEvent = new Event
            {
                type = EventType.KeyUp,
                keyCode = parsedKeyCode,
                character = '\0',
                modifiers = eventModifiers
            };

            using (var upEvt = KeyUpEvent.GetPooled(keyUpNativeEvent))
            {
                upEvt.target = target;
                target.SendEvent(upEvt);
            }

            return new
            {
                success = true,
                keyCode = parsedKeyCode.ToString(),
                modifiers = eventModifiers.ToString(),
                targetElement = target.name ?? "(root)"
            };
        }

        // ─────────────────────────────────────────────────────────────
        //  Skill 5: pointer_click
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Simulate a pointer click at screen coordinates.
        /// Useful for clicking on 3D scene objects via their screen position.
        /// Uses panel.Pick to find the hit element, then sends PointerDown + PointerUp.
        /// </summary>
        [UnitySkill("pointer_click", "Simulate a pointer click at screen coordinates (Play Mode required). x: screen X coordinate. y: screen Y coordinate.")]
        public static object PointerClick(float x, float y)
        {
            if (!EditorApplication.isPlaying)
                return new { error = "Play Mode required." };

            var documents = UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None)
                .OrderBy(d => d.sortingOrder)
                .ToArray();

            if (documents.Length == 0)
                return new { error = "No UIDocument found in scene" };

            var root = documents[0].rootVisualElement;
            if (root == null)
                return new { error = "UIDocument root is null" };

            var position = new Vector2(x, y);

            // Find which element is at this position using panel.Pick
            var hitElement = root.panel.Pick(position);

            // Send to the hit element, or root if nothing was hit
            var target = hitElement ?? root;

            using (var downEvt = PointerDownEvent.GetPooled())
            {
                downEvt.target = target;
                SetPointerEventPosition(downEvt, position);
                target.SendEvent(downEvt);
            }

            using (var upEvt = PointerUpEvent.GetPooled())
            {
                upEvt.target = target;
                SetPointerEventPosition(upEvt, position);
                target.SendEvent(upEvt);
            }

            return new
            {
                success = true,
                position = new { x, y },
                hitElement = hitElement != null ? ElementSummary(hitElement) : null,
                hitNothing = hitElement == null
            };
        }

        // ─────────────────────────────────────────────────────────────
        //  Wait job infrastructure (async start/poll pattern)
        // ─────────────────────────────────────────────────────────────

        private const string WaitActiveJobsKey = "InteractionSkills_WaitActiveJobs";
        private const string WaitJobKeyPrefix = "InteractionSkills_WaitJob_";

        private static readonly Dictionary<string, WaitJob> _waitJobs = new Dictionary<string, WaitJob>();
        private static readonly Dictionary<string, EditorApplication.CallbackFunction> _waitCallbacks
            = new Dictionary<string, EditorApplication.CallbackFunction>();

        private static readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            StringEscapeHandling = StringEscapeHandling.Default,
            NullValueHandling = NullValueHandling.Ignore
        };

        internal class WaitJob
        {
            public string JobId;
            public string Status = "waiting";  // waiting | found | not_found | timeout | error
            public string Selector;
            public string Condition;           // exists, not_exists, text_contains, text_equals
            public string Value;               // for text_contains/text_equals
            public int TimeoutSeconds;
            public long StartTimeTicks;
            public string ResultMessage;
            public object ResultElement;       // ElementSummary when found

            [JsonIgnore]
            public DateTime StartTime
            {
                get => new DateTime(StartTimeTicks);
                set => StartTimeTicks = value.Ticks;
            }
        }

        [InitializeOnLoadMethod]
        private static void OnDomainReload()
        {
            _waitJobs.Clear();
            _waitCallbacks.Clear();

            // Restore running jobs from SessionState
            var activeIds = GetWaitActiveJobIds();
            foreach (var jobId in activeIds.ToList())
            {
                var job = LoadWaitJob(jobId);
                if (job == null || job.Status != "waiting")
                {
                    if (job != null) _waitJobs[jobId] = job;
                    continue;
                }
                _waitJobs[jobId] = job;
                RegisterWaitCallback(jobId);
            }
        }

        private static void PersistWaitJob(WaitJob job)
        {
            var json = JsonConvert.SerializeObject(job, _jsonSettings);
            SessionState.SetString(WaitJobKeyPrefix + job.JobId, json);
            var activeIds = GetWaitActiveJobIds();
            if (!activeIds.Contains(job.JobId))
            {
                activeIds.Add(job.JobId);
                SessionState.SetString(WaitActiveJobsKey, JsonConvert.SerializeObject(activeIds));
            }
        }

        private static WaitJob LoadWaitJob(string jobId)
        {
            var json = SessionState.GetString(WaitJobKeyPrefix + jobId, "");
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonConvert.DeserializeObject<WaitJob>(json, _jsonSettings); }
            catch { return null; }
        }

        private static void RemoveWaitJob(string jobId)
        {
            SessionState.EraseString(WaitJobKeyPrefix + jobId);
            var activeIds = GetWaitActiveJobIds();
            activeIds.Remove(jobId);
            SessionState.SetString(WaitActiveJobsKey, JsonConvert.SerializeObject(activeIds));
        }

        private static List<string> GetWaitActiveJobIds()
        {
            var json = SessionState.GetString(WaitActiveJobsKey, "[]");
            try { return JsonConvert.DeserializeObject<List<string>>(json) ?? new List<string>(); }
            catch { return new List<string>(); }
        }

        private static WaitJob GetOrLoadWaitJob(string jobId)
        {
            if (_waitJobs.TryGetValue(jobId, out var cached)) return cached;
            var loaded = LoadWaitJob(jobId);
            if (loaded != null) _waitJobs[jobId] = loaded;
            return loaded;
        }

        private static void RegisterWaitCallback(string jobId)
        {
            if (_waitCallbacks.TryGetValue(jobId, out var existing))
                EditorApplication.update -= existing;
            EditorApplication.CallbackFunction callback = null;
            callback = () => PollWaitCondition(jobId, callback);
            _waitCallbacks[jobId] = callback;
            EditorApplication.update += callback;
        }

        private static void UnregisterWaitCallback(string jobId)
        {
            if (_waitCallbacks.TryGetValue(jobId, out var cb))
            {
                EditorApplication.update -= cb;
                _waitCallbacks.Remove(jobId);
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Skill 6a: wait_for_element (start)
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Start waiting for a UI element to meet a condition.
        /// Returns a jobId for polling via wait_get_result.
        /// If the condition is already satisfied, returns the result immediately.
        /// condition: "exists" | "not_exists" | "text_contains" | "text_equals"
        /// </summary>
        [UnitySkill("wait_for_element", "Start waiting for a UI element condition (Play Mode required). selector: USS selector. condition: 'exists'/'not_exists'/'text_contains'/'text_equals'. value: text to match (for text_ conditions). timeout: seconds to wait (default 10).")]
        public static object WaitForElement(string selector, string condition = "exists", string value = "", int timeout = 10)
        {
            if (!EditorApplication.isPlaying)
                return new { error = "Play Mode required." };

            if (string.IsNullOrEmpty(selector))
                return new { error = "selector is required" };

            var validConditions = new[] { "exists", "not_exists", "text_contains", "text_equals" };
            if (!validConditions.Contains(condition))
                return new { error = $"Invalid condition: '{condition}'. Valid: {string.Join(", ", validConditions)}" };

            if ((condition == "text_contains" || condition == "text_equals") && string.IsNullOrEmpty(value))
                return new { error = $"value parameter is required for condition '{condition}'" };

            if (timeout < 1 || timeout > 60)
                return new { error = "timeout must be between 1 and 60 seconds" };

            // Check condition immediately (may already be satisfied)
            var immediateResult = CheckWaitCondition(selector, condition, value);
            if (immediateResult.satisfied)
            {
                return new
                {
                    jobId = (string)null,
                    status = "found",
                    message = $"Condition '{condition}' already satisfied",
                    element = immediateResult.elementSummary,
                    elapsed = 0.0
                };
            }

            // Start async wait
            var jobId = "wait-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var job = new WaitJob
            {
                JobId = jobId,
                Selector = selector,
                Condition = condition,
                Value = value ?? "",
                TimeoutSeconds = timeout,
                StartTime = DateTime.Now
            };

            _waitJobs[jobId] = job;
            PersistWaitJob(job);
            RegisterWaitCallback(jobId);

            return new { jobId, status = "waiting", message = $"Waiting for '{selector}' condition '{condition}'" };
        }

        // ─────────────────────────────────────────────────────────────
        //  Skill 6b: wait_get_result (poll)
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Get the result of a wait_for_element job.
        /// </summary>
        [UnitySkill("wait_get_result", "Get the result of a wait_for_element job.")]
        public static object WaitGetResult(string jobId)
        {
            if (string.IsNullOrEmpty(jobId))
                return new { error = "jobId is required" };

            var job = GetOrLoadWaitJob(jobId);
            if (job == null)
                return new { error = $"Wait job not found: {jobId}" };

            var elapsed = (DateTime.Now - job.StartTime).TotalSeconds;

            if (job.Status == "waiting")
            {
                return new
                {
                    jobId,
                    status = "waiting",
                    elapsed,
                    selector = job.Selector,
                    condition = job.Condition
                };
            }

            // Terminal state - clean up
            return new
            {
                jobId,
                status = job.Status,
                elapsed,
                message = job.ResultMessage,
                element = job.ResultElement
            };
        }

        // ─────────────────────────────────────────────────────────────
        //  Wait polling logic (EditorApplication.update callback)
        // ─────────────────────────────────────────────────────────────

        private static void PollWaitCondition(string jobId, EditorApplication.CallbackFunction callback)
        {
            var job = GetOrLoadWaitJob(jobId);
            if (job == null || job.Status != "waiting")
            {
                UnregisterWaitCallback(jobId);
                return;
            }

            var elapsed = (DateTime.Now - job.StartTime).TotalSeconds;

            // Timeout check
            if (elapsed > job.TimeoutSeconds)
            {
                job.Status = "timeout";
                job.ResultMessage = $"Timeout after {job.TimeoutSeconds}s waiting for '{job.Selector}' condition '{job.Condition}'";
                PersistWaitJob(job);
                UnregisterWaitCallback(jobId);
                return;
            }

            // Not in play mode anymore
            if (!EditorApplication.isPlaying)
            {
                job.Status = "error";
                job.ResultMessage = "Play Mode exited while waiting";
                PersistWaitJob(job);
                UnregisterWaitCallback(jobId);
                return;
            }

            // Check condition
            var result = CheckWaitCondition(job.Selector, job.Condition, job.Value);
            if (result.satisfied)
            {
                job.Status = "found";
                job.ResultMessage = $"Condition '{job.Condition}' satisfied after {elapsed:F1}s";
                job.ResultElement = result.elementSummary;
                PersistWaitJob(job);
                UnregisterWaitCallback(jobId);
            }
        }

        /// <summary>
        /// Check if a wait condition is currently satisfied.
        /// </summary>
        private static (bool satisfied, object elementSummary) CheckWaitCondition(string selector, string condition, string value)
        {
            var (element, _, _) = FindElementAcrossDocuments(selector);

            switch (condition)
            {
                case "exists":
                    if (element != null && element.resolvedStyle.display != DisplayStyle.None)
                        return (true, ElementSummary(element));
                    return (false, null);

                case "not_exists":
                    if (element == null || element.resolvedStyle.display == DisplayStyle.None)
                        return (true, null);
                    return (false, null);

                case "text_contains":
                    if (element is TextElement te1 && te1.text != null && te1.text.Contains(value))
                        return (true, ElementSummary(element));
                    return (false, element != null ? ElementSummary(element) : null);

                case "text_equals":
                    if (element is TextElement te2 && te2.text == value)
                        return (true, ElementSummary(element));
                    return (false, element != null ? ElementSummary(element) : null);

                default:
                    return (false, null);
            }
        }
    }
}
