package club.njitmcl.agent;

import net.bytebuddy.asm.Advice;
import net.bytebuddy.implementation.bytecode.assign.Assigner;

/**
 * 注入 MinecraftServer#getSingleplayerProfile（1.20.1 里是 {@code N()}）。
 *
 * 这个方法返回的是「房主自己的档案」，早于任何 PlayerInfo 广播被调用，
 * 所以在这里补属性，其它客户端收到的就是带 textures 的那一份。
 *
 * 匹配靠**描述符**而不是方法名：混淆版本里名字是 N()、aX() 这种，
 * 但 {@code ()Lcom/mojang/authlib/GameProfile;} 是不变的。
 *
 * 返回值可能被换掉：authlib 10.x 的 GameProfile 是 record，只能重建。
 * {@code readOnly = false} + DYNAMIC 才能写回。
 */
public final class HostProfileAdvice {

    private HostProfileAdvice() {
    }

    @Advice.OnMethodExit(suppress = Throwable.class)
    public static void exit(@Advice.Return(readOnly = false, typing = Assigner.Typing.DYNAMIC) Object returned) {
        if (returned == null) {
            return;
        }
        // attach 自己兜异常；建议体被内联进别人的类，这里不能抛
        Object filled = HostSkin.attach(returned);
        if (filled != null && filled != returned) {
            returned = filled;
        }
    }
}
