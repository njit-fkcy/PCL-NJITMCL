package club.njitmcl.agent;

import net.bytebuddy.asm.Advice;

/**
 * 注入 MinecraftServer#setSingleplayerProfile（1.20.1 里是 {@code b(GameProfile)}）。
 *
 * getSingleplayerProfile 不是唯一的入口：集成服务端在构造函数里就把档案塞进去了，
 * 有时候玩家对象直接引用那个字段。这条对应「写」的一侧，越早补越好——
 * authlib 4.x 的 PropertyMap 可以原地改，改的是同一个对象，
 * 后面无论谁读这个档案都能看到 textures。
 *
 * 只在返回值是 void、且唯一参数是 GameProfile 的方法上生效，
 * 同类的 {@code a(GameProfile)Z}、{@code c(GameProfile)I} 不会被打到。
 */
public final class HostProfileSetAdvice {

    private HostProfileSetAdvice() {
    }

    @Advice.OnMethodEnter(suppress = Throwable.class)
    public static void enter(@Advice.Argument(0) Object profile) {
        if (profile == null) {
            return;
        }
        // 4.x 原地改写即完成；10.x 会返回新对象，这条路径拿不到，
        // 但那边 getSingleplayerProfile 的读侧会补上
        HostSkin.attach(profile);
    }
}
