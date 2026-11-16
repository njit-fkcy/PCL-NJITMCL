package club.njitmcl.agent;

/**
 * 运行时挂载器：把代理注入到一个已经在跑的 JVM。
 *
 * 这里用反射调 com.sun.tools.attach 而不是直接 import，原因很实际：
 * 直接 import 会让这个类的字节码绑定到构建机的 JDK 版本，
 * 而它要由「服务端自己那个 java」执行——1.12 整合包的服务端往往还是 Java 8。
 * 反射写法编译目标可以是 Java 8，任何带 jdk.attach 的 JVM 都能跑。
 *
 * 用法：java -cp njit-online-attach.jar club.njitmcl.agent.Attacher <pid> <agent.jar> [选项]
 */
public final class Attacher {

    private Attacher() {
    }

    public static void main(String[] args) {
        if (args.length < 2) {
            System.err.println("usage: Attacher <pid> <agent.jar> [options]");
            System.exit(2);
        }
        Object vm = null;
        try {
            Class<?> vmClass = Class.forName("com.sun.tools.attach.VirtualMachine");
            vm = vmClass.getMethod("attach", String.class).invoke(null, args[0]);
            String options = args.length > 2 ? args[2] : "";
            vmClass.getMethod("loadAgent", String.class, String.class).invoke(vm, args[1], options);
            System.out.println("OK");
        } catch (Throwable t) {
            Throwable cause = t.getCause() != null ? t.getCause() : t;
            System.err.println(cause.toString());
            System.exit(1);
        } finally {
            if (vm != null) {
                try {
                    vm.getClass().getMethod("detach").invoke(vm);
                } catch (Throwable ignored) {
                    // 分离失败不影响注入结果
                }
            }
        }
    }
}
