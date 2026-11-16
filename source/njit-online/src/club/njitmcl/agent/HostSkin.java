package club.njitmcl.agent;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * 房主（集成服务端那一位）自己的皮肤属性。
 *
 * 问题的形状：联机时每个人都看得见自己的皮肤，却都看不见别人的皮肤里的「房主那一半」。
 * 原因不在 hasJoinedServer——房主根本不走那条路。集成服务端的单机档案来自
 * {@code MinecraftServer#getSingleplayerProfile()}，它的来源是
 * {@code User#getGameProfile()}，而那个方法只塞了 UUID 和名字，
 * properties 是个空表。服务端把这份空档案广播进 PlayerInfo，别人只能画默认皮肤。
 *
 * 所以这里做的事很简单：启动时从命令行拿到自己的名字和 UUID，后台去会话服务器
 * 拉一次 profile（Mojang 优先，取不到再问 MUA），把 properties 缓存成原始 JSON；
 * 等 getSingleplayerProfile 被调用（世界加载时就会调用）再把属性挂上去。
 *
 * 为什么要预取：拉属性要过网络，不能卡在世界加载线程上。代理在 JVM 启动时就装好了，
 * 而世界加载要等主菜单点完，中间有足够时间。真赶上没拉完，最多等 2 秒，
 * 且只等一次——之后 fetchDone 落地，永远不会再等。
 *
 * 属性对象必须用**游戏那边**的类加载器构造：代理的类和 authlib 的 GameProfile
 * 分属两个加载器，直接 new 出来的 Property 传过去就是 ClassCastException。
 * 所以缓存的是 Json 解析出来的 Map，构造 Property 交给 Profiles.applyProperties 现做。
 */
public final class HostSkin {

    private static final int TIMEOUT_MS = 4000;
    private static final long WAIT_BUDGET_MS = 2000L;
    private static final long WAIT_STEP_MS = 50L;

    private static final AtomicBoolean STARTED = new AtomicBoolean(false);

    /** 自己的名字与 UUID（32 位无连字符），来自 sun.java.command。 */
    private static volatile String hostName = "";
    private static volatile String hostId = "";

    /** 会话服务器返回的 properties 数组（Json 结构：List&lt;Map&gt;），未构造 Property。 */
    private static volatile List<Object> properties;

    private static volatile boolean fetchDone;
    private static volatile boolean reported;

    private HostSkin() {
    }

    /** 由 AgentMain 在 boot 阶段调用一次；失败也只是没有皮肤，不影响任何其它功能。 */
    public static void init() {
        if (!STARTED.compareAndSet(false, true)) {
            return;
        }
        try {
            parseCommand(System.getProperty("sun.java.command", ""));
        } catch (Throwable t) {
            Log.write("解析启动参数失败：" + t);
            return;
        }
        if (hostName.isEmpty() || hostId.isEmpty()) {
            // 专用服务端、或者启动命令被魔改过：没有自报身份，本层不介入
            return;
        }
        Thread worker = new Thread(new Runnable() {
            @Override
            public void run() {
                fetch();
            }
        }, "njit-host-skin");
        worker.setDaemon(true);
        worker.start();
    }

    /** sun.java.command = 主类 + 全部参数，这里只挑 --username / --uuid。 */
    private static void parseCommand(String command) {
        if (command == null || command.isEmpty()) {
            return;
        }
        String[] parts = command.split("\\s+");
        String name = "";
        String id = "";
        for (int i = 0; i < parts.length - 1; i++) {
            if ("--username".equals(parts[i])) {
                name = parts[i + 1];
            } else if ("--uuid".equals(parts[i])) {
                id = parts[i + 1];
            }
        }
        if (name.isEmpty() || id.isEmpty()) {
            return;
        }
        hostName = name;
        hostId = id.replace("-", "").toLowerCase(Locale.ROOT);
    }

    /**
     * 拉一次 profile。Mojang 先问：正版档案自带签名好的 textures，
     * 直接可用；问不到再问 MUA，覆盖 MUA 登录的房主。
     */
    private static void fetch() {
        List<Object> found = null;
        try {
            String mojang = trim(System.getProperty("njit.mojang.sessionserver", Sessions.MOJANG_SESSION));
            found = request(mojang + "/session/minecraft/profile/" + hostId, "Mojang");
            if (found == null) {
                String external = trim(AgentState.muaApi);
                found = request(external + "/sessionserver/session/minecraft/profile/" + hostId, "外置");
            }
        } catch (Throwable t) {
            Log.write("房主皮肤查询失败：" + t);
        }
        properties = found;
        fetchDone = true;
        if (found != null) {
            // 落到共用缓存：同一台机器上的另一个进程（比如外置账号那台客户端）
            // 要查同一个 UUID 时直接读文件，不必再去问会被限流的 sessionserver
            SkinCache.write(hostId, found);
        }
        Log.write("房主档案属性：" + (found == null ? "未取到" : found.size() + " 条") + "（" + hostName + "）");
    }

    private static List<Object> request(String url, String label) {
        HttpURLConnection connection = null;
        try {
            // unsigned=false：第三方 Yggdrasil 只有带这个参数才返回带签名的 textures
            String full = url + "?unsigned=false";
            connection = (HttpURLConnection) new URL(full).openConnection();
            connection.setConnectTimeout(TIMEOUT_MS);
            connection.setReadTimeout(TIMEOUT_MS);
            connection.setRequestProperty("User-Agent", "NJITMCL-Agent/1");
            connection.setRequestProperty("Accept", "application/json");
            int code = connection.getResponseCode();
            if (code != 200) {
                // 204 / 404：这个 UUID 在该站不存在，属于正常结果，不打日志
                if (code != 204 && code != 404) {
                    Log.write(label + " profile 返回 " + code + "：" + hostName);
                }
                return null;
            }
            Object parsed = Json.parse(readAll(connection.getInputStream()));
            if (!(parsed instanceof Map)) {
                return null;
            }
            Object raw = ((Map<?, ?>) parsed).get("properties");
            if (!(raw instanceof List)) {
                return null;
            }
            List<?> list = (List<?>) raw;
            if (list.isEmpty()) {
                return null;
            }
            List<Object> copy = new ArrayList<Object>(list.size());
            for (Object item : list) {
                copy.add(item);
            }
            return copy;
        } catch (Throwable t) {
            Log.write(label + " profile 查询失败（" + hostName + "）：" + t);
            return null;
        } finally {
            if (connection != null) {
                connection.disconnect();
            }
        }
    }

    /**
     * 给一份还没有属性的单机档案挂上皮肤。
     *
     * 返回的可能是新对象：authlib 10.x 的 GameProfile 是 record，只能重建；
     * 调用方（Advice）必须把返回值写回去。
     */
    public static Object attach(Object profile) {
        if (profile == null || hostName.isEmpty()) {
            return profile;
        }
        try {
            if (Profiles.propertyCount(profile) > 0) {
                // 已经带了属性（正版档案自带，或上一轮补过），什么都不用做
                return profile;
            }
            List<Object> cached = awaitProperties();
            if (cached == null) {
                return profile;
            }
            String name = AuthAdvice.nameOf(profile);
            if (name == null || !name.equalsIgnoreCase(hostName)) {
                // 只有自己那份档案需要补；别人的档案走 hasJoinedServer，本来就有属性
                return profile;
            }
            Object filled = Profiles.applyProperties(profile, cached, profile.getClass().getClassLoader());
            if (filled == null) {
                return profile;
            }
            int count = Profiles.propertyCount(filled);
            if (count > 0 && !reported) {
                reported = true;
                Log.write("房主档案已补上皮肤属性：" + hostName + "（" + count + " 条"
                        + (filled == profile ? "" : "，record 型已重建") + "）");
            }
            return filled;
        } catch (Throwable t) {
            Log.write("房主档案补属性失败：" + t);
            return profile;
        }
    }

    /** 预取没赶上就等一会儿；只等一次，fetchDone 之后永不阻塞。 */
    private static List<Object> awaitProperties() {
        List<Object> hit = properties;
        if (hit != null || fetchDone) {
            return hit;
        }
        long deadline = System.currentTimeMillis() + WAIT_BUDGET_MS;
        while (System.currentTimeMillis() < deadline) {
            try {
                Thread.sleep(WAIT_STEP_MS);
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
                break;
            }
            hit = properties;
            if (hit != null || fetchDone) {
                return hit;
            }
        }
        return properties;
    }

    private static String trim(String value) {
        if (value == null) {
            return "";
        }
        String text = value.trim();
        while (text.endsWith("/")) {
            text = text.substring(0, text.length() - 1);
        }
        return text;
    }

    private static String readAll(InputStream stream) {
        try {
            ByteArrayOutputStream buffer = new ByteArrayOutputStream();
            byte[] chunk = new byte[4096];
            int read;
            while ((read = stream.read(chunk)) > 0) {
                buffer.write(chunk, 0, read);
            }
            return new String(buffer.toByteArray(), StandardCharsets.UTF_8);
        } catch (Throwable t) {
            return null;
        } finally {
            try {
                stream.close();
            } catch (Throwable ignored) {
                // 关闭失败不影响结果
            }
        }
    }
}
