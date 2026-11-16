package club.njitmcl.agent;

import net.bytebuddy.asm.Advice;

/**
 * 皮肤链路的现场记录（抛异常那一侧）。
 *
 * getTextures(profile, requireSecure=true) 在两种情况下会抛：
 *
 *   MissingException            属性压根没签名
 *   InvalidSignatureException   签了，但不是 Mojang 的密钥
 *
 * 这两个异常在 SkinManager 里被 catch 吞掉，接着清空 properties 去问 Mojang，
 * 最后画默认皮肤，日志里一个字都没有。这里把它捞出来打一行，
 * 以后排查皮肤问题不用再猜是哪一层拦的。
 */
public final class SkinDebugThrowAdvice {

    private SkinDebugThrowAdvice() {
    }

    @Advice.OnMethodExit(onThrowable = Throwable.class, suppress = Throwable.class)
    public static void exit(@Advice.Argument(0) Object profile,
                            @Advice.Origin("#m") String name,
                            @Advice.Thrown Throwable thrown) {
        if (thrown == null) {
            return;
        }
        Log.debug("皮肤·" + name + " 抛出：" + SkinDebugAdvice.who(profile)
                + " → " + thrown.getClass().getName() + "：" + thrown.getMessage());
    }
}
