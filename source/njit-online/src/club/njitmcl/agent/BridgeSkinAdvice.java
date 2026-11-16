package club.njitmcl.agent;

import net.bytebuddy.asm.Advice;

/**
 * 取贴图（getTextures / getPackedTextures）的两个动作：
 *
 *   enter —— 档案缺 textures 就先补上（Bridge.prepare → PeerSkin）
 *   exit  —— 记一行「属性几条 → 解出几张」，用来定位是哪一层断的
 *
 * 为什么要「先补」：1.18.2 的 AbstractClientPlayer 把皮肤贴图位置缓存在字段里，
 * 第一次算成默认值之后就不再重算。实测 14:59:03 客户端日志：
 *
 *   皮肤·取贴图 ImQiez_test：档案属性 0 条 → 0 张     ← 这一枪被缓存了
 *   皮肤·取贴图 ImQiez_test：档案属性 2 条 → 1 张     ← 后来的，用不上
 *
 * 体内只碰 JDK 类型 —— 理由见 BridgeLoginAdvice：内联代码一旦引用 club.*，
 * 在 Fabric / NeoForge / Forge 1.17-1.19 的加载器里就是 NoClassDefFoundError。
 */
public final class BridgeSkinAdvice {

    private BridgeSkinAdvice() {
    }

    @Advice.OnMethodEnter
    public static void enter(@Advice.Argument(0) Object profile) {
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
            bridge.getMethod("prepare", Object.class).invoke(null, profile);
        } catch (Throwable t) {
            // 补属性失败不影响原生流程
        }
    }

    @Advice.OnMethodExit
    public static void exit(@Advice.Argument(0) Object profile,
                            @Advice.Return final Object map) {
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
            bridge.getMethod("skin", Object.class, Object.class).invoke(null, profile, map);
        } catch (Throwable t) {
            // 观测失败绝不影响取皮肤
        }
    }
}
