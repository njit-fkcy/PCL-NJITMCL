package club.njitmcl.agent;

import java.lang.reflect.Method;
import java.util.Collections;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;

/**
 * 「看得见代理类」的那一半。
 *
 * 背景（2026-11-15 一整天绕的弯）：advice 是**内联进目标类**执行的，
 * 它体内一旦出现 club.njitmcl.agent.* 的符号，就要求**目标类的加载器**能解析它。
 * Fabric 的 KnotClassLoader、NeoForge / Forge 1.17-1.19 的 securejarhandler
 * 都不向应用加载器委派，于是：
 *
 *   - 带 suppress 的富版 advice：异常被吞，功能静默无效（1.18.2「验证用户名失败」）
 *   - 不带 suppress：NoClassDefFoundError 直接崩服务端（NeoForge 1.21.4）
 *
 * bootstrap 那条路已经证伪（Knot 根本不查 bootstrap）。
 *
 * 现在改成：advice 体内**只用 JDK 类型**，靠 Class.forName 反射回到这个类里干活。
 * 反射带显式加载器，所以不管目标类在哪个加载器，都能摸到我们。
 * 逻辑仍然只有一份 —— 就是这里。
 */
public final class Bridge {

    /** 已经记过日志的纹理域名，避免一次进服刷几十行。 */
    private static final Set<String> SEEN_DOMAIN =
            Collections.newSetFromMap(new ConcurrentHashMap<String, Boolean>());

    private Bridge() {
    }

    /**
     * 接管 hasJoinedServer。
     *
     * @param service 会话服务实例（用来反查返回类型）
     * @param args    原方法实参
     * @return 非 null 表示「用这个值当返回值」，原方法体跳过；null 表示走原生逻辑
     */
    public static Object hasJoined(Object service, Object[] args) {
        try {
            boolean mojangOnly = AgentState.mode == AgentState.ModeMojang;
            if (mojangOnly && (!AgentState.externalSession || AgentState.modeMojangNative)) {
                // 进程没被外置登录注入：原生那套问的就是 Mojang，没必要接管
                return null;
            }
            if (!AgentState.supported(AgentState.mode)) {
                return null;
            }
            if (service == null || args == null || args.length < 2) {
                return null;
            }
            Class<?> want = returnTypeOf(service, args.length);
            if (want == null) {
                Log.write("找不到 hasJoinedServer 的返回类型，放弃接管");
                return null;
            }
            if (mojangOnly && !AgentState.externalTakeoverLogged) {
                AgentState.externalTakeoverLogged = true;
                Log.write("正版档：本进程的会话服务器已被外置登录替换，改由代理直接问 Mojang；"
                        + "外置账号会在这档被拒（要两边都进请开双验证）");
            }
            if (AgentState.target.isEmpty()) {
                AgentState.target = service.getClass().getName() + "#hasJoinedServer";
                AgentState.returnType = want.getName();
                ControlServer.refresh();
            }
            String name = Profiles.nameOf(args[0]);
            if (name == null || name.isEmpty()) {
                return null;
            }
            String serverId = args[1] == null ? "" : String.valueOf(args[1]);
            ClassLoader loader = service.getClass().getClassLoader();
            Object profile = Sessions.verifyMojang(name, serverId, loader);
            if (profile == null && mojangOnly) {
                // 正版档只认 Mojang，查不到就交空，让服务端照常拒绝
                Log.write("正版未取到档案：" + name + "（serverId=" + serverId + "）");
                return null;
            }
            if (profile == null) {
                profile = Sessions.verifyExternal(name, serverId, AgentState.muaApi, loader);
            }
            if (profile == null) {
                Log.write("双验证未取到档案：" + name + "（serverId=" + serverId + "）");
                return null;
            }
            Object value = Profiles.wrap(profile, want);
            if (value == null) {
                Log.write("档案转换失败：" + name + " → " + want.getName());
                return null;
            }
            Log.write((mojangOnly ? "正版放行：" : "双验证放行：") + name + " → " + want.getSimpleName());
            return value;
        } catch (Throwable t) {
            Log.write("Bridge.hasJoined 失败：" + t);
            return null;
        }
    }

    /**
     * joinServer 进门：记下这次要登记的 serverId。
     *
     * 客户端这一步才是「向会话服务器登记本次加入」；它没成，服务端的 hasJoined
     * 必然 204，界面显示「验证用户名失败！」。以前两边都看不到这一步的结果。
     */
    public static void joinEnter(Object[] args) {
        try {
            String who = (args != null && args.length > 0 && args[0] != null)
                    ? String.valueOf(args[0]) : "?";
            Log.write("join 发送：账号 " + who + "，serverId=" + lastString(args) + "，参数 "
                    + shape(args));
        } catch (Throwable t) {
            Log.write("join 记录失败：" + t);
        }
    }

