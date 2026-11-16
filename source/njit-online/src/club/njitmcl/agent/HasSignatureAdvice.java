package club.njitmcl.agent;

import net.bytebuddy.asm.Advice;

/**
 * authlib 的 Property#hasSignature：属性上到底有没有签名。
 *
 * 这是皮肤链路上**第一个**会拦人的地方，比 isSignatureValid 更早：
 *
 *   YggdrasilMinecraftSessionService#getTextures(profile, requireSecure=true)
 *     → if (!property.hasSignature()) throw MissingException
 *     → if (!servicesKeyInfo.validateProperty(property)) throw InvalidSignatureException
 *
 * isSignatureValid 只接得住「签了但签名不是 Mojang 的」，接不住「压根没签」。
 * 外置登录站没有配置签名私钥时，textures 就是裸的，走到这里直接抛异常，
 * 上层 SkinManager 吞掉异常、清空 properties、再去 Mojang 问一遍（外置账号必然 204），
 * 结果就是所有人默认皮肤。
 *
 * 双验证模式下这里统一放行：属性内容照旧往下解，URL 是外置站自己的，能取到就行。
 * -Dnjit.skin.unsigned=false 可关。
 */
public final class HasSignatureAdvice {

    private HasSignatureAdvice() {
    }

    @Advice.OnMethodExit(suppress = Throwable.class)
    public static void exit(@Advice.Return(readOnly = false) boolean signed) {
        // 不看校验模式：皮肤是显示层的事，谁能进服是校验层的事。
        // 房主用 MUA 开服、客人是正版账号时，正版那侧仍要把 MUA 玩家画出来 ——
        // 这里判模式就会静默退回默认皮肤（2026-11-16 早上复发的就是这一处）。
        if ("false".equalsIgnoreCase(System.getProperty("njit.skin.unsigned", "true").trim())) {
            return;
        }
        signed = true;
    }
}
