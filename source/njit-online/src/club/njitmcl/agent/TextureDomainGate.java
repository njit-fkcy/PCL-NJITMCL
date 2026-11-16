package club.njitmcl.agent;

import java.util.Collections;
import java.util.LinkedHashSet;
import java.util.Set;

/**
 * 26.x 的贴图域名白名单，放行 + 只记一次。
 *
 * authlib 10 把「这张贴图能不能用」的判定搬到了这里：
 *
 *   MinecraftServicesSessionService.unpackTextures(Property)
 *     → discoveryService.isAllowedTextureDomain(url)
 *       → getValidUris(Service.PROFILES, "getTexture") 里逐条 startsWith
 *       → 不在 Mojang 自己的域名前缀下 → false
 *
 * 返回 false 的后果不是报错，而是**悄悄把这张图丢掉**：
 *
 *   皮肤·进入 registerTextures：[7d0c5190-…, MinecraftProfileTextures[skin=null, cape=null,
 *                                elytra=null, signatureState=SIGNED]]
 *
 * 签名是好的（SIGNED）、属性是好的（Url 就在里面），只有 url 被白名单判掉，
 * 于是 skin=null，最后回落默认皮肤 —— 屏幕上那个细胳膊 Steve 就这么来的。
 *
 * 1.20.1 那条链用的是 authlib 4 的 TextureUrlChecker（另一个类，已单独放过），
 * 所以两代版本的补丁位置不同，这里补的是 10.x 的新位置。
 */
public final class TextureDomainGate {

    private static final Set<String> SEEN = Collections.synchronizedSet(new LinkedHashSet<String>());

    private TextureDomainGate() {
    }

    /** public：供内联代码调用。返回最终是否放行。 */
    public static boolean allow(String url, boolean current) {
        try {
            // 不看校验模式：皮肤是显示层的事，谁能进服是校验层的事。
            // 房主用 MUA 开服、客人是正版账号时，正版那侧仍要把 MUA 玩家画出来 ——
            // 这里判模式就会静默退回默认皮肤（2026-11-16 早上复发的就是这一处）。
            if ("false".equalsIgnoreCase(System.getProperty("njit.texture.domain", "true").trim())) {
                return current;
            }
            if (SEEN.size() < 8 && SEEN.add(String.valueOf(url))) {
                Log.write("纹理域名判定：" + url + "（原生 " + current + " → 放行 " + true + "）");
            }
            return true;
        } catch (Throwable t) {
            return current;
        }
    }
}