    /** 一条 authlib HTTP 调用回来时记一行：地址、状态码、请求号。 */
    public static void httpDone(Object url, Object connection, Throwable thrown) {
        try {
            String target = url == null ? "?" : url.toString();
            // 只看会话相关的：整局启动时还有几十条无关请求，全记会淹掉重点
            if (target.indexOf("/session/minecraft/") < 0) {
                return;
            }
            int code = -1;
            String reqId = "";
            if (connection instanceof java.net.HttpURLConnection) {
                java.net.HttpURLConnection http = (java.net.HttpURLConnection) connection;
                code = http.getResponseCode();
                reqId = http.getHeaderField("x-minecraft-request-id");
            }
            Log.write("HTTP " + code + " " + target
                    + (reqId == null || reqId.isEmpty() ? "" : "  reqid=" + reqId)
                    + (thrown == null ? "" : "  ← " + chain(thrown)));
        } catch (Throwable t) {
            Log.write("HTTP 记录失败：" + t);
        }
    }

    /**
     * joinServer 出门：正常返回 = 会话服务器已登记（该接口成功就是 204 空响应）。
     */
    public static void joinExit(Object[] args, Throwable thrown) {
        try {
            if (thrown == null) {
                Log.write("join 成功：会话服务器已登记（serverId=" + lastString(args) + "）");
                return;
            }
            Log.write("join 失败：" + chain(thrown));
        } catch (Throwable t) {
            Log.write("join 记录失败：" + t);
        }
    }

    /** 方法实参里最后那个字符串：老版是第三个（serverId），新版是第二个。 */
    private static String lastString(Object[] args) {
        if (args == null) {
            return "(无参数)";
        }
        for (int i = args.length - 1; i >= 0; i--) {
            if (args[i] instanceof String) {
                return (String) args[i];
            }
        }
        return "(没有字符串参数)";
    }

    /** 参数形状：只看类型名，不碰对象内容（GameProfile 可能不可读）。 */
    private static String shape(Object[] args) {
        if (args == null || args.length == 0) {
            return "()";
        }
        StringBuilder text = new StringBuilder("(");
        for (int i = 0; i < args.length; i++) {
            if (i > 0) {
                text.append(", ");
            }
            text.append(args[i] == null ? "null" : args[i].getClass().getSimpleName());
        }
        return text.append(")").toString();
    }

    /** 异常连同前几层 cause 一起记：HTTP 状态码通常藏在最里面那层。 */
    private static String chain(Object thrown) {
        StringBuilder text = new StringBuilder();
        Throwable current = thrown instanceof Throwable ? (Throwable) thrown : null;
        for (int depth = 0; current != null && depth < 4; depth++) {
            if (depth > 0) {
                text.append(" ← ");
            }
            text.append(current.getClass().getSimpleName());
            if (current.getMessage() != null) {
                text.append("：").append(current.getMessage());
            }
            current = current.getCause();
        }
        return text.length() == 0 ? String.valueOf(thrown) : text.toString();
    }

    /**
     * 取贴图之前先把档案补好。
     *
     * 1.18.2 的 AbstractClientPlayer 把「皮肤贴图位置」缓存成字段：第一次算出来是
     * 默认值，之后不管档案变成什么样都不再重算。所以必须让**第一次**就带上 textures。
     *
     * 实测（14:59:03 客户端日志）：
     *   皮肤·取贴图 ImQiez_test：档案属性 0 条 → 0 张      ← 这一枪被缓存了
     *   皮肤·取贴图 ImQiez_test：档案属性 2 条 → 1 张      ← 后来的，用不上
     *
     * 打开「先补再算」之后，第一次就能拿到 1 张，缓存也就缓存对了。
     */
    public static void prepare(Object profile) {
        try {
            // 这里**不看校验模式**。
            // 皮肤是显示层的事，「谁能进服」是校验层的事 —— 客人那台常年停在正版档
            // （它不需要双验证，进服靠的是房主那侧），如果这里判模式，
            // 客人就永远不会补属性，第一次取贴图拿 0 张，然后被缓存锁死。
            // 2026-11-15 15:09 就是这么复发的：房主那边补上了（日志有「补上皮肤属性」），
            // 客人那边一条都没有。
            if (profile == null || Profiles.propertyCount(profile) > 0) {
                return;
            }
            PeerSkin.fill(profile);
        } catch (Throwable t) {
            // 补不上就让原生逻辑照常走
        }
    }

    /** 只记一次。 */
    private static volatile boolean SECURE_LOGGED;

    /**
     * 26.x 的 PlayerSkin#secure()：外置站的皮肤永远不是「可信」的，
     * 而 createLookup 里有 filter((secure, skin) -> !secure || skin.secure())。
     * 直接让它返回 true。
     *
     * Forge 26.3 上这条早就有了（SkinSecureFlagAdvice），但它引用代理类，
     * 在 Fabric / NeoForge 的加载器里会被跳过 —— 所以补一个 bridge 版本。
     */
    public static boolean secureFlag() {
        if (!SECURE_LOGGED) {
            SECURE_LOGGED = true;
            Log.write("皮肤 secure 标记已放行（外置站自签）");
        }
        return true;
    }

