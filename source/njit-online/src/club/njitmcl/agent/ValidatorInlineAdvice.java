package club.njitmcl.agent;

import net.bytebuddy.asm.Advice;

/**
 * 公钥校验器的穷版：所有返回 boolean 的方法一律 true，**不引用代理里的任何类**。
 *
 * 与 AuthFlagInlineAdvice 同理 —— NeoForge 1.21.4 的游戏类加载器看不见
 * club.njitmcl.agent.*，富版（ValidatorTrueAdvice 会打日志、读 AgentState）
 * 一进去就 NoClassDefFoundError，把服务端打死。
 *
 * 双验证模式下把校验放行：外置登录站的公钥不在 Mojang 的公钥集里，
 * 原生实现必判 false，玩家进来就被踢「无效的站点签名」。
 */
public final class ValidatorInlineAdvice {

    private ValidatorInlineAdvice() {
    }

    @Advice.OnMethodExit
    public static void exit(@Advice.Return(readOnly = false) boolean returned) {
        if (!"4".equals(System.getProperty("njit.mode"))) {
            return;
        }
        returned = true;
    }
}
