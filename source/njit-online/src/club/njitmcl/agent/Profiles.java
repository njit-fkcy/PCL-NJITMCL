package club.njitmcl.agent;

import java.lang.reflect.Constructor;
import java.nio.charset.StandardCharsets;
import java.util.Optional;
import java.util.UUID;

/**
 * 用反射构造认证返回值，编译期不依赖 authlib。
 *
 * 不直接 import com.mojang.authlib.GameProfile 的原因有两个：
 * 1) 代理要能在 authlib 还没加载时完成注入，不能有静态依赖；
 * 2) 不同版本 authlib 的返回类型不同（GameProfile / ProfileResult / Optional&lt;GameProfile&gt;），
 *    只有运行期反射才能覆盖。
 */
public final class Profiles {

    private Profiles() {
    }

    /**
     * 用认证服务器给的 UUID 构造 GameProfile（MUA 路径）。
     * id 是 32 位无连字符十六进制；解析失败时退回离线 UUID，别让玩家因为一个脏 id 进不来。
     */
    public static Object makeWithId(String name, String idHex, ClassLoader loader) {
        if (name == null || name.isEmpty()) {
            name = "Player";
        }
        try {
            ClassLoader target = loader != null ? loader : Profiles.class.getClassLoader();
            Class<?> profileClass = Class.forName("com.mojang.authlib.GameProfile", true, target);
            UUID id = parseUuid(idHex);
            if (id == null) {
                id = UUID.nameUUIDFromBytes(("OfflinePlayer:" + name).getBytes(StandardCharsets.UTF_8));
            }
            return profileClass.getConstructor(UUID.class, String.class).newInstance(id, name);
        } catch (Throwable t) {
            Log.write("构造 GameProfile 失败：" + t);
            return null;
        }
    }

    /** 32 位十六进制（可带连字符）→ UUID；不合法返回 null。 */
    public static UUID parseUuid(String raw) {
        if (raw == null) {
            return null;
        }
        String hex = raw.trim().replace("-", "");
        if (hex.length() != 32) {
            return null;
        }
        try {
            long most = Long.parseUnsignedLong(hex.substring(0, 16), 16);
            long least = Long.parseUnsignedLong(hex.substring(16), 16);
            return new UUID(most, least);
        } catch (Throwable t) {
            return null;
        }
    }

    /**
     * 把认证服务器返回的 properties（textures / uploadable textures 等）挂到 profile 上。
     * 不挂的话玩家能进服但所有人都是默认皮肤，等于白做一半。
     *
     * 返回值就是「应当使用的 profile」：authlib 10.x 起 GameProfile 变成了 record，
     * 只有 properties() 访问器，没有 getProperties()，也不能原地改；这种情况只能
     * 造一个新的 GameProfile（UUID, name, PropertyMap）返回。老版本仍然原地写。
     *
     * 全程反射：代理编译期不能依赖 authlib，也不能假定版本。
     */
    public static Object applyProperties(Object profile, Object propertiesJson, ClassLoader loader) {
        if (profile == null || !(propertiesJson instanceof java.util.List)) {
            return profile;
        }
        try {
            ClassLoader target = loader != null ? loader : Profiles.class.getClassLoader();
            Class<?> propertyClass = Class.forName("com.mojang.authlib.properties.Property", true, target);
            java.util.List<Object> pending = new java.util.ArrayList<Object>();
            for (Object item : (java.util.List<?>) propertiesJson) {
                if (!(item instanceof java.util.Map)) {
                    continue;
                }
                java.util.Map<?, ?> entry = (java.util.Map<?, ?>) item;
                String key = string(entry.get("name"));
                String value = string(entry.get("value"));
                String signature = string(entry.get("signature"));
                if (key == null || value == null) {
                    continue;
                }
                pending.add(propertyClass
                        .getConstructor(String.class, String.class, String.class)
                        .newInstance(key, value, signature == null ? "" : signature));
            }
            if (pending.isEmpty()) {
                return profile;
            }

            // 先走 record 路线：造新 GameProfile
            Object rebuilt = rebuild(profile, pending, target, propertyClass, true);
            if (rebuilt != null) {
                return rebuilt;
            }
            // 回退：老版本 PropertyMap 可原地写
            Object map = readAny(profile, "getProperties", "properties");
            if (map == null) {
                Log.write("读不到 PropertyMap，皮肤将不会生效");
                return profile;
            }
            java.lang.reflect.Method put = null;
            for (java.lang.reflect.Method candidate : map.getClass().getMethods()) {
                if (candidate.getName().equals("put") && candidate.getParameterCount() == 2) {
                    put = candidate;
                    break;
                }
            }
            if (put == null) {
                Log.write("找不到 PropertyMap.put，皮肤将不会生效");
                return profile;
            }
            for (Object property : pending) {
                put.invoke(map, readAny(property, "name", "getName"), property);
            }
            return profile;
        } catch (Throwable t) {
            Log.write("写入 profile 属性失败（皮肤可能不显示）：" + t);
            return profile;
        }
    }

