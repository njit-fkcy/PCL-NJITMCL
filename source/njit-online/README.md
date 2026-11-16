# njit-online-agent — 联机进服校验热开关（正版 / 双验证）

给 PCL-NJITMCL 的联机功能加一个「谁能进服」的两档开关：官方正版、双验证。
**不写 `server.properties`、不重启服务端、随时可逆。**

| 模式 | 语义 | UUID 来源 | 客户端要求 |
|---|---|---|---|
| `1` 正版 | 与官方完全一致 | Mojang | 微软账号 |
| `4` 双验证 | 先问 Mojang，问不到再问 MUA；同名时正版优先 | Mojang；MUA 回退时按名字对齐到 Mojang UUID（查不到才用 MUA 的） | 两者皆可 |

`0`（免验证）、`2`（仅 MUA）与 `3`（MUA 优先）已移除。取值不重编号，旧脚本里的 `1` 含义不变；
手写的 `-Dnjit.mode=0/2/3` 会被忽略并回落到默认的正版模式。

## 为什么不是改 online-mode

| 方案 | 问题 |
|---|---|
| 改 `server.properties` 的 `online-mode` | 必须重启；改完忘了改回来，下一局就是错的 |
| 反射改 `MinecraftServer.usesAuthentication` | 依赖混淆映射：vanilla 是 `a`/`b`，Fabric 是 `class_3176`，Forge 1.17 前后还不一样 |
| **改 `com.mojang.authlib`** | authlib 是 Mojang 的独立库，**从不参与混淆**，1.7 ~ 26.x 类名方法名都一样 |

## 原理

服务端登录流程里，身份校验只发生在一步：

```
ServerLoginPacketListenerImpl.handleHello
  → server.getSessionService().hasJoinedServer(name, serverHash, address)
      → YggdrasilAuthenticationService / YggdrasilMinecraftSessionService   ← 注入点
```

代理接管这一步，按模式决定返回什么：

- **`1` 正版**：`@Advice.OnMethodEnter` 返回 `false`，方法照常执行，零行为差异。
- **`4` 双验证**：`enter` 返回 `true` **跳过原方法体**，先问 Mojang；没有资料再问 MUA。

MUA 一侧走的是与 Mojang 完全一致的 `hasJoined` 协议，只是换了域名：

```
GET {api}/sessionserver/session/minecraft/hasJoined?username=…&serverId=…
→ 200 {"id":"069a79f444e94726a5befca90e38aaf5","name":"Alex",
       "properties":[{"name":"textures","value":"…","signature":"…"}]}
→ 204 没有这条会话
```

拿回来的 `id` 转成 UUID，`properties` 通过反射塞进 `GameProfile.getProperties()`——
**不塞的话玩家能进服但所有人都是默认皮肤，等于白做一半**。

**加密握手不受影响**：离线客户端照样能完成 RSA/AES 握手，卡住它们的从来只有这一句会话校验。

## 服务端不需要装 authlib-injector

这是这套方案和"标准外置登录"最大的区别：

| 方案 | 服务端 | 客户端 |
|---|---|---|
| 标准外置登录 | 要装 `authlib-injector.jar`（再开一个 javaagent） | 要装 |
| 本代理双验证 | **不用装**，代理自己问 MUA | 要装（启动器已内置） |

`hasJoined` 的协议在 Mojang 和 MUA 之间是完全一致的，只是域名不同。客户端已经用
authlib-injector 把 `joinServer` 发到了 MUA，服务端只要问同一个会话服务器就能对上。

客户端那半边 PCL 本来就齐了：登录页 `RadioLoginTypeMua` → `ApplyMuaAuthSettings()` 把
`VersionServerAuthServer` 指向 MUA，启动时注入 `-javaagent:authlib-injector.jar=<MUA>`。
所以客户端用 MUA 登录、联机页选「双验证」，MUA 会话就能对上；
只选「正版」时 MUA 账号进不来，这是设计如此。

## 双验证：两种账号都能进

社团里既有 MUA 账号也有正版账号，只能二选一很别扭。模式 `4` 就是两边都收，顺序固定为正版优先：

```
enter 跳过原方法体
  ↓
问 Mojang → 拿到就返回
  ↓ 没拿到
问 MUA → 拿到就返回
  ↓
两个都没给资料 → 返回 null → 服务端照常踢人
```

**为什么是正版优先。** 取舍在身份归属，不在延迟：

| | 正版优先（`4`） |
|---|---|
| 延迟 | 正版玩家一次请求就进，MUA 玩家多一次 Mojang 往返 |
| 同名风险 | 正版身份优先，外置账号顶不掉同名正版玩家 |
| 适合 | 有正版玩家、且在意身份归属的社团服 |

