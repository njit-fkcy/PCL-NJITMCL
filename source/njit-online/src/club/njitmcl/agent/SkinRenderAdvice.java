package club.njitmcl.agent;

import net.bytebuddy.asm.Advice;

/**
 * 渲染器拿到的 PlayerSkin：只记**第一次见到的不同值**，最多 8 条，
 * 并带上 entity 的 UUID —— 「默认皮肤」这件事必须钉到具体的人身上。
 *
 * 规则（血泪）：advice 内联进目标类，体内只能调 public 成员，私有字段一律不行。
 */
public final class SkinRenderAdvice {

    private SkinRenderAdvice() {
    }

    @Advice.OnMethodExit
    public static void exit(@Advice.This Object self, @Advice.Return final Object skin) {
        SkinOwnerProbe.note(self, skin);
    }
}
