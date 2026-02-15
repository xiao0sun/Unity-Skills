using UnityEngine;
using UnityEngine.Events;
using UnityEditor;
using UnityEditor.Events;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

namespace UnitySkills
{
    /// <summary>
    /// Event management skills - inspect and modify UnityEvents (e.g. Button.onClick).
    /// </summary>
    public static class EventSkills
    {
        [UnitySkill("event_get_listeners", "Get persistent listeners of a UnityEvent")]
        public static object EventGetListeners(string objectName, string componentName, string eventName)
        {
            var (go, findErr) = GameObjectFinder.FindOrError(name: objectName);
            if (findErr != null) return findErr;

            // Find component
            var component = go.GetComponent(componentName);
            if (component == null)
                return new { error = $"Component not found: {componentName} on {objectName}" };

            // Find event field via reflection
            var type = component.GetType();
            var field = type.GetField(eventName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var property = type.GetProperty(eventName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            
            UnityEventBase unityEvent = null;

            if (field != null)
                unityEvent = field.GetValue(component) as UnityEventBase;
            else if (property != null)
                unityEvent = property.GetValue(component) as UnityEventBase;

            if (unityEvent == null)
                return new { error = $"UnityEvent field/property '{eventName}' not found or null on {componentName}" };

            // Inspect listeners
            int count = unityEvent.GetPersistentEventCount();
            var listeners = new List<object>();

            for (int i = 0; i < count; i++)
            {
                var target = unityEvent.GetPersistentTarget(i);
                var methodName = unityEvent.GetPersistentMethodName(i);
                var state = unityEvent.GetPersistentListenerState(i);

                listeners.Add(new
                {
                    index = i,
                    target = target != null ? target.name : "null",
                    targetType = target != null ? target.GetType().Name : "null",
                    method = methodName,
                    state = state.ToString()
                });
            }

            return new
            {
                success = true,
                gameObject = objectName,
                component = componentName,
                eventName = eventName,
                listenerCount = count,
                listeners
            };
        }

        [UnitySkill("event_add_listener", "Add a persistent listener to a UnityEvent (Editor time). Supported args: void, int, float, string, bool, Object.")]
        public static object EventAddListener(
            string objectName, string componentName, string eventName,
            string targetObjectName, string targetComponentName, string methodName,
            string mode = "RuntimeOnly",
            string argType = "void", // void, int, float, string, bool, object
            float floatArg = 0, int intArg = 0, string stringArg = null, bool boolArg = false)
        {
            var (go, goErr) = GameObjectFinder.FindOrError(name: objectName);
            if (goErr != null) return goErr;

            var component = go.GetComponent(componentName);
            if (component == null) return new { error = $"Source Component not found: {componentName}" };

            var (targetGo, tgtErr) = GameObjectFinder.FindOrError(name: targetObjectName);
            if (tgtErr != null) return tgtErr;

            var targetComponent = targetGo.GetComponent(targetComponentName);
            if (targetComponent == null) return new { error = $"Target Component not found: {targetComponentName}" };

            // Find UnityEvent
            var type = component.GetType();
            var field = type.GetField(eventName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var property = type.GetProperty(eventName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            
            UnityEvent unityEvent = null; // Note: Only supporting standard UnityEvent for now, not generic UnityEvent<T> as field type

            object rawEvent = null;
            if (field != null) rawEvent = field.GetValue(component);
            else if (property != null) rawEvent = property.GetValue(component);

            if (rawEvent == null)
                return new { error = $"UnityEvent '{eventName}' not found on {componentName}" };

            if (!(rawEvent is UnityEvent))
            {
                // Try casting if it is a subclass of UnityEventBase but maybe generic? 
                // UnityEventTools usually requires strictly UnityEvent or specific subclasses.
                // For simplicity, we cast to UnityEventBase but UnityEventTools needs UnityEvent usually.
                // Actually UnityEventTools.AddPersistentListener overloads take UnityEvent or UnityEvent<T>.
                // Dynamic dispatch might be needed for UnityEvent<T>, skipping for V1.
                 unityEvent = rawEvent as UnityEvent;
                 if (unityEvent == null)
                     return new { error = $"Field '{eventName}' is not a standard UnityEvent. Generic events (UnityEvent<T>) not yet supported in this version." };
            }
            else
            {
                unityEvent = (UnityEvent)rawEvent;
            }

            // Record Undo
            WorkflowManager.SnapshotObject(component);
            Undo.RecordObject(component, "Add Event Listener");

            // Resolve Method
            // We need to find the method on target targetComponent
            MethodInfo methodInfo = null;
            
            // Search logic based on argType
            switch (argType.ToLower())
            {
                case "void":
                    methodInfo = targetComponent.GetType().GetMethod(methodName, 
                        BindingFlags.Instance | BindingFlags.Public, null, System.Type.EmptyTypes, null);
                    if (methodInfo == null) return new { error = $"Method '{methodName}()' not found on {targetComponentName}" };
                    
                    var voidDelegate = System.Delegate.CreateDelegate(typeof(UnityAction), targetComponent, methodInfo) as UnityAction;
                    UnityEventTools.AddPersistentListener(unityEvent, voidDelegate);
                    break;

                case "float":
                    methodInfo = targetComponent.GetType().GetMethod(methodName, 
                        BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(float) }, null);
                    if (methodInfo == null) return new { error = $"Method '{methodName}(float)' not found" };
                    
                    var floatDelegate = System.Delegate.CreateDelegate(typeof(UnityAction<float>), targetComponent, methodInfo) as UnityAction<float>;
                    UnityEventTools.AddFloatPersistentListener(unityEvent, floatDelegate, floatArg);
                    break;

                case "int":
                    methodInfo = targetComponent.GetType().GetMethod(methodName, 
                        BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(int) }, null);
                    if (methodInfo == null) return new { error = $"Method '{methodName}(int)' not found" };
                    
                    var intDelegate = System.Delegate.CreateDelegate(typeof(UnityAction<int>), targetComponent, methodInfo) as UnityAction<int>;
                    UnityEventTools.AddIntPersistentListener(unityEvent, intDelegate, intArg);
                    break;

                case "string":
                    methodInfo = targetComponent.GetType().GetMethod(methodName, 
                        BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(string) }, null);
                    if (methodInfo == null) return new { error = $"Method '{methodName}(string)' not found" };
                    
                    var stringDelegate = System.Delegate.CreateDelegate(typeof(UnityAction<string>), targetComponent, methodInfo) as UnityAction<string>;
                    UnityEventTools.AddStringPersistentListener(unityEvent, stringDelegate, stringArg);
                    break;

                case "bool":
                    methodInfo = targetComponent.GetType().GetMethod(methodName, 
                        BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(bool) }, null);
                    if (methodInfo == null) return new { error = $"Method '{methodName}(bool)' not found" };
                    
                    var boolDelegate = System.Delegate.CreateDelegate(typeof(UnityAction<bool>), targetComponent, methodInfo) as UnityAction<bool>;
                    UnityEventTools.AddBoolPersistentListener(unityEvent, boolDelegate, boolArg);
                    break;
                    
                default:
                    return new { error = $"Unsupported argType: {argType}" };
            }

