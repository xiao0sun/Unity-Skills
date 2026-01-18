# UnitySkills 配置与测试指南

## 项目结构

```
unity-skills/
├── MCPForUnity/              # Unity Package
│   ├── Editor/
│   │   ├── Skills/
│   │   │   ├── UnitySkillAttribute.cs
│   │   │   ├── SkillsHttpServer.cs
│   │   │   ├── SkillRouter.cs
│   │   │   └── SampleSkills.cs
│   │   ├── UnitySkillsWindow.cs
│   │   └── UnitySkills.Editor.asmdef
│   └── package.json
├── claude_skill_unity/       # Claude Skill
└── docs/
```

---

## 安装

### 方式 A：Git URL
```
Window > Package Manager > + > Add package from git URL
https://github.com/Besty0728/unity-mcp-skill.git?path=/MCPForUnity
```

### 方式 B：本地
将 `MCPForUnity` 复制到 `Packages/` 目录

---

## 使用

### 1. 打开窗口
**Window > UnitySkills**

### 2. 启动服务器
点击 **Start Server**

### 3. 测试
浏览器打开：http://localhost:8090/skills

---

## Python 调用

```python
import requests

# 创建立方体
requests.post("http://localhost:8090/skill/create_cube", 
    json={"x": 0, "y": 1, "z": 0, "name": "MyCube"})
```

---

## 添加自定义技能

```csharp
using UnitySkills.Editor;

public static class MySkills
{
    [UnitySkill("my_skill", "Description")]
    public static string MySkill(string param1, float param2 = 0)
    {
        return "Done";
    }
}
```
