using System;

namespace UnitySkills.Editor
{
    /// <summary>
    /// Marks a static method as a Unity Skill.
    /// Skills are automatically discovered and exposed via REST API.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class UnitySkillAttribute : Attribute
    {
        public string Name { get; set; }
        public string Description { get; set; }

        public UnitySkillAttribute() { }

        public UnitySkillAttribute(string name, string description = null)
        {
            Name = name;
            Description = description;
        }
    }

    /// <summary>
    /// Optional attribute for skill parameters.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
    public class SkillParameterAttribute : Attribute
    {
        public string Description { get; set; }
        public bool Required { get; set; } = true;

        public SkillParameterAttribute(string description = null)
        {
            Description = description;
        }
    }
}
