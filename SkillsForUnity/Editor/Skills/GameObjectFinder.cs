using UnityEngine;
using UnityEditor;
using System.Linq;

namespace UnitySkills
{
    /// <summary>
    /// Unified utility for finding GameObjects by multiple methods.
    /// Supports: name, instance ID, hierarchy path.
    /// </summary>
    public static class GameObjectFinder
    {
        /// <summary>
        /// Find a GameObject using flexible parameters.
        /// Priority: instanceId > path > name
        /// </summary>
        /// <param name="name">Simple name to search (uses GameObject.Find)</param>
        /// <param name="instanceId">Unity instance ID (most precise)</param>
        /// <param name="path">Hierarchy path like "Parent/Child/Target"</param>
        /// <returns>Found GameObject or null</returns>
        public static GameObject Find(string name = null, int instanceId = 0, string path = null)
        {
            // Priority 1: Instance ID (most precise, works regardless of selection/focus)
            if (instanceId != 0)
            {
                var obj = EditorUtility.InstanceIDToObject(instanceId);
                if (obj is GameObject go)
                    return go;
            }

            // Priority 2: Hierarchy path (works for nested objects)
            if (!string.IsNullOrEmpty(path))
            {
                var go = FindByPath(path);
                if (go != null)
                    return go;
            }

            // Priority 3: Simple name search
            if (!string.IsNullOrEmpty(name))
            {
                return GameObject.Find(name);
            }

            return null;
        }

        /// <summary>
        /// Find a GameObject by hierarchy path (e.g., "Canvas/Panel/Button")
        /// </summary>
        public static GameObject FindByPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            var parts = path.Split('/');
            if (parts.Length == 0)
                return null;

            // First, find root objects
            var rootObjects = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            
            // Find first part in root
            var current = rootObjects.FirstOrDefault(go => go.name == parts[0]);
            if (current == null)
                return null;

            // Navigate down the hierarchy
            for (int i = 1; i < parts.Length; i++)
            {
                var child = current.transform.Find(parts[i]);
                if (child == null)
                    return null;
                current = child.gameObject;
            }

            return current;
        }

        /// <summary>
        /// Get the full hierarchy path of a GameObject
        /// </summary>
        public static string GetPath(GameObject go)
        {
            if (go == null)
                return null;

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
        /// Find or report error
        /// </summary>
        public static (GameObject go, object error) FindOrError(string name = null, int instanceId = 0, string path = null)
        {
            var go = Find(name, instanceId, path);
            if (go == null)
            {
                var identifier = instanceId != 0 ? $"instanceId {instanceId}" : 
                    !string.IsNullOrEmpty(path) ? $"path '{path}'" : 
                    $"name '{name}'";
                return (null, new { error = $"GameObject not found: {identifier}" });
            }
            return (go, null);
        }
    }
}
