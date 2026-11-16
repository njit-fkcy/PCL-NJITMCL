package club.njitmcl.agent;

import net.bytebuddy.asm.Advice;

/**
 * 直接把 PlayerSkin#secure() 压成 true。
 *
 * 26.x 决定「这张皮肤能不能显示给别人」的最终判据就是这个布尔值：
 *
 *   SkinManager.createLookup(profile, secure)
 *     → get(profile) → 解出 PlayerSkin
 *     → Optional.filter((secure, skin) -> !secure || skin.secure())
 *
 * PlayerSkin.secure 的来源是 textures 属性有没有被 Mojang 的密钥签过。
 * 外置登录站用自己的密钥签，这个值恒为 false —— 属性、URL、下载都没问题，
 * 就是在最后这一步被判为「不可信」，回落到默认皮肤。
 *
 * 前面试过改 createLookup 的入参，但那一步在 26.x 的渲染链上不一定被调用；
 * 而 secure() 只要有人来看皮肤就必然被读一次，所以改在这里最稳：
 * 读取即返回 true，filter 直接放行。
 *
 * 双验证模式下生效，-Dnjit.skin.unsigned=false 可关。
 */
public final class SkinSecureFlagAdvice {

    private SkinSecureFlagAdvice() {
    }

    @Advice.OnMethodExit(suppress = Throwable.class)
    public static void exit(@Advice.Return(readOnly = false) boolean secure) {
        // 不看校验模式：皮肤是显示层的事，谁能进服是校验层的事。
        // 房主用 MUA 开服、客人是正版账号时，正版那侧仍要把 MUA 玩家画出来 ——
        // 这里判模式就会静默退回默认皮肤（2026-11-16 早上复发的就是这一处）。
        if ("false".equalsIgnoreCase(System.getProperty("njit.skin.unsigned", "true").trim())) {
            return;
        }
        secure = true;
    }
}
