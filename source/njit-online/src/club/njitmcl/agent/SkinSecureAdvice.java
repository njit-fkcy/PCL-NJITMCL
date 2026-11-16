package club.njitmcl.agent;

import net.bytebuddy.asm.Advice;

/**
 * 把 SkinManager#createLookup(GameProfile, boolean) 的 secure 参数压成 false。
 *
 * 26.x：PlayerInfo.createSkinLookup → SkinManager.createLookup(profile, secure=!isLocal)
 *   → get(profile) → 解码 → Optional.filter((secure, skin) -> !secure || skin.secure())
 *
 * 外置站的 textures 不是 Mojang 签的，PlayerSkin.secure 恒为 false，
 * 于是在「看别人」这条路上被 filter 掉。这里把入参压成 false 直接放行。
 *
 * 同样不用 suppress：自己接异常并打出来，避免「跑到但炸了」被吞。
 */
public final class SkinSecureAdvice {

    private SkinSecureAdvice() {
    }

    @Advice.OnMethodEnter
    public static void enter(@Advice.Argument(0) Object profile,
                             @Advice.Argument(value = 1, readOnly = false) boolean secure) {
        try {
        // 不看校验模式：皮肤是显示层的事，谁能进服是校验层的事。
        // 房主用 MUA 开服、客人是正版账号时，正版那侧仍要把 MUA 玩家画出来 ——
        // 这里判模式就会静默退回默认皮肤（2026-11-16 早上复发的就是这一处）。
            if ("false".equalsIgnoreCase(System.getProperty("njit.skin.unsigned", "true").trim())) {
                return;
            }
            // 先改值再记日志：日志出任何问题都不能影响「放行」这个动作
            boolean was = secure;
            secure = false;
            Log.debug("皮肤·createLookup 被调用（secure=" + was + " → false）");
        } catch (Throwable t) {
            Log.debug("皮肤·createLookup 处理失败：" + t);
        }
    }
}
