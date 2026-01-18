using UnityEngine;
using UnityEditor;

namespace UnitySkills.Editor
{
    /// <summary>
    /// Sample skills for testing.
    /// </summary>
    public static class SampleSkills
    {
        [UnitySkill("create_cube", "Create a cube at position")]
        public static string CreateCube(float x = 0, float y = 0, float z = 0, string name = "Cube")
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.position = new Vector3(x, y, z);
            Undo.RegisterCreatedObjectUndo(cube, "Create " + name);
            return $"Created {name} at ({x},{y},{z})";
        }

        [UnitySkill("create_sphere", "Create a sphere at position")]
        public static string CreateSphere(float x = 0, float y = 0, float z = 0, string name = "Sphere")
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = name;
            sphere.transform.position = new Vector3(x, y, z);
            Undo.RegisterCreatedObjectUndo(sphere, "Create " + name);
            return $"Created {name} at ({x},{y},{z})";
        }

        [UnitySkill("delete_object", "Delete a GameObject by name")]
        public static string DeleteObject(string name)
        {
            var obj = GameObject.Find(name);
            if (obj == null) return $"Not found: {name}";
            Undo.DestroyObjectImmediate(obj);
            return $"Deleted {name}";
        }

        [UnitySkill("get_scene_info", "Get current scene information")]
        public static string GetSceneInfo()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            return $"Scene: {scene.name}, Objects: {roots.Length}";
        }
    }
}
