# 古月工具箱 GuyueBox（工具包仓库）

本仓库用于**发布软件本体**：存放 C# / WinForms 源码、自动更新代码，并通过 **GitHub Releases** 分发安装包。

> 官网与版本清单另设独立仓库 `guyue-toolbox-site`（GitHub Pages）。两个仓库职责分离：
> - **本仓库（guyue-toolbox）**：软件源码 + Releases 安装包
> - **官网仓库（guyue-toolbox-site）**：静态站点 + `update.json` 版本清单

## 目录内容

```
guyue-toolbox/
├─ UpdateChecker.cs      # 自动更新核心类（C#，读取官网仓库的 update.json）
├─ UpdateExample.cs      # 调用示例（C#，含 WinForms 集成片段）
├─ （你的软件源码）        # 把 C:\Users\Administrator\CodeBuddy\系统优化工具箱 里的工程复制进来
├─ LICENSE              # MIT
└─ .gitignore           # 含 C# / Visual Studio 忽略规则
```

`UpdateChecker.cs` 中的清单地址已指向官网仓库：
`https://Gu-yue-fy.github.io/guyue-toolbox-site/update.json`

## 如何发新版本

1. 在本仓库编译生成安装包 `古月工具箱_setup.exe`；
2. 在 GitHub 本仓库 **Releases** 中发布，上传该安装包（建议带 `v1.x.x` 标签）；
3. 打开官网仓库 `guyue-toolbox-site`，修改 `update.json` 的 `version` / `date` / `assets.win` 直链，提交推送；
   GitHub Pages 会自动重新构建，官网版本号与软件内更新提示同步生效。

## 本地初始化（首次）

```bash
cd toolkit
git init
git add .
git commit -m "init toolkit"
git branch -M main
git remote add origin https://github.com/Gu-yue-fy/guyue-toolbox.git
git push -u origin main
# 然后在 GitHub 网页上传第一个 Release 安装包
```
