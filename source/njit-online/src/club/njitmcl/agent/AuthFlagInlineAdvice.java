package club.njitmcl.agent;

import net.bytebuddy.asm.Advice;

/**
 * usesAuthentication → true，**不引用代理里的任何类**。
 *
 * 为什么要有这个「穷版」：NeoForge 1.21.4（securejarhandler 3.0.8）的游戏类装在
 * 自己的模块加载器里，那个加载器**看不见** club.njitmcl.agent.*。
 * advice 会被内联进目标类，一旦它去调 PlayerEntryProbe 这类辅助类，
 * 就抛 NoClassDefFoundError —— 直接把服务端 tick 循环打死（2026-11-15 13:07 崩端）。
 *
 * 所以这类「把布尔值钉成 true」的补丁另写一份：体内只用 JDK 的
 * System.getProperty，不 import 任何代理里的类，任何加载器都能跑。
 *
 * 模式从系统属性读：ControlServer 每次切换模式时同步写 njit.mode，
 * 这样内联代码也能知道当前是不是双验证。
 */
public final class AuthFlagInlineAdvice {

    private AuthFlagInlineAdvice() {
    }

    @Advice.OnMethodExit
    public static void exit(@Advice.Return(readOnly = false) boolean returned) {
        if (!"4".equals(System.getProperty("njit.mode"))) {
            return;
        }
        returned = true;
    }
}
