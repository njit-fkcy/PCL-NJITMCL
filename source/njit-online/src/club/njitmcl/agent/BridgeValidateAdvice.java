package club.njitmcl.agent;

import net.bytebuddy.asm.Advice;

/**
 * ServicesKeyInfo#validateProperty(Property) 一律放行 —— 任意加载器都能跑。
 *
 * 1.20.2 起的验签走这里，不经过 Property#isSignatureValid。
 * 外置站自签必然 false → getTextures(requireSecure=true) 抛 InsecureTextureException
 * → 上层清属性、回落默认皮肤，而且一点日志都不留。
 *
 * 体内只碰 JDK 类型（理由见 BridgeLoginAdvice）。
 */
public final class BridgeValidateAdvice {

    private BridgeValidateAdvice() {
    }

    @Advice.OnMethodExit
    public static void exit(@Advice.Return(readOnly = false) boolean valid) {
        if (valid) {
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
            Object value = bridge.getMethod("propertyValid").invoke(null);
            if (Boolean.TRUE.equals(value)) {
                valid = true;
            }
        } catch (Throwable t) {
            // 放行失败就维持原生结果
        }
    }
}
