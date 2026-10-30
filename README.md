# PCL-NJITMCL

NJIT MCL 启动器（[Plain Craft Launcher 2](https://github.com/Hex-Dragon/PCL2) 定制版）的**发行仓库**，隶属 GitHub 组织 [njit-fkcy](https://github.com/njit-fkcy)。

> 整合包与服务器存档在 [PCL-NJITMCL-Modpacks](https://github.com/njit-fkcy/PCL-NJITMCL-Modpacks)。
> 早期地址 `github.com/xwan5840/PCL-NJITMCL` 会 301 跳转到本仓库，历史链接仍然可用。

## 目录

- [一眼看懂](#一眼看懂)
- [下载与安装](#下载与安装)
- [更新机制](#更新机制)
- [版本一览](#版本一览)
- [发版流程](#发版流程)
- [常见问题](#常见问题)
- [接手须知](#接手须知)

## 一眼看懂

| 项 | 值 |
|---|---|
| 组织 | [njit-fkcy](https://github.com/njit-fkcy) |
| 最新版本 | **v1.5.4** |
| Release 数量 | 22（v1.0.3 ～ v1.5.4） |
| 每个 Release 资产 | 10 个（9 个运行文件 + 1 个完整 zip） |
| 更新清单 | https://njitmc.club/update/njitmcl.json |
| 官网 | https://njitmc.club |
| 技术支持 / 问题反馈 | 官网「反馈」入口，或启动器内「意见反馈」 |

## 下载与安装

每个 Release 固定 10 个资产：

`PCL-NJITMCL.exe`、`PCL-NJITMCL.exe.config`、`PCL-NJITMCL.xml`、`MeloongCore.dll`、
`MeloongCore.Wpf.dll`、`Microsoft.Win32.Registry.dll`、`PCLCS.dll`、`wg.exe`、`wireguard.exe`、
`PCL-NJITMCL-<版本>.zip`

**安装**：下载 `PCL-NJITMCL-<版本>.zip`，解压到任意目录（建议路径不含中文与空格），运行 `PCL-NJITMCL.exe`。
**升级**：用新版本的 9 个文件覆盖旧目录即可，存档与设置不受影响（`PCL` 子目录保留）。

下载源（客户端内置，按顺序自动回退，任一可用即可）：

1. GitHub 直链 `https://github.com/njit-fkcy/PCL-NJITMCL/releases/download/v<版本>/`
2. `https://gh-proxy.com/` + 直链
3. 校园内网 `https://wg.njit-fkcy.top/modpacks/_launcher/v<版本>/`
4. `https://ghfast.top/` + 直链（后备）
5. `https://gh.llkk.cc/` + 直链（后备）

## 更新机制

- 启动器启动时读取 `https://njitmc.club/update/njitmcl.json`，比较其中的 `versionCode` 与本机内置值。
- **只比 `versionCode`**：`NewCode <= VersionCode` 即判定为最新。因此同一版本号重新构建不会被已更新用户收到，需要替换构建时必须递增版本号。
- 清单里每个文件带 5 个下载源与 `sha256`，客户端按顺序回退，校验通过才落盘。
- 清单与官网下载页由官网服务器（`njitmc.club`）托管；GitHub 只负责存放二进制。

## 版本一览

| 版本 | Tag | 主程序 | 资产 | 发布 |
|---|---|---|---|---|
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

九步，缺一步视为未发布：**定版本 → 编译 → 打包 → GitHub → 内网 → 清单 → 官网 → 验证 → 建档**。

1. 改 `Modules\Base\ModBase.vb` 的 `VersionBaseName` 与 `VersionCode`（Release 分支与快照分支同时改，快照 = 正式 + 1）。
2. 编译必须用 VS2022 BuildTools 的 MSBuild，并先设 `MSBuildSDKsPath`、`MSBuildEnableWorkloadResolver=false`（`dotnet msbuild` 会因缺 XAML 代码生成失败）。
3. 收集 9 个文件并打 zip，记录 sha256 与大小。
4. 在本仓库创建 `v<版本>` Release，上传 10 个资产（同 tag 重发要先删同名资产）。
5. 上传到校园内网 `/var/www/modpacks/_launcher/v<版本>/`（644）。
6. 生成并上传 `njitmcl.json`（`njitmc.club:/var/www/update/`），9 个文件各 5 源。
7. 更新官网两套 `app.js`（主站 + 测试站）的版本号、下载链接与 SHA256。
8. 验证：五源 `curl -r 0-1023`（GitHub 302、其余可用源 206）、线上清单字段、官网版本号残留为 0、两处运行目录 exe 均已替换。
9. 建档：社团档案 `档案-05-NJITMCL启动器.md` §六、官网侧 `档案-04` §三、本机 `档案-01` §二。

发布工具：本地 `发布工具\发布工具.py`（GUI，含编译后的一键发布、整合包修订、存档分片上传）。它把启动器发到 `PCL-NJITMCL`、把整合包与存档发到 `PCL-NJITMCL-Modpacks`。

## 常见问题

| 现象 | 处理 |
|---|---|
| 双击没反应 | 启动器是单实例（互斥名 `NJITMCL_SingletonMutex`），先关掉已在运行的实例；仍不行看 `PCL\Log1.txt` |
| GitHub 直链 404 | 检查仓库名与 owner：启动器 `njit-fkcy/PCL-NJITMCL`，整合包 `/PCL-NJITMCL-Modpacks` |
| 下载很慢 / 卡住 | 客户端会自动换源；校园网内建议用「校园内网」源直连 |
| 提示已是最新但想回退 | 直接下载对应版本的 zip 覆盖，注意 `versionCode` 只增不减，回退后不会再收到更新提示 |
| 整合包更新失败 | 多为补丁 `sources` 为空或 revision 未递增，见另一个仓库的说明 |

## 接手须知

- 源码：社团内部仓库/本地 `PCL-src`（PCL 二次开发版，VB.NET + WPF，.NET Framework 4.8.1）。
- 权威档案（含版本基线、编译命令、发布九步、已知坑、回滚）：`档案-05-NJITMCL启动器.md`。
- 发布凭据放在本地 `发布工具\config.json`（GitHub Token、内网服务器账号），**不写进本仓库**。
- 发布机器需装：VS2022 BuildTools（MSBuild）、.NET SDK 8、Python 3（`paramiko`）。
- 组织成员才有推送权限；发版前先在组织内确认版本号未被他人占用。

## 规范

- 版本号 `主.次.修订`，`VersionCode` 单调递增；已发布的版本号不复用。
- 修 Bug 递增修订号，加功能递增次版本号。
- 不要在已有 Release 上改 tag —— 所有已分发链接都基于 tag 路径，改名会断链。
- 本仓库只放启动器资产；整合包、存档请放 `PCL-NJITMCL-Modpacks`。

---

_版本表由 GitHub API 生成于 2026-10-29，以 Releases 列表为准。_
