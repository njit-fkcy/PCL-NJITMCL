package club.njitmcl.agent;

import net.bytebuddy.jar.asm.ClassReader;
import net.bytebuddy.jar.asm.ClassVisitor;
import net.bytebuddy.jar.asm.ClassWriter;
import net.bytebuddy.jar.asm.MethodVisitor;
import net.bytebuddy.jar.asm.Opcodes;

import java.lang.instrument.ClassFileTransformer;
import java.security.ProtectionDomain;
import java.util.HashSet;
import java.util.Set;

/**
 * 不依赖任何名字的补丁层：给混淆版本的登录门闸上锁。
 *
 * 26.x 不混淆，直接用 {@code named("usesAuthentication")} 就能改；
 * 但 1.14 — 1.21 的原版 jar 里方法名是 {@code U()}、{@code O()} 这种，
 * 名字匹配全部落空，「正版档」于是等于免验证。
 *
 * 这里改用**字符串常量**定位——字符串不会被混淆：
 *
 *   含 "Invalid characters in username"            → 登录监听类的 handleHello
 *   含 "Failed to verify username but will let..."  → 认证线程的 run()
 *
 * 只改**含这两个字符串的那个方法**，同类的其它方法（例如取地址时的
 * {@code preventProxyConnections}）一律不碰。
 *
 * 改法是把类里对 {@code MinecraftServer.()Z} 的调用压成常量：
 *
 *   登录监听：{@code invokevirtual U()Z} → {@code pop; iconst_1; nop}   强制走校验
 *   认证线程：{@code invokevirtual O()Z} → {@code pop; iconst_0; nop}   验不过就拒绝
 *
 * 为什么这么换是安全的：{@code INVOKEVIRTUAL} 与 {@code POP;ICONST_x;NOP}
 * 都是 3 字节，栈净变化也一致（吃掉一个引用、压回一个 int），
 * 所以指令偏移、栈映射帧、max_stack 都不用重算，类长度不变。
 *
 * 任何异常都吞掉并返回原始字节——补丁失败退回原版行为，不能把游戏带崩。
 */
public final class ShapePatcher implements ClassFileTransformer {

    private static final String MARK_LISTENER = "Invalid characters in username";
    private static final String MARK_ALLOW = "Failed to verify username but will let them in anyway!";
    private static final String MARK_SIGNATURE = "Failed to verify Services signature";
    private static final String SERVER_SUFFIX = "/MinecraftServer";
    private static final int ASM = Opcodes.ASM9;

    @Override
    public byte[] transform(ClassLoader loader, String className, Class<?> classBeingRedefined,
                            ProtectionDomain protectionDomain, byte[] classfileBuffer) {
        if (classfileBuffer == null) {
            return null;
        }
        if ("false".equalsIgnoreCase(System.getProperty("njit.enforce", "true").trim())) {
            // 总开关关掉时整层不介入，行为回到原版
            return null;
        }
        boolean listener;
        boolean authenticator;
        boolean signature;
        try {
            listener = contains(classfileBuffer, MARK_LISTENER);
            authenticator = contains(classfileBuffer, MARK_ALLOW);
            signature = contains(classfileBuffer, MARK_SIGNATURE);
        } catch (Throwable t) {
            return null;
        }
        if (!listener && !authenticator && !signature) {
            return null;
        }
        try {
            if (signature) {
                if ("false".equalsIgnoreCase(System.getProperty("njit.chat.bypass", "true").trim())) {
                    return null;
                }
                return patchSignature(className, classfileBuffer);
            }
            return patch(className, classfileBuffer, listener, authenticator);
        } catch (Throwable t) {
            Log.write("字节码特征补丁失败（" + className + "）：" + t);
            return null;
        }
    }

    /**
     * 公钥签名校验（1.20.1 里是 apj，26.x 里是 SignatureValidator）。
     *
     * MUA 的公钥是 MUA 签的，用 Mojang 的 ServicesKeySet 必验不过，
     * 表现为进服一秒钟就被踢「无效的服务器公钥签名」。
     *
     * 这个类里所有返回 boolean 的方法都是验签本身（包括 lambda 生成的那几个），
     * 统一改成 `return true`，等价于官方行为里的 NO_VALIDATION。
     * 只动返回值，不动任何分支结构——方法体整体替换成两条指令，不产生新跳转。
     */
    private static byte[] patchSignature(String className, byte[] bytes) {
        ClassReader reader = new ClassReader(bytes);
        ClassWriter writer = new ClassWriter(reader, 0);
        final int[] patched = {0};
        reader.accept(new ClassVisitor(ASM, writer) {
            @Override
            public MethodVisitor visitMethod(int access, String name, String descriptor,
                                             String signature, String[] exceptions) {
                MethodVisitor inner = super.visitMethod(access, name, descriptor, signature, exceptions);
                boolean hasBody = (access & (Opcodes.ACC_ABSTRACT | Opcodes.ACC_NATIVE)) == 0;
                if (!hasBody || !descriptor.endsWith(")Z")) {
                    return inner;
                }
                patched[0]++;
                return new ConstantBody(inner, Opcodes.ICONST_1, Opcodes.IRETURN);
            }
        }, 0);
        if (patched[0] == 0) {
            return null;
        }
        Log.write("签名校验已放行：" + className + "（" + patched[0] + " 个方法）");
        return writer.toByteArray();
    }

    /** 把方法体整体替换成「压一个常量再返回」，其余指令全部丢弃。 */
    private static final class ConstantBody extends MethodVisitor {
        private final int constant;
        private final int returnOpcode;

        ConstantBody(MethodVisitor inner, int constant, int returnOpcode) {
            super(ASM, inner);
            this.constant = constant;
            this.returnOpcode = returnOpcode;
        }

