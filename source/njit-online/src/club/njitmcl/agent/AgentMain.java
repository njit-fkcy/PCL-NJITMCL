package club.njitmcl.agent;

import java.lang.instrument.Instrumentation;
import java.lang.management.ManagementFactory;
import java.lang.reflect.Method;
import java.security.ProtectionDomain;
import java.util.Collections;
import java.util.HashMap;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;

import net.bytebuddy.agent.builder.AgentBuilder;
import net.bytebuddy.asm.Advice;
import net.bytebuddy.description.type.TypeDescription;
import net.bytebuddy.dynamic.ClassFileLocator;
import net.bytebuddy.dynamic.DynamicType;
import net.bytebuddy.matcher.ElementMatchers;
import net.bytebuddy.utility.JavaModule;

/**
 * 入口。同时实现 premain（-javaagent 启动时加载）与 agentmain（运行时挂载），
 * 两条路径最终都走 install + 控制端口。
 */
public final class AgentMain {

    private static final Object LOCK = new Object();
    private static boolean installed;
    private static Instrumentation instrumentation;

    /** 已经加过读边的模块，避免重复调用 redefineModule。 */
    private static final Set<Object> MODULES_FIXED =
            Collections.newSetFromMap(new ConcurrentHashMap<Object, Boolean>());

    private AgentMain() {
    }

    /**
     * 强制重新转换两个服务端类。
     *
     * Forge 的 TransformingClassLoader 在首次定义类时可能不使用 instrumentation
     * 返回的字节（实验现象：日志显示已改写，但方法返回值没变）。重转换走的是
     * JVM 的定义替换路径，加载器无法忽略，因此每次切模式时补一刀。
     */
    /** 已经补过的服务端类，切模式时不再重复重转换。 */
    private static final Set<String> REAPPLIED =
            Collections.newSetFromMap(new ConcurrentHashMap<String, Boolean>());

    public static void reapplyAuthFlag() {
        if (instrumentation == null) {
            return;
        }
        for (Class<?> type : instrumentation.getAllLoadedClasses()) {
            String name = type.getName();
            if (!"net.minecraft.server.MinecraftServer".equals(name)
                    && !"net.minecraft.client.server.IntegratedServer".equals(name)
                    && !"net.minecraft.util.SignatureValidator".equals(name)
                    && !"com.mojang.authlib.services.MinecraftServicesSessionService".equals(name)
                    && !"com.mojang.authlib.services.MinecraftServicesKeyInfo".equals(name)
                    && !"com.mojang.authlib.properties.Property".equals(name)) {
                continue;
            }
            // 每个类只补一次。
            // Forge 的 EVENTBUS 转换器在重转换时会重建事件处理器，而这个时机
            // （世界已经跑起来、类已加载）它拿不到父类，2026-11-15 14:51 就在
            // AppClassLoader 里刷了一串 "Could not find parent ... WorldEvent"。
            // 而 advice 是**运行时读属性**判断模式的，切模式根本不需要重转换 ——
            // 只需要保证「第一次」那次落上。
            if (!REAPPLIED.add(name)) {
                continue;
            }
            if (!instrumentation.isModifiableClass(type)) {
                Log.write("不可重转换：" + name);
                continue;
            }
            try {
                instrumentation.retransformClasses(type);
                Log.write("已重转换 " + name);
            } catch (Throwable t) {
                Log.write("重转换失败 " + name + "：" + t);
            }
        }
    }

    /**
     * 补一次「按形状认」的目标：公钥校验器（1.20.1 里是 apj）。
     *
     * 为什么要单独补：JVM 在转换某个类时如果顺带触发了另一个类的加载
     * （典型是父类/接口的解析），那次加载不会走 transformer——于是 apj 这类
     * 接口可能在「别人的转换过程里」被悄悄定义，日志里连一行都看不到。
     * 实测：客户端进程能命中 apj，房主进程一次都没命中，就是被这样漏掉的。
     *
     * 这里直接扫已加载的类，按「接口 + 方法参数里有 ServicesKeySet」认出来，
     * 再 retransform 一次。retransform 用的是原始字节重新跑转换器，
     * 所以无论之前怎么漏的，这一刀都会落上。
     */
    /** 已经补过的类，避免每轮扫描重复 retransform（重复转换会触发反优化并刷屏日志）。 */
    private static final Set<String> SHAPE_PATCHED =
            Collections.newSetFromMap(new ConcurrentHashMap<String, Boolean>());

    public static void patchShapeTargets(String why) {
        if (instrumentation == null) {
            return;
        }
        Class<?>[] all;
        try {
            all = instrumentation.getAllLoadedClasses();
        } catch (Throwable t) {
            return;
        }
        // 第一遍只看类名，不做任何解析。
        // 1.19 之前没有 ServicesKeySet（没有 profile key 那套东西），补刀毫无意义；
        // 而 isInterface() 和反射方法表都会触发类型解析 —— 2026-11-15 14:29 在
        // Forge 1.18.2 上就是这么把 EventSubclassTransformer 惹毛的：
        //   Could not find parent net/minecraft/world/entity/Entity
        //   for class net/minecraftforge/entity/PartEntity
        boolean keysPresent = false;
        for (Class<?> type : all) {
            if (type.getName().contains("ServicesKeySet")) {
                keysPresent = true;
                break;
            }
        }
        if (!keysPresent) {
            return;
        }
        for (Class<?> type : all) {
            try {
                if (!type.isInterface() || !mentionsServicesKeySet(type)) {
                    continue;
                }
                if (!SHAPE_PATCHED.add(type.getName())) {
                    continue;
                }
                if (!instrumentation.isModifiableClass(type)) {
                    continue;
                }
                instrumentation.retransformClasses(type);
                Log.write("公钥校验器补刀（" + why + "）：" + type.getName());
            } catch (Throwable t) {
                Log.write("公钥校验器补刀失败（" + type.getName() + "）：" + t);
            }
        }
    }

