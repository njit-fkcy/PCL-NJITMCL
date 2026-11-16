package club.njitmcl.agent;

import net.bytebuddy.asm.Advice;

/**
 * Property#isSignatureValid(PublicKey) 一律放行 —— 任意加载器都能跑。
 *
 * 1.18.2 的 getTextures(requireSecure=true) 会在方法体里直接调它，
 * 外置站的签名必然验不过，于是抛 InsecureTextureException，
 * 上层清属性、回落默认皮肤。
 *
 * 体内只碰 JDK 类型（理由见 BridgeLoginAdvice）。
 */
public final class BridgeSignatureAdvice {

    private BridgeSignatureAdvice() {
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
            Object value = bridge.getMethod("signatureValid").invoke(null);
            if (Boolean.TRUE.equals(value)) {
                valid = true;
            }
        } catch (Throwable t) {
            // 放行失败就维持原生结果
        }
    }
}
