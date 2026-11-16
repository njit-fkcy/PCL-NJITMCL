package club.njitmcl.agent;

import net.bytebuddy.asm.Advice;

/**
 * joinServer 的观测点 —— **只记日志，绝不改行为**。
 *
 * 为什么要它：客户端进服时先做一次 session/minecraft/join，把「某账号加入过某 serverId」
 * 登记到会话服务器；服务端随后用同一个 serverId 去 hasJoined 查。登记没成功，
 * 服务端再怎么查都只能拿到 204，界面上就是「验证用户名失败！」——
 * 而这一步的失败，原来在两边日志里都看不见：客户端的 authlib 大多吞掉异常，
 * 房主只看得到 204。
 *
 * 和 BridgeLoginAdvice 同一条硬规矩：方法体内不许出现 agent 包的符号，
 * 加载器搜索只能内联写。这里连返回值都不碰，出任何问题都只是少一行日志。
 */
public final class BridgeJoinAdvice {

    private BridgeJoinAdvice() {
    }

    @Advice.OnMethodEnter(suppress = Throwable.class)
    public static void enter(@Advice.AllArguments Object[] args) {
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
            bridge.getMethod("joinEnter", Object[].class).invoke(null, (Object) args);
        } catch (Throwable ignored) {
            // 观测失败不能影响进服
        }
    }

    @Advice.OnMethodExit(onThrowable = Throwable.class, suppress = Throwable.class)
    public static void exit(@Advice.AllArguments Object[] args, @Advice.Thrown Throwable thrown) {
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
            bridge.getMethod("joinExit", Object[].class, Throwable.class).invoke(null, args, thrown);
        } catch (Throwable ignored) {
            // 观测失败不能影响进服
        }
    }
}
