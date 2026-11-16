package club.njitmcl.agent;

import java.util.Collections;
import java.util.LinkedHashSet;
import java.util.Set;

/**
 * 谁在画、画的是哪一份档案：把「默认皮肤」这件事钉到具体的 UUID 上。
 *
 * 11:29 的日志已经把矛盾摊平了：
 *
 *   皮肤·渲染取值 PlayerSkin[body=ResourceTexture[slim/steve], model=SLIM]        ← 默认
 *   皮肤·渲染贴图 id=minecraft:textures/entity/player/slim/steve.png              ← 画的是它
 *   皮肤·渲染取值 PlayerSkin[body=DownloadedTexture[minecraft:skins/2f4b…]]       ← 正确
 *
 * 同一个 getSkin()，同一个进程，两条不同的答案。而 AbstractClientPlayer.getSkin()
 * 的字节码只有两条路（Forge 的 client jar 里一模一样）：
 *
 *   PlayerInfo info = getPlayerInfo();
 *   return info == null ? DefaultPlayerSkin.get(getUUID()) : info.getSkin();
 *
 * 所以「默认 slim/steve」只有一种成因：那一刻 getPlayerInfo() 返回 null，
 * 也就是客户端手里没有这个 UUID 的 PlayerInfo —— 而这个 UUID 在默认皮肤里
 * 正好被判定成 slim/steve，和屏幕上那个一模一样。
 *
 * 这个 probe 就是把 UUID 和结果配在一起打出来，确认是不是真的走了 null 分支。
 */
public final class SkinOwnerProbe {

    private static final Set<String> SEEN = Collections.synchronizedSet(new LinkedHashSet<String>());

    private SkinOwnerProbe() {
    }

    /** public：供内联代码调用。 */
    public static void note(Object entity, Object skin) {
        try {
            String line = uuidOf(entity) + " → " + skin;
            if (SEEN.contains(line) || SEEN.size() > 12) {
                return;
            }
            SEEN.add(line);
            Log.debug("皮肤·档案归属 " + line);
        } catch (Throwable t) {
            Log.debug("皮肤·档案归属 读取失败：" + t);
        }
    }

    private static String uuidOf(Object entity) {
        if (entity == null) {
            return "entity=null";
        }
        try {
            Object uuid = entity.getClass().getMethod("getUUID").invoke(entity);
            return String.valueOf(uuid);
        } catch (Throwable t) {
            return "uuid读不到(" + t.getClass().getSimpleName() + ")";
        }
    }
}