    /**
     * 用「原有 properties + 新 properties」造一个新的 GameProfile。
     * 只有 authlib 10.x 之后（GameProfile 是 record）才走得通，失败返回 null 让调用方回退。
     */
    private static Object rebuild(Object profile, java.util.List<Object> pending,
                                  ClassLoader target, Class<?> propertyClass, boolean quiet) {
        try {
            Class<?> profileClass = Class.forName("com.mojang.authlib.GameProfile", true, target);
            Class<?> mapClass = Class.forName("com.mojang.authlib.properties.PropertyMap", true, target);
            Class<?> multimapClass = Class.forName("com.google.common.collect.Multimap", true, target);
            Class<?> hashClass = Class.forName("com.google.common.collect.HashMultimap", true, target);

            Object multimap = hashClass.getMethod("create").invoke(null);
            if (multimap == null) {
                return null;
            }
            java.lang.reflect.Method put = multimapClass.getMethod("put", Object.class, Object.class);

            // 原有属性照搬（例如正版路径下 Mojang 已经填好的 textures）
            Object oldMap = readAny(profile, "properties", "getProperties");
            if (oldMap != null) {
                try {
                    Object asMap = oldMap.getClass().getMethod("asMap").invoke(oldMap);
                    if (asMap instanceof java.util.Map) {
                        for (java.util.Map.Entry<?, ?> entry : ((java.util.Map<?, ?>) asMap).entrySet()) {
                            if (!(entry.getValue() instanceof java.util.Collection)) {
                                continue;
                            }
                            for (Object property : (java.util.Collection<?>) entry.getValue()) {
                                put.invoke(multimap, entry.getKey(), property);
                            }
                        }
                    }
                } catch (Throwable ignored) {
                    // 拿不到就只放新属性，总比整条链路失败好
                }
            }
            for (Object property : pending) {
                put.invoke(multimap, readAny(property, "name", "getName"), property);
            }

            Object newMap = mapClass.getConstructor(multimapClass).newInstance(multimap);
            Object id = readAny(profile, "id", "getId");
            Object name = readAny(profile, "name", "getName");
            if (id == null || name == null) {
                return null;
            }
            return profileClass.getConstructor(java.util.UUID.class, String.class, mapClass)
                    .newInstance(id, name, newMap);
        } catch (Throwable t) {
            if (!quiet) {
                Log.write("重建 GameProfile 失败：" + t);
            }
            return null;
        }
    }

    /**
     * 一行诊断：textures 在不在、签没签名、贴图指向哪里。
     *
     * 皮肤问题的三种现场——属性没挂上、挂了但没签名、签了但图取不到——
     * 光看属性条数分不出来，所以把这三件事压进一行日志。
     */
    public static String describe(Object profile) {
        try {
            Object map = readAny(profile, "properties", "getProperties");
            if (map == null) {
                return "，读不到 PropertyMap";
            }
            Object textures = map.getClass().getMethod("get", Object.class).invoke(map, "textures");
            if (!(textures instanceof java.util.Collection) || ((java.util.Collection<?>) textures).isEmpty()) {
                return "，textures 缺失";
            }
            Object first = ((java.util.Collection<?>) textures).iterator().next();
            Object value = readAny(first, "value", "getValue");
            Object signature = readAny(first, "signature", "getSignature");
            boolean signed = signature != null && !signature.toString().isEmpty();
            return "，textures " + (signed ? "已签名" : "未签名") + " " + skinUrl(value == null ? null : value.toString());
        } catch (Throwable t) {
            return "，属性内容读取失败：" + t;
        }
    }