    private static boolean mentionsServicesKeySet(Class<?> type) {
        try {
            for (java.lang.reflect.Method method : type.getDeclaredMethods()) {
                for (Class<?> parameter : method.getParameterTypes()) {
                    if (parameter.getName().endsWith("ServicesKeySet")) {
                        return true;
                    }
                }
            }
        } catch (Throwable ignored) {
            // 拿不到方法表就跳过，不影响其它类
        }
        return false;
    }

    public static void premain(String args, Instrumentation inst) {
        boot(args, inst);
    }

    public static void agentmain(String args, Instrumentation inst) {
        boot(args, inst);
    }

    private static void boot(String args, Instrumentation inst) {
        int pid = currentPid();
        instrumentation = inst;
        Log.init(pid);
        synchronized (LOCK) {
            if (installed) {
                // 重复挂载：只保证控制端口还在，不重复装转换器
                ControlServer.start(args, pid, inst);
                return;
            }
            installed = true;
            readInitialMode(args);
            // LAN 档默认离线，代理把它翻成正版校验；-Dnjit.enforce=false 可关
            AgentState.enforceAuth =
                    !"false".equalsIgnoreCase(System.getProperty("njit.enforce", "true").trim());
            // 内联版 advice 只能读系统属性，启动时先把当前模式写进去
        System.setProperty("njit.mode", String.valueOf(AgentState.mode));
        Log.write("代理加载，pid=" + pid + "，参数：" + (args == null || args.isEmpty() ? "(空)" : args)
                    + "，初始模式=" + AgentState.mode + "，强制校验=" + AgentState.enforceAuth
                    + "，外置会话=" + AgentState.externalSession);
        // 出口情况：外置登录注入器会往进程里塞本地代理，
        // 那时候「问 Mojang」到底发去了哪，得先看清楚
        Log.write("HTTP 出口：" + Sessions.ProxyNote());
            // 房主档案的皮肤属性：JVM 一起来就预取，别等世界加载时再问网络
            try {
                HostSkin.init();
            } catch (Throwable t) {
                Log.write("房主皮肤预取启动失败：" + t);
            }
            try {
                install(inst);
            } catch (Throwable t) {
                Log.write("安装方法转换失败：" + t);
            }
            ControlServer.start(args, pid, inst);
            startShapeSweeper();
        }
    }

    /**
     * 启动后低频扫一段时间：有些类（例如公钥校验器）会在别人的转换过程里被定义，
     * 那一次不走 transformer。扫到就补刀，两分钟内每 2 秒一次，之后不再打扰。
     */
    private static void startShapeSweeper() {
        Thread worker = new Thread(new Runnable() {
            @Override
            public void run() {
                long deadline = System.currentTimeMillis() + 120000L;
                while (System.currentTimeMillis() < deadline) {
                    try {
                        patchShapeTargets("启动扫描");
                        Thread.sleep(2000L);
                    } catch (InterruptedException e) {
                        Thread.currentThread().interrupt();
                        return;
                    } catch (Throwable t) {
                        return;
                    }
                }
            }
        }, "njit-shape-sweeper");
        worker.setDaemon(true);
        worker.start();
    }

    /**
     * 初始模式：-Dnjit.mode=1/4，或 agentmain 参数 mode=。
     * 服务端启动脚本里直接带 -Dnjit.mode 就能定死模式，不依赖控制端口。
     * 0 / 2 / 3 已移除，传进来会被忽略并回落到默认的正版模式。
     */
    private static void readInitialMode(String args) {
        String raw = System.getProperty("njit.mode", "");
        if (raw.isEmpty() && args != null) {
            for (String pair : args.split("[;,]")) {
                int eq = pair.indexOf('=');
                if (eq > 0 && pair.substring(0, eq).trim().equalsIgnoreCase("mode")) {
                    raw = pair.substring(eq + 1).trim();
                }
            }
        }
        try {
            int mode = Integer.parseInt(raw.trim());
            if (AgentState.supported(mode)) {
                AgentState.mode = mode;
            }
        } catch (Throwable ignored) {
            // 没给或给错就保持默认的正版模式
        }
        // 自建 / 内网 Yggdrasil 时可以在这里改地址，省掉启动器再发一条 SET
        String mua = System.getProperty("njit.mua.api", "").trim();
        if (!mua.isEmpty()) {
            AgentState.muaApi = mua;
        }
    }


