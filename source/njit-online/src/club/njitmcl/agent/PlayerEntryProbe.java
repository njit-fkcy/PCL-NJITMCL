package club.njitmcl.agent;

import java.util.Collections;
import java.util.LinkedHashSet;
import java.util.Set;

/**
 * 服务器发出去 / 客户端收进来的那份玩家档案，到底带不带 textures。
 *
 * 26.x 的 PlayerInfo.getSkin() 只认自己手里那份 profile：
 *
 *   skinLookup == null ? (skinLookup = createSkinLookup(profile)) : skinLookup
 *   return skinLookup.get()
 *
 * 所以「客户端画默认皮肤」和「profile 里没有 textures」是一回事。
 * ClientboundPlayerInfoUpdatePacket$Entry 是这条链的载体，它的规范构造器在
 * 两侧都会走：服务端组包一次、客户端解码一次（集成服务端在同一个进程里，
 * 两条日志都落在同一个文件）。对比这两行的属性条数，就知道属性是哪一段丢的。
 */
public final class PlayerEntryProbe {

    private static final Set<String> SEEN = Collections.synchronizedSet(new LinkedHashSet<String>());

    private PlayerEntryProbe() {
    }

    /** public：供内联代码调用。 */
    public static void note(Object profile) {
        try {
            if (profile == null) {
                return;
            }
            String line = nameOf(profile) + " 属性 " + Profiles.propertyCount(profile)
                    + " 条" + Profiles.describe(profile);
            if (SEEN.contains(line) || SEEN.size() > 16) {
                return;
            }
            SEEN.add(line);
            Log.write("玩家信息包 Entry：" + line);
        } catch (Throwable t) {
            Log.write("玩家信息包 Entry 读取失败：" + t);
        }
    }

    private static String nameOf(Object profile) {
        try {
            Object name = profile.getClass().getMethod("getName").invoke(profile);
            if (name != null) {
                return String.valueOf(name);
            }
        } catch (Throwable ignored) {
            // authlib 新版可能叫 name()
        }
        try {
            Object name = profile.getClass().getMethod("name").invoke(profile);
            return String.valueOf(name);
        } catch (Throwable t) {
            return "名字读不到";
        }
    }
}
