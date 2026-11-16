package club.njitmcl.agent;

import net.bytebuddy.asm.Advice;

/**
 * authlib 的属性签名校验：ServicesKeyInfo#validateProperty(Property)。
 *
 * 这是 1.20.1 上真正拦皮肤的那一道，位置比 Property#isSignatureValid 更靠外：
 *
 *   YggdrasilMinecraftSessionService#getSecurePropertyValue(property)
 *     → servicesKeySet.keys(PROFILE_PROPERTY).stream().noneMatch(k -> k.validateProperty(property))
 *        → 成立就抛 InsecurePublicKeyException.InvalidException
 *
 * 而 4.x 的 YggdrasilServicesKeyInfo#validateProperty 是**自己拿 Signature 验**的，
 * 并不调用 Property#isSignatureValid —— 所以只补 isSignatureValid 在这里一次都不会命中。
 * 签名是 MUA 的私钥签的，用 Mojang 的公钥验必然 false，异常往上抛，
 * SkinManager 接住后把 profile 的 properties 整个清空、再去 Mojang 问一遍
 * （外置账号必然 204），最后画默认皮肤，日志里一个字都没有。
 *
 * 双验证模式下这条直接放行。 -Dnjit.skin.unsigned=false 可关。
 */
public final class ValidatePropertyAdvice {

    private ValidatePropertyAdvice() {
    }

    @Advice.OnMethodExit(suppress = Throwable.class)
    public static void exit(@Advice.Return(readOnly = false) boolean valid) {
        // 不看校验模式：皮肤是显示层的事，谁能进服是校验层的事。
        // 房主用 MUA 开服、客人是正版账号时，正版那侧仍要把 MUA 玩家画出来 ——
        // 这里判模式就会静默退回默认皮肤（2026-11-16 早上复发的就是这一处）。
        if ("false".equalsIgnoreCase(System.getProperty("njit.skin.unsigned", "true").trim())) {
            return;
        }
        valid = true;
    }
}