国内访问 `sessionserver.mojang.com` 常年不稳，这是这套顺序的代价：MUA 玩家登录时先要付一次
Mojang 往返，网络不通时最多等到 5 秒超时才落到 MUA。要彻底避开这次往返，只能回到「只认 MUA」，
而那条路已经移除了——社团服的取舍是身份归属优先。

**只问一次，不做双跑。** 两个地址都先算好再挑会让每次登录都付两次往返，登录界面会明显卡顿，
所以实现上是「先问一个，失败再问另一个」。

### 身份归一：同一个名字只有一个 UUID

MUA 回退成功之后还有一步：拿这个名字去问 Mojang
（`GET https://api.mojang.com/users/profiles/minecraft/<名字>`）。

- 名字在 Mojang 有记录 → **UUID 换成 Mojang 的**，`properties` 仍用 MUA 的，皮肤不变。
- 名字在 Mojang 没有记录 → 保持 MUA 返回的 UUID。
- 查询失败或超时 → 保持 MUA 的 UUID，不阻断登录，日志里留一行。

这样同一个人用正版登录、或用 MUA 登录，落到同一个 UUID，存档、白名单、OP 不会分裂。
结果缓存 5 分钟（查不到缓存 1 分钟），避免每次登录都打一次 Mojang。

实现位置是 `Sessions.verifyAt`：构造 `GameProfile` 之前把 `id` 换掉，
不重写已在线玩家，也不影响模式 1 的正版路径。

**代价：名字即身份。** 归一化按名字对齐，任何 MUA 账号只要用了某个正版玩家的名字，
就会拿到那个正版 UUID——「同名顶号」从风险变成了确定行为。不能接受就关掉：

```bat
java -Dnjit.canonical=false -javaagent:njit-online-agent.jar -jar server.jar nogui
```

关掉后 MUA 登录保留 MUA 自己的 UUID，同名会分裂成两套数据，但不会顶替正版身份。
自建或内网接口可以用 `-Dnjit.mojang.api=http://…` 覆盖查询地址。

