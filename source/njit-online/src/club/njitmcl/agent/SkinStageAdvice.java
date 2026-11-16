package club.njitmcl.agent;

import java.util.Arrays;

import net.bytebuddy.asm.Advice;

/**
 * 皮肤流水线上每一站的进出记录：谁被调用、参数是什么。
 *
 * 走到 08:32 那一步时，链路已经全通了：
 *
 *   createLookup（secure=true → false）
 *   SkinManager.get 收到 ImQiez_test，属性 2 条，已签名 https://…
 *   getPackedTextures 取到同一条属性
 *
 * 也就是说「属性有、过滤器放行了、URL 也对」，剩下的只可能在
 * 解码或下载。这一站一站打出来，就不用再猜。
 *
 * 一律不用 suppress：自己接异常并打日志，避免安静的失败。
 */
public final class SkinStageAdvice {

    private SkinStageAdvice() {
    }

    @Advice.OnMethodEnter
    public static void enter(@Advice.Origin("#m") String method, @Advice.AllArguments Object[] args) {
        try {
            Log.debug("皮肤·进入 " + method + "：" + Arrays.toString(args));
        } catch (Throwable t) {
            // 日志本身失败不影响业务
        }
    }
}