    private static void install(Instrumentation inst) {
        // 只作用于 authlib 的两个实现类；名字从未被混淆过，跨版本稳定
        AgentBuilder builder = new AgentBuilder.Default()
                .disableClassFormatChanges()
                // authlib 4.x 的 hasJoinedServer(GameProfile, ...) 首参是 authlib 自己的类型，
                // 默认的类文件定位器有时读不到它，会让整条 advice 装不上（表现为静默无效）。
                // 给一个兜底定位器，保证参数类型能解析。
                .with(ClassFileLocator.ForClassLoader.of(ClassLoader.getSystemClassLoader()))
                .with(AgentBuilder.RedefinitionStrategy.RETRANSFORMATION)
                .with(new AgentBuilder.Listener() {
                    @Override
                    public void onDiscovery(String typeName, ClassLoader classLoader, JavaModule module, boolean loaded) {
                    }

                    @Override
                    public void onTransformation(TypeDescription typeDescription, ClassLoader classLoader,
                                                 JavaModule module, boolean loaded, DynamicType dynamicType) {
                        AgentState.patched++;
                        Log.write("已改写 " + typeDescription.getName()
                                + "（loader=" + (classLoader == null ? "bootstrap" : classLoader.getClass().getName())
                                + "，loaded=" + loaded + "）");
                    }

                    @Override
                    public void onIgnored(TypeDescription typeDescription, ClassLoader classLoader,
                                          JavaModule module, boolean loaded) {
                    }

                    @Override
                    public void onError(String typeName, ClassLoader classLoader, JavaModule module,
                                        boolean loaded, Throwable throwable) {
                        // 静默失败最难查，这里必须留痕
                        Log.write("改写失败 " + typeName + "：" + throwable);
                    }

                    @Override
                    public void onComplete(String typeName, ClassLoader classLoader, JavaModule module, boolean loaded) {
                    }
                })
                .type(ElementMatchers.named("com.mojang.authlib.yggdrasil.YggdrasilAuthenticationService")
                        .or(ElementMatchers.named("com.mojang.authlib.yggdrasil.YggdrasilMinecraftSessionService"))
                        .or(ElementMatchers.named("com.mojang.authlib.yggdrasil.YggdrasilServicesKeyInfo"))
                        // authlib 10.x（26.x 起）搬到 services 包，类名也换了
                        .or(ElementMatchers.named("com.mojang.authlib.services.MinecraftServicesSessionService"))
                        .or(ElementMatchers.named("com.mojang.authlib.services.MinecraftServicesKeyInfo"))
                        .or(ElementMatchers.named("com.mojang.authlib.properties.Property"))
                        // 纹理域名白名单：外置站的贴图域名不在里面，会被当成
                        // 「blocked domain」整包丢掉，属性再好也画不出来
                        .or(ElementMatchers.named("com.mojang.authlib.yggdrasil.TextureUrlChecker"))
                        // authlib 10.x 把域名闸搬到了 discovery service 上（实例方法），
                        // 26.3 就是在这里把 MUA 的贴图静默丢成 skin=null
                        .or(ElementMatchers.named("com.mojang.authlib.services.MinecraftServicesDiscoveryService"))
                        // authlib 的 HTTP 出口：只在这里记一行（地址 + 状态码 + 请求号），
                        // 用来确认客户端那次 join 到底发去了哪、回的是什么
                        .or(ElementMatchers.named("com.mojang.authlib.minecraft.client.MinecraftClient")))
                .transform(new AgentBuilder.Transformer() {
                    @Override
                    public DynamicType.Builder<?> transform(DynamicType.Builder<?> builder,
                                                           TypeDescription typeDescription,
                                                           ClassLoader classLoader,
                                                           JavaModule module,
                                                           ProtectionDomain protectionDomain) {
                        // Forge 1.17+ / NeoForge 把 authlib 放进命名模块，内联进来的 Advice
                        // 引用未命名模块里的 AgentState 会抛 IllegalAccessError，这里补读边。
                        allowAgentAccess(module);
                        boolean visible = canSeeAgent(classLoader);
                        if (!visible) {
                            noteSkip(typeDescription.getName());
                        }
                        // 这两组是「核心功能」，advice 体内只用 JDK + 反射回到 Bridge，
                        // 不引用任何代理类 —— 所以任何加载器都能跑，不看 visible。
                        // Fabric（Knot）/ NeoForge / Forge 1.17-1.19（securejarhandler）
                        // 全靠它们，这也是 1.18.2「验证用户名失败」的解法。
                        // 二分开关：-Dnjit.bridge=false 时退回 14:31 那版的行为（核心补丁全不挂），
                        // 用来分辨「某个故障是不是这组反射 advice 引起的」。
                        // 1.18.2 出现过一个 guava 类 NoClassDefFoundError（securejarhandler
                        // 在算栈帧时解析失败），不确定是不是我们压出来的，需要这个开关来切。
                        boolean bridge = !"false".equalsIgnoreCase(
                                System.getProperty("njit.bridge", "true").trim());
                        if (!bridge) {
                            return builder;
                        }
                        DynamicType.Builder<?> core = builder
                                // 首参三种形态：
                                //   authlib 4.x（≤1.20.1）  hasJoinedServer(GameProfile, String, InetAddress)
                                //   authlib 7.x/10.x（1.20.2+）hasJoinedServer(String, String, InetAddress)
                                //   更老的 1.7 — 1.12       hasJoinedServer(String, String)
                                .visit(Advice.to(BridgeLoginAdvice.class)
                                        .on(ElementMatchers.named("hasJoinedServer")))
                                // 客户端那一步：向会话服务器登记本次加入。
                                // 只记日志不改行为；它失败了，服务端查什么都只能是 204。
                                .visit(Advice.to(BridgeJoinAdvice.class)
                                        .on(ElementMatchers.named("joinServer")))
                                // 所有 authlib HTTP 的落点：只记 /session/minecraft/ 那几条
                                .visit(Advice.to(HttpProbeAdvice.class)
                                        .on(ElementMatchers.named("readServiceResponse")))
                                .visit(Advice.to(BridgeDomainAdvice.class)
                                        .on(ElementMatchers.named("isAllowedTextureDomain")
                                                .and(ElementMatchers.takesArguments(1))
                                                .and(ElementMatchers.returns(
                                                        ElementMatchers.named("boolean")))))
                                .visit(Advice.to(BridgeDomainAdvice.class)
                                        .on(ElementMatchers.named("isWhitelistedDomain")
                                                .and(ElementMatchers.takesArguments(1))
                                                .and(ElementMatchers.returns(
                                                        ElementMatchers.named("boolean")))))
                                // textures 的签名校验：外置站自签，Mojang 公钥必然验不过。
                                // 不拦这一下，getTextures(requireSecure=true) 会抛
                                // InsecureTextureException，上层清属性回默认皮肤，
                                // 而 exit 上的观测一行都不会出现（15:16 的形态）。
                                .visit(Advice.to(BridgeSignatureAdvice.class)
                                        .on(ElementMatchers.named("isSignatureValid")
                                                .and(ElementMatchers.takesArguments(1))
                                                .and(ElementMatchers.returns(
                                                        ElementMatchers.named("boolean")))))
                                // 1.20.2 – 1.21 的验签入口（不经过 isSignatureValid）
                                .visit(Advice.to(BridgeValidateAdvice.class)
                                        .on(ElementMatchers.named("validateProperty")
                                                .and(ElementMatchers.takesArguments(1))
                                                .and(ElementMatchers.returns(
                                                        ElementMatchers.named("boolean")))))
                                // 取贴图那一步的现场：属性几条、解出几张。
                                // 1.18.2「看不见皮肤」要靠这行分清是「属性没到」还是「解不出来」。
                                .visit(Advice.to(BridgeSkinAdvice.class)
                                        .on(ElementMatchers.named("getTextures")
                                                .and(ElementMatchers.takesArguments(2))))
                                .visit(Advice.to(BridgeSkinAdvice.class)
                                        .on(ElementMatchers.named("getPackedTextures")
                                                .and(ElementMatchers.takesArguments(1))));
                        if (!visible) {
                            return core;
                        }
                        return core
                                .visit(Advice.to(PropertyAdvice.class)
                                        .on(ElementMatchers.named("isSignatureValid")))
                                // 1.20.1 真正拦皮肤的是这一道：ServicesKeyInfo#validateProperty
                                // 自己在里面用 Signature 验，不经过 Property#isSignatureValid。
                                .visit(Advice.to(ValidatePropertyAdvice.class)
                                        .on(ElementMatchers.named("validateProperty")
                                                .and(ElementMatchers.takesArguments(1))
                                                .and(ElementMatchers.returns(ElementMatchers.named("boolean")))))
                                // 比 isSignatureValid 更早的一道闸：属性压根没签名时
                                // getTextures(requireSecure=true) 直接抛 MissingException，
                                // 上层把 properties 清空再问 Mojang，最后画默认皮肤。
                                .visit(Advice.to(HasSignatureAdvice.class)
                                        .on(ElementMatchers.named("hasSignature")
                                                .and(ElementMatchers.takesArguments(0))))
                                // 「别人那份档案没带 textures」：进门先按 UUID 补一次。
                                // 1.7.10 的集成服务端只传 UUID 和名字，属性不在里面，
                                // 客户端就会在 getTextures 的第一个 return 处直接空手而归。
                                .visit(Advice.to(PeerSkinAdvice.class)
                                        .on(ElementMatchers.named("getTextures")
                                                .and(ElementMatchers.takesArguments(2))
                                                .and(ElementMatchers.takesArgument(0,
                                                        ElementMatchers.named("com.mojang.authlib.GameProfile")))))
                                .visit(Advice.to(PeerSkinAdvice.class)
                                        .on(ElementMatchers.named("getPackedTextures")
                                                .and(ElementMatchers.takesArguments(1))
                                                .and(ElementMatchers.takesArgument(0,
                                                        ElementMatchers.named("com.mojang.authlib.GameProfile")))))
                                // 皮肤链路的现场记录：谁来过、返回几张图、抛了什么。
                                // 这几行是排查「看不见皮肤」唯一靠得住的证据。
                                // 1.20.2+ 换成了 getPackedTextures：只读属性，不验签
                                .visit(Advice.to(PackedTexturesAdvice.class)
                                        .on(ElementMatchers.named("getPackedTextures")))
                                .visit(Advice.to(SkinDebugAdvice.class)
                                        .on(ElementMatchers.named("getTextures")
                                                .or(ElementMatchers.named("fillProfileProperties"))))
                                .visit(Advice.to(SkinDebugThrowAdvice.class)
                                        .on(ElementMatchers.named("getTextures")
                                                .or(ElementMatchers.named("fillProfileProperties"))));
                    }
                });
        // 26.x 的 LAN 档是离线模式，服务端不看 hasJoinedServer 的结果。
        // 把 usesAuthentication() 翻成 true，登录才会真的走校验。
        builder = builder
                .type(ElementMatchers.named("net.minecraft.server.MinecraftServer")
                        .or(ElementMatchers.named("net.minecraft.client.server.IntegratedServer")))
                .transform(new AgentBuilder.Transformer() {
                    @Override
                    public DynamicType.Builder<?> transform(DynamicType.Builder<?> builder,
                                                           TypeDescription typeDescription,
                                                           ClassLoader classLoader,
                                                           JavaModule module,
                                                           ProtectionDomain protectionDomain) {
                        allowAgentAccess(module);
                        if (!canSeeAgent(classLoader)) {
                            // NeoForge 1.21.4：游戏类加载器看不见代理类，富版 advice 一进来就
                            // NoClassDefFoundError，直接把服务端 tick 打死（13:07 崩端）。
                            // 退回只改返回值的穷版，功能保住，观测放弃。
                            noteSkip(typeDescription.getName());
                            return builder.visit(Advice.to(AuthFlagInlineAdvice.class)
                                    .on(ElementMatchers.named("usesAuthentication")
                                            .and(ElementMatchers.takesArguments(0))));
                        }
                        return builder
                                .visit(Advice.to(AuthFlagAdvice.class)
                                        .on(ElementMatchers.named("usesAuthentication")
                                                .and(ElementMatchers.takesArguments(0))))
                                // 房主自己那份档案没有 properties：会话服务器的档案来自
                                // User.getGameProfile()，只带 UUID 和名字。读侧补一次，
                                // 服务端广播出去的 PlayerInfo 才带 textures。
                                // 名字在这里是混淆的，按描述符匹配。
                                .visit(Advice.to(HostProfileAdvice.class)
                                        .on(ElementMatchers.takesArguments(0)
                                                .and(ElementMatchers.returns(
                                                        ElementMatchers.named("com.mojang.authlib.GameProfile")))))
                                // 写侧也补一刀：有的版本玩家对象直接拿字段，不经过读侧
                                .visit(Advice.to(HostProfileSetAdvice.class)
                                        .on(ElementMatchers.takesArguments(1)
                                                .and(ElementMatchers.takesArgument(0,
                                                        ElementMatchers.named("com.mojang.authlib.GameProfile")))
                                                .and(ElementMatchers.returns(ElementMatchers.named("void")))))
                                // 集成服务端 isSingleplayer() 恒为 true，会让「验证失败」也放行；
                                // 只在认证线程上把它翻成 false，才真的会拒绝。
                                .visit(Advice.to(SingleplayerAdvice.class)
                                        .on(ElementMatchers.named("isSingleplayer")
                                                .and(ElementMatchers.takesArguments(0))));
                    }
                });
        // 玩家公钥校验器（1.20.1 里是 apj）：按形状认——一个接口，且声明了
        // 「拿 ServicesKeySet 当参数」的方法。命中的类型里所有返回 boolean 的方法
        // 统一放行，外置登录站的人才不会被「无效的站点签名」踢掉。
        builder = builder
                .type(ElementMatchers.isInterface()
                        .and(ElementMatchers.declaresMethod(ElementMatchers.takesArguments(2)
                                .and(ElementMatchers.takesArgument(0,
                                        ElementMatchers.nameEndsWith("ServicesKeySet"))))))
                .transform(new AgentBuilder.Transformer() {
                    @Override
                    public DynamicType.Builder<?> transform(DynamicType.Builder<?> builder,
                                                           TypeDescription typeDescription,
                                                           ClassLoader classLoader,
                                                           JavaModule module,
                                                           ProtectionDomain protectionDomain) {
                        allowAgentAccess(module);
                        if (!canSeeAgent(classLoader)) {
                            noteSkip(typeDescription.getName());
                            return builder;
                        }
                        if (!canSeeAgent(classLoader)) {
                            noteSkip(typeDescription.getName());
                            return builder.visit(Advice.to(ValidatorInlineAdvice.class)
                                    .on(ElementMatchers.returns(ElementMatchers.named("boolean"))
                                            .and(ElementMatchers.not(ElementMatchers.isEquals()))
                                            .and(ElementMatchers.not(ElementMatchers.isHashCode()))));
                        }
                        Log.write("公钥校验器：" + typeDescription.getName());
                        return builder.visit(Advice.to(ValidatorTrueAdvice.class)
                                .on(ElementMatchers.returns(ElementMatchers.named("boolean"))
                                        .and(ElementMatchers.not(ElementMatchers.isEquals()))
                                        .and(ElementMatchers.not(ElementMatchers.isHashCode()))));
                    }
                });
        // 渲染器真正读到的值：AbstractClientPlayer#getSkin()
        builder = builder
                .type(ElementMatchers.named("net.minecraft.client.player.AbstractClientPlayer"))
                .transform(new AgentBuilder.Transformer() {
                    @Override
                    public DynamicType.Builder<?> transform(DynamicType.Builder<?> builder,
                                                           TypeDescription typeDescription,
                                                           ClassLoader classLoader,
                                                           JavaModule module,
                                                           ProtectionDomain protectionDomain) {
                        allowAgentAccess(module);
                        if (!canSeeAgent(classLoader)) {
                            noteSkip(typeDescription.getName());
                            return builder;
                        }
                        return builder.visit(Advice.to(SkinRenderAdvice.class)
                                .on(ElementMatchers.takesArguments(0)
                                        .and(ElementMatchers.returns(
                                                ElementMatchers.named("net.minecraft.world.entity.player.PlayerSkin")))));
                    }
                });
        // 玩家信息包的载体：Entry(UUID, GameProfile, ...) 的规范构造器。
        // 服务端组包、客户端解码各走一次，属性条数两侧对比就知道 textures 哪一段丢的。
        builder = builder
                .type(ElementMatchers.named("net.minecraft.network.protocol.game.ClientboundPlayerInfoUpdatePacket$Entry"))
                .transform(new AgentBuilder.Transformer() {
                    @Override
                    public DynamicType.Builder<?> transform(DynamicType.Builder<?> builder,
                                                           TypeDescription typeDescription,
                                                           ClassLoader classLoader,
                                                           JavaModule module,
                                                           ProtectionDomain protectionDomain) {
                        allowAgentAccess(module);
                        if (!canSeeAgent(classLoader)) {
                            noteSkip(typeDescription.getName());
                            return builder;
                        }
                        return builder.visit(Advice.to(PlayerEntryAdvice.class)
                                .on(ElementMatchers.isConstructor()
                                        .and(ElementMatchers.takesArguments(9))));
                    }
                });
        // 屏幕上真正采样哪张图：AvatarRenderer#getTextureLocation(AvatarRenderState)
        // 方法体只有四行——state.skin → body() → texturePath()。
        // 它的返回值就是最终贴图 id，和「TextureManager 里有没有这张图」一起打出来。
        builder = builder
                .type(ElementMatchers.named("net.minecraft.client.renderer.entity.player.AvatarRenderer"))
                .transform(new AgentBuilder.Transformer() {
                    @Override
                    public DynamicType.Builder<?> transform(DynamicType.Builder<?> builder,
                                                           TypeDescription typeDescription,
                                                           ClassLoader classLoader,
                                                           JavaModule module,
                                                           ProtectionDomain protectionDomain) {
                        allowAgentAccess(module);
                        if (!canSeeAgent(classLoader)) {
                            noteSkip(typeDescription.getName());
                            return builder;
                        }
                        return builder.visit(Advice.to(AvatarTextureAdvice.class)
                                .on(ElementMatchers.takesArguments(1)
                                        .and(ElementMatchers.takesArgument(0,
                                                ElementMatchers.nameEndsWith("AvatarRenderState")))
                                        // 同一个类里有三个方法收 AvatarRenderState，
                                        // 只有返回 Identifier 的那个是取贴图 id 的
                                        .and(ElementMatchers.returns(
                                                ElementMatchers.nameEndsWith("Identifier")))));
                    }
                });
        // 皮肤流水线的中段：解码结果 + 贴图下载。26.x 名字可读，直接按名字挂。
        builder = builder
                .type(ElementMatchers.named("net.minecraft.client.renderer.texture.SkinTextureDownloader"))
                .transform(new AgentBuilder.Transformer() {
                    @Override
                    public DynamicType.Builder<?> transform(DynamicType.Builder<?> builder,
                                                           TypeDescription typeDescription,
                                                           ClassLoader classLoader,
                                                           JavaModule module,
                                                           ProtectionDomain protectionDomain) {
                        allowAgentAccess(module);
                        if (!canSeeAgent(classLoader)) {
                            noteSkip(typeDescription.getName());
                            return builder;
                        }
                        return builder
                                .visit(Advice.to(SkinStageAdvice.class)
                                        .on(ElementMatchers.named("downloadAndRegisterSkin")
                                                .or(ElementMatchers.named("downloadSkin"))))
                                .visit(Advice.to(SkinStageThrowAdvice.class)
                                        .on(ElementMatchers.named("downloadAndRegisterSkin")
                                                .or(ElementMatchers.named("downloadSkin"))))
                                .visit(Advice.to(SkinFutureAdvice.class)
                                        .on(ElementMatchers.named("downloadAndRegisterSkin")));
                    }
                });
        // 26.x 的 secure 过滤器：bridge 版（不看加载器可见性）。
        // PlayerSkin 不在 authlib 里，所以要单独一段。
        builder = builder
                .type(ElementMatchers.named("net.minecraft.world.entity.player.PlayerSkin"))
                .transform(new AgentBuilder.Transformer() {
                    @Override
                    public DynamicType.Builder<?> transform(DynamicType.Builder<?> builder,
                                                           TypeDescription typeDescription,
                                                           ClassLoader classLoader,
                                                           JavaModule module,
                                                           ProtectionDomain protectionDomain) {
                        allowAgentAccess(module);
                        return builder.visit(Advice.to(BridgeSecureAdvice.class)
                                .on(ElementMatchers.named("secure")
                                        .and(ElementMatchers.takesArguments(0))
                                        .and(ElementMatchers.returns(ElementMatchers.named("boolean")))));
                    }
                });
        // 26.x 的最终判据：PlayerSkin#secure()。读取即返回 true，
        // 外置站的皮肤就不会被 filter 掉（这条一定会被读到，不依赖调用链）
        builder = builder
                .type(ElementMatchers.named("net.minecraft.world.entity.player.PlayerSkin"))
                .transform(new AgentBuilder.Transformer() {
                    @Override
                    public DynamicType.Builder<?> transform(DynamicType.Builder<?> builder,
                                                           TypeDescription typeDescription,
                                                           ClassLoader classLoader,
                                                           JavaModule module,
                                                           ProtectionDomain protectionDomain) {
                        allowAgentAccess(module);
                        if (!canSeeAgent(classLoader)) {
                            noteSkip(typeDescription.getName());
                            return builder;
                        }
                        Log.write("挂上 PlayerSkin 类：" + typeDescription.getName());
                        return builder.visit(Advice.to(SkinSecureFlagAdvice.class)
                                .on(ElementMatchers.named("secure")
                                        .and(ElementMatchers.takesArguments(0))
                                        .and(ElementMatchers.returns(ElementMatchers.named("boolean")))));
                    }
                });
        // 打点二号：26.x 还有一个 PlayerSkinRenderCache，皮肤也可能走它
        builder = builder
                .type(ElementMatchers.named("net.minecraft.client.renderer.PlayerSkinRenderCache"))
                .transform(new AgentBuilder.Transformer() {
                    @Override
                    public DynamicType.Builder<?> transform(DynamicType.Builder<?> builder,
                                                           TypeDescription typeDescription,
                                                           ClassLoader classLoader,
                                                           JavaModule module,
                                                           ProtectionDomain protectionDomain) {
                        allowAgentAccess(module);
                        if (!canSeeAgent(classLoader)) {
                            noteSkip(typeDescription.getName());
                            return builder;
                        }
                        return builder.visit(Advice.to(TraceAdvice.class)
                                .on(ElementMatchers.isConstructor()));
                    }
                });
        // 26.x（游戏类不混淆）的皮肤入口：SkinManager#get(GameProfile)。
        // 上游那一份档案到底带不带 textures，这一行说了算。
        builder = builder
                .type(ElementMatchers.named("net.minecraft.client.resources.SkinManager"))
                .transform(new AgentBuilder.Transformer() {
                    @Override
                    public DynamicType.Builder<?> transform(DynamicType.Builder<?> builder,
                                                           TypeDescription typeDescription,
                                                           ClassLoader classLoader,
                                                           JavaModule module,
                                                           ProtectionDomain protectionDomain) {
                        allowAgentAccess(module);
                        if (!canSeeAgent(classLoader)) {
                            noteSkip(typeDescription.getName());
                            return builder;
                        }
                        // Forge 运行时把方法名重映射成 SRG（m_xxxxx_），按名字匹配在
                        // Forge 上必然落空——这里一律按描述符认方法。
                        // 同一个类只注册一次，构造器打点也并进来，避免两段 transform
                        // 互相覆盖（上一版就是只有构造器那条生效）。
                        Log.write("挂上 SkinManager 类：" + typeDescription.getName());
                        return builder
                                .visit(Advice.to(TraceAdvice.class)
                                        .on(ElementMatchers.isConstructor()))
                                .visit(Advice.to(SkinProfileAdvice.class)
                                        .on(ElementMatchers.takesArguments(1)
                                                .and(ElementMatchers.takesArgument(0,
                                                        ElementMatchers.named("com.mojang.authlib.GameProfile")))))
                                // 外置站的皮肤 secure=false，会被
                                // filter((secure, skin) -> !secure || skin.secure()) 扔掉
                                .visit(Advice.to(SkinSecureAdvice.class)
                                        .on(ElementMatchers.takesArguments(2)
                                                .and(ElementMatchers.takesArgument(0,
                                                        ElementMatchers.named("com.mojang.authlib.GameProfile")))
                                                .and(ElementMatchers.takesArgument(1, ElementMatchers.is(boolean.class)))))
                                // 解码那一站：registerTextures(UUID, MinecraftProfileTextures)
                                .visit(Advice.to(SkinStageAdvice.class)
                                        .on(ElementMatchers.named("registerTextures")))
                                .visit(Advice.to(SkinStageThrowAdvice.class)
                                        .on(ElementMatchers.named("registerTextures")))
                                .visit(Advice.to(SkinFutureAdvice.class)
                                        .on(ElementMatchers.named("registerTextures")))
                                // get() 返回前等它完成，避免 createLookup 把默认皮肤冻结
                                .visit(Advice.to(SkinSyncAdvice.class)
                                        .on(ElementMatchers.takesArguments(1)
                                                .and(ElementMatchers.takesArgument(0,
                                                        ElementMatchers.named("com.mojang.authlib.GameProfile")))
                                                .and(ElementMatchers.returns(
                                                        ElementMatchers.named("java.util.concurrent.CompletableFuture")))));
                    }
                });
        // 皮肤查表：客户端把档案翻成贴图那一步。名字是混淆的，只能按形状认——
        // 一张 (GameProfile) → Map 的方法，整个 jar 里只有 SkinManager 有这个形状。
        // 只读不打补丁，认错了最多多几行日志。
        builder = builder
                .type(ElementMatchers.declaresMethod(ElementMatchers.takesArguments(1)
                        .and(ElementMatchers.takesArgument(0, ElementMatchers.named("com.mojang.authlib.GameProfile")))
                        .and(ElementMatchers.returns(ElementMatchers.is(java.util.Map.class)))))
                .transform(new AgentBuilder.Transformer() {
                    @Override
                    public DynamicType.Builder<?> transform(DynamicType.Builder<?> builder,
                                                           TypeDescription typeDescription,
                                                           ClassLoader classLoader,
                                                           JavaModule module,
                                                           ProtectionDomain protectionDomain) {
                        allowAgentAccess(module);
                        if (!canSeeAgent(classLoader)) {
                            noteSkip(typeDescription.getName());
                            return builder;
                        }
                        return builder.visit(Advice.to(SkinLookupAdvice.class)
                                .on(ElementMatchers.takesArguments(1)
                                        .and(ElementMatchers.takesArgument(0,
                                                ElementMatchers.named("com.mojang.authlib.GameProfile")))
                                        .and(ElementMatchers.returns(ElementMatchers.is(java.util.Map.class)))));
                    }
                });
        // 公钥签名校验：MUA 的公钥不是 Mojang 签的，原生实现必然验不过，
        // 表现为进服后立刻被踢「无效的服务器公钥签名」。from(...) 统一返回 NO_VALIDATION。
        builder = builder
                .type(ElementMatchers.named("net.minecraft.util.SignatureValidator"))
                .transform(new AgentBuilder.Transformer() {
                    @Override
                    public DynamicType.Builder<?> transform(DynamicType.Builder<?> builder,
                                                           TypeDescription typeDescription,
                                                           ClassLoader classLoader,
                                                           JavaModule module,
                                                           ProtectionDomain protectionDomain) {
                        allowAgentAccess(module);
                        if (!canSeeAgent(classLoader)) {
                            noteSkip(typeDescription.getName());
                            return builder;
                        }
                        if (!canSeeAgent(classLoader)) {
                            noteSkip(typeDescription.getName());
                            return builder.visit(Advice.to(ValidatorInlineAdvice.class)
                                    .on(ElementMatchers.returns(ElementMatchers.named("boolean"))
                                            .and(ElementMatchers.not(ElementMatchers.isEquals()))
                                            .and(ElementMatchers.not(ElementMatchers.isHashCode()))));
                        }
                        return builder.visit(Advice.to(SignatureValidatorAdvice.class)
                                .on(ElementMatchers.named("from")
                                        .and(ElementMatchers.takesArguments(2))));
                    }
                });
        builder.installOn(inst);
        // 混淆版本（1.14 — 1.21 原版）里方法名是 U()、O() 这种，名字匹配全部落空，
        // 这里再用字符串常量定位一次，把登录门闸直接压成常量。
        inst.addTransformer(new ShapePatcher(), true);
        Log.write("转换器已安装（含字节码特征补丁）");
    }

