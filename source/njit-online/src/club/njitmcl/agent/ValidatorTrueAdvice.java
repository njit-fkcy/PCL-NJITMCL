package club.njitmcl.agent;

import net.bytebuddy.asm.Advice;

/**
 * 服务端验「玩家公钥」时用的那套校验器（1.20.1 里混淆成 apj）。
 *
 * 这条链路和资产属性无关，它验的是客户端带上来的 profile key：
 *
 *   SignatureValidator#validate(内容, 签名)
 *     → 遍历 ServicesKeySet 里的每个密钥
 *       → keyInfo.signature().update(内容); verify(签名)
 *         验不过就 LOGGER.error("Failed to verify Services signature") → 返回 false
 *
 * 外置登录站的密钥不在 Mojang 的公钥集里，必验不过，于是房主的服务端把人踢掉：
 * 「无效的站点签名」（2026-11-15 07:12:34 实测）。这条和皮肤是两件事，
 * 但都是同一类问题：Mojang 的信任链不认外置身份。
 *
 * 这个类在混淆后的 jar 里没有名字可用，只能按形状认：
 * 一个接口，声明了「拿 ServicesKeySet 当参数」的方法。命中的类型里
 * 所有返回 boolean 的方法（equals/hashCode 除外）统一放行。
 */
public final class ValidatorTrueAdvice {

    private ValidatorTrueAdvice() {
    }

    @Advice.OnMethodExit(suppress = Throwable.class)
    public static void exit(@Advice.Return(readOnly = false) boolean valid) {
        // 不看校验模式：皮肤是显示层的事，谁能进服是校验层的事。
        // 房主用 MUA 开服、客人是正版账号时，正版那侧仍要把 MUA 玩家画出来 ——
        // 这里判模式就会静默退回默认皮肤（2026-11-16 早上复发的就是这一处）。
        if ("false".equalsIgnoreCase(System.getProperty("njit.enforce", "true").trim())) {
            return;
        }
        valid = true;
    }
}
