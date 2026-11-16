package club.njitmcl.agent;

import java.lang.reflect.Method;
import java.util.Map;

import net.bytebuddy.asm.Advice;
import net.bytebuddy.implementation.bytecode.assign.Assigner;

/**
 * 皮肤链路的现场记录（正常返回那一侧）。
 *
 * 客户端把「别人的档案」变成一张贴图，只走两条路：
 *
 *   SkinManager#getInsecureSkinInformation(profile)  直接读 textures 属性解 base64，不验签
 *   sessionService#getTextures(profile, requireSecure)  走会话服务的验签路径
 *
 * 两条都不通时，SkinManager 会把 profile 的 properties **清空**、再去 Mojang
 * 问一次（外置账号必然 204），最后画默认皮肤——而且不留任何日志。
 *
 * 这个 advice 只做观测：谁来过、拿了几个属性、返回几张图；
 * 拿到图之后顺手把 URL 拉一次，把网络层的结果也打出来。
 * 抛异常那一侧在 {@link SkinDebugThrowAdvice}，两个类分开是因为
 * 同一个 advice 方法里同时读 @Return 和 @Thrown 会拿不到已赋值的局部变量。
 */
public final class SkinDebugAdvice {

    private SkinDebugAdvice() {
    }

    @Advice.OnMethodEnter(suppress = Throwable.class)
    public static void enter(@Advice.Argument(0) Object profile,
                             @Advice.Argument(1) boolean requireSecure,
                             @Advice.Origin("#m") String name) {
        Log.debug("皮肤·" + name + " 进入：" + SkinDebugAdvice.who(profile)
                + "，属性 " + Profiles.propertyCount(profile)
                + "，requireSecure=" + requireSecure);
    }

    @Advice.OnMethodExit(suppress = Throwable.class)
    public static void exit(@Advice.Argument(0) Object profile,
                            @Advice.Origin("#m") String name,
                            @Advice.Return(typing = Assigner.Typing.DYNAMIC) Object returned) {
        if (!(returned instanceof Map)) {
            Log.debug("皮肤·" + name + " 返回：" + who(profile) + " → "
                    + (returned == null ? "null" : returned.getClass().getName())
                    + "，属性 " + Profiles.propertyCount(profile));
            return;
        }
        Map<?, ?> textures = (Map<?, ?>) returned;
        Log.debug("皮肤·" + name + " 返回：" + who(profile) + " → " + textures.size() + " 张图，属性 "
                + Profiles.propertyCount(profile));
        for (Object value : textures.values()) {
            probe(urlOf(value));
        }
    }

    /** 把贴图 URL 拉一次，只记结果——数据层通不通，一眼就能看出来。 */
    public static void probe(final String url) {
        if (url == null || url.isEmpty()) {
            return;
        }
        Thread worker = new Thread(new Runnable() {
            @Override
            public void run() {
                java.net.HttpURLConnection connection = null;
                try {
                    connection = (java.net.HttpURLConnection) new java.net.URL(url).openConnection();
                    connection.setConnectTimeout(8000);
                    connection.setReadTimeout(8000);
                    connection.setRequestProperty("User-Agent", "Minecraft Java/1.20.1");
                    int code = connection.getResponseCode();
                    java.io.InputStream stream = code >= 400 ? connection.getErrorStream() : connection.getInputStream();
                    int length = 0;
                    if (stream != null) {
                        byte[] chunk = new byte[8192];
                        int read;
                        while ((read = stream.read(chunk)) > 0) {
                            length += read;
                        }
                        stream.close();
                    }
                    Log.debug("皮肤·贴图 " + code + "（" + length + " 字节） " + url);
                } catch (Throwable t) {
                    Log.debug("皮肤·贴图失败 " + t + " → " + url);
                } finally {
                    if (connection != null) {
                        connection.disconnect();
                    }
                }
            }
        }, "njit-skin-probe");
        worker.setDaemon(true);
        worker.start();
    }

    public static String urlOf(Object texture) {
        if (texture == null) {
            return null;
        }
        try {
            Method getUrl = texture.getClass().getMethod("getUrl");
            Object value = getUrl.invoke(texture);
            return value == null ? null : value.toString();
        } catch (Throwable t) {
            return null;
        }
    }

    /**
     * 必须 public：这个方法是**内联进别人类里**执行的（advice），
     * 包级私有在别的模块/加载器里调用会抛 IllegalAccessError。
     * 2026-11-15 08:23 就是这么翻车的：advice 抛异常，
     * SkinManager.get 整个方法跟着中断，皮肤反而更不显示。
     */
    public static String who(Object profile) {
        if (profile == null) {
            return "null";
        }
        String name = AuthAdvice.nameOf(profile);
        if (name != null) {
            return name;
        }
        try {
            Object id = profile.getClass().getMethod("getId").invoke(profile);
            return String.valueOf(id);
        } catch (Throwable t) {
            return profile.getClass().getName();
        }
    }
}
