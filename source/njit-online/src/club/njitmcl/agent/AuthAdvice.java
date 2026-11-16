package club.njitmcl.agent;

import java.lang.reflect.Method;

import net.bytebuddy.asm.Advice;
import net.bytebuddy.implementation.bytecode.assign.Assigner;

/**
 * 注入到 hasJoinedServer 的方法体。
 *
 * 为什么用 Advice 而不是 MethodDelegation：
 * 已加载的类只能重转换（retransform），此时 ByteBuddy 的类格式改动被禁用，
 * @SuperCall 需要重新定义类，用不了。Advice 只改方法体，是唯一可行路线。
 *
 * 各模式的行为：
 *   1 正版      → 进程没被外置注入时原样执行（零行为差异）；
 *                 被注入时由代理直接问 Mojang，因为原样执行问的是外置站，
 *                 正版账号在那儿必然查不到 —— 这就是「MUA 开服，正版进不去」。
 *   4 双验证    → 跳过原方法体，先问 Mojang，问不到再问 MUA（正版优先）。
 *
 * 双验证跳过原方法体：既不向 Mojang 发多余请求，也不受其超时拖累。
 */
public final class AuthAdvice {

    private AuthAdvice() {
    }

    @Advice.OnMethodEnter(skipOn = Advice.OnNonDefaultValue.class, suppress = Throwable.class)
    public static boolean enter(@Advice.Argument(0) Object first, @Advice.Origin Method method) {
        // 无论什么模式都记一次注入点，启动器不必等有人登录就能显示「接管了哪个版本」
        if (AgentState.target.isEmpty()) {
            AgentState.target = method.getDeclaringClass().getName() + "#" + method.getName();
            AgentState.returnType = method.getReturnType().getName();
            ControlServer.refresh();
        }
        boolean mojangOnly = AgentState.mode == AgentState.ModeMojang;
        boolean takeOver = !mojangOnly || (AgentState.externalSession && !AgentState.modeMojangNative);
        if (takeOver && mojangOnly && !AgentState.externalTakeoverLogged) {
            AgentState.externalTakeoverLogged = true;
            Log.write("正版档：本进程的会话服务器已被外置登录替换，改由代理直接问 Mojang；"
                    + "外置账号会在这档被拒（要两边都进请开双验证）");
        }
        return takeOver;
    }

    @Advice.OnMethodExit(suppress = Throwable.class)
    public static void exit(@Advice.Argument(0) Object first,
                            @Advice.Argument(1) String serverId,
                            @Advice.Origin Method method,
                            @Advice.Return(readOnly = false, typing = Assigner.Typing.DYNAMIC) Object returned) {
        // suppress 会把 advice 内部的异常一并吞掉，这里自己兜一层并留痕，否则排查只能靠猜
        try {
            if (AgentState.mode == AgentState.ModeMojang
                    && (!AgentState.externalSession || AgentState.modeMojangNative)) {
                return;
            }
            if (returned != null) {
                return;
            }
            String userName = nameOf(first);
            if (userName == null || userName.isEmpty()) {
                return;
            }
            Object profile = Resolve(userName, serverId, method.getDeclaringClass().getClassLoader());
            if (profile == null) {
                // 双验证两边都没给档案 —— 服务端接下来就会以「验证用户名失败！」拒绝。
                // 不记这一行的话，日志上完全看不出这里失败过。
                Log.write("双验证未取到档案：" + userName + "（serverId=" + serverId + "）");
                return;
            }
            Object value = Profiles.wrap(profile, method.getReturnType());
            if (value != null) {
                // 必须在本方法里写回：@Advice.Return 是值传递，交给 helper 改不会生效
                returned = value;
                // 登录刚通过，紧接着就是服务端的「玩家公钥」校验。
                // 那一步用的校验器（apj）可能因为类加载时机被漏掉，
                // 这里趁这一百来毫秒补一刀，把人留住。
                AgentMain.patchShapeTargets("登录时补");
            }
        } catch (Throwable t) {
            Log.write("exit 处理失败：" + t);
        }
    }

    /**
     * 从第一个参数里取用户名。
     *
     * authlib 4.x 及更早传的是 GameProfile（getName），7.x 起改成直接传 String，
     * 10.x 的 GameProfile 是 record（name）。三种都认，否则老版本会静默失效。
     *
     * 必须是 public：方法体会被内联进 authlib 的类里执行。
     */
    public static String nameOf(Object first) {
        if (first == null) {
            return null;
        }
        if (first instanceof String) {
            return (String) first;
        }
        for (String accessor : new String[]{"getName", "name"}) {
            try {
                Object value = first.getClass().getMethod(accessor).invoke(first);
                if (value != null) {
                    return value.toString();
                }
            } catch (Throwable ignored) {
                // 换下一个访问器
            }
        }
        return null;
    }

    /**
     * 双验证取资料：先问 Mojang，问不到再问 MUA。
     *
     * 必须是 public：Advice 的方法体是被**内联进目标类**执行的，
     * 从那里调用本类的 private 方法会抛 IllegalAccessError。
     *
     * 双验证必须「先问一个、失败再问另一个」，不能两个都先算好再挑——
     * 那样每次登录都要多付一次跨洋往返，登录界面会明显卡顿。
     * 顺序固定为正版优先：同名时 Mojang 身份胜出，外置账号顶不掉正版玩家。
     */
    public static Object Resolve(String userName, String serverId, ClassLoader loader) {
        Object profile = Sessions.verifyMojang(userName, serverId, loader);
        if (profile != null) {
            return profile;
        }
        if (AgentState.mode == AgentState.ModeMojang) {
            // 正版档只认 Mojang：查不到就交空，服务端照常拒绝。
            // 外置账号本来就该在这一档被挡住，这里记一行便于区分「被拒」与「网络不通」。
            Log.write("正版未取到档案：" + userName + "（serverId=" + serverId + "）");
            return null;
        }
        return Sessions.verifyExternal(userName, serverId, AgentState.muaApi, loader);
    }
}