    /** 单个属性的一行诊断：签名在不在、贴图指向哪里。 */
    public static String describeProperty(Object property) {
        if (property == null) {
            return "未取到";
        }
        try {
            Object value = readAny(property, "value", "getValue");
            Object signature = readAny(property, "signature", "getSignature");
            boolean signed = signature != null && !signature.toString().isEmpty();
            return (signed ? "已签名 " : "未签名 ") + skinUrl(value == null ? null : value.toString());
        } catch (Throwable t) {
            return "读取失败：" + t;
        }
    }

    /**
     * 从「档案 / 名字 / 其它形态」里取用户名。
     *
     * authlib 4.x 及更早传 GameProfile（getName），7.x 起直接传 String，
     * 10.x 的 GameProfile 是 record（name）。三种都认，否则老版本静默失效。
     *
     * 放在 Profiles 而不是某个 Advice 类里：Advice 类带着 ByteBuddy 的注解，
     * 只能被 ByteBuddy 当 advice 用，不适合当工具类（2026-11-15 因为混用把 1.20.1 弄挂过一轮）。
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

    /** 从 textures 属性的 base64 里抠出 SKIN 的 URL；解不出来返回 "?"。 */
    private static String skinUrl(String base64) {
        if (base64 == null || base64.isEmpty()) {
            return "?";
        }
        try {
            String json = new String(java.util.Base64.getDecoder().decode(base64), StandardCharsets.UTF_8);
            Object parsed = Json.parse(json);
            if (!(parsed instanceof java.util.Map)) {
                return "?";
            }
            Object textures = ((java.util.Map<?, ?>) parsed).get("textures");
            if (!(textures instanceof java.util.Map)) {
                return "?";
            }
            Object skin = ((java.util.Map<?, ?>) textures).get("SKIN");
            if (!(skin instanceof java.util.Map)) {
                return "?";
            }
            Object url = ((java.util.Map<?, ?>) skin).get("url");
            return url == null ? "?" : url.toString();
        } catch (Throwable t) {
            return "?" + t;
        }
    }

    /** 读 profile 上挂了几个属性；读不到返回 -1。用于日志诊断皮肤问题。 */
    public static int propertyCount(Object profile) {        Object map = readAny(profile, "properties", "getProperties");
        if (map == null) {
            return -1;
        }
        try {
            Object size = map.getClass().getMethod("size").invoke(map);
            return size instanceof Integer ? (Integer) size : -1;
        } catch (Throwable t) {
            return -1;
        }
    }

    /** 依次尝试若干无参方法，返回第一个拿到的值。 */
    private static Object readAny(Object target, String... names) {
        if (target == null) {
            return null;
        }
        for (String name : names) {
            try {
                return target.getClass().getMethod(name).invoke(target);
            } catch (Throwable ignored) {
                // 换下一个名字
            }
        }
        return null;
    }

    private static String string(Object value) {
        return value == null ? null : value.toString();
    }

    /**
     * 把 GameProfile 包成方法真正要返回的类型。
     * 1.20.2+ 的会话服务返回 ProfileResult，老版本返回 GameProfile，个别分支返回 Optional，
     * 这里按目标类型逐个尝试，匹配不上就返回 null（调用方会退化成原样）。
     */
    public static Object wrap(Object profile, Class<?> want) {
        if (want == null || profile == null) {
            return null;
        }
        try {
            if (want == Optional.class) {
                return Optional.of(profile);
            }
            if (want.isInstance(profile)) {
                return profile;
            }
            // 26.x 的 ProfileResult 有两个构造：一个只收 GameProfile，另一个还要 actions。
            // 取参数最少的那个：参数越多，我们越可能凑不齐（例如 Set&lt;ProfileActionType&gt;）。
            Constructor<?> best = null;
            for (Constructor<?> ctor : want.getConstructors()) {
                Class<?>[] params = ctor.getParameterTypes();
                if (params.length == 0 || !params[0].isInstance(profile)) {
                    continue;
                }
                boolean allFeasible = true;
                for (int i = 1; i < params.length; i++) {
                    if (!params[i].isPrimitive()) {
                        allFeasible = false;
                        break;
                    }
                }
                if (!allFeasible) {
                    continue;
                }
                if (best == null || params.length < best.getParameterCount()) {
                    best = ctor;
                }
            }
            if (best == null) {
                return null;
            }
            Object[] args = new Object[best.getParameterCount()];
            args[0] = profile;
            for (int i = 1; i < args.length; i++) {
                args[i] = false;
            }
            return best.newInstance(args);
        } catch (Throwable t) {
            Log.write("包装返回值 " + want.getName() + " 失败：" + t);
        }
        return null;
    }
}
