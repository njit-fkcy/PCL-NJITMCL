# PCL-NJITMCL

NJIT MCL 启动器（[Plain Craft Launcher 2](https://github.com/Hex-Dragon/PCL2) 定制版）的**发行仓库**，隶属 GitHub 组织 [njit-fkcy](https://github.com/njit-fkcy)。

> 整合包与服务器存档在 [PCL-NJITMCL-Modpacks](https://github.com/njit-fkcy/PCL-NJITMCL-Modpacks)。
> 早期地址 `github.com/xwan5840/PCL-NJITMCL` 会 301 跳转到本仓库，历史链接仍然可用。

## 目录

- [一眼看懂](#一眼看懂)
- [下载与安装](#下载与安装)
- [内网镜像目录](#内网镜像目录)
- [更新机制](#更新机制)
- [版本一览](#版本一览)
- [发版流程](#发版流程)
- [常见问题](#常见问题)
- [接手须知](#接手须知)

## 一眼看懂

| 项 | 值 |
|---|---|
| 组织 | [njit-fkcy](https://github.com/njit-fkcy) |
| 最新版本 | **v1.8.3** |
| Release 数量 | 35（v1.0.3 ～ v1.8.3） |
| 每个 Release 资产 | 10 个（9 个运行文件 + 1 个完整 zip） |
| 源码 | 本仓库 [`source/`](source/)（每版一个 tag `v<版本>-source`；内网镜像 `_launcher/_source/v<版本>/`） |
| 更新清单 | https://njitmc.club/update/njitmcl.json |
| 官网 | https://njitmc.club |
| 技术支持 / 问题反馈 | 官网「反馈」入口，或启动器内「意见反馈」 |

## 源码

启动器源码在本仓库 [`source/`](source/) 目录，与发行版同仓，随版本打 tag `v<版本>-source`
（当前 `v1.8.3-source`）。

- 技术栈：VB.NET + WPF，.NET Framework 4.8.1，旧式 `.vbproj`
- 编译：必须用 VS2022 BuildTools 的 MSBuild（先设 `MSBuildSDKsPath`、`MSBuildEnableWorkloadResolver=false`），详见 `source/README.md`
- 快照排除：`bin`、`obj`、`.vs`、`Plain Craft Launcher 2\WireGuard\` 二进制、`.github`（上游 CI 依赖上游 secrets）
- 历史源码仓库 `PCL-NJITMCL-Source` 已归档，仅作查档

## 下载与安装

每个 Release 固定 10 个资产：

`PCL-NJITMCL.exe`、`PCL-NJITMCL.exe.config`、`PCL-NJITMCL.xml`、`MeloongCore.dll`、
`MeloongCore.Wpf.dll`、`Microsoft.Win32.Registry.dll`、`PCLCS.dll`、`wg.exe`、`wireguard.exe`、
`PCL-NJITMCL-<版本>.zip`

**安装**：下载 `PCL-NJITMCL-<版本>.zip`，解压到任意目录（建议路径不含中文与空格），运行 `PCL-NJITMCL.exe`。
**升级**：用新版本的 9 个文件覆盖旧目录即可，存档与设置不受影响（`PCL` 子目录保留）。

下载源（客户端内置，按顺序回退，任一可用即可；1.6.1 起为下列 4 个）：

1. GitHub 直链 `https://github.com/njit-fkcy/PCL-NJITMCL/releases/download/v<版本>/`
2. 校园内网 `https://wg.njit-fkcy.top/modpacks/_launcher/v<版本>/`
3. `https://gh-proxy.com/` + 直链
4. `https://ghproxy.net/` + 直链

> 源池会随可用性调整：`ghfast.top`、`gh.llkk.cc` 在 2026-11-09 已不可达，故自 1.6.1 起移除；
> `gh-proxy.net` 会 302 跳转到广告站，**不要使用**。

## 内网镜像目录

校园内网 `wg.njit-fkcy.top` 保存一份完整镜像，供校园网内直连下载，同时充当 GitHub 的备份。目录与 GitHub 一一对应：

| URL | 服务器目录 | 内容 | 对应 GitHub |
|---|---|---|---|
| `https://wg.njit-fkcy.top/modpacks/_launcher/v<版本>/` | `/var/www/modpacks/_launcher/v<版本>/` | 该版本 9 个运行文件 + `PCL-NJITMCL-<版本>.zip`，共 10 个，权限 644 | Release `v<版本>` 的同名资产 |
| `https://wg.njit-fkcy.top/modpacks/_launcher/_source/v<版本>/` | `/var/www/modpacks/_launcher/_source/v<版本>/` | `PCL-NJITMCL-Source-<版本>.tar.gz` + `SHA256.txt`，权限 644 | 源码仓库 tag `v<版本>` |
| `https://wg.njit-fkcy.top/modpacks/<整合包>/` | `/var/www/modpacks/<整合包>/` | 整合包与存档分片 | [PCL-NJITMCL-Modpacks](https://github.com/njit-fkcy/PCL-NJITMCL-Modpacks) |

约定：

- **目录名带 `v` 前缀**（`v1.5.6`），与 Release tag 完全一致；**文件名不带**（`PCL-NJITMCL-1.5.6.zip`、`PCL-NJITMCL-Source-1.5.6.tar.gz`）。
- 目录列表被禁用：直接访问目录 URL 返回 `403`，只能按完整文件名取；文件支持 `Range`，断点续传返回 `206`。
- `_source/` 从 **v1.5.6** 起才有；v1.0.3 ～ v1.5.5 只有二进制，无源码镜像。
- 更新清单**不在内网**，由官网 `https://njitmc.club/update/njitmcl.json` 托管（1.7.0 起 9 个文件各 4 源 + `sha256`）。
- 新增版本时 `_launcher/v<版本>/` 与 `_launcher/_source/v<版本>/` 必须**同时建立**，只建二进制目录视为未发布。
- 内网只做镜像，**不存清单、不存官网页面**；清单与下载页由官网服务器负责。

## 更新机制

- 启动器启动时读取 `https://njitmc.club/update/njitmcl.json`，比较其中的 `versionCode` 与本机内置值。
- **只比 `versionCode`**：`NewCode <= VersionCode` 即判定为最新。因此同一版本号重新构建不会被已更新用户收到，需要替换构建时必须递增版本号。
- 清单里每个文件带多个下载源（1.6.1 为 4 个）与 `sha256`，客户端按顺序回退，校验通过才落盘。
- 1.6.1 起下载层带超时（连接 8s / 读写 20s）与单源重试：某个源返回 5xx 或超时会换下一个源，4xx 直接放弃该源，不再因为一个源失败而整次更新失败。
- 清单与官网下载页由官网服务器（`njitmc.club`）托管；GitHub 只负责存放二进制。

## 版本一览

| 版本 | Tag | 主程序 | 资产 | 发布 |
|---|---|---|---|---|
| 1.8.3 | `v1.8.3` | 5.8 MB | 10 | 2026-11-10 |
| 1.8.2 | `v1.8.2` | 5.8 MB | 10 | 2026-11-10 |
| 1.8.1 | `v1.8.1` | 5.8 MB | 10 | 2026-11-10 |
| 1.8.0 | `v1.8.0` | 5.8 MB | 10 | 2026-11-10 |
| 1.7.0 | `v1.7.0` | 5.8 MB | 10 | 2026-11-10 |
| 1.6.2 | `v1.6.2` | 5.8 MB | 10 | 2026-11-09 |
| 1.6.1 | `v1.6.1` | 5.8 MB | 10 | 2026-11-09 |
| 1.6.0 | `v1.6.0` | 5.8 MB | 10 | 2026-11-09 |
| 1.5.9 | `v1.5.9` | 5.8 MB | 10 | 2026-11-08 |
| 1.5.8 | `v1.5.8` | 5.8 MB | 10 | 2026-11-05 |
| 1.5.7 | `v1.5.7` | 5.8 MB | 10 | 2026-11-05 |
| 1.5.6 | `v1.5.6` | 5.8 MB | 10 | 2026-11-01 |
| 1.5.5 | `v1.5.5` | 5.8 MB | 10 | 2026-10-31 |
| 1.5.4 | `v1.5.4` | 5.8 MB | 10 | 2026-10-30 |
| 1.5.3 | `v1.5.3` | 5.8 MB | 10 | 2026-10-29 |
| 1.5.2 | `v1.5.2` | 5.8 MB | 10 | 2026-10-29 |
| 1.5.1 | `v1.5.1` | 5.8 MB | 10 | 2026-10-29 |
| 1.5.0 | `v1.5.0` | 5.8 MB | 10 | 2026-10-29 |
| 1.4.6 | `v1.4.6` | 5.8 MB | 10 | 2026-10-26 |
| 1.4.5 | `v1.4.5` | 5.8 MB | 10 | 2026-10-26 |
| 1.4.4 | `v1.4.4` | 5.8 MB | 10 | 2026-10-26 |
| 1.4.3 | `v1.4.3` | 5.8 MB | 10 | 2026-10-26 |
| 1.4.2 | `v1.4.2` | 5.8 MB | 10 | 2026-10-26 |
| 1.4.1 | `v1.4.1` | 5.8 MB | 10 | 2026-10-26 |
| 1.4.0 | `v1.4.0` | 5.8 MB | 10 | 2026-10-26 |
| 1.3.0 | `v1.3.0` | 5.8 MB | 10 | 2026-10-25 |
| 1.2.3 | `v1.2.3` | 5.8 MB | 10 | 2026-10-25 |
| 1.2.2 | `v1.2.2` | 5.8 MB | 10 | 2026-10-24 |
| 1.2.1 | `v1.2.1` | 5.8 MB | 10 | 2026-10-23 |
| 1.2.0 | `v1.2.0` | 5.8 MB | 10 | 2026-10-23 |
| 1.1.2 | `v1.1.2` | 5.8 MB | 10 | 2026-10-23 |
| 1.1.1 | `v1.1.1` | 5.8 MB | 10 | 2026-10-22 |
| 1.1.0 | `v1.1.0` | 5.8 MB | 10 | 2026-10-22 |
| 1.0.4 | `v1.0.4` | 5.8 MB | 8 | 2026-10-22 |
| 1.0.3 | `v1.0.3` | 5.7 MB | 10 | 2026-10-21 |

## 发版流程

十一步，缺一步视为未发布：**定版本 → 编译 → 打包 → GitHub → 源码同步 → 内网 → 清单 → 官网 → 根 README → 验证 → 建档**。

1. 改 `Modules\Base\ModBase.vb` 的 `VersionBaseName` 与 `VersionCode`（Release 分支与快照分支同时改，快照 = 正式 + 1）。
2. 编译必须用 VS2022 BuildTools 的 MSBuild，并先设 `MSBuildSDKsPath`、`MSBuildEnableWorkloadResolver=false`（`dotnet msbuild` 会因缺 XAML 代码生成失败）。快照分支用 `Plain Craft Launcher 2.local.vbproj`。若 `nuget.org` 不可达（校园网常见），加 `-p:RestoreSources=https://repo.huaweicloud.com/repository/nuget/v3/index.json`。
3. 收集 9 个文件并打 zip，记录 sha256 与大小。
4. 在本仓库创建 `v<版本>` Release，上传 10 个资产（同 tag 重发要先删同名资产）。
5. **源码同步**：把定制源码提交到本仓库 [`source/`](source/) 的 `main` 并打 tag `v<版本>-source`；同一份快照打成 `PCL-NJITMCL-Source-<版本>.tar.gz`（`tar --exclude=.git`），与 `SHA256.txt` 上传到内网 `/var/www/modpacks/_launcher/_source/v<版本>/`（644）。快照排除 `.git`、`.github`、`bin`、`obj`、`.vs`、`WireGuard\`、`最新正式版.zip`、`*.local.vbproj`。
6. 上传到校园内网 `/var/www/modpacks/_launcher/v<版本>/`（644）。**所有上传到 GitHub 的资产都必须同步内网镜像**——GitHub 只是分发入口之一，内网是校园网主源。
7. 生成并上传 `njitmcl.json`（官网服务器的 update 目录），9 个文件各 4 源（GitHub / 内网 / gh-proxy / ghproxy.net）。
8. 更新官网两套 `app.js`（主站 + 测试站）的版本号、下载链接与 SHA256。
9. **更新本仓库根 README**：`一眼看懂`（最新版本、Release 数量）、`源码`（当前 tag）、`版本一览`（新增一行）、`更新机制`/`下载与安装` 中的源数量。这是仓库首页，漏更新就会出现「README 写 v1.6.1、实际已到 1.8.0」。
10. 验证：五源 `curl -r 0-1023`（GitHub 302、其余可用源 206）、线上清单字段、官网版本号残留为 0、两处运行目录 exe 均已替换、源码 tag 的 raw 文件 200 且内网源码包 206 与远端 sha256 一致、根 README 无旧版本号残留。
11. 建档：官网侧 `档案-04` §三、本机 `档案-01` §二；`档案-05-NJITMCL启动器.md` 目前缺失（索引 APP-01 指向失效），重建前以本 README 与 changelog 手册的流程为准。

发布工具：本地 `发布工具\发布工具.py`（GUI，含编译后的一键发布、整合包修订、存档分片上传）。它把启动器发到 `PCL-NJITMCL`、把整合包与存档发到 `PCL-NJITMCL-Modpacks`。

## 常见问题

| 现象 | 处理 |
|---|---|
| 双击没反应 | 启动器是单实例（互斥名 `NJITMCL_SingletonMutex`），先关掉已在运行的实例；仍不行看 `PCL\Log1.txt` |
| 更新失败，提示「下载失败：xxx（远程服务器返回错误: (522)）」 | 1.6.1 之前的缺陷：中转站 5xx 会一票否决整次更新。1.6.1 起改为逐源重试；卡在旧版的先手动下载 zip 覆盖一次 |
| 更新失败，提示「更新清单中没有启动器主程序」 | 1.6.1 之前的缺陷：把 exe 改过名的安装识别不到主程序。手动覆盖一次即可，**exe 不必改回原名** |
| 启动器被改过名（如 `PCL (NJIT MCL).exe`） | 1.6.1 起不是问题：更新按实际文件名落盘，并自动补齐同名 `.config`；只有从旧版升级到 1.6.1 这一步需要手动覆盖 |
| GitHub 直链 404 | 检查仓库名与 owner：启动器 `njit-fkcy/PCL-NJITMCL`，整合包 `/PCL-NJITMCL-Modpacks` |
| 下载很慢 / 卡住 | 客户端会自动换源；校园网内建议用「校园内网」源直连 |
| 提示已是最新但想回退 | 直接下载对应版本的 zip 覆盖，注意 `versionCode` 只增不减，回退后不会再收到更新提示 |
| 内网目录 403 | 正常。目录列表被禁用，必须带完整文件名访问 |
| 内网源码包 404 | 该版本在 v1.5.5 及以前没有 `_source/`；只有 v1.5.6 起才有 |
| 整合包更新失败 | 多为补丁 `sources` 为空或 revision 未递增，见另一个仓库的说明 |

## 接手须知

- 源码：本仓库 [`source/`](source/)（PCL 二次开发版，VB.NET + WPF，.NET Framework 4.8.1；`MeloongCore` 已内联）。本地工作副本为 `PCL-src-162`。
- 权威档案：`档案-05-NJITMCL启动器.md` 目前缺失，发版流程以本 README「发版流程」与 changelog 仓库手册为准；版本基线与回滚见本机 `档案-01`、官网 `档案-04`。
- 发布凭据放在本地 `发布工具\config.json`（GitHub Token、内网服务器账号），**不写进本仓库**。
- 发布机器需装：VS2022 BuildTools（MSBuild）、.NET SDK 8、Python 3（`paramiko`）。
- 组织成员才有推送权限；发版前先在组织内确认版本号未被他人占用。

## 规范

- 版本号 `主.次.修订`，`VersionCode` 单调递增；已发布的版本号不复用。
- 修 Bug 递增修订号，加功能递增次版本号。
- 不要在已有 Release 上改 tag —— 所有已分发链接都基于 tag 路径，改名会断链。
- 本仓库只放启动器资产；整合包、存档请放 `PCL-NJITMCL-Modpacks`。
- 内网目录名必须与 tag 一致（`v<版本>`），文件名不带 `v` —— 客户端清单按此拼接。
- **所有 GitHub 变更必须同步内网源**：Release 资产、插件包、源码包、文档，发 GitHub 的同时必须落到内网镜像；只发 GitHub 视为未发布。

---

_版本表由 GitHub API 生成于 2026-11-10，以 Releases 列表为准。_
