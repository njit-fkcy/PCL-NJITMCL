# PCL-NJITMCL

NJIT MCL 启动器（[Plain Craft Launcher 2](https://github.com/Hex-Dragon/PCL2) 定制版）发行仓库。

> 整合包与服务器存档在 [PCL-NJITMCL-Modpacks](https://github.com/xwan5840/PCL-NJITMCL-Modpacks)。

## 一眼看懂

| 项 | 值 |
|---|---|
| 最新版本 | **v1.5.3** |
| Release 数量 | 21（v1.0.3 ～ v1.5.3） |
| 每个 Release 资产 | 10 个（9 个运行文件 + 1 个完整 zip） |
| 更新清单 | https://njitmc.club/update/njitmcl.json |
| 官网 | https://njitmc.club |

## 下载

每个 Release 固定 10 个资产：

`PCL-NJITMCL.exe`、`PCL-NJITMCL.exe.config`、`PCL-NJITMCL.xml`、`MeloongCore.dll`、
`MeloongCore.Wpf.dll`、`Microsoft.Win32.Registry.dll`、`PCLCS.dll`、`wg.exe`、`wireguard.exe`、
`PCL-NJITMCL-<版本>.zip`

下载源优先级（客户端 `njitmcl.json` 内置，自动回退）：

1. GitHub 直链 `https://github.com/xwan5840/PCL-NJITMCL/releases/download/v<版本>/`
2. `https://gh-proxy.com/` + 直链
3. 校园内网 `https://wg.njit-fkcy.top/modpacks/_launcher/v<版本>/`
4. `https://ghfast.top/` + 直链（后备）
5. `https://gh.llkk.cc/` + 直链（后备）

手动安装：下载 zip 解压到任意目录后运行 `PCL-NJITMCL.exe`；或用 `PCL-NJITMCL.exe` 等 9 个文件覆盖旧目录。

## 版本一览

| 版本 | Tag | 主程序 | 资产 | 发布 |
|---|---|---|---|---|
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

## 发布规范

- 版本号 `主.次.修订`，`VersionCode` 单调递增；已发布的版本号不复用（客户端只比较 `VersionCode`）。
- 修 Bug 递增修订号，加功能递增次版本号。
- 同 tag 重发视为覆盖；需要替换构建时必须递增版本号。
- 不要在已有 Release 上改 tag，已分发的链接都基于 tag 路径。
- 发布九步（定版本 → 编译 → 打包 → GitHub → 内网 → 清单 → 官网 → 验证 → 建档）见社团档案 `档案-05-NJITMCL启动器.md` §三。

---

_本页表格由 GitHub API 生成于 2026-10-29，以 Releases 列表为准。_
