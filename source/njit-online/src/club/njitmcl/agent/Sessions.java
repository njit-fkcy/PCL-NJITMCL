package club.njitmcl.agent;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.net.URLEncoder;
import java.nio.charset.StandardCharsets;
import java.util.Locale;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;

/**
 * 向会话服务器确认「这个用户名刚加入过这台服务器」。
 *
 * Mojang 与各外置登录站（MUA / LittleSkin / Blessing Skin）用的是同一套协议，
 * 只有 URL 前缀不同：
 *
 *   Mojang  https://sessionserver.mojang.com/session/minecraft/hasJoined
 *   外置    {apiRoot}/sessionserver/session/minecraft/hasJoined
 *
 * 双验证模式就是按顺序问这两处，谁先给出资料就用谁。
 * MUA 回退成功后会再按名字查一次 Mojang：有同名正版账号就把 UUID 对齐过去，
 * 让同一个人无论从哪边登录都落到同一个 UUID（`-Dnjit.canonical=false` 可关）。
 * 返回结构也一致：{id, name, properties[{name, value, signature}]}。
 */
public final class Sessions {

    /**
     * 拼接常量 —— **URL 一律不要写成字面量**。
     *
     * authlib-injector 的 Constant URL Transformer 会改写类里的 URL 字符串常量，
     * 连我们自己的类它都改：
     *
     *   Transformed [club.njitmcl.agent.Sessions] with [Constant URL Transformer]
     *   Transformed [club.njitmcl.agent.HostSkin]  with [Constant URL Transformer]
     *
     * 改写后 "https://sessionserver.mojang.com" 会变成
     * "http://127.0.0.1:52810/https/sessionserver.mojang.com"，那条路通到外置站：
     * hasJoined 永远 204、响应里没有 x-minecraft-request-id。
     * 2026-11-16 07:45 的日志正是这样，白查了一整晚。
     *
     * 逐段拼出来的字符串不是编译期常量，注入器认不出来；同时因为字段不再是常量表达式，
     * javac 也不会把值内联进 HostSkin / PeerSkin。
     */
    private static String assemble(String... parts) {
        StringBuilder text = new StringBuilder();
        for (String part : parts) {
            text.append(part);
        }
        return text.toString();
    }

    /** MUA 的 Yggdrasil 根地址，与启动器 ModNJIT.MuaAuthServer 保持一致。 */
    public static final String MUA_API =
            assemble("https", "://", "skin", ".", "mualliance", ".", "ltd", "/api/yggdrasil");

    /** Mojang 会话服务器根地址。 */
    public static final String MOJANG_SESSION =
            assemble("https", "://", "sessionserver", ".", "mojang", ".", "com");

    /** Mojang 名称 → UUID 查询接口。用于把 MUA 身份对齐到正版身份。 */
    public static final String MOJANG_API =
            assemble("https", "://", "api", ".", "mojang", ".", "com");

    private static final int TIMEOUT_MS = 5000;
    private static final long UUID_TTL_MS = 5 * 60 * 1000L;
    private static final long UUID_MISS_TTL_MS = 60 * 1000L;

    /** 名字 → Mojang UUID 的短缓存；值可以是 null（表示这个 ID 在 Mojang 不存在）。 */
    private static final Map<String, CachedUuid> UUID_CACHE = new ConcurrentHashMap<String, CachedUuid>();

    private static final class CachedUuid {
        final String uuid;
        final long at;

        CachedUuid(String uuid, long at) {
            this.uuid = uuid;
            this.at = at;
        }
    }

    private Sessions() {
    }

    /** 问 MUA（或任意外置登录站）。 */
    public static Object verifyExternal(String userName, String serverId, String apiRoot, ClassLoader loader) {
        String base = (apiRoot == null || apiRoot.trim().isEmpty()) ? MUA_API : apiRoot.trim();
        while (base.endsWith("/")) {
            base = base.substring(0, base.length() - 1);
        }
        return verifyAt(base + "/sessionserver/session/minecraft/hasJoined", userName, serverId, loader, "外置", true);
    }

