package club.njitmcl.agent;

import net.bytebuddy.asm.Advice;

/**
 * 卡在 AvatarRenderer#getTextureLocation(AvatarRenderState) 的出口。
 *
 * 这个方法就是渲染器拿贴图 id 的唯一来源（字节码只有四行）：
 *
 *   aload_1
 *   getfield  AvatarRenderState.skin : PlayerSkin
 *   invokevirtual PlayerSkin.body() : ClientAsset$Texture
 *   invokeinterface ClientAsset$Texture.texturePath() : Identifier
 *   areturn
 *
 * 所以它的返回值 = 屏幕上真正采样那张图。配合 TextureResolveProbe 一起，
 * 一次就能分清「state.skin 是默认」还是「id 对了但没注册」。
 */
public final class AvatarTextureAdvice {

    private AvatarTextureAdvice() {
    }

    @Advice.OnMethodExit
    public static void exit(@Advice.Argument(0) Object state,
                            @Advice.Return Object id) {
        TextureResolveProbe.note(state, id);
    }
}
