package club.njitmcl.agent;

import net.bytebuddy.asm.Advice;
import net.bytebuddy.implementation.bytecode.assign.Assigner;

/**
 * 注入 SessionService#getPackedTextures(GameProfile)。
 *
 * 1.20.2 之后客户端换了一条路：SkinManager 不再自己解 base64，而是向会话服务要
 * 「打包好的 textures 属性」：
 *
 *   SkinManager#get(profile)
 *     → services.sessionService().getPackedTextures(profile)   ← 就是这里
 *     → CacheKey(uuid, property) → 解码 → 下载贴图
 *
 * authlib 10 的实现只有四行：从 profile 的 properties 里取第一个 textures 返回，
 * 既不验签也不过域名白名单。所以 26.x 上「看不见皮肤」只有两种可能：
 * 属性根本没到客户端，或者属性到了但图下不下来。
 *
 * 这个 advice 只做观测：把「客户端手里那条属性长什么样」直接打出来。
 */
public final class PackedTexturesAdvice {

    private PackedTexturesAdvice() {
    }

    @Advice.OnMethodExit(suppress = Throwable.class)
    public static void exit(@Advice.Argument(0) Object profile,
                            @Advice.Return(typing = Assigner.Typing.DYNAMIC) Object returned) {
        Log.debug("皮肤·getPackedTextures " + SkinDebugAdvice.who(profile)
                + " → " + Profiles.describeProperty(returned)
                + "，profile 属性 " + Profiles.propertyCount(profile) + " 条");
    }
}
