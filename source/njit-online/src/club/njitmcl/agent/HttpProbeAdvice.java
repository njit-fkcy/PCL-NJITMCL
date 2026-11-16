package club.njitmcl.agent;

import net.bytebuddy.asm.Advice;

/**
 * authlib 的 HTTP 出口观测点：只记日志，不碰请求与响应。
 *
 * 挂的是 MinecraftClient#readServiceResponse —— authlib 所有服务端调用都从这里回来，
 * 手上正好有 URL、HttpURLConnection 和抛出的异常，一行就能看出：
 *   这次 join 到底发去了哪个地址、回来什么状态码、是不是真 Mojang 回的。
 *
 * 23:56 那一轮：客户端 join 返回「成功」、房主同一秒拿同一个 serverId 查 204，
 * 而我在本机用同一个账号手写 join + hasJoined 却是 204 + 200 —— 说明链路本身没问题，
 * 差别只可能在这条 HTTP 请求的细节上，所以把这行打出来。
 *
 * 和 BridgeLoginAdvice 同一条硬规矩：方法体内不出现 agent 包符号，加载器搜索内联写。
 */
public final class HttpProbeAdvice {

    private HttpProbeAdvice() {
    }

    @Advice.OnMethodExit(onThrowable = Throwable.class, suppress = Throwable.class)
    public static void exit(@Advice.Argument(0) Object url,
                            @Advice.Argument(2) Object connection,
                            @Advice.Thrown Throwable thrown) {
        try {
            Class<?> bridge = null;
            ClassLoader[] roots = new ClassLoader[]{
                    ClassLoader.getSystemClassLoader(),
                    Thread.currentThread().getContextClassLoader()};
            for (int i = 0; i < roots.length && bridge == null; i++) {
                for (ClassLoader cur = roots[i]; cur != null && bridge == null; cur = cur.getParent()) {
                    try {
                        bridge = Class.forName("club.njitmcl.agent.Bridge", true, cur);
                    } catch (Throwable ignored) {
                        // 换下一个
                    }
                }
            }
            if (bridge == null) {
                return;
            }
            bridge.getMethod("httpDone", Object.class, Object.class, Throwable.class)
                    .invoke(null, url, connection, thrown);
        } catch (Throwable t) {
            // 观测失败不影响进服，但绝不静默：
            // 反射签名对不上会抛 NoSuchMethodException，之前就是这样一条都打不出来。
            try {
                System.err.println("[njit] HTTP 探针失败：" + t);
            } catch (Throwable ignored) {
            }
        }
    }
}
