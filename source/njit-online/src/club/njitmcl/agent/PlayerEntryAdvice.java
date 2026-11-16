package club.njitmcl.agent;

import net.bytebuddy.asm.Advice;

/**
 * ClientboundPlayerInfoUpdatePacket$Entry 的规范构造器：
 *
 *   Entry(UUID profileId, GameProfile profile, boolean listed, int latency,
 *         GameType gameMode, Component displayName, boolean showHat,
 *         int listOrder, RemoteChatSession$Data chatSession)
 *
 * 服务端组包时走一次，客户端解码时走一次（集成服务端在同一进程，两行都在一份日志里）。
 * 属性条数在这两侧对比，就能看出 textures 是哪一段丢的。
 */
public final class PlayerEntryAdvice {

    private PlayerEntryAdvice() {
    }

    @Advice.OnMethodEnter
    public static void enter(@Advice.Argument(1) Object profile) {
        PlayerEntryProbe.note(profile);
    }
}
