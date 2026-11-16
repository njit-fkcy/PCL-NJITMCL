package club.njitmcl.agent;

import java.io.BufferedReader;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.InputStreamReader;
import java.io.OutputStreamWriter;
import java.io.PrintWriter;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * 同一台机器上，各进程共用一份「谁长什么样」的档案缓存。
 *
 * 起因：2026-11-15 12:49 的定位结果显示，MUA 客户端去问 sessionserver.mojang.com
 * 拿正版那位的档案，拿回来的是 429（限流）—— 实测同一条 URL 第一次 429、
 * 第二次 200。而这份档案**房主那个进程几分钟前刚取过**，就在同一台机器上。
 *
 * 于是把取到的属性落到 %TEMP%\njit-online-profile-&lt;uuid&gt;.txt，
 * 别的进程先看文件，有就直接用，不再打网络。格式是每行一条属性：
 *
 *   name \t value \t signature
 *
 * 空 signature 用 - 占位。文件带有效期（6 小时），过期重取。
 */
public final class SkinCache {

    private static final long TTL_MS = 6L * 60 * 60 * 1000;

    private SkinCache() {
    }

    /** 读缓存；没有、过期、读坏都返回 null。 */
    public static List<Object> read(String id) {
        File file = fileOf(id);
        if (file == null || !file.isFile()) {
            return null;
        }
        if (System.currentTimeMillis() - file.lastModified() > TTL_MS) {
            return null;
        }
        BufferedReader reader = null;
        try {
            reader = new BufferedReader(new InputStreamReader(
                    new FileInputStream(file), StandardCharsets.UTF_8));
            List<Object> result = new ArrayList<Object>();
            String line;
            while ((line = reader.readLine()) != null) {
                if (line.isEmpty()) {
                    continue;
                }
                String[] parts = line.split("\t", -1);
                if (parts.length < 2) {
                    continue;
                }
                Map<String, Object> property = new LinkedHashMap<String, Object>();
                property.put("name", parts[0]);
                property.put("value", parts[1]);
                if (parts.length >= 3 && !"-".equals(parts[2])) {
                    property.put("signature", parts[2]);
                }
                result.add(property);
            }
            return result.isEmpty() ? null : result;
        } catch (Throwable t) {
            return null;
        } finally {
            close(reader);
        }
    }

    /** 写缓存；失败静默（缓存只是省一次网络，写不进去不影响功能）。 */
    public static void write(String id, List<Object> properties) {
        File file = fileOf(id);
        if (file == null || properties == null || properties.isEmpty()) {
            return;
        }
        PrintWriter writer = null;
        try {
            writer = new PrintWriter(new OutputStreamWriter(
                    new FileOutputStream(file), StandardCharsets.UTF_8));
            for (Object item : properties) {
                if (!(item instanceof Map)) {
                    continue;
                }
                Map<?, ?> property = (Map<?, ?>) item;
                Object name = property.get("name");
                Object value = property.get("value");
                if (name == null || value == null) {
                    continue;
                }
                Object signature = property.get("signature");
                writer.print(String.valueOf(name));
                writer.print('\t');
                writer.print(String.valueOf(value));
                writer.print('\t');
                writer.print(signature == null || String.valueOf(signature).isEmpty()
                        ? "-" : String.valueOf(signature));
                writer.println();
            }
        } catch (Throwable t) {
            // 缓存写失败不影响功能
        } finally {
            if (writer != null) {
                writer.close();
            }
        }
    }

    private static File fileOf(String id) {
        if (id == null || id.isEmpty()) {
            return null;
        }
        try {
            String dir = System.getProperty("java.io.tmpdir", ".");
            return new File(dir, "njit-online-profile-" + id + ".txt");
        } catch (Throwable t) {
            return null;
        }
    }

    private static void close(BufferedReader reader) {
        if (reader == null) {
            return;
        }
        try {
            reader.close();
        } catch (Throwable ignored) {
            // 关闭失败不影响结果
        }
    }
}
