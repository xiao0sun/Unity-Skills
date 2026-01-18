using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Reflection;

namespace UnitySkills
{
    /// <summary>
    /// Component management skills - add, remove, get, set properties.
    /// Now supports finding by name, instanceId, or path.
    /// </summary>
    public static class ComponentSkills
    {
        [UnitySkill("component_add", "Add a component to a GameObject (supports name/instanceId/path)")]
        public static object ComponentAdd(string name = null, int instanceId = 0, string path = null, string componentType = null)
        {
            if (string.IsNullOrEmpty(componentType))
                return new { error = "componentType is required" };

            var (go, error) = GameObjectFinder.FindOrError(name, instanceId, path);
            if (error != null) return error;

            var type = FindComponentType(componentType);
            if (type == null)
                return new { error = $"Component type not found: {componentType}" };

            var comp = Undo.AddComponent(go, type);
            return new { success = true, gameObject = go.name, instanceId = go.GetInstanceID(), component = type.Name };
        }

        [UnitySkill("component_remove", "Remove a component from a GameObject (supports name/instanceId/path)")]
        public static object ComponentRemove(string name = null, int instanceId = 0, string path = null, string componentType = null)
        {
            if (string.IsNullOrEmpty(componentType))
                return new { error = "componentType is required" };

            var (go, error) = GameObjectFinder.FindOrError(name, instanceId, path);
            if (error != null) return error;

            var type = FindComponentType(componentType);
            if (type == null)
                return new { error = $"Component type not found: {componentType}" };

            var comp = go.GetComponent(type);
            if (comp == null)
                return new { error = $"Component not found on {go.name}: {componentType}" };

            Undo.DestroyObjectImmediate(comp);
            return new { success = true, gameObject = go.name, removed = componentType };
        }

        [UnitySkill("component_list", "List all components on a GameObject (supports name/instanceId/path)")]
        public static object ComponentList(string name = null, int instanceId = 0, string path = null)
        {
            var (go, error) = GameObjectFinder.FindOrError(name, instanceId, path);
            if (error != null) return error;

            var components = go.GetComponents<Component>()
                .Where(c => c != null)
                .Select(c => new { type = c.GetType().Name, enabled = (c as Behaviour)?.enabled ?? true })
                .ToArray();

            return new { gameObject = go.name, instanceId = go.GetInstanceID(), path = GameObjectFinder.GetPath(go), components };
        }

        [UnitySkill("component_set_property", "Set a property on a component (supports name/instanceId/path)")]
        public static object ComponentSetProperty(string name = null, int instanceId = 0, string path = null, string componentType = null, string propertyName = null, string value = null)
        {
            if (string.IsNullOrEmpty(componentType) || string.IsNullOrEmpty(propertyName))
                return new { error = "componentType and propertyName are required" };

            var (go, error) = GameObjectFinder.FindOrError(name, instanceId, path);
            if (error != null) return error;

            var type = FindComponentType(componentType);
            var comp = go.GetComponent(type);
            if (comp == null)
                return new { error = $"Component not found: {componentType}" };

            var prop = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            var field = type.GetField(propertyName, BindingFlags.Public | BindingFlags.Instance);

            if (prop == null && field == null)
                return new { error = $"Property/field not found: {propertyName}" };

            Undo.RecordObject(comp, "Set Property");

            try
            {
                if (prop != null)
                {
                    var converted = ConvertValue(value, prop.PropertyType);
                    prop.SetValue(comp, converted);
                }
                else
                {
                    var converted = ConvertValue(value, field.FieldType);
                    field.SetValue(comp, converted);
                }
                return new { success = true, gameObject = go.name, property = propertyName, value };
            }
            catch (System.Exception ex)
            {
                return new { error = ex.Message };
            }
        }

        [UnitySkill("component_get_properties", "Get all properties of a component (supports name/instanceId/path)")]
        public static object ComponentGetProperties(string name = null, int instanceId = 0, string path = null, string componentType = null)
        {
            if (string.IsNullOrEmpty(componentType))
                return new { error = "componentType is required" };

            var (go, error) = GameObjectFinder.FindOrError(name, instanceId, path);
            if (error != null) return error;

            var type = FindComponentType(componentType);
            var comp = go.GetComponent(type);
            if (comp == null)
                return new { error = $"Component not found: {componentType}" };

            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && !p.GetIndexParameters().Any())
                .Select(p =>
                {
                    try { return new { name = p.Name, type = p.PropertyType.Name, value = p.GetValue(comp)?.ToString() }; }
                    catch { return new { name = p.Name, type = p.PropertyType.Name, value = "(error)" }; }
                })
                .ToArray();

            return new { gameObject = go.name, component = componentType, properties = props };
        }

        private static System.Type FindComponentType(string name)
        {
            return System.Type.GetType(name) ??
                System.AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return new System.Type[0]; } })
                    .FirstOrDefault(t => t.Name == name && typeof(Component).IsAssignableFrom(t));
        }

        private static object ConvertValue(string value, System.Type targetType)
        {
            if (targetType == typeof(string)) return value;
            if (targetType == typeof(int)) return int.Parse(value);
            if (targetType == typeof(float)) return float.Parse(value);
            if (targetType == typeof(bool)) return bool.Parse(value);
            if (targetType == typeof(Vector3))
            {
                var parts = value.Split(',');
                return new Vector3(float.Parse(parts[0]), float.Parse(parts[1]), float.Parse(parts[2]));
            }
            if (targetType == typeof(Color))
            {
                var parts = value.Split(',');
                return new Color(float.Parse(parts[0]), float.Parse(parts[1]), float.Parse(parts[2]), parts.Length > 3 ? float.Parse(parts[3]) : 1);
            }
            return System.Convert.ChangeType(value, targetType);
        }
    }
}
