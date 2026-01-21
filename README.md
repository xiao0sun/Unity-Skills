# 🎮 UnitySkills


<p align="center">
  <img src="https://img.shields.io/badge/Unity-2021.3%2B-black?style=for-the-badge&logo=unity" alt="Unity">
  <img src="https://img.shields.io/badge/Skills-100%2B-green?style=for-the-badge" alt="Skills">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-orange?style=for-the-badge" alt="License"></a>
</p>

<p align="center">
  <b>基于 REST API 的 AI 驱动型 Unity 编辑器自动化引擎</b><br>
  <i>让 AI 通过Skills直接掌控 Unity 场景</i>
</p>

## 🤝 致谢
本项目基于 [unity-mcp](https://github.com/CoplayDev/unity-mcp) 的优秀理念深度重构与功能扩展。

---

## 🚀 核心特性

- ⚡ **极致效能**：直接基于 HTTP 通信，规避 MCP 中间层损耗，响应近乎实时。
- 🛠️ **全能工具库**：内置 100+ 工业级 Skills，涵盖从物理系统到 UI 布局的全方位操作。
- 🤖 **深度集成**：独家支持 **Antigravity Slash Commands**，解锁 `/unity-skills` 交互新体验。
- 🔌 **全环境兼容**：完美支持 Claude Code, Antigravity, Gemini CLI 等主流 AI 终端。

---

## 🏗️ 支持的 IDE / 终端

本项目针对以下环境进行了深度优化，确保持续、稳定的开发体验：

| AI 终端 | 支持状态 | 特色功能 |
| :--- | :---: | :--- |
| **Antigravity** | ✅ 完美支持 | 支持 `/unity-skills` 斜杠命令，原生集成工作流。 |
| **Claude Code** | ✅ 完美支持 | 智能识别 Skill 意图，支持复杂多步自动化。 |
| **Gemini CLI** | ✅ 完美支持 | 实验性支持，适配最新 `experimental.skills` 规范。 |

---

## 🏁 快速开始

### 1. 安装 Unity 插件
通过 Unity Package Manager 直接添加 Git URL：
```
https://github.com/Besty0728/unity-mcp-skill.git?path=/SkillsForUnity
```

### 2. 启动服务
在 Unity 中点击菜单：`Window > UnitySkills > Start Server`

### 3. 一键配置 AI Skills
1. 打开 `Window > UnitySkills > Skill Installer`。
2. 选择对应的终端图标（Claude / Antigravity / Gemini）。
3. 点击 **"Install"** 即可完成环境配置，无需手动拷贝代码。

---

## 📦 Skills 分类概要 (100+)

| 分类 | 数量 | 核心功能 |
| :--- | :---: | :--- |
| **GameObject** | 7 | 增删改查、层级管理、变换同步。 |
| **Component** | 5 | 智能组件挂载、属性劫持与配置。 |
| **Scene** | 6 | 场景无缝切换、状态保存、高清截图。 |
| **Material** | 17 | HDR 发光控制、材质球属性批量修改。 |
| **UI System** | 10 | 响应式 Canvas 构建、Button/Text 组件生成。 |
| **Others** | 55+ | 涵盖 Animator, Light, Prefab, Validation 等。 |

---

## 📂 项目结构

```bash
.
├── SkillsForUnity/           # Unity 编辑器插件 (Package 核心)
│   └── Editor/Skills/        # 核心 Skill 逻辑与 C# 安装器
├── unity-skills/             # 跨平台 AI Skill 模板 (核心源码)
│   ├── SKILL.md              # Skill 定义与 Prompt 设计
│   └── scripts/              # Python Helper 封装
├── CHANGELOG.md              # 详尽的更新记录与路线图
└── LICENSE                   # MIT 开源协议
```

---

## 📄 开源协议
本项目采用 [MIT License](LICENSE) 许可。