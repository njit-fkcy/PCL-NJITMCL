package club.njitmcl.agent;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.lang.reflect.Method;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;

/**
 * 「别人那份档案没带 textures」——按 UUID 去会话服务器补一次。
 *
 * 2026-11-15 的 1.7.10 实测把两件事分开了：
 *
 *   正版房主（101728）12:44:47  补上皮肤属性（ImQiez）：1 条
 *   MUA 客户端（89932）12:45:12  补上皮肤属性（ImQiez_test）：2 条
 *                              皮肤档案没取到属性：ImQiez（bd366481…）   ← 卡这
 *
 * 第二条说明方法进来了、UUID 也对，是**取属性这一步失败**。原因有两个：
 *
 * 一、超时太短。1.2 秒对 sessionserver.mojang.com 不够（房主那侧用的是 4 秒，
 *    所以它成功了）。请求被掐断，于是「没取到」。
 * 二、失败被当成结论缓存了。TRIED 一加，这个 UUID 整个会话都不再问 ——
 *    一次网络抖动就变成永久默认皮肤。
 *
 * 现在的策略：
 * - 取属性放到后台线程，请求本身给 5 秒；
 * - 调用方（getTextures）最多等 4 秒拿结果，拿不到这一帧就画默认，不影响后续；
 * - 「网站上确实没有」（204/404）才算结论并缓存；超时/异常不缓存，下次再试；
 * - 每个 UUID 最多试 3 次，之后放弃，避免一直打网络。
 */
public final class PeerSkin {

    private static final int TIMEOUT_MS = 5000;
    private static final long WAIT_BUDGET_MS = 4000L;
    private static final long WAIT_STEP_MS = 40L;
    private static final int MAX_ATTEMPTS = 3;

    /** uuid(32 位小写) → 属性 JSON 列表；空列表表示「网站上确实没有」。 */
    private static final Map<String, List<Object>> CACHE =
            Collections.synchronizedMap(new LinkedHashMap<String, List<Object>>());

    /** 正在进行中的查询，避免同一 UUID 并发开线程。 */
    private static final Set<String> RUNNING =
            Collections.newSetFromMap(new ConcurrentHashMap<String, Boolean>());

    /** 已尝试次数，超上限后不再打扰网络。 */
    private static final Map<String, Integer> ATTEMPTS =
            Collections.synchronizedMap(new LinkedHashMap<String, Integer>());

    /** 已经报告过的 UUID，避免刷屏。 */
    private static final Set<String> REPORTED =
            Collections.newSetFromMap(new ConcurrentHashMap<String, Boolean>());

    private PeerSkin() {
    }

    /** 供内联代码调用：profile 缺 textures 就补上（原地改）。 */
    public static void fill(Object profile) {
        try {
            if (profile == null) {
                return;
            }
            // 不看校验模式：补的是显示用的贴图，跟「谁能进来」是两件事
            if (Profiles.propertyCount(profile) > 0) {
                return;
            }
            String id = uuidOf(profile);
            if (id == null || id.isEmpty()) {
                return;
            }
            String name = AuthAdvice.nameOf(profile);
            List<Object> props = obtain(id, name);
            if (props == null || props.isEmpty()) {
                return;
            }
            Object filled = Profiles.applyProperties(profile, props, profile.getClass().getClassLoader());
            if (filled == null || filled != profile) {
                // record 型档案只能重建，调用方拿不到新对象 —— 那一边不补，保持原样
                return;
            }
            Log.write("补上皮肤属性（" + name + "）：" + props.size() + " 条");
        } catch (Throwable t) {
            Log.write("补皮肤属性失败：" + t);
        }
    }

    /** 拿属性：命中缓存直接返回，否则起后台任务并等一小会儿。 */
    private static List<Object> obtain(String id, String name) {
        List<Object> hit = CACHE.get(id);
        if (hit != null) {
            return hit;
        }
        Integer tried = ATTEMPTS.get(id);
        if (tried != null && tried >= MAX_ATTEMPTS) {
            return null;
        }
        startFetch(id, name);

        long deadline = System.currentTimeMillis() + WAIT_BUDGET_MS;
        while (System.currentTimeMillis() < deadline) {
            hit = CACHE.get(id);
            if (hit != null) {
                return hit;
            }
            if (!RUNNING.contains(id)) {
                // 后台已经收工但没写缓存 —— 网络失败，这一帧放弃，下次再试
                return null;
            }
            try {
                Thread.sleep(WAIT_STEP_MS);
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
                return null;
            }
        }
        return CACHE.get(id);
    }

