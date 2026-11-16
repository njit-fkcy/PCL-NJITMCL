package club.njitmcl.agent;

import net.bytebuddy.asm.Advice;
import net.bytebuddy.implementation.bytecode.assign.Assigner;

/**
 * hasJoinedServer 的「任意加载器都能跑」版本。
 *
 * 这里的硬规矩：**方法体内不许出现任何 club.njitmcl.agent.* 的符号**（连本类的静态方法都不行）——
 * 内联进去的代码跑在目标类的加载器里，一旦解析不到就 NoClassDefFoundError（崩）
 * 或被 catch 吞掉（静默无效）。所以体内只有 JDK 类型，加载器搜索也写成内联的一小段。
 *
 * 真正的逻辑在 Bridge 里，靠 Class.forName + 显式加载器摸过去。
 * 加载器按「系统 → 上下文 → 各自父链」挨个试：Fabric 的 Knot、
 * Forge 1.17-1.19 的 ModuleClassLoader，父链上总有一个能看见代理 jar。
 */
public final class BridgeLoginAdvice {

    private BridgeLoginAdvice() {
    }

    @Advice.OnMethodEnter(skipOn = Advice.OnNonDefaultValue.class)
    public static boolean enter(@Advice.This Object service,
                                @Advice.AllArguments Object[] args,
                                @Advice.Local("njitRet") Object result) {
        result = null;
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
                return false;
            }
            Object value = bridge.getMethod("hasJoined", Object.class, Object[].class)
                    .invoke(null, service, args);
            if (value == null) {
                return false;
            }
            result = value;
            return true;
        } catch (Throwable t) {
            // 任何异常都退回原生行为，绝不打断登录流程
            return false;
        }
    }

    @Advice.OnMethodExit
    public static void exit(@Advice.Local("njitRet") Object result,
                            @Advice.Return(readOnly = false, typing = Assigner.Typing.DYNAMIC) Object returned) {
        if (result != null) {
            returned = result;
        }
    }
}