    /**
     * 让被改写的命名模块对代理开放。
     *
     * Forge 1.17+ / NeoForge 用 JPMS 装载游戏与 authlib。ByteBuddy 的 AgentBuilder 会
     * 自动补「目标模块 → 代理模块」的读边，但不会导出包；代理构造 GameProfile、
     * 写 properties 都是反射调用，命名模块不导出就会 IllegalAccessException。
     *
     * 这里只处理显式模块：补读边 + 把该模块所有包导出/开放给代理。
     * 自动模块本来就读取并导出一切，直接跳过。
     *
     * 全部走反射，保证字节码目标仍是 Java 8；Java 8 没有命名模块，会直接跳过。
     */
    /** 已经报过「这个加载器看不见代理」的类名，避免刷屏。 */
    private static final Set<String> INVISIBLE = Collections.newSetFromMap(
            new ConcurrentHashMap<String, Boolean>());

    /**
     * 这个加载器能不能看见代理自己的类？
     *
     * 关键区别（2026-11-15 13:07 的崩端）：advice 是**内联进目标类**执行的，
     * 它调到的辅助类必须由**目标类的加载器**能加载到。
     * JPMS 的读边只解决访问权限，不解决类可见性。
     *
     * Forge 26.x 的 TransformingClassLoader 会委派给系统加载器，所以一直没问题；
     * NeoForge 1.21.4 的 securejarhandler 3.0.8 不会 —— 于是 PlayerEntryProbe
     * 抛 NoClassDefFoundError，服务端 tick 循环直接终止。
     */
    private static boolean canSeeAgent(ClassLoader loader) {
        try {
            Class.forName("club.njitmcl.agent.AgentState", false, loader);
            return true;
        } catch (Throwable t) {
            return false;
        }
    }

