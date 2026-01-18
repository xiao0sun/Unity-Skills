using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace UnitySkills.Editor
{
    /// <summary>
    /// Routes requests to skill methods.
    /// </summary>
    public static class SkillRouter
    {
        private static Dictionary<string, MethodInfo> _skills;

        public static void Initialize()
        {
            if (_skills != null) return;
            _skills = new Dictionary<string, MethodInfo>(StringComparer.OrdinalIgnoreCase);

            var allTypes = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic)
                .SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } });

            foreach (var type in allTypes)
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    var attr = method.GetCustomAttribute<UnitySkillAttribute>();
                    if (attr != null)
                    {
                        var name = attr.Name ?? ToSnakeCase(method.Name);
                        _skills[name] = method;
                    }
                }
            }
            Debug.Log($"[UnitySkills] Found {_skills.Count} skills");
        }

        public static string GetManifest()
        {
            Initialize();
            var skills = _skills.Select(kv =>
            {
                var attr = kv.Value.GetCustomAttribute<UnitySkillAttribute>();
                var ps = kv.Value.GetParameters().Select(p => new
                {
                    name = p.Name,
                    type = GetType(p.ParameterType),
                    required = !p.HasDefaultValue
                });
                return new { name = kv.Key, description = attr?.Description ?? "", parameters = ps };
            });
            return ToJson(new { version = "1.0", skills });
        }

        public static string Execute(string name, string json)
        {
            Initialize();
            if (!_skills.TryGetValue(name, out var method))
                return ToJson(new { error = $"Skill '{name}' not found" });

            try
            {
                var args = ParseJson(json);
                var ps = method.GetParameters();
                var invoke = new object[ps.Length];

                for (int i = 0; i < ps.Length; i++)
                {
                    var p = ps[i];
                    if (args.TryGetValue(p.Name, out var val))
                        invoke[i] = Convert.ChangeType(val, p.ParameterType);
                    else if (p.HasDefaultValue)
                        invoke[i] = p.DefaultValue;
                    else
                        return ToJson(new { error = $"Missing: {p.Name}" });
                }

                var result = method.Invoke(null, invoke);
                return ToJson(new { status = "success", result });
            }
            catch (Exception ex)
            {
                return ToJson(new { error = ex.InnerException?.Message ?? ex.Message });
            }
        }

        private static string ToSnakeCase(string s) =>
            System.Text.RegularExpressions.Regex.Replace(s, "([a-z])([A-Z])", "$1_$2").ToLower();

        private static string GetType(Type t)
        {
            if (t == typeof(string)) return "string";
            if (t == typeof(int) || t == typeof(long)) return "integer";
            if (t == typeof(float) || t == typeof(double)) return "number";
            if (t == typeof(bool)) return "boolean";
            return "object";
        }

        private static string ToJson(object obj)
        {
            return JsonUtility.ToJson(new Wrapper { data = obj.ToString() });
        }

        private static Dictionary<string, object> ParseJson(string json)
        {
            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(json)) return dict;
            
            json = json.Trim();
            if (!json.StartsWith("{")) return dict;
            
            json = json.Substring(1, json.Length - 2);
            foreach (var pair in json.Split(','))
            {
                var kv = pair.Split(new[] { ':' }, 2);
                if (kv.Length == 2)
                {
                    var key = kv[0].Trim().Trim('"');
                    var val = kv[1].Trim().Trim('"');
                    if (float.TryParse(val, out var f)) dict[key] = f;
                    else dict[key] = val;
                }
            }
            return dict;
        }

        [Serializable]
        private class Wrapper { public string data; }
    }
}