**和 MultiYggdrasil 的关系。** [MultiYggdrasil](https://github.com/QiuShui1012/MultiYggdrasil)
是同一个思路的模组实现（配置里用 `ordinal` 决定顺序，`OFFICIAL` / `BLESSING_SKIN` 两类来源）。
区别在这里：本代理

- 不需要装模组，**纯服务端、与加载器无关**（Forge / Fabric / NeoForge / 原版 / Paper 都能用）；
- 不读 `multi-yggdrasil.ini`，模式通过控制端口热切，或 `-Dnjit.mode` 定死；
- 只接管 `hasJoinedServer` 一个方法，不替换整个 authlib。

代价是它只做「会话校验」这一层：MultiYggdrasil 还能配 LittleSkin、Blessing Skin 等任意多个源，
本代理目前是「Mojang + MUA」两源。要多源的话，MUA 一侧的地址是参数化的，逻辑上扩成列表只是一次循环。

## 版本覆盖（含 26.x 新年号）

2026 年起 Minecraft 换成 `YY.D.H` 年号：`1.21.11`（2025-12-09）是最后一个 1.x，
之后是 `26.1 Tiny Takeover`（3-24）、`26.2 Chaos Cubed`（6-16）、`26.3 Wilderness Bound`（9-15）。

| 变化 | 影响 |
|---|---|
| 26.1 起要求 **Java 25** | 代理目标 Java 8，跑在 25 上没问题（ByteBuddy 1.18.17 支持 Java 25 class 文件） |
| 26.1 起服务端**不再混淆** | 与代理无关，只是多了条退路 |
| `hasJoinedServer` 换了位置与返回类型 | 见下表 |

| 版本段 | 实际命中的注入点 | 返回类型 |
|---|---|---|
| ≤ 1.20.1 | `YggdrasilAuthenticationService#hasJoinedServer` | `GameProfile` |
| 1.20.2 ~ 26.x | `YggdrasilMinecraftSessionService#hasJoinedServer` | `ProfileResult` |

26.x 的 `ProfileResult` 是 `record ProfileResult(GameProfile profile, Set<ProfileActionType> actions)`，
有两个构造；代理按「参数最少且能凑齐」挑，落到单参那个，`actions` 为空集合。
注入口不靠猜：命中后会写进握手文件，启动器卡片直接显示「返回 ProfileResult」。

## Forge 1.17+ / NeoForge（JPMS）

这两个加载器把游戏与 authlib 放进命名模块。代理在改写目标类之前会：

- 给该模块补「读取代理所在未命名模块」的读边（ByteBuddy 的 AgentBuilder 也会自动补）
- 把该模块的所有包导出/开放给代理，供 `GameProfile` 反射构造与 `properties` 写入

Forge 实际使用的 authlib 是没有 `module-info` 的自动模块，本来就读取并导出一切，
只补读边即可；显式模块才需要额外导出。

`redefineModule` 不可用或导出被拒绝时，退回只补读边并在日志留痕，注入继续，不中断登录。
日志里会写：

```
模块访问已放行：authlib（读边 + 导出 N 个包）
模块访问已放行：authlib.auto（自动模块，仅补读边）
```

## 构建

```bat
:: 需要 JDK 17+（只用于构建，产物目标 Java 8）
build.bat
```

| 文件 | 大小 | 用途 |
|---|---|---|
| `dist/njit-online-agent.jar` | 4.4 MB | 代理本体，已内联 ByteBuddy，无外部依赖 |
| `dist/njit-online-attach.jar` | 1.7 KB | 运行时挂载器 |

两个 jar 放到 `PCL-NJITMCL.exe` 同级目录。依赖 `lib/byte-buddy-1.18.17.jar` 仅在构建期与内联用。

## 两种挂载方式

**运行时挂载**（启动器点按钮自动做）：

```
java -cp njit-online-attach.jar club.njitmcl.agent.Attacher <服务端PID> <代理jar>
```

`Attacher` 用反射调 `com.sun.tools.attach`，字节码目标 Java 8，由服务端自己那个 java 执行。
需要完整 JDK（含 `jdk.attach`）；整合包给精简 JRE 时会明确报错。

**启动参数挂载**（最稳，不依赖 JDK）：

```bat
java -Xmx4G -javaagent:njit-online-agent.jar -jar server.jar nogui
```

`premain` 路径在纯 JRE 上也成立，1.7.10 的老服一样能用。26.x 建议走这条——
Java 25 对运行时动态挂载开始收紧。

## 局域网（对局域网开放）

LAN 的服务端不是独立进程，它跑在房主的**游戏客户端进程**里（集成服务端）。
启动器原先的进程评分会把带 `--username` 的客户端直接排除，所以 LAN 永远找不到目标。

现在改成两条路：

1. **启动时注入**：`ModLaunch` 在拼 JVM 参数时加 `-javaagent:njit-online-agent.jar`，
   前提是 `njit-online-agent.jar` 与启动器同级、且联机模式没被关闭。premain 在 authlib
   加载前装好，不依赖 JDK 的 attach。
2. **进程回填**：启动游戏后把 PID 记进 `ModOnlineMode.RegisterGameProcess`；房间页刷新时
   先按 PID 找，再按房间端口反查监听进程（`netstat -ano`，缓存 5 秒）。

两条都失败时退回原来的评分逻辑，只认独立服务端。

注意：LAN 的集成服务端默认 `online-mode=true`；如果装了 LAN Server Properties 之类的模组
把它改成 false，`hasJoinedServer` 不会被调用，代理不生效。

## 1.19+ 聊天签名（MUA）

1.19 起服务端会用 authlib 的 `YggdrasilServicesKeyInfo#signature()` 校验玩家公钥签名。
MUA 客户端的签名不是 Mojang 签的，原生服务端会报 `Invalid signature for profile public key`
或禁用聊天。

代理在**双验证模式**下做两件事：

- `YggdrasilServicesKeyInfo#signature()` 返回 `DummySignature`，`engineVerify` 恒为 true
- `Property#isSignatureValid` 直接返回 true，MUA 的 textures 签名也能过

纯正版模式（`1`）不动，保持官方校验。LAN 下客户端与服务端同进程，一次注入两边生效；
独立服务端建议用 `-javaagent`，运行时 attach 可能晚于服务端创建校验器。

`enforce-secure-profile` 不需要改。客户端完全不发送公钥时仍可能被拒，但 authlib-injector
1.2.0 起会发送一个带零字节签名的公钥，配合上面的替换即可通过。

## 控制协议

代理在 `127.0.0.1` 开随机端口，把端口与令牌写进 `%TEMP%\njit-online-<pid>.json`：

```json
{"pid":72992,"port":55527,"token":"1b8f97b1…","mode":1,
 "muaApi":"https://skin.mualliance.ltd/api/yggdrasil","patched":1,
 "target":"…YggdrasilMinecraftSessionService#hasJoinedServer","returnType":"…ProfileResult"}
```

| 指令 | 答复 | 含义 |
|---|---|---|
| `PING` | `OK pong` | 探活 |
| `STATE` | `OK 1/4` | 当前模式 |
| `INFO` | `OK 类#方法\|返回类型\|次数` | 实际注入点 |
| `SET 1\|4 [api]` | `OK 模式` | 切换；双验证可临时覆盖 MUA 的 Yggdrasil 根地址，0/2/3 返回 `ERR unsupported-mode` |

每条指令带令牌，防止同机其他进程乱发。日志写在 `%TEMP%\njit-online-<pid>.log`。

## 启动器侧接入

`Modules/ModOnlineMode.vb`（已注册进 `.vbproj`），联机页 `PageEnter` 挂一张卡片：

```vb
Private Sub UpdatePanelVisibility() Handles Me.PageEnter
    ...
    If LinkState = LinkStates.Waiting Then ModOnlineMode.Mount(Me)
End Sub
```

卡片两个按钮「正版 / 双验证」，点哪个就接管并切到哪个；「双验证」固定正版优先，对应模式 `4`。
找服务端 JVM 时先按独立服务端特征评分；LAN 场景回退到启动器记录的游戏 PID，再回退到房间端口反查。

## 验证记录（本机实测）

环境 JDK 25，用桩类模拟 authlib，本地 Python 起一个假的 MUA 会话服务器。

| 场景 | 结果 |
|---|---|
| 默认（正版模式），桩抛 `Invalid session` | `REJECTED`——与不装代理完全一致 |
| `SET 4`：Mojang 有（Alex） | `ACCEPTED Alex 11111111-… {textures=MOJANG_TEXTURE}` |
| `SET 4`：Mojang 无、MUA 有（Steve） | `ACCEPTED Steve 069a79f4-… {textures=MUA_TEXTURE}` |
| `SET 4`：MUA 名 `Both`，Mojang 名下同 ID | `ACCEPTED Both aaaaaaaa-… {textures=MUA_TEXTURE}`，UUID 已归一 |
| `-Dnjit.canonical=false`，同上 | `ACCEPTED Both cccccccc-…`，保持 MUA UUID |
| MUA 返回的 `id` 是 32 位十六进制 | 正确转成带连字符 UUID |
| `properties` | 反射写入成功，`textures` 带签名一起挂上 |
| 26.x 形状桩（会话服务返回 `ProfileResult`） | 注入点自动落到 `YggdrasilMinecraftSessionService` |
| Forge 1.17+ 形态：显式模块且包未导出 | `ACCEPTED`，日志「读边 + 导出 2 个包」 |
| Forge authlib 形态：自动模块 | `ACCEPTED`，日志「自动模块，仅补读边」 |
| 1.19+ 聊天签名桩，模式 `4` | `signature()` 返回 `DummySignature`，`isSignatureValid` 返回 true |
| 同上，模式 `1` | 保持原返回值，官方校验不变 |
| 运行时 attach 到已加载的类 | `patched=1, loaded=true`，重转换生效 |
| 令牌错误 | `ERR bad-token`，状态不变 |
| `SET 0` / `SET 2` / `SET 3` / `SET 9` | `ERR unsupported-mode`，状态不变 |

## 注意

**双验证不是「验证两次」，是「两家任一」。** 只要有一家给出资料就放行，
所以安全性等于「较弱的那家」：MUA 侧不可达时就退化成仅正版。

**双验证固定正版优先。** 同名时正版身份胜出，MUA 账号顶不掉正版玩家；
正版账号在 MUA 侧没有记录也不影响，Mojang 那一问就会放行。

**身价归一按名字对齐，不是账号绑定。** 同名 MUA 账号会继承正版 UUID，这是有意的统一，
也是同名顶号的入口；关掉用 `-Dnjit.canonical=false`。Mojang 名称接口不可达时会退回 MUA UUID，
此时同名仍可能分裂，日志里有记录。

测试里 MUA 的 `Steve` 被归一到了 Mojang 真实 UUID `8667ba71-…`，这就是同名顶号的具体表现，
不是 bug。聊天补丁可用 `-Dnjit.chat.bypass=false` 关闭。

**只影响 `hasJoinedServer`。** BungeeCord / Velocity 在**代理进程**上验证，要注入代理那个 JVM。
装了 AuthMe 之类登录插件的服，密码校验在插件层，与本开关叠加。

**MUA 会话服务器不可达时按拒绝处理。** 返回 null → 服务端照常踢人，日志里有具体原因，
不会退化成"放行"——安全侧永远是失败即拒绝。

**局域网被模组关掉正版验证时，代理无效。** LAN Server Properties、自定义局域网联机这类模组会把
`online-mode` 置为 false，`hasJoinedServer` 根本不会被调用，三档开关不生效。

**不跨重启。** 切的是运行时状态，服务端重启回到 `server.properties` 的值。
配置文件始终是唯一事实来源，代理只在本次运行里临时改写行为。