    private static void noteSkip(String name) {
        if (INVISIBLE.size() < 12 && INVISIBLE.add(name)) {
            Log.write("跳过观测（该加载器看不见代理类）：" + name);
        }
    }

    private static void allowAgentAccess(JavaModule module) {
        if (instrumentation == null || module == null || !module.isNamed()) {
            return;
        }
        try {
            Object actual = module.unwrap();
            if (actual == null || !MODULES_FIXED.add(actual)) {
                return;
            }
            Class<?> moduleClass = Class.forName("java.lang.Module");
            if (!(Boolean) moduleClass.getMethod("isNamed").invoke(actual)) {
                return;
            }
            Object agentModule = Class.class.getMethod("getModule").invoke(AgentMain.class);
            if (agentModule == actual) {
                return;
            }
            Object descriptor = moduleClass.getMethod("getDescriptor").invoke(actual);
            boolean automatic = descriptor != null
                    && (Boolean) descriptor.getClass().getMethod("isAutomatic").invoke(descriptor);
            Map<String, Set<Object>> exports = new HashMap<String, Set<Object>>();
            Map<String, Set<Object>> opens = new HashMap<String, Set<Object>>();
            if (!automatic) {
                Object packages = moduleClass.getMethod("getPackages").invoke(actual);
                for (Object pkg : (Set<?>) packages) {
                    exports.put((String) pkg, Collections.singleton(agentModule));
                    opens.put((String) pkg, Collections.singleton(agentModule));
                }
            }
            Method redefine = Instrumentation.class.getMethod("redefineModule", moduleClass,
                    Set.class, Map.class, Map.class, Set.class, Map.class);
            try {
                redefine.invoke(instrumentation, actual, Collections.singleton(agentModule),
                        exports, opens, Collections.emptySet(), Collections.emptyMap());
            } catch (Throwable first) {
                // 某些模块（例如 open module）可能拒绝额外导出，退回只补读边，不阻断注入
                Log.write("模块导出失败，退回只补读边（" + module.getActualName() + "）：" + first);
                redefine.invoke(instrumentation, actual, Collections.singleton(agentModule),
                        Collections.emptyMap(), Collections.emptyMap(), Collections.emptySet(), Collections.emptyMap());
            }
            Log.write("模块访问已放行：" + module.getActualName()
                    + (automatic ? "（自动模块，仅补读边）" : "（读边 + 导出 " + exports.size() + " 个包）"));
        } catch (Throwable t) {
            Log.write("模块访问修正失败（" + (module == null ? "?" : module.getActualName()) + "）：" + t);
        }
    }

    private static int currentPid() {
        try {
            String name = ManagementFactory.getRuntimeMXBean().getName();
            int at = name.indexOf('@');
            return Integer.parseInt(at > 0 ? name.substring(0, at) : name);
        } catch (Throwable t) {
            return 0;
        }
    }
}
