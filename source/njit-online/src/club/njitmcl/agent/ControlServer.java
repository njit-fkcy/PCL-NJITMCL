package club.njitmcl.agent;

import java.io.BufferedReader;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.io.OutputStreamWriter;
import java.io.Writer;
import java.net.InetAddress;
import java.net.ServerSocket;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.util.UUID;

/**
 * 本地控制端口 + 握手文件。
 *
 * 握手文件 %TEMP%\njit-online-<pid>.json 是启动器与代理之间的唯一约定：
 * 启动器按服务端进程号读它，拿到端口与令牌后发一行文本指令。
 * 这样无论代理是 -javaagent 预先加载还是运行时挂载，启动器都不需要知道额外的参数。
 */
public final class ControlServer {

    private static ServerSocket server;
    private static int pid;
    private static File handshakeFile;

    private ControlServer() {
    }

    public static synchronized void start(String args, int processId, java.lang.instrument.Instrumentation inst) {
        pid = processId;
        if (AgentState.token.isEmpty()) {
            String fromArgs = readArg(args, "token");
            AgentState.token = fromArgs.isEmpty() ? UUID.randomUUID().toString().replace("-", "") : fromArgs;
        }
        if (server != null) {
            writeHandshake();
            return;
        }
        try {
            int wantPort = parseInt(readArg(args, "port"), 0);
            InetAddress loop = InetAddress.getByName("127.0.0.1");
            server = wantPort > 0 ? new ServerSocket(wantPort, 8, loop) : new ServerSocket(0, 8, loop);
            AgentState.port = server.getLocalPort();
            writeHandshake();
            Thread worker = new Thread(ControlServer::loop, "njit-online-ctl");
            worker.setDaemon(true);
            worker.start();
            Log.write("控制端口就绪：127.0.0.1:" + AgentState.port);
        } catch (Throwable t) {
            Log.write("控制端口启动失败：" + t);
        }
    }

    private static void loop() {
        while (true) {
            Socket client = null;
            try {
                client = server.accept();
                client.setSoTimeout(3000);
                handle(client);
            } catch (Throwable t) {
                if (server != null && !server.isClosed()) {
                    try {
                        Thread.sleep(200L);
                    } catch (InterruptedException ignored) {
                        Thread.currentThread().interrupt();
                    }
                }
            } finally {
                if (client != null) {
                    try {
                        client.close();
                    } catch (IOException ignored) {
                        // 忽略关闭异常
                    }
                }
            }
        }
    }

    private static void handle(Socket client) {
        try {
            BufferedReader reader = new BufferedReader(
                    new InputStreamReader(client.getInputStream(), StandardCharsets.UTF_8));
            OutputStream out = client.getOutputStream();
            String line = reader.readLine();
            if (line == null || line.trim().isEmpty()) {
                reply(out, "ERR empty");
                return;
            }
            String[] parts = line.trim().split("\\s+");
            if (parts.length < 2 || !AgentState.token.equals(parts[0])) {
                reply(out, "ERR bad-token");
                return;
            }
            String command = parts[1].toUpperCase();
            switch (command) {
                case "PING":
                    reply(out, "OK pong");
                    return;
                case "STATE":
                    reply(out, "OK " + AgentState.mode);
                    return;
                case "INFO":
                    reply(out, "OK " + AgentState.target + "|" + AgentState.returnType
                            + "|" + AgentState.patched);
                    return;
                case "SET":
                    if (parts.length < 3) {
                        reply(out, "ERR missing-value");
                        return;
                    }
                    // 只接受两档：SET 1 = 正版（Mojang）；SET 4 = 双验证
                    int mode;
                    try {
                        mode = Integer.parseInt(parts[2]);
                    } catch (NumberFormatException ex) {
                        reply(out, "ERR bad-value");
                        return;
                    }
                    if (!AgentState.supported(mode)) {
                        reply(out, "ERR unsupported-mode");
                        return;
                    }
                    // 第四个参数可选：临时改写 MUA 的 Yggdrasil 根地址（双验证的 MUA 一侧用）
                    if (mode == AgentState.ModeDualMojangFirst && parts.length >= 4 && parts[3].startsWith("http")) {
                        AgentState.muaApi = parts[3];
                    }
                    AgentState.mode = mode;
                    // 同步到系统属性：被内联进游戏类的「穷版」advice（NeoForge 那种
                    // 加载器看不见代理类的环境）只能读系统属性，读不到 AgentState
                    System.setProperty("njit.mode", String.valueOf(mode));
                    writeHandshake();
                    Log.write("校验模式切换为：" + describe(mode));
                    // 首次加载时改写可能被 Forge 的类加载器丢弃，这里强制重转换一次
                    AgentMain.reapplyAuthFlag();
                    reply(out, "OK " + mode);
                    return;
                default:
                    reply(out, "ERR unknown-command");
            }
        } catch (Throwable t) {
            Log.write("处理控制指令失败：" + t);
        }
    }

    private static void reply(OutputStream out, String text) throws IOException {
        Writer writer = new OutputStreamWriter(out, StandardCharsets.UTF_8);
        writer.write(text);
        writer.write("\n");
        writer.flush();
    }

    /** 供注入侧在命中目标后刷新握手文件，让启动器马上看到真实注入点。 */
    public static void refresh() {
        writeHandshake();
    }

    private static synchronized void writeHandshake() {
        try {
            if (handshakeFile == null) {
                String dir = System.getProperty("java.io.tmpdir", ".");
                handshakeFile = new File(dir, "njit-online-" + pid + ".json");
            }
            String json = "{\"pid\":" + pid + ",\"port\":" + AgentState.port + ",\"token\":\"" + AgentState.token
                    + "\",\"mode\":" + AgentState.mode
                    + ",\"muaApi\":\"" + AgentState.muaApi + "\""
                    + ",\"patched\":" + AgentState.patched
                    + ",\"target\":\"" + AgentState.target + "\""
                    + ",\"returnType\":\"" + AgentState.returnType + "\"}";
            FileOutputStream stream = new FileOutputStream(handshakeFile, false);
            try {
                stream.write(json.getBytes(StandardCharsets.UTF_8));
                stream.flush();
            } finally {
                stream.close();
            }
        } catch (Throwable t) {
            Log.write("写握手文件失败：" + t);
        }
    }

    private static String describe(int mode) {
        switch (mode) {
            case AgentState.ModeDualMojangFirst:
                return "双验证（正版优先）";
            default:
                return "正版";
        }
    }

    /** agentmain 参数形如 token=xxx;port=12345。 */
    private static String readArg(String args, String key) {
        if (args == null || args.isEmpty()) {
            return "";
        }
        for (String pair : args.split("[;,]")) {
            int eq = pair.indexOf('=');
            if (eq <= 0) {
                continue;
            }
            if (pair.substring(0, eq).trim().equalsIgnoreCase(key)) {
                return pair.substring(eq + 1).trim();
            }
        }
        return "";
    }

    private static int parseInt(String value, int fallback) {
        try {
            return Integer.parseInt(value.trim());
        } catch (Throwable t) {
            return fallback;
        }
    }
}