        @Override
        public void visitCode() {
            super.visitCode();
            super.visitInsn(constant);
            super.visitInsn(returnOpcode);
        }

        @Override
        public void visitInsn(int opcode) {
        }

        @Override
        public void visitIntInsn(int opcode, int operand) {
        }

        @Override
        public void visitVarInsn(int opcode, int varIndex) {
        }

        @Override
        public void visitTypeInsn(int opcode, String type) {
        }

        @Override
        public void visitFieldInsn(int opcode, String owner, String name, String descriptor) {
        }

        @Override
        public void visitMethodInsn(int opcode, String owner, String name, String descriptor, boolean itf) {
        }

        @Override
        public void visitInvokeDynamicInsn(String name, String descriptor,
                                           net.bytebuddy.jar.asm.Handle handle,
                                           Object... args) {
        }

        @Override
        public void visitJumpInsn(int opcode, net.bytebuddy.jar.asm.Label label) {
        }

        @Override
        public void visitLdcInsn(Object value) {
        }

        @Override
        public void visitIincInsn(int varIndex, int increment) {
        }

        @Override
        public void visitTableSwitchInsn(int min, int max, net.bytebuddy.jar.asm.Label dflt,
                                         net.bytebuddy.jar.asm.Label... labels) {
        }

        @Override
        public void visitLookupSwitchInsn(net.bytebuddy.jar.asm.Label dflt, int[] keys,
                                          net.bytebuddy.jar.asm.Label[] labels) {
        }

        @Override
        public void visitMultiANewArrayInsn(String descriptor, int numDimensions) {
        }

        @Override
        public void visitFrame(int type, int numLocal, Object[] local, int numStack, Object[] stack) {
        }

        @Override
        public void visitLabel(net.bytebuddy.jar.asm.Label label) {
        }

        @Override
        public void visitLineNumber(int line, net.bytebuddy.jar.asm.Label start) {
        }

        @Override
        public void visitTryCatchBlock(net.bytebuddy.jar.asm.Label start,
                                       net.bytebuddy.jar.asm.Label end,
                                       net.bytebuddy.jar.asm.Label handler, String type) {
        }

        @Override
        public void visitLocalVariable(String name, String descriptor, String signature,
                                       net.bytebuddy.jar.asm.Label start,
                                       net.bytebuddy.jar.asm.Label end, int index) {
        }
    }

    private static byte[] patch(String className, byte[] bytes, boolean listener, boolean authenticator) {
        ClassReader reader = new ClassReader(bytes);

        // 第一遍：找出「方法里带了哪个标记字符串」，只改这些方法里的调用
        final Set<String> allowMethods = new HashSet<String>();
        final Set<String> listenerMethods = new HashSet<String>();
        reader.accept(new ClassVisitor(ASM) {
            @Override
            public MethodVisitor visitMethod(int access, String name, String descriptor,
                                             String signature, String[] exceptions) {
                final String key = name + descriptor;
                return new MethodVisitor(ASM) {
                    @Override
                    public void visitLdcInsn(Object value) {
                        if (value instanceof String) {
                            if (MARK_ALLOW.equals(value)) {
                                allowMethods.add(key);
                            }
                            if (MARK_LISTENER.equals(value)) {
                                listenerMethods.add(key);
                            }
                        }
                    }
                };
            }
        }, ClassReader.SKIP_DEBUG | ClassReader.SKIP_FRAMES);

        if (allowMethods.isEmpty() && listenerMethods.isEmpty()) {
            return null;
        }

        // 第二遍：把命中方法里的 MinecraftServer.()Z 调用压成常量
        ClassWriter writer = new ClassWriter(reader, 0);
        final boolean[] touched = {false};
        reader.accept(new ClassVisitor(ASM, writer) {
            @Override
            public MethodVisitor visitMethod(int access, String name, String descriptor,
                                             String signature, String[] exceptions) {
                final String key = name + descriptor;
                final boolean reject = allowMethods.contains(key);
                final boolean force = listenerMethods.contains(key);
                MethodVisitor inner = super.visitMethod(access, name, descriptor, signature, exceptions);
                if (!reject && !force) {
                    return inner;
                }
                return new MethodVisitor(ASM, inner) {
                    @Override
                    public void visitMethodInsn(int opcode, String owner, String methodName,
                                                String methodDescriptor, boolean isInterface) {
                        boolean target = opcode == Opcodes.INVOKEVIRTUAL
                                && owner != null
                                && owner.endsWith(SERVER_SUFFIX)
                                && "()Z".equals(methodDescriptor);
                        if (!target) {
                            super.visitMethodInsn(opcode, owner, methodName, methodDescriptor, isInterface);
                            return;
                        }
                        super.visitInsn(Opcodes.POP);
                        super.visitInsn(reject ? Opcodes.ICONST_0 : Opcodes.ICONST_1);
                        super.visitInsn(Opcodes.NOP);
                        touched[0] = true;
                    }
                };
            }
        }, 0);

        if (!touched[0]) {
            return null;
        }
        Log.write("特征补丁已生效：" + className
                + (authenticator ? "（认证线程强制拒绝）" : "（登录强制校验）"));
        return writer.toByteArray();
    }

    private static boolean contains(byte[] haystack, String needle) {
        byte[] pattern = needle.getBytes(java.nio.charset.StandardCharsets.UTF_8);
        outer:
        for (int i = 0; i <= haystack.length - pattern.length; i++) {
            for (int j = 0; j < pattern.length; j++) {
                if (haystack[i + j] != pattern[j]) {
                    continue outer;
                }
            }
            return true;
        }
        return false;
    }
}
