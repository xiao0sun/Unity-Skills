using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace UnitySkills
{
    /// <summary>
    /// Global JSON settings for UnitySkills.
    /// Ensures consistent serialization across all skills and server responses.
    /// Key feature: Prevents Unicode escaping to support Chinese characters properly.
    /// </summary>
    public static class JsonSettings
    {
        public static readonly JsonSerializerSettings Default = new JsonSerializerSettings
        {
            // Use CamelCase for properties (e.g. "gameObjectName" instead of "GameObjectName")
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            
            // Allow Unicode characters (Chinese) to be serialized as-is, mainly for display
            StringEscapeHandling = StringEscapeHandling.Default,
            
            // Ignore nulls to save bytes
            NullValueHandling = NullValueHandling.Ignore,
            
            // Prevent circular reference errors
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            
            // Compact output by default (use Formatting.Indented explicitly where needed)
            Formatting = Formatting.None
        };
    }
}
