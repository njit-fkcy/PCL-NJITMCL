package club.njitmcl.agent;

import net.bytebuddy.asm.Advice;

/**
 * 纹理域名白名单的「任意加载器都能跑」版本。
 *
 * 三处同名不同家，方法形状都是 (String) → boolean：
 *   authlib 1.5.x   YggdrasilMinecraftSessionService#isWhitelistedDomain
 *   authlib 4.x/7.x TextureUrlChecker#isAllowedTextureDomain
 *   authlib 10.x    MinecraftServicesDiscoveryService#isAllowedTextureDomain
 *
 * 体内只碰 JDK：反射进 Bridge.domain。理由同 BridgeLoginAdvice。
 */
public final class BridgeDomainAdvice {

    private BridgeDomainAdvice() {
    }

    @Advice.OnMethodExit
    public static void exit(@Advice.Argument(0) String url,
                            @Advice.Return(readOnly = false) boolean allowed) {
        if (allowed) {
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
            Object value = bridge.getMethod("domain", String.class).invoke(null, url);
            if (Boolean.TRUE.equals(value)) {
                allowed = true;
            }
        } catch (Throwable t) {
            // 放行失败就维持原生结果，不影响别的
        }
    }
}
