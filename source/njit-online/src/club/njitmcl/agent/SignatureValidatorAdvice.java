package club.njitmcl.agent;

import net.bytebuddy.asm.Advice;
import net.bytebuddy.implementation.bytecode.assign.Assigner;

/**
 * 让服务端的公钥签名校验直接放行。
 *
 * 26.x 的登录链路会拿 {@code SignatureValidator.from(ServicesKeySet, ServicesKeyType)}
 * 造一个校验器，再用它验客户端送上来的公钥签名。MUA 的公钥是 MUA 签的，
 * 用 Mojang 的 ServicesKeySet 必然验不过，日志里是：
 *
 *   Failed to verify Services signature
 *   lost connection: 无效的服务器公钥签名
 *
 * 这里把 from(...) 的返回值统一换成 {@code SignatureValidator.NO_VALIDATION}。
 * 取字段靠反射 + {@code @Advice.Origin}，因为代理编译期看不到 minecraft 的类；
 * 不引用 {@code AgentState}，因为目标类由 Forge 的 TransformingClassLoader 加载，
 * 看不到未命名模块里的代理类（引用会抛 NoClassDefFoundError，被 suppress 吞掉后表现为静默无效）。
 *
 * 这一层只管签名，不管身份：谁能进服仍然由 hasJoinedServer 决定。
 * 要恢复官方校验加 -Dnjit.chat.bypass=false。
 */
public final class SignatureValidatorAdvice {

    private SignatureValidatorAdvice() {
    }

    @Advice.OnMethodExit(suppress = Throwable.class)
    public static void exit(@Advice.Origin Class<?> origin,
                            @Advice.Return(readOnly = false, typing = Assigner.Typing.DYNAMIC) Object returned) {
        if ("false".equalsIgnoreCase(System.getProperty("njit.chat.bypass", "true").trim())) {
            return;
        }
        try {
            Object passthrough = origin.getField("NO_VALIDATION").get(null);
            if (passthrough != null) {
                returned = passthrough;
            }
        } catch (Throwable t) {
            // 这里不能调 Log：本方法体会被内联进 minecraft 的类，
            // 那个加载器看不到 club.njitmcl.agent.*，调用会抛 NoClassDefFoundError。
            System.err.println("[njit-agent] 替换签名校验器失败：" + t);
        }
    }
}
