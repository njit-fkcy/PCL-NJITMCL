package club.njitmcl.agent;

/**
 * 代理全局状态。被注入的 authlib 方法会直接读这里的 volatile 字段，
 * 因此切换是热生效的：下一个登录请求立刻按新状态处理，已在线玩家不受影响。
 */
public final class AgentState {

    public static final int ModeMojang = 1;
    /** 双验证：先问 Mojang，问不到再问 MUA（正版同名优先）。 */
    public static final int ModeDualMojangFirst = 4;

    /**
     * 只保留两档：1 正版、4 双验证（正版优先）。
     * 0（免验证）、2（仅 MUA）、3（MUA 优先）已移除；取值不重编号，旧脚本里的 1 含义不变。
     */
    public static boolean supported(int value) {
        return value == ModeMojang || value == ModeDualMojangFirst;
    }

    /** 校验模式：1 = 正版（Mojang）；4 = 双验证。 */
    public static volatile int mode = ModeMojang;

    /**
     * 是否强制服务端走校验流程（-Dnjit.enforce=false 可关）。
     *
     * 26.x 起「对局域网开放」的 LAN 档本身就是离线模式：
     * MinecraftServer#usesAuthentication() 返回 false，服务端拿到 hasJoinedServer
     * 的结果直接忽略，谁都能进。这里把它翻成 true，才能让模式真正起作用。
     * 已在线的玩家不受影响，只作用于之后的登录。
     */
    public static volatile boolean enforceAuth = true;

    /** 调试日志开关：-Dnjit.debug=true。 */
    public static volatile boolean debug = Boolean.getBoolean("njit.debug");

    /** MUA 的 Yggdrasil 根地址，可由启动器在 SET 时覆盖。 */
    public static volatile String muaApi = Sessions.MUA_API;

    /**
     * 本进程的会话服务器是否被 authlib-injector 换成了外置站。
     *
     * 用外置账号启动时，启动器会带 -javaagent:authlib-injector.jar=… 并塞
     * -Dauthlibinjector.yggdrasil.prefetched=… 。有这条属性就说明服务端问的
     * 不是 Mojang 而是那个外置站 —— 正是「正版档下正版账号进不去」的根因。
     */
    public static final boolean externalSession =
            System.getProperty("authlibinjector.yggdrasil.prefetched") != null;

    /**
     * 正版档是否退回「原样执行」（2.2.0 之前的行为）：-Dnjit.mode1.native=true。
     * 退回后正版档在 MUA 开的服上只认 MUA，Mojang 账号进不来 —— 需要「只放社团的人」时才要这个。
     */
    public static final boolean modeMojangNative = Boolean.getBoolean("njit.mode1.native");

    /** 接管日志只写一次，避免每次登录都刷。 */
    public static volatile boolean externalTakeoverLogged = false;

    /** 已改写的方法数量，供握手文件诊断用。 */
    public static volatile int patched = 0;

    /** 控制端口令牌，随机生成，防止本机其他进程乱发指令。 */
    public static volatile String token = "";

    /** 控制端口，绑定在 127.0.0.1。 */
    public static volatile int port = 0;

    /** 实际命中的类#方法，用于诊断目标版本是否换了注入点。 */
    public static volatile String target = "";

    /** 该方法的返回类型全名，例如 com.mojang.authlib.yggdrasil.ProfileResult。 */
    public static volatile String returnType = "";

    private AgentState() {
    }
}
