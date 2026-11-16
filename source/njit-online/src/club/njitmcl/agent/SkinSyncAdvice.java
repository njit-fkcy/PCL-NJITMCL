package club.njitmcl.agent;

import java.util.concurrent.CompletableFuture;
import java.util.concurrent.TimeUnit;

import net.bytebuddy.asm.Advice;

/**
 * 让 SkinManager#get 返回的 future 在返回前就完成——这是「冻结」问题的正解。
 *
 * 26.x 的 createLookup 是这么写的（字节码实证）：
 *
 *   Optional<PlayerSkin> optional = future.getNow(null);   // 立刻取，不等
 *   if (optional != null) return () -> optional.filter(...).orElse(default);   // 冻结
 *   else                  return 延迟 lambda（每次 get 都重新看 future）
 *
 * 第一次解析时 future 还没完成（要下载/解码），getNow 返回 null —— 走延迟路径，
 * 本该没问题。但只要有任何一次 getNow 拿到的是「已完成但为空」，supplier 就被
 * 冻结成默认皮肤，之后真皮肤到了也不会再变。房主看到的默认 Steve 就是这么来的：
 * 解析出来的 PlayerSkin 是 SLIM（mua 那个人的），屏幕上画的却是默认 WIDE。
 *
 * 修法：在 get() 返回前把 future 等完成（最多 2 秒，通常命中本地缓存只有几十毫秒）。
 * 这样 createLookup 拿到的一定是已完成的结果，冻结也就冻结在正确的值上。
 */
public final class SkinSyncAdvice {

    private SkinSyncAdvice() {
    }

    @Advice.OnMethodExit
    public static void exit(@Advice.Return final Object returned) {
        if (!(returned instanceof CompletableFuture)) {
            return;
        }
        CompletableFuture<?> future = (CompletableFuture<?>) returned;
        if (future.isDone()) {
            return;
        }
        try {
            future.get(2L, TimeUnit.SECONDS);
            Log.debug("皮肤·等待完成：已就绪");
        } catch (Throwable t) {
            Log.debug("皮肤·等待完成超时/失败：" + t.getClass().getSimpleName());
        }
    }
}