    private static void startFetch(final String id, final String name) {
        if (!RUNNING.add(id)) {
            return;
        }
        Thread worker = new Thread(new Runnable() {
            @Override
            public void run() {
                try {
                    List<Object> found = fetch(id);
                    if (found != null) {
                        CACHE.put(id, found);
                        return;
                    }
                    // fetch 返回 null 有两种：网站上没有（结论）、网络失败（不是结论）
                    if (KNOWN_ABSENT.contains(id)) {
                        CACHE.put(id, Collections.<Object>emptyList());
                        note(name, id, "网站上没有这份档案");
                    } else {
                        ATTEMPTS.put(id, attempts(id) + 1);
                        note(name, id, "网络没通，下次再试");
                    }
                } catch (Throwable t) {
                    ATTEMPTS.put(id, attempts(id) + 1);
                    note(name, id, "查询异常：" + t.getClass().getSimpleName());
                } finally {
                    RUNNING.remove(id);
                }
            }
        }, "njit-peer-skin");
        worker.setDaemon(true);
        worker.start();
    }

    /** 网站明确回答「查无此人」的 UUID。 */
    private static final Set<String> KNOWN_ABSENT =
            Collections.newSetFromMap(new ConcurrentHashMap<String, Boolean>());

    private static int attempts(String id) {
        Integer value = ATTEMPTS.get(id);
        return value == null ? 0 : value;
    }

    private static void note(String name, String id, String why) {
        if (REPORTED.size() >= 10 || !REPORTED.add(id)) {
            return;
        }
        Log.write("皮肤档案没取到属性：" + name + "（" + id.substring(0, Math.min(8, id.length()))
                + "…，" + why + "）");
    }

    private static List<Object> fetch(String id) {
        // 同一台机器上别的进程（比如房主那侧）可能刚取过同一份档案，先看本地缓存。
        // sessionserver.mojang.com 会限流，实测同一 URL 第一次 429、第二次 200，
        // 能不打网络就不打。
        List<Object> shared = SkinCache.read(id);
        if (shared != null) {
            return shared;
        }
        Result first = request(trim(System.getProperty("njit.mojang.sessionserver", Sessions.MOJANG_SESSION))
                + "/session/minecraft/profile/" + id);
        if (first.properties != null) {
            SkinCache.write(id, first.properties);
            return first.properties;
        }
        String external = trim(AgentState.muaApi);
        Result second = external.isEmpty() ? null
                : request(external + "/sessionserver/session/minecraft/profile/" + id);
        if (second != null && second.properties != null) {
            SkinCache.write(id, second.properties);
            return second.properties;
        }
        // 只有「每一个问过的站都明确说没有」才算结论；
        // 只要有一处是网络失败/限流，就当没结论，下次再试
        if (first.absent && (second == null || second.absent)) {
            KNOWN_ABSENT.add(id);
        }
        return null;
    }

    private static final class Result {
        List<Object> properties;
        boolean absent;
        int status;
    }

    private static Result request(String base) {
        // 429 是限流，隔一会儿重试一次；两次都 429 就按「网络失败」处理，不写结论
        Result result = attempt(base);
        if (result.status == 429) {
            try {
                Thread.sleep(700L);
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
            }
            result = attempt(base);
        }
        return result;
    }

    private static Result attempt(String base) {
        Result result = new Result();
        HttpURLConnection connection = null;
        try {
            connection = (HttpURLConnection) new URL(base + "?unsigned=false").openConnection();
            connection.setConnectTimeout(TIMEOUT_MS);
            connection.setReadTimeout(TIMEOUT_MS);
            connection.setRequestProperty("User-Agent", "NJITMCL-Agent/1");
            connection.setRequestProperty("Accept", "application/json");
            int code = connection.getResponseCode();
            result.status = code;
            if (code == 204 || code == 404) {
                result.absent = true;
                return result;
            }
            if (code != 200) {
                return result;
            }
            Object parsed = Json.parse(readAll(connection.getInputStream()));
            if (!(parsed instanceof Map)) {
                return result;
            }
            Object raw = ((Map<?, ?>) parsed).get("properties");
            if (!(raw instanceof List)) {
                result.absent = true;
                return result;
            }
            List<?> list = (List<?>) raw;
            if (list.isEmpty()) {
                result.absent = true;
                return result;
            }
            List<Object> copy = new ArrayList<Object>(list.size());
            for (Object item : list) {
                copy.add(item);
            }
            result.properties = copy;
            return result;
        } catch (Throwable t) {
            return result;
        } finally {
            if (connection != null) {
                connection.disconnect();
            }
        }
    }

    private static String uuidOf(Object profile) {
        try {
            Object value = call(profile, "getId");
            if (value == null) {
                value = call(profile, "id");
            }
            if (value == null) {
                return null;
            }
            return value.toString().replace("-", "").toLowerCase(Locale.ROOT);
        } catch (Throwable t) {
            return null;
        }
    }

    private static Object call(Object target, String name) {
        try {
            Method method = target.getClass().getMethod(name);
            return method.invoke(target);
        } catch (Throwable t) {
            return null;
        }
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
