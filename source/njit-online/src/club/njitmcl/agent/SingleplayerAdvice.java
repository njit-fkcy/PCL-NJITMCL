package club.njitmcl.agent;

import net.bytebuddy.asm.Advice;

/**
 * 让「验证失败但照样放行」那条路失效。
 *
 * 26.x 的登录线程里，hasJoinedServer 返回 null 之后是这么分支的：
 *
 *   if (server.isSingleplayer()) {
 *       LOGGER.warn("Failed to verify username but will let them in anyway!");
 *       startClientVerification(离线 profile);      // 放行
 *   } else {
 *       disconnect("multiplayer.disconnect.unverified_username");
 *   }
 *
 * 集成服务端 isSingleplayer() 恒为 true（实现就是 singleplayerProfile != null），
 * 所以哪怕开了 usesAuthentication、哪怕 Mojang 明确回了 204，玩家还是会被放进来。
 *
 * 这里只在「User Authenticator」线程上把它翻成 false——也就是只在做会话校验的那一瞬间
 * 生效，其它调用点（白名单、作弊权限、玩家列表等）保持原样。
 *
 * 方法体是内联进目标类的，目标类由 Forge 的 TransformingClassLoader 加载，
 * 看不到代理的类，所以这里只用 JDK API。
 */
public final class SingleplayerAdvice {

    private SingleplayerAdvice() {
    }

    @Advice.OnMethodExit(suppress = Throwable.class)
    public static void exit(@Advice.Return(readOnly = false) boolean result) {
        if (!result) {
            return;
        }
        if ("false".equalsIgnoreCase(System.getProperty("njit.enforce", "true").trim())) {
            return;
        }
        String thread = Thread.currentThread().getName();
        if (thread == null || !thread.startsWith("User Authenticator")) {
            return;
        }
        // 走到这里说明正在做进服校验：按真正的多人生存服处理，验不过就拒绝
        result = false;
    }
}