    /** 问 Mojang 官方会话服务器。 */
    public static Object verifyMojang(String userName, String serverId, ClassLoader loader) {
        String base = mojangBase();
        return verifyAt(base + "/session/minecraft/hasJoined", userName, serverId, loader, "Mojang", false);
    }

    /**
     * Mojang 会话服务器地址 —— **一定是真的 Mojang**。
     *
     * 外置登录注入器（authlib-injector）会把进程里的会话地址改写成它自己的本地代理，
     * 形如 http://127.0.0.1:52810/https/sessionserver.mojang.com/…。
     * 那条路会把 hasJoined 送到外置站：外置站不认识微软的会话，只能回 204，
     * 而且响应里没有 x-minecraft-request-id —— 2026-11-16 07:45 的日志就是这样：
     *
     *   请求 http://127.0.0.1:52810/https/sessionserver.mojang.com/session/…/hasJoined?…
     *   reqid=（缺，可能不是 Mojang 回的）
     *
     * 所以这里把回环地址的配置一律丢掉，直接用常量。真要换镜像，给个真域名。
     */
    private static String mojangBase() {
        String configured = System.getProperty("njit.mojang.sessionserver", "").trim();
        String base = MOJANG_SESSION;
        if (!configured.isEmpty()
                && !configured.contains("127.0.0.1")
                && !configured.contains("localhost")
                && !configured.contains("[::1]")) {
            base = configured;
            Log.write("Mojang 会话地址按配置改成：" + base);
        } else if (!configured.isEmpty()) {
            Log.write("忽略回环的会话地址配置（外置登录注入器的代理）：" + configured
                    + "，仍用 " + MOJANG_SESSION);
        }
        while (base.endsWith("/")) {
            base = base.substring(0, base.length() - 1);
        }
        return base;
    }

    /**
     * 发一次 hasJoined 并构造 GameProfile。成功返回 profile，否则 null。
     * 调用发生在登录线程上，与官方实现一样是同步的，所以超时要短。
     */
    /**
     * 本 JVM 的 HTTP 出口情况。
     *
     * 外置登录注入器会在进程里装一个本地代理（日志里见过
     * http://127.0.0.1:57022/https/sessionserver.mojang.com/... 这种地址），
     * 一旦它改的是全局 ProxySelector，我们这条「直接问 Mojang」的请求也会被它转走 ——
     * 那时日志上写着问 Mojang，实际问的可能是外置站，结果永远是 204。
     */
    public static String ProxyNote() {
        String host = System.getProperty("https.proxyHost", System.getProperty("http.proxyHost", ""));
        String port = System.getProperty("https.proxyPort", System.getProperty("http.proxyPort", ""));
        String systemProxies = System.getProperty("java.net.useSystemProxies", "false");
        String selector = "";
        try {
            java.net.ProxySelector current = java.net.ProxySelector.getDefault();
            selector = current == null ? "null" : current.getClass().getName();
        } catch (Throwable t) {
            selector = "读取失败：" + t;
        }
        return "proxyHost=" + (host.isEmpty() ? "无" : host + ":" + port)
                + "，useSystemProxies=" + systemProxies
                + "，ProxySelector=" + selector;
    }

