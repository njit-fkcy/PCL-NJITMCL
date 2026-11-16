package club.njitmcl.agent;

import java.util.Map;

import net.bytebuddy.asm.Advice;
import net.bytebuddy.implementation.bytecode.assign.Assigner;

/**
 * 客户端把档案翻成贴图的那一步（SkinManager 的「不验签」路径）。
 *
 * 为什么不直接盯会话服务：1.20.1 的渲染链对**在线玩家**走的是
 * {@code SkinManager#getInsecureSkinInformation(profile)}——只看 properties 里有没有
 * textures、解 base64、拼 HttpTexture，全程不碰 MinecraftSessionService，
 * 所以 getTextures 那条 advice 在这条路上一次都不会响。
 *
 * 这类方法在混淆后的 jar 里没有名字可用，只能按**形状**认：
 * 一张 (GameProfile) → Map 的方法。返回几张图、URL 是什么，是这条链路唯一的出口证据。
 */
public final class SkinLookupAdvice {

    private SkinLookupAdvice() {
    }

    @Advice.OnMethodExit(suppress = Throwable.class)
    public static void exit(@Advice.Argument(0) Object profile,
                            @Advice.Return(typing = Assigner.Typing.DYNAMIC) Object returned) {
        if (!(returned instanceof Map)) {
            return;
        }
        Map<?, ?> textures = (Map<?, ?>) returned;
        StringBuilder detail = new StringBuilder();
        for (Object value : textures.values()) {
            String url = urlOf(value);
            if (url != null) {
                detail.append(' ').append(url);
            }
        }
        Log.debug("皮肤·查表 " + SkinDebugAdvice.who(profile) + " → " + textures.size() + " 张图，属性 "
                + Profiles.propertyCount(profile) + detail);
    }

    public static String urlOf(Object texture) {
        if (texture == null) {
            return null;
        }
        try {
            Object url = texture.getClass().getMethod("getUrl").invoke(texture);
            return url == null ? null : url.toString();
        } catch (Throwable t) {
            return null;
        }
    }
}
