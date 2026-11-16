package club.njitmcl.agent;

import net.bytebuddy.asm.Advice;

/**
 * 只留一行痕迹：某个类被实例化了。
 *
 * 用来回答「这条链路到底有没有被用到」。皮肤在 26.x 有几条并行路径
 * （SkinManager / PlayerSkinRenderCache / 服务端下发的 PlayerSkin），
 * 猜错一个就白改一轮，所以先在构造器上打点。
 */
public final class TraceAdvice {

    private TraceAdvice() {
    }

    @Advice.OnMethodEnter(suppress = Throwable.class)
    public static void enter() {
        Log.write("痕迹：调用到达这里");
    }
}
