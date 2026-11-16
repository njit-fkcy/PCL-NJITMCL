package club.njitmcl.agent;

import net.bytebuddy.asm.Advice;

/**
 * getTextures(GameProfile, boolean) 进门先补属性。
 *
 * authlib 1.5.x / 4.x / 7.x 都有这个方法，形状一致；10.x 换成
 * getPackedTextures(GameProfile)，两个名字都挂。
 *
 * 只观测不改返回值，异常一律吞在自己那层 —— 补不上皮肤是小事，不能影响取皮肤本身。
 */
public final class PeerSkinAdvice {

    private PeerSkinAdvice() {
    }

    @Advice.OnMethodEnter
    public static void enter(@Advice.Argument(0) Object profile) {
        PeerSkin.fill(profile);
    }
}
