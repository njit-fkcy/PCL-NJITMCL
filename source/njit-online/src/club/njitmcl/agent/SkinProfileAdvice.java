package club.njitmcl.agent;

import net.bytebuddy.asm.Advice;

/**
 * 26.x 的入口观测：SkinManager#get(GameProfile)。
 *
 * 注意这里的写法：**不用 suppress = Throwable**。
 * 之前那一版用 suppress 把整个 advice 包在 try/catch 里，一旦里面的反射
 * 或辅助类链接失败，异常被吞掉，日志上什么都不留——排查时看起来就像
 * 「这条 advice 根本没织进来」。现在改成自己接异常并把失败打出来，
 * 「没跑到」和「跑到但炸了」一眼就能分开。
 */
public final class SkinProfileAdvice {

    private SkinProfileAdvice() {
    }

    @Advice.OnMethodEnter
    public static void enter(@Advice.Argument(0) Object profile) {
        Log.debug("皮肤·SkinManager.get 被调用");
        try {
            Log.debug("皮肤·SkinManager.get 收到 " + SkinDebugAdvice.who(profile)
                    + "，属性 " + Profiles.propertyCount(profile) + " 条" + Profiles.describe(profile));
        } catch (Throwable t) {
            Log.debug("皮肤·SkinManager.get 描述档案失败：" + t);
        }
    }
}
