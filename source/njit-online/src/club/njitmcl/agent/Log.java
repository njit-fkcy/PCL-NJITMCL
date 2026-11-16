package club.njitmcl.agent;

import java.io.File;
import java.io.FileOutputStream;
import java.io.OutputStreamWriter;
import java.io.PrintWriter;
import java.nio.charset.StandardCharsets;
import java.text.SimpleDateFormat;
import java.util.Date;

/**
 * 极简日志：同时写到服务端控制台与 %TEMP%\njit-online-<pid>.log。
 * 不依赖任何第三方库，避免影响被注入进程的类路径。
 */
public final class Log {

    private static PrintWriter file;
    private static boolean broken;

    private Log() {
    }

    public static synchronized void init(int pid) {
        if (file != null || broken) {
            return;
        }
        try {
            String dir = System.getProperty("java.io.tmpdir", ".");
            File target = new File(dir, "njit-online-" + pid + ".log");
            file = new PrintWriter(new OutputStreamWriter(
                    new FileOutputStream(target, true), StandardCharsets.UTF_8), true);
        } catch (Throwable t) {
            broken = true;
        }
    }

    /**
     * 皮肤链路的细粒度观测，默认关。
     *
     * 这一片（皮肤· 开头）是排查 26.x 默认皮肤时一层层加出来的，
     * 一次进服十几行，发包时不该刷别人的日志。出问题时加：
     *
     *   -Dnjit.debug.skin=true
     *
     * 打开后 Include：「SkinManager.get 收到什么」「registerTextures 解出什么」
     * 「渲染器最终采样哪张图」——这三行足够定位任何一段。
     */
    public static void debug(String message) {
        if (!"true".equalsIgnoreCase(System.getProperty("njit.debug.skin", "false").trim())) {
            return;
        }
        write(message);
    }

    public static void write(String message) {
        String line = "[" + new SimpleDateFormat("HH:mm:ss").format(new Date()) + "] " + message;
        try {
            System.out.println("[NJIT-Agent] " + line);
        } catch (Throwable ignored) {
            // 控制台可能已被重定向或关闭
        }
        PrintWriter writer = file;
        if (writer != null) {
            writer.println(line);
            writer.flush();
        }
    }
}
