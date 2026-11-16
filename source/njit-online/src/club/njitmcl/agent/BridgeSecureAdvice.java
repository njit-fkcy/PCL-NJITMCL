package club.njitmcl.agent;

import net.bytebuddy.asm.Advice;

/**
 * PlayerSkin#secure() 一律返回 true —— 任意加载器都能跑。
 *
 * 26.x 的 SkinManager.createLookup 里有
 *   filter((secure, skin) -> !secure || skin.secure())
 * 外置站的皮肤 secure 恒为 false，于是在「看别人」这条路上被扔掉，
 * 回落默认皮肤。属性、URL、下载全对，只差这一个布尔。
 *
 * 体内只碰 JDK 类型（理由见 BridgeLoginAdvice）。
 */
public final class BridgeSecureAdvice {

    private BridgeSecureAdvice() {
    }

    @Advice.OnMethodExit
    public static void exit(@Advice.Return(readOnly = false) boolean secure) {
        if (secure) {
            return;
        }
        try {
            Class<?> bridge = null;
            ClassLoader[] roots = new ClassLoader[]{
                    ClassLoader.getSystemClassLoader(),
                    Thread.currentThread().getContextClassLoader()};
            for (int i = 0; i < roots.length && bridge == null; i++) {
                for (ClassLoader cur = roots[i]; cur != null && bridge == null; cur = cur.getParent()) {
                    try {
                        bridge = Class.forName("club.njitmcl.agent.Bridge", true, cur);
                    } catch (Throwable ignored) {
                        // 换下一个
                    }
                }
            }
            if (bridge == null) {
                return;
            }
            Object value = bridge.getMethod("secureFlag").invoke(null);
            if (Boolean.TRUE.equals(value)) {
                secure = true;
            }
        } catch (Throwable t) {
            // 放行失败就维持原生结果
        }
    }
}
