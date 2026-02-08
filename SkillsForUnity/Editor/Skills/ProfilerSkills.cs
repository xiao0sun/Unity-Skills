using System;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEngine.Profiling;

namespace UnitySkills
{
    /// <summary>
    /// Profiler skills - FPS, memory, stats.
    /// </summary>
    public static class ProfilerSkills
    {
        // 使用反射访问 UnityStats，因为它是内部 API，不同 Unity 版本属性可能不同
        private static readonly Type s_UnityStatsType =
            typeof(Editor).Assembly.GetType("UnityEditor.UnityStats");

        private static float GetStatFloat(string name)
        {
            var prop = s_UnityStatsType?.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            if (prop == null) return -1f;
            try { return Convert.ToSingle(prop.GetValue(null)); }
            catch { return -1f; }
        }

        private static int GetStatInt(string name)
        {
            var prop = s_UnityStatsType?.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            if (prop == null) return -1;
            try { return Convert.ToInt32(prop.GetValue(null)); }
            catch { return -1; }
        }

        [UnitySkill("profiler_get_stats", "Get performance statistics (FPS, Memory, Batches)")]
        public static object ProfilerGetStats()
        {
            // Note: UnityStats is only accurate in the Game View when it is visible

            long totalAllocatedMemory = Profiler.GetTotalAllocatedMemoryLong();
            long totalReservedMemory = Profiler.GetTotalReservedMemoryLong();
            long totalUnusedReservedMemory = Profiler.GetTotalUnusedReservedMemoryLong();

            // Calculate FPS from frameTime (frameTime is in ms, so we divide 1000 by it)
            float frameTime = GetStatFloat("frameTime");
            float fps = frameTime > 0 ? 1000f / frameTime : 0f;

            // Count visible skinned meshes manually
            int visibleSkinnedMeshes = 0;
            var skinnedMeshRenderers = UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>();
            foreach (var smr in skinnedMeshRenderers)
            {
                if (smr.isVisible)
                    visibleSkinnedMeshes++;
            }

            // Count visible animators
            int visibleAnimators = 0;
            var animators = UnityEngine.Object.FindObjectsOfType<Animator>();
            foreach (var anim in animators)
            {
                var renderer = anim.GetComponent<Renderer>();
                if (renderer != null && renderer.isVisible)
                    visibleAnimators++;
            }

            return new
            {
                fps,
                frameTime,
                renderTime = GetStatFloat("renderTime"),
                triangles = GetStatInt("triangles"),
                vertices = GetStatInt("vertices"),
                batches = GetStatInt("batches"),
                setPassCalls = GetStatInt("setPassCalls"),
                drawCalls = GetStatInt("drawCalls"),
                dynamicBatchedDrawCalls = GetStatInt("dynamicBatchedDrawCalls"),
                staticBatchedDrawCalls = GetStatInt("staticBatchedDrawCalls"),
                instancedBatchedDrawCalls = GetStatInt("instancedBatchedDrawCalls"),
                visibleSkinnedMeshes,
                visibleAnimators,
                memory = new
                {
                    totalAllocatedMB = totalAllocatedMemory / (1024f * 1024f),
                    totalReservedMB = totalReservedMemory / (1024f * 1024f),
                    unusedReservedMB = totalUnusedReservedMemory / (1024f * 1024f),
                    monoHeapMB = Profiler.GetMonoHeapSizeLong() / (1024f * 1024f),
                    monoUsedMB = Profiler.GetMonoUsedSizeLong() / (1024f * 1024f)
                }
            };
        }
    }
}
