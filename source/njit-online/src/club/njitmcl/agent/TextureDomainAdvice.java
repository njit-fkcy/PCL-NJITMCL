package club.njitmcl.agent;

import net.bytebuddy.asm.Advice;

/**
 * 贴图域名白名单 —— 两代 authlib 各有一道同名的闸：
 *
 *   authlib 4.x / 7.x（≤1.21.x）
 *     TextureUrlChecker#isAllowedTextureDomain(String)          ← 静态
 *       不通过就 LOGGER.error("Textures payload contains blocked domain") 并整包丢弃
 *
 *   authlib 10.x（26.x 起）
 *     MinecraftServicesDiscoveryService#isAllowedTextureDomain(String)   ← 实例
 *       不通过就**悄悄**让皮肤变成 null：
 *         registerTextures 收到 MinecraftProfileTextures[skin=null, signatureState=SIGNED]
 *
 * 后者是 2026-11-15 12:07 那一局真正的原因：签名 SIGNED、属性 2 条、URL 全能打开，
 * 唯独 skin=null，于是画默认 Steve。两个类都挂这一个 advice，放行与日志交给 TextureDomainGate。
 */
public final class TextureDomainAdvice {

    private TextureDomainAdvice() {
    }

    @Advice.OnMethodExit
    public static void exit(@Advice.Argument(0) String url,
                            @Advice.Return(readOnly = false) boolean allowed) {
        allowed = TextureDomainGate.allow(url, allowed);
    }
}
