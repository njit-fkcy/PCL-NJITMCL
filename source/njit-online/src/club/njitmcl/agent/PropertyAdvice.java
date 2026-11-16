package club.njitmcl.agent;

import net.bytebuddy.asm.Advice;

/**
 * authlib 的 Property#isSignatureValid：皮肤等 properties 的签名校验。
 *
 * MUA 返回的 textures 带的是 MUA 的签名，原生实现按 Mojang 公钥验会失败，
 * 结果就是玩家进服了但所有人默认皮肤。双验证模式下直接放行。
 */
public final class PropertyAdvice {

    private PropertyAdvice() {
    }

    @Advice.OnMethodExit(suppress = Throwable.class)
    public static void exit(@Advice.Return(readOnly = false) boolean valid) {
        // 不看校验模式：皮肤是显示层的事，谁能进服是校验层的事。
        // 房主用 MUA 开服、客人是正版账号时，正版那侧仍要把 MUA 玩家画出来 ——
        // 这里判模式就会静默退回默认皮肤（2026-11-16 早上复发的就是这一处）。
        if ("false".equalsIgnoreCase(System.getProperty("njit.chat.bypass", "true").trim())) {
            return;
        }
        valid = true;
    }
}