            // Set Call State (Runtime/Editor)
            // The newly added listener is always the last one
            int index = unityEvent.GetPersistentEventCount() - 1;
            UnityEventCallState callState = UnityEventCallState.RuntimeOnly;
            if (mode.ToLower() == "editorandruntime") callState = UnityEventCallState.EditorAndRuntime;
            else if (mode.ToLower() == "off") callState = UnityEventCallState.Off;
            
            unityEvent.SetPersistentListenerState(index, callState);

            return new
            {
                success = true,
                message = $"Added listener {targetComponentName}.{methodName} to {componentName}.{eventName}",
                index
            };
        }

        [UnitySkill("event_remove_listener", "Remove a persistent listener by index")]
        public static object EventRemoveListener(string objectName, string componentName, string eventName, int index)
        {
            var (go, findErr) = GameObjectFinder.FindOrError(name: objectName);
            if (findErr != null) return findErr;

            var component = go.GetComponent(componentName);
            if (component == null) return new { error = $"Component not found: {componentName}" };

            var type = component.GetType();
            var field = type.GetField(eventName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var property = type.GetProperty(eventName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            UnityEventBase unityEvent = null;
            if (field != null) unityEvent = field.GetValue(component) as UnityEventBase;
            else if (property != null) unityEvent = property.GetValue(component) as UnityEventBase;

            if (unityEvent == null) return new { error = "UnityEvent not found" };

            if (Validate.InRange(index, 0, unityEvent.GetPersistentEventCount() - 1, "index") is object rangeErr) return rangeErr;

            WorkflowManager.SnapshotObject(component);
            Undo.RecordObject(component, "Remove Event Listener");
            UnityEventTools.RemovePersistentListener(unityEvent, index);

            return new { success = true, remainingCount = unityEvent.GetPersistentEventCount() };
        }

        [UnitySkill("event_invoke", "Invoke a UnityEvent explicitly (Runtime only)")]
        public static object EventInvoke(string objectName, string componentName, string eventName)
        {
             var (go, goErr) = GameObjectFinder.FindOrError(name: objectName);
            if (goErr != null) return goErr;

            var component = go.GetComponent(componentName);
            if (component == null) return new { error = $"Component not found: {componentName}" };

            var type = component.GetType();
            var field = type.GetField(eventName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var property = type.GetProperty(eventName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            UnityEventBase unityEvent = null;
            if (field != null) unityEvent = field.GetValue(component) as UnityEventBase;
            else if (property != null) unityEvent = property.GetValue(component) as UnityEventBase;

             if (unityEvent == null) return new { error = "UnityEvent not found" };
            
            // Invoke via Reflection 'Invoke' method
            // UnityEvent.Invoke() is public
            var invokeMethod = unityEvent.GetType().GetMethod("Invoke", BindingFlags.Instance | BindingFlags.Public);
            if (invokeMethod == null)
                return new { error = "Could not find Invoke method on event" };
                
            invokeMethod.Invoke(unityEvent, null);

            return new { success = true, message = "Event invoked" };
        }

        // Helper to find UnityEventBase on a component
        private static (UnityEventBase evt, Component comp, object error) FindEvent(string objectName, string componentName, string eventName)
        {
            var (go, findErr) = GameObjectFinder.FindOrError(name: objectName);
            if (findErr != null) return (null, null, findErr);
            var component = go.GetComponent(componentName);
            if (component == null) return (null, null, new { error = $"Component not found: {componentName}" });
            var type = component.GetType();
            var field = type.GetField(eventName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var property = type.GetProperty(eventName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            UnityEventBase evt = null;
            if (field != null) evt = field.GetValue(component) as UnityEventBase;
            else if (property != null) evt = property.GetValue(component) as UnityEventBase;
            if (evt == null) return (null, null, new { error = $"UnityEvent '{eventName}' not found" });
            return (evt, component, null);
        }

        [UnitySkill("event_clear_listeners", "Remove all persistent listeners from a UnityEvent")]
        public static object EventClearListeners(string objectName, string componentName, string eventName)
        {
            var (evt, comp, err) = FindEvent(objectName, componentName, eventName);
            if (err != null) return err;
            WorkflowManager.SnapshotObject(comp);
            Undo.RecordObject(comp, "Clear Listeners");
            int count = evt.GetPersistentEventCount();
            for (int i = count - 1; i >= 0; i--)
                UnityEventTools.RemovePersistentListener(evt, i);
            return new { success = true, removed = count };
        }

        [UnitySkill("event_set_listener_state", "Set a listener's call state (Off, RuntimeOnly, EditorAndRuntime)")]
        public static object EventSetListenerState(string objectName, string componentName, string eventName, int index, string state)
        {
            var (evt, comp, err) = FindEvent(objectName, componentName, eventName);
            if (err != null) return err;
            if (index < 0 || index >= evt.GetPersistentEventCount()) return new { error = "Index out of range" };
            if (!System.Enum.TryParse<UnityEventCallState>(state, true, out var callState)) return new { error = $"Invalid state: {state}" };
            WorkflowManager.SnapshotObject(comp);
            Undo.RecordObject(comp, "Set Listener State");
            evt.SetPersistentListenerState(index, callState);
            return new { success = true, index, state = callState.ToString() };
        }

        [UnitySkill("event_list_events", "List all UnityEvent fields on a component")]
        public static object EventListEvents(string objectName, string componentName)
        {
            var (go, findErr) = GameObjectFinder.FindOrError(name: objectName);
            if (findErr != null) return findErr;
            var component = go.GetComponent(componentName);
            if (component == null) return new { error = $"Component not found: {componentName}" };
            var type = component.GetType();
            var events = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(f => typeof(UnityEventBase).IsAssignableFrom(f.FieldType))
                .Select(f => { var e = f.GetValue(component) as UnityEventBase; return new { name = f.Name, type = f.FieldType.Name, listenerCount = e?.GetPersistentEventCount() ?? 0 }; })
                .ToArray();
            return new { success = true, component = componentName, count = events.Length, events };
        }

        [UnitySkill("event_add_listener_batch", "Add multiple listeners at once. items: JSON array of {targetObjectName, targetComponentName, methodName}")]
        public static object EventAddListenerBatch(string objectName, string componentName, string eventName, string items)
        {
            var list = Newtonsoft.Json.JsonConvert.DeserializeObject<List<BatchListenerItem>>(items);
            if (list == null || list.Count == 0) return new { error = "No items provided" };
            int added = 0;
            foreach (var item in list)
            {
                var result = EventAddListener(objectName, componentName, eventName, item.targetObjectName, item.targetComponentName, item.methodName);
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(result);
                if (!json.Contains("\"error\"")) added++;
            }
            return new { success = true, added, total = list.Count };
        }

        private class BatchListenerItem
        {
            public string targetObjectName { get; set; }
            public string targetComponentName { get; set; }
            public string methodName { get; set; }
        }

        [UnitySkill("event_copy_listeners", "Copy listeners from one event to another")]
        public static object EventCopyListeners(string sourceObject, string sourceComponent, string sourceEvent,
            string targetObject, string targetComponent, string targetEvent)
        {
            var (srcEvt, srcComp, srcErr) = FindEvent(sourceObject, sourceComponent, sourceEvent);
            if (srcErr != null) return srcErr;
            var (tgtEvt, tgtComp, tgtErr) = FindEvent(targetObject, targetComponent, targetEvent);
            if (tgtErr != null) return tgtErr;
            if (!(tgtEvt is UnityEvent tgtUnityEvent)) return new { error = "Target must be a standard UnityEvent" };
            WorkflowManager.SnapshotObject(tgtComp);
            Undo.RecordObject(tgtComp, "Copy Listeners");
            int copied = 0;
            for (int i = 0; i < srcEvt.GetPersistentEventCount(); i++)
            {
                var target = srcEvt.GetPersistentTarget(i);
                var method = srcEvt.GetPersistentMethodName(i);
                if (target == null) continue;
                var mi = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public, null, System.Type.EmptyTypes, null);
                if (mi != null)
                {
                    var del = System.Delegate.CreateDelegate(typeof(UnityAction), target, mi) as UnityAction;
                    UnityEventTools.AddPersistentListener(tgtUnityEvent, del);
                    copied++;
                }
            }
            return new { success = true, copied };
        }

        [UnitySkill("event_get_listener_count", "Get the number of persistent listeners on a UnityEvent")]
        public static object EventGetListenerCount(string objectName, string componentName, string eventName)
        {
            var (evt, comp, err) = FindEvent(objectName, componentName, eventName);
            if (err != null) return err;
            return new { success = true, count = evt.GetPersistentEventCount() };
        }
    }
}
