package club.njitmcl.agent;

import net.bytebuddy.asm.Advice;

/**
 * 同一批方法抛异常时的那一行。
 *
 * `SkinManager` 的解码跑在 guava 的 LoadingCache 里，异常会被包进
 * UncheckedExecutionException，外层 `getNow(null)` 拿到的就是 null，
 * 于是安静地回落到默认皮肤。这里把异常原文捞出来。
 */
public final class SkinStageThrowAdvice {

    private SkinStageThrowAdvice() {
    }

    @Advice.OnMethodExit(onThrowable = Throwable.class)
    public static void exit(@Advice.Origin("#m") String method, @Advice.Thrown Throwable thrown) {
        if (thrown == null) {
            return;
        }
        try {
            Log.debug("皮肤·失败 " + method + "：" + thrown.getClass().getName() + "：" + thrown.getMessage());
        } catch (Throwable ignored) {
            // 日志失败不影响业务
        }
    }
}
