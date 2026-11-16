# Plain Craft Launcher（NJIT MCL 定制分支）

> **本仓库为 NJIT MCL 社团定制源码**，基于上游 [Meloong-Git/PCL](https://github.com/Meloong-Git/PCL)。
> 二进制发布在 [`njit-fkcy/PCL-NJITMCL`](https://github.com/njit-fkcy/PCL-NJITMCL)（24 个 Release，v1.0.3 ～ v1.5.6）。
> 源码每版同步到本仓库与内网镜像 `https://wg.njit-fkcy.top/modpacks/_launcher/_source/`。

[![Stars](https://img.shields.io/github/stars/Meloong-Git/PCL?style=flat&logo=data:image/svg%2bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZlcnNpb249IjEiIHdpZHRoPSIxNiIgaGVpZ2h0PSIxNiI+PHBhdGggZD0iTTggLjI1YS43NS43NSAwIDAgMSAuNjczLjQxOGwxLjg4MiAzLjgxNSA0LjIxLjYxMmEuNzUuNzUgMCAwIDEgLjQxNiAxLjI3OWwtMy4wNDYgMi45Ny43MTkgNC4xOTJhLjc1MS43NTEgMCAwIDEtMS4wODguNzkxTDggMTIuMzQ3bC0zLjc2NiAxLjk4YS43NS43NSAwIDAgMS0xLjA4OC0uNzlsLjcyLTQuMTk0TC44MTggNi4zNzRhLjc1Ljc1IDAgMCAxIC40MTYtMS4yOGw0LjIxLS42MTFMNy4zMjcuNjY4QS43NS43NSAwIDAgMSA4IC4yNVoiIGZpbGw9IiNlYWM1NGYiLz48L3N2Zz4=&logoSize=auto&label=Stars&labelColor=444444&color=eac54f)](https://github.com/Meloong-Git/PCL/)
[![Issues](https://img.shields.io/github/issues/Meloong-Git/PCL?style=flat&label=Issues&labelColor=444444&color=1F883D)](https://github.com/Meloong-Git/PCL/issues)
[![哔哩哔哩](https://img.shields.io/badge/动态-BiliBili-00A4DB?style=flat&labelColor=444444&logoSize=auto)](https://space.bilibili.com/11343203/dynamic)
[![爱发电](https://img.shields.io/badge/赞助-爱发电-946ce6?style=flat&labelColor=444444&logoSize=auto)](https://meloong.com/afd/a/LTCat)

包含 PCL 的大部分源代码，包括 UI 库、动画模块、下载模块、Minecraft 启动模块等！

## 本仓库状态

| 项 | 值 |
|---|---|
| 基线版本 | **1.5.6 / 444**（`VersionBaseName` / `VersionCode`） |
| 起始 tag | `v1.5.6`（本仓库第一个源码 tag） |
| 语言 / 框架 | VB.NET + WPF，.NET Framework 4.8.1 |
| 子模块 | 无。`MeloongCore` 已内联（`MeloongCore.csproj`、`MeloongCore.Wpf.csproj` 已按本机环境改为 net481 / LangVersion 12） |
| 上游历史 | **不包含**。本仓库从一条干净基线开始，只记录社团定制，便于 diff 与回溯 |
| 上游 CI | **不包含**。`.github` 被排除（上游 workflow 依赖上游 secrets，推送后必红叉） |

## 内网镜像目录

源码每版同步到校园内网，目录与 tag 严格对应：

| URL | 服务器目录 | 内容 |
|---|---|---|
| `https://wg.njit-fkcy.top/modpacks/_launcher/_source/v<版本>/` | `/var/www/modpacks/_launcher/_source/v<版本>/` | `PCL-NJITMCL-Source-<版本>.tar.gz` + `SHA256.txt`，权限 644 |
| `https://wg.njit-fkcy.top/modpacks/_launcher/v<版本>/` | `/var/www/modpacks/_launcher/v<版本>/` | 同版本 9 个运行文件 + `PCL-NJITMCL-<版本>.zip`（二进制镜像） |

约定：

- **目录名带 `v` 前缀**（`v1.5.6`），与 tag 完全一致；**源码包名不带**（`PCL-NJITMCL-Source-1.5.6.tar.gz`）。
- 打包必须 `tar -czf ... --exclude=.git`，正常约 **13.3 MB**；把 `.git` 打进去会异常变大。
- 快照排除：`.git`、`.github`、`bin`、`obj`、`.vs`、`Plain Craft Launcher 2\WireGuard\`（二进制）、`最新正式版.zip`、`*.local.vbproj`。
- `SHA256.txt` 格式为 `<sha256>  <文件名>`；上传后远端 `sha256sum` 必须与本地一致。
- 目录列表被禁用（访问目录 URL 返回 `403`），只能按完整文件名取；文件支持 `Range`（`206`）。
- `_source/` 从 **v1.5.6** 起才有；v1.0.3 ～ v1.5.5 只有二进制镜像，没有源码包。

## 构建

编译与打包都在仓库**上一级**的 `tools\` 里（不进版本库），根目录没有重复脚本：

```cmd
tools\build-test.cmd                   一键出实机测试包：编译 → 收件 → 源码 → 说明 → 哈希 → zip
tools\build.ps1                        正式发版的编译与打包（build-test.cmd 内部会调它）
```

- `build-test.cmd` 是双击用的壳，实际跑 `build-test.ps1`：先 `build.ps1 -NoWireGuard -NoZip -RequireFrpc` 编出
  `dist\v<版本>\`，再把 `njit-online\dist` 的两个 jar 放进 `PCL\`，写 `测试说明.md`、`SHA256.txt`、`源码.zip`，最后打 `PCL-NJITMCL-<版本>-test.zip`。
- `build.ps1` 自己找 BuildTools 的构建工具、自己挑带 `Sdks` 的 .NET SDK，NuGet 默认走华为云镜像（校园网），
  第二遍带 `-p:LibDir` 把 `PCLCS.dll` / `MeloongCore*.dll` 内嵌进 exe —— 少了这一遍，exe 起来会提示缺少依赖库。
- 手工等价命令见 `tools\build.ps1` 第 113、130 行；`dotnet msbuild` / 裸 `msbuild` 会因缺 XAML 代码生成失败。
- 发布用正式工程；本机调试用 `Plain Craft Launcher 2.local.vbproj`（已排除出本仓库）。

## 发版

完整流程见 [PCL-NJITMCL 的 README「发版流程」](https://github.com/njit-fkcy/PCL-NJITMCL#发版流程)：十步，源码同步为第 5 步。

源码侧只做三件事：

1. 从 `PCL-src` 工作树生成快照（按上面的排除清单），提交到本仓库 `main`，提交信息 `NJIT MCL <版本>（versionCode <code>）`。
2. 打 tag `v<版本>` 并推送；tag 必须与 Release 的 tag 同名同版。
3. 同一份快照打成 `PCL-NJITMCL-Source-<版本>.tar.gz` + `SHA256.txt`，上传内网 `_source/v<版本>/`（644）。

## 相关资源

- [PCL 下载](https://meloong.com/pcl)：下载可免费使用的正式版 PCL
- [PCL 功能投票](https://github.com/Meloong-Git/PCL/discussions/2)
- [帮助文档库](https://github.com/LTCatt/PCL2Help)：PCL 内置帮助文档的存储库
