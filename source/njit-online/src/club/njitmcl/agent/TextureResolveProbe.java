package club.njitmcl.agent;

import java.lang.reflect.Field;
import java.util.Collections;
import java.util.LinkedHashSet;
import java.util.Set;

/**
 * 屏幕上真正采样哪张图：AvatarRenderer#getTextureLocation 的返回值。
 *
 * 11:29 那次已经给出了答案的一半：
 *
 *   皮肤·渲染贴图 id=minecraft:textures/entity/player/slim/steve.png
 *                    state.skin=PlayerSkin[body=ResourceTexture[slim/steve], model=SLIM]
 *
 * 也就是说画的时候 state.skin 已经是**默认皮肤**了，跟下载、注册、纹理绑定都无关。
 * 剩下要查的是「为什么那一份 state.skin 是默认」——交给 SkinOwnerProbe / PlayerEntryProbe。
 *
 * 这里保留一行 id + skin 的记录，但不再去 TextureManager 里查（那一步会 NPE，
 * 而且对当前判断已经没有信息量）。
 */
public final class TextureResolveProbe {

    private static final Set<String> SEEN = Collections.synchronizedSet(new LinkedHashSet<String>());

    private TextureResolveProbe() {
    }

    /** 供内联代码调用。 */
    public static void note(Object state, Object id) {
        try {
            String line = String.valueOf(id) + "  ← " + readSkin(state);
            if (SEEN.contains(line) || SEEN.size() > 12) {
                return;
            }
            SEEN.add(line);
            Log.debug("皮肤·渲染贴图 " + line);
        } catch (Throwable t) {
            Log.debug("皮肤·渲染贴图 读取失败：" + t);
        }
    }

    private static String readSkin(Object state) {
        if (state == null) {
            return "state=null";
        }
        try {
            Field field = state.getClass().getField("skin");
            return String.valueOf(field.get(state));
        } catch (Throwable t) {
            return "读不到(" + t.getClass().getSimpleName() + ")";
        }
    }
}
