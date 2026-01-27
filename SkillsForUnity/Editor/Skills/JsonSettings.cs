using Newtonsoft.Json;

namespace UnitySkills
{
    /// <summary>
    /// 统一 JSON 序列化配置，确保中文字符正确显示
    /// </summary>
    public static class JsonSettings
    {
        public static readonly JsonSerializerSettings Default = new JsonSerializerSettings
        {
            StringEscapeHandling = StringEscapeHandling.Default,
            Formatting = Formatting.None
        };
        
        public static readonly JsonSerializerSettings Indented = new JsonSerializerSettings
        {
            StringEscapeHandling = StringEscapeHandling.Default,
            Formatting = Formatting.Indented
        };
        
        public static string Serialize(object obj, bool indented = false)
        {
            return JsonConvert.SerializeObject(obj, indented ? Indented : Default);
        }
    }
}
