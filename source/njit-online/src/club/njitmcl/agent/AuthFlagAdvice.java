package club.njitmcl.agent;

import net.bytebuddy.asm.Advice;

/**
 * 强制服务端走正版校验。
 *
 * 26.x 的「对局域网开放」有三档网络模式：LAN / Online / OFF。选 LAN 时
 * MinecraftServer#usesAuthentication() 返回 false，服务端即使问到 hasJoinedServer
 * 的结果也只打一行 "Failed to verify username but will let them in anyway!" 然后放行。
 *
 * 注意：方法体是用 Advice **内联**进目标类的，所以这里只能引用 JDK 自己的 API。
 * 目标类由 Forge 的 TransformingClassLoader 加载，看不到代理的 club.njitmcl.agent.*，
 * 一旦引用就会 NoClassDefFoundError，再被 suppress 吞掉，表现为「补丁装了但没效果」。
 * 因此开关从系统属性读，不碰 AgentState。
 */
public final class AuthFlagAdvice {

    @Advice.OnMethodExit(suppress = Throwable.class)
    public static void exit(@Advice.Return(readOnly = false) boolean result) {
        // 一次性留痕：确认补丁在目标类里真的执行了（只依赖 JDK，任何加载器都可见）
        if (System.getProperty("njit.trace.flag") == null) {
            System.setProperty("njit.trace.flag", "1");
            System.out.println("[njit-agent] usesAuthentication 进入补丁，原值=" + result);
        }
        if (result) {
            return;
        }
        if (!"false".equalsIgnoreCase(System.getProperty("njit.enforce", "true"))) {
            result = true;
        }
    }

    private AuthFlagAdvice() {
    }
}
