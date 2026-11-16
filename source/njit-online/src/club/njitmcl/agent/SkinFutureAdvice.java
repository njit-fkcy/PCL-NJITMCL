package club.njitmcl.agent;

import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.atomic.AtomicBoolean;

import net.bytebuddy.asm.Advice;

/**
 * 皮肤 future 的完成情况——用「3 秒后回看」的方式判定，不依赖回调。
 *
 * 08:50 那次的日志：
 *   皮肤·挂 whenComplete registerTextures，当前已完成=false
 *   然后没有任何「完成」行
 *
 * 这有两种解释，日志分不开：
 *   a) future 真的没完成
 *   b) 完成了，但我们的回调没被调用（advice/代理类的可见性问题）
 *
 * 所以这里换个问法：把 future 记下来，起一个守护线程 3 秒后回看 isDone()。
 * 回看是直接从另一个线程读状态，不经过任何回调，b) 就没有藏身之处了。
 */
public final class SkinFutureAdvice {

    private static final List<String> PENDING = new ArrayList<String>();
    private static final AtomicBoolean STARTED = new AtomicBoolean(false);

    private SkinFutureAdvice() {
    }

    @Advice.OnMethodExit
    public static void exit(@Advice.Origin("#m") String method, @Advice.Return final Object returned) {
        if (!(returned instanceof CompletableFuture)) {
            return;
        }
        // 只做两件事：记一行 + 交给 watch。
        // 匿名内部类**不能**写在这里——advice 会被内联进目标类，
        // 而匿名类是包级私有，跨模块 new 它同样抛 IllegalAccessError。
        try {
            Log.debug("皮肤·挂 whenComplete " + method + "，当前已完成="
                    + ((CompletableFuture<?>) returned).isDone());
            watch(method, (CompletableFuture<?>) returned);
        } catch (Throwable ignored) {
            // 任何观测动作都不能影响业务
        }
    }

    /** 3 秒后回看：这是「future 到底完不完成」唯一干净的问法。 */
    public static void watch(final String method, final CompletableFuture<?> future) {
        synchronized (PENDING) {
            PENDING.add(method);
        }
        future.whenComplete(new java.util.function.BiConsumer<Object, Throwable>() {
            @Override
            public void accept(Object value, Throwable thrown) {
                try {
                    Log.debug("皮肤·完成 " + method + (thrown == null
                            ? " → " + value
                            : " 失败：" + thrown.getClass().getName() + "：" + thrown.getMessage()));
                } catch (Throwable ignored) {
                    // 日志失败不影响业务
                }
            }
        });
        if (!STARTED.compareAndSet(false, true)) {
            return;
        }
        Thread worker = new Thread(new Runnable() {
            @Override
            public void run() {
                try {
                    Thread.sleep(3000L);
                } catch (InterruptedException ignored) {
                    return;
                }
                int index;
                synchronized (PENDING) {
                    index = PENDING.size();
                }
                try {
                    Log.debug("皮肤·回看 3 秒后：共记录 " + index + " 个 future，本批最后一个已完成=" + future.isDone());
                } catch (Throwable ignored) {
                    // 日志失败不影响业务
                }
                STARTED.set(false);
            }
        }, "njit-skin-watch");
        worker.setDaemon(true);
        worker.start();
    }
}