    private static Object verifyAt(String endpoint, String userName, String serverId,
                                   ClassLoader loader, String label, boolean canonicalize) {
        if (userName == null || userName.isEmpty() || serverId == null || serverId.isEmpty()) {
            return null;
        }
        HttpURLConnection connection = null;
        try {
            String url = endpoint + "?username=" + URLEncoder.encode(userName, "UTF-8")
                    + "&serverId=" + URLEncoder.encode(serverId, "UTF-8")
                    // unsigned=false 决定皮肤能不能显示：外置登录站（MUA / Blessing Skin）
                    // 不带这个参数时返回的 textures **没有签名**，而客户端取皮肤走的是
                    // MinecraftSessionService#getTextures(profile, requireSecure=true)，
                    // 遇到无签名属性会抛 InsecurePublicKeyException，SkinManager 把它吞掉、
                    // 清空 properties、再去 Mojang 问一次（外置账号必然 204），最后画默认皮肤。
                    // 带上参数才会用站点的私钥签一遍，签名校验那一层（PropertyAdvice）才接得住。
                    // Mojang 官方同样接受这个参数，无行为差异。
                    + "&unsigned=false";
            // 请求重试策略：
            //  · 502/503/504 —— CDN 抖动，重试两轮
            //  · 204（仅 Mojang 那一路）—— 客户端明明刚 join 成功、我们立刻来查却查不到，
            //    那多半是 Mojang 那侧还没同步（23:53:59 客户端「join 成功」、
            //    房主同一秒 204，serverId 一模一样）。204 当「还没同步」再查两轮，
            //    三轮都是 204 才算真没登记。
            //    外置站那一路不重试：正版账号问 MUA 得到 204 是正常答案，重试只会拖慢。
            boolean retryOnMissing = !canonicalize;
            int code = -1;
            for (int attempt = 1; attempt <= 3; attempt++) {
                connection = (HttpURLConnection) new URL(url).openConnection();
                connection.setConnectTimeout(TIMEOUT_MS);
                connection.setReadTimeout(TIMEOUT_MS);
                connection.setRequestProperty("User-Agent", "NJITMCL-Agent/1");
                connection.setRequestProperty("Accept", "application/json");
                code = connection.getResponseCode();
                boolean serverError = code == 502 || code == 503 || code == 504;
                boolean unsynced = code == 204 && retryOnMissing;
                if ((!serverError && !unsynced) || attempt == 3) {
                    break;
                }
                Log.write(label + " hasJoined " + (unsynced ? "204（还没同步）" : ("返回 " + code))
                        + "（第 " + attempt + " 次），400ms 后复查");
                connection.disconnect();
                connection = null;
                Thread.sleep(400);
            }
            if (code == 204) {
                // 三轮都是 204：这个账号确实没有登记过这次加入。
                // Mojang 那侧 204 一般是外置账号（本来就不在），但**正版档**下出现 204
                // 只可能是客户端那一步 join 没成（CDN 503、断网、token 失效），
                // 必须记下来 —— 不然日志上只剩「验证用户名失败」，看不出是谁的问题。
                if (canonicalize || AgentState.mode == AgentState.ModeMojang) {
                    Log.write(label + " hasJoined 204（这条会话没登记）：" + userName
                            + "，serverId=" + serverId
                            + "，reqid=" + requestId(connection)
                            + "，出口 " + ProxyNote()
                            + "，请求 " + url);
                }
                return null;
            }
            if (code != 200) {
                if (code == 429) {
                    // 429 是 Mojang 按出口 IP 限流：同一台机器反复重连、多个客户端
                    // 同时开，几次之内就会被挡住。挡住的是「客户端那一步 join」，
                    // 服务端再问什么都只能拿到 204，看起来就是「验证用户名失败」。
                    Log.write(label + " hasJoined 被限流（429）：" + userName
                            + "，这台机器的出口 IP 请求太密，等几分钟再试");
                } else {
                    Log.write(label + " hasJoined 返回 " + code + "：" + userName);
                }
                return null;
            }
            String body = readAll(connection.getInputStream());
            if (body == null || body.trim().isEmpty()) {
                return null;
            }
            Object parsed = Json.parse(body);
            if (!(parsed instanceof Map)) {
                Log.write(label + " hasJoined 响应无法解析：" + body.substring(0, Math.min(120, body.length())));
                return null;
            }
            Map<?, ?> root = (Map<?, ?>) parsed;
            String name = root.get("name") == null ? userName : root.get("name").toString();
            String idHex = root.get("id") == null ? null : root.get("id").toString();
            if (canonicalize) {
                String mojangId = mojangUuid(name);
                if (mojangId != null) {
                    String externalId = idHex == null ? "" : idHex.replace("-", "");
                    if (!mojangId.equalsIgnoreCase(externalId)) {
                        Log.write("身份归一：" + name + " 的" + label + " UUID " + externalId
                                + " → Mojang UUID " + mojangId);
                    }
                    idHex = mojangId;
                }
            }
            Object profile = Profiles.makeWithId(name, idHex, loader);
            if (profile == null) {
                return null;
            }
            // authlib 10.x 的 GameProfile 是 record，属性只能通过新对象带进去，
            // 所以这里必须用返回值覆盖原引用。
            profile = Profiles.applyProperties(profile, root.get("properties"), loader);
            Log.write(label + " 验证通过：" + name + "（" + idHex + "），属性 "
                    + Profiles.propertyCount(profile) + " 条" + Profiles.describe(profile));
            return profile;
        } catch (Throwable t) {
            Log.write(label + " 验证失败（" + userName + "）：" + t);
            return null;
        } finally {
            if (connection != null) {
                connection.disconnect();
            }
        }
    }