    /** 只记一次，避免刷屏。 */
    private static volatile boolean PROPERTY_LOGGED;

    /**
     * 1.20.2 – 1.21 的验签：ServicesKeyInfo#validateProperty(Property)。
     *
     * 这条路不经过 Property#isSignatureValid，是自己拿 Signature 验的，
     * 外置站自签必然 false → getTextures(requireSecure=true) 抛
     * InsecureTextureException → 回落默认皮肤。
     *
     * Fabric 1.21.1 的实测形态（2026-11-15 15:43）：
     *   双验证放行 → ProfileResult          （登录通了）
     *   纹理域名判定 …（原生 false → 放行 true）  （域名闸放行了）
     *   皮肤·取贴图 … 档案属性 2 条 → -1 张      （返回的不是 Map，走的是另一条路）
     * 也就是属性、域名都对，卡在「这张图可不可信」。
     */
    public static boolean propertyValid() {
        if (!PROPERTY_LOGGED) {
            PROPERTY_LOGGED = true;
            Log.write("属性验签已放行（外置站自签）");
        }
        return true;
    }

    /** 只记一次，避免每张图都写一行。 */
    private static volatile boolean SIGNATURE_LOGGED;

    /**
     * textures 属性的签名校验：一律放行。
     *
     * 外置登录站的 textures 由它自己的私钥签，Mojang 的公钥验不过，于是
     * 1.18.2 的 getTextures(requireSecure=true) 走 isSignatureValid → false →
     * 抛 InsecureTextureException。上层接住、把属性清空、回落默认皮肤 ——
     * 而且**异常不在 exit 上体现**，所以日志里只剩 enter 那行「补上皮肤属性」，
     * 没有 exit 的「取贴图」行（2026-11-15 15:16 就是这个形态）。
     *
     * 只影响「这张贴图是否可信」，不影响登录。
     */
    public static boolean signatureValid() {
        if (!SIGNATURE_LOGGED) {
            SIGNATURE_LOGGED = true;
            Log.write("纹理签名校验已放行（外置站自签）");
        }
        return true;
    }

    /** 已经记过的「玩家→张数」组合，避免每帧刷屏。 */
    private static final Set<String> SEEN_SKIN =
            Collections.newSetFromMap(new ConcurrentHashMap<String, Boolean>());

    /**
     * 取贴图那一步的现场记录：客户端手里的档案到底带不带 textures、解出几张。
     *
     * 1.18.2 上「正版那台看不见 MUA 的皮肤」需要先分清两件事：
     *   属性没到（档案 0 条）  vs  属性到了但解不出来/被域名闸拦（2 条 → 0 张）。
     * 没有这一行就只能猜，而这一片今天已经猜了太多轮。
     */
    public static void skin(Object profile, Object map) {
        try {
            String name = Profiles.nameOf(profile);
            int props = Profiles.propertyCount(profile);
            String shape;
            if (map instanceof java.util.Map) {
                shape = ((java.util.Map<?, ?>) map).size() + " 张";
            } else {
                // 1.20.2+ 走 getPackedTextures，返回的是 Property / Optional，
                // 这里把它叫什么写清楚，省得下次又猜
                shape = "非 Map（" + (map == null ? "null" : map.getClass().getName()) + "）";
            }
            if (name != null && SEEN_SKIN.size() < 16 && SEEN_SKIN.add(name + ":" + shape)) {
                Log.write("皮肤·取贴图 " + name + "：档案属性 " + props + " 条 → " + shape
                        + Profiles.describe(profile));
            }
        } catch (Throwable t) {
            // 观测失败不影响业务
        }
    }

    /**
     * 纹理域名白名单：放行非 Mojang 的贴图域名。
     *
     * 不看校验模式 —— 皮肤是显示层的事。房主用 MUA 开服、客人是正版账号时，
     * 正版那侧也必须能把 MUA 玩家画出来；这里判模式就会静默回默认皮肤。
     *
     * @return true 表示「这个域名算合法」
     */
    public static boolean domain(String url) {
        try {
            if (url != null && SEEN_DOMAIN.size() < 8 && SEEN_DOMAIN.add(url)) {
                Log.write("纹理域名判定：" + url + "（原生 false → 放行 true）");
            }
            return true;
        } catch (Throwable t) {
            return false;
        }
    }

    private static Class<?> returnTypeOf(Object service, int argCount) {
        try {
            for (Method method : service.getClass().getMethods()) {
                if (method.getName().equals("hasJoinedServer")
                        && method.getParameterCount() == argCount) {
                    return method.getReturnType();
                }
            }
        } catch (Throwable ignored) {
            // 拿不到就交给调用方退回原生行为
        }
        return null;
    }
}
