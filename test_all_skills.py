#!/usr/bin/env python3
"""批量测试所有 Unity Skills"""
import requests
import json
import sys
import io
from concurrent.futures import ThreadPoolExecutor, as_completed

# 修复 Windows 编码问题
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

BASE_URL = "http://localhost:8090"

# 获取所有 skills 的参数信息
def get_skills_info():
    resp = requests.get(f"{BASE_URL}/skills", timeout=10)
    return {s['name']: s for s in resp.json()['skills']}

# 危险操作的安全测试参数（避免删除/修改核心目录）
SAFE_PARAMS = {
    'asset_delete': {'assetPath': 'Assets/__nonexistent_test_file__.txt'},
    'asset_delete_batch': {'items': '[]'},
    'asset_move': {'sourcePath': 'Assets/__nonexistent__.txt', 'destinationPath': 'Assets/__nonexistent2__.txt'},
    'asset_move_batch': {'items': '[]'},
    'asset_duplicate': {'assetPath': 'Assets/__nonexistent_test_file__.txt'},
    'asset_import': {'sourcePath': 'C:/__nonexistent__.txt', 'destinationPath': 'Assets/__test__.txt'},
    'asset_import_batch': {'items': '[]'},
    'cleaner_delete_assets': {'paths': []},
    'script_delete': {'scriptPath': 'Assets/__nonexistent__.cs'},
    'shader_delete': {'shaderPath': 'Assets/__nonexistent__.shader'},
    'gameobject_delete': {'name': '__NonExistentObject__'},
    'gameobject_delete_batch': {'items': '[]'},
    'prefab_unpack': {'name': '__NonExistentPrefab__'},
}

# 为每个 skill 生成测试参数
def generate_test_params(skill_name, skill_info):
    # 优先使用安全参数
    if skill_name in SAFE_PARAMS:
        return SAFE_PARAMS[skill_name]

    params = {}
    for p in skill_info.get('parameters', []):
        name = p['name']
        ptype = p['type']
        default = p.get('defaultValue')
        required = p.get('required', False)

        if default is not None and default != 'null':
            params[name] = default
        elif required:
            # 根据类型生成测试值
            if ptype == 'string':
                if 'assetpath' in name.lower():
                    params[name] = 'Assets/__nonexistent_test__.asset'
                elif 'path' in name.lower() or 'folder' in name.lower():
                    params[name] = 'Assets'
                elif 'name' in name.lower():
                    params[name] = 'TestObject'
                elif 'items' in name.lower():
                    params[name] = '[]'
                elif 'filter' in name.lower() or 'search' in name.lower():
                    params[name] = '*'
                else:
                    params[name] = 'test'
            elif ptype == 'integer':
                params[name] = 0
            elif ptype == 'number':
                params[name] = 0.0
            elif ptype == 'boolean':
                params[name] = False
            elif ptype == 'array':
                params[name] = []
            elif ptype == 'object':
                params[name] = {}
    return params

def test_skill(skill_name, skill_info):
    """测试单个 skill，返回 (name, status, message)"""
    try:
        params = generate_test_params(skill_name, skill_info)
        resp = requests.post(
            f"{BASE_URL}/skill/{skill_name}",
            json=params,
            timeout=30
        )
        data = resp.json()

        # 判断是否成功
        if resp.status_code == 200:
            if 'error' in data:
                return (skill_name, "⚠️ WARN", data.get('error', '')[:80])
            elif 'success' in data and data['success'] == False:
                return (skill_name, "⚠️ WARN", str(data.get('message', data.get('error', '')))[:80])
            else:
                return (skill_name, "✅ PASS", "OK")
        else:
            return (skill_name, "❌ FAIL", f"HTTP {resp.status_code}: {str(data)[:60]}")
    except requests.exceptions.Timeout:
        return (skill_name, "⏱️ TIMEOUT", "Request timeout")
    except Exception as e:
        return (skill_name, "❌ ERROR", str(e)[:80])

def main():
    print("获取 Skills 列表...")
    skills_info = get_skills_info()
    total = len(skills_info)
    print(f"共 {total} 个 Skills，开始测试...\n")

    results = []

    # 并行测试
    with ThreadPoolExecutor(max_workers=10) as executor:
        futures = {executor.submit(test_skill, name, info): name
                   for name, info in skills_info.items()}

        for i, future in enumerate(as_completed(futures), 1):
            name, status, msg = future.result()
            results.append((name, status, msg))
            print(f"[{i}/{total}] {status} {name}")

    # 按名称排序
    results.sort(key=lambda x: x[0])

    # 统计
    passed = sum(1 for r in results if 'PASS' in r[1])
    warned = sum(1 for r in results if 'WARN' in r[1])
    failed = sum(1 for r in results if 'FAIL' in r[1] or 'ERROR' in r[1] or 'TIMEOUT' in r[1])

    # 生成 Markdown 表格
    print("\n" + "="*80)
    print(f"\n## 测试结果汇总: {passed} 通过, {warned} 警告, {failed} 失败\n")
    print("| # | Skill Name | Status | Message |")
    print("|---|------------|--------|---------|")
    for i, (name, status, msg) in enumerate(results, 1):
        # 转义 | 字符
        msg_escaped = msg.replace('|', '\\|')
        print(f"| {i} | `{name}` | {status} | {msg_escaped} |")

    # 保存到文件
    with open('E:/Betsy/temp/Unity-Skills/test_results.md', 'w', encoding='utf-8') as f:
        f.write(f"# Unity Skills 测试报告\n\n")
        f.write(f"- 总计: {total} 个 Skills\n")
        f.write(f"- 通过: {passed}\n")
        f.write(f"- 警告: {warned}\n")
        f.write(f"- 失败: {failed}\n\n")
        f.write("| # | Skill Name | Status | Message |\n")
        f.write("|---|------------|--------|---------||\n")
        for i, (name, status, msg) in enumerate(results, 1):
            msg_escaped = msg.replace('|', '\\|')
            f.write(f"| {i} | `{name}` | {status} | {msg_escaped} |\n")

    print(f"\n结果已保存到 test_results.md")

if __name__ == '__main__':
    main()