    /**
     * 查这个名字在 Mojang 的 UUID。没有、查询失败或关闭归一化时返回 null。
     *
     * 归一化的目的：同一个人用 MUA 登录和用正版登录时，落到同一个 UUID，
     * 存档、白名单、OP 不会分裂成两套。默认开启，-Dnjit.canonical=false 可关。
     */
    public static String mojangUuid(String name) {
        if (name == null || name.trim().isEmpty()) {
            return null;
        }
        if ("false".equalsIgnoreCase(System.getProperty("njit.canonical", "true").trim())) {
            return null;
        }
        String key = name.toLowerCase(Locale.ROOT);
        long now = System.currentTimeMillis();
        CachedUuid hit = UUID_CACHE.get(key);
        if (hit != null && now - hit.at < (hit.uuid == null ? UUID_MISS_TTL_MS : UUID_TTL_MS)) {
            return hit.uuid;
        }
        String found = fetchMojangUuid(name);
        UUID_CACHE.put(key, new CachedUuid(found, now));
        return found;
    }

    private static String fetchMojangUuid(String name) {
        HttpURLConnection connection = null;
        try {
            String base = System.getProperty("njit.mojang.api", MOJANG_API).trim();
            while (base.endsWith("/")) {
                base = base.substring(0, base.length() - 1);
            }
            String url = base + "/users/profiles/minecraft/" + URLEncoder.encode(name, "UTF-8");
            connection = (HttpURLConnection) new URL(url).openConnection();
            connection.setConnectTimeout(3000);
            connection.setReadTimeout(3000);
            connection.setRequestProperty("User-Agent", "NJITMCL-Agent/1");
            int code = connection.getResponseCode();
            if (code == 204 || code == 404) {
                // 这个名字在 Mojang 不存在，保持外置 UUID
                return null;
            }
            if (code != 200) {
                Log.write("Mojang 名称查询返回 " + code + "：" + name);
                return null;
            }
            Object parsed = Json.parse(readAll(connection.getInputStream()));
            if (parsed instanceof Map) {
                Object id = ((Map<?, ?>) parsed).get("id");
                if (id != null) {
                    String hex = id.toString().replace("-", "").trim();
                    if (hex.length() == 32) {
                        return hex;
                    }
                }
            }
            return null;
        } catch (Throwable t) {
            Log.write("Mojang 名称查询失败（" + name + "）：" + t);
            return null;
        } finally {
            if (connection != null) {
                connection.disconnect();
            }
        }
    }

    /** 响应头里的请求号。真 Mojang 一定带；没有就说明这条响应不是它回的。 */
    private static String requestId(HttpURLConnection connection) {
        try {
            if (connection == null) {
                return "无连接";
            }
            String id = connection.getHeaderField("x-minecraft-request-id");
            return id == null || id.trim().isEmpty() ? "（缺，可能不是 Mojang 回的）" : id.trim();
        } catch (Throwable t) {
            return "读取失败";
        }
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
