import club.njitmcl.agent.PeerSkinAdvice;
import net.bytebuddy.ByteBuddy;
import net.bytebuddy.asm.Advice;
import net.bytebuddy.description.type.TypeDescription;
import net.bytebuddy.dynamic.ClassFileLocator;
import net.bytebuddy.matcher.ElementMatchers;
import net.bytebuddy.pool.TypePool;
import java.io.File;

public class WeavePeer {
    public static void main(String[] a) throws Exception {
        legacy("C:/Users/m1520/AppData/Roaming/.minecraft/libraries/com/mojang/authlib/1.5.21/authlib-1.5.21.jar", "net.minecraft.client", false);
        modern("C:/Users/m1520/AppData/Roaming/.minecraft/libraries/com/mojang/authlib/4.0.43/authlib-4.0.43.jar", "com.mojang.authlib.yggdrasil.YggdrasilMinecraftSessionService", "getTextures");
        modern("C:/Users/m1520/AppData/Roaming/.minecraft/libraries/com/mojang/authlib/10.0.77/authlib-10.0.77.jar", "com.mojang.authlib.services.MinecraftServicesSessionService", "getPackedTextures");
    }

    static void legacy(String jarPath, String ignored, boolean unused) {
        try {
            File jar = new File(jarPath);
            ClassFileLocator loc = new ClassFileLocator.Compound(ClassFileLocator.ForJarFile.of(jar),
                    ClassFileLocator.ForJarFile.of(new File("C:/Users/m1520/AppData/Roaming/.minecraft/libraries/org/apache/logging/log4j/log4j-api/2.0-beta9/log4j-api-2.0-beta9.jar")),
                    ClassFileLocator.ForJarFile.of(new File("C:/Users/m1520/AppData/Roaming/.minecraft/libraries/com/google/guava/guava/15.0/guava-15.0.jar")),
                    ClassFileLocator.ForClassLoader.ofSystemLoader());
            TypePool pool = new TypePool.Default.WithLazyResolution(new TypePool.CacheProvider.Simple(), loc, TypePool.Default.ReaderMode.FAST);
            TypeDescription td = pool.describe("com.mojang.authlib.yggdrasil.YggdrasilMinecraftSessionService").resolve();
            var m = ElementMatchers.named("getTextures").and(ElementMatchers.takesArguments(2))
                    .and(ElementMatchers.takesArgument(0, ElementMatchers.named("com.mojang.authlib.GameProfile")));
            System.out.println("1.5.21 getTextures 命中=" + td.getDeclaredMethods().filter(m).size());
            new ByteBuddy().redefine(td, loc).visit(Advice.to(PeerSkinAdvice.class).on(m)).make();
            System.out.println("  WEAVED OK");
        } catch (Throwable t) {
            System.out.println("  FAIL: " + t);
        }
    }

    static void modern(String jarPath, String cls, String method) {
        try {
            File jar = new File(jarPath);
            ClassFileLocator loc = new ClassFileLocator.Compound(ClassFileLocator.ForJarFile.of(jar),
                    ClassFileLocator.ForClassLoader.ofSystemLoader());
            TypePool pool = new TypePool.Default.WithLazyResolution(new TypePool.CacheProvider.Simple(), loc, TypePool.Default.ReaderMode.FAST);
            TypeDescription td = pool.describe(cls).resolve();
            var m = ElementMatchers.named(method).and(ElementMatchers.takesArguments(1))
                    .and(ElementMatchers.takesArgument(0, ElementMatchers.named("com.mojang.authlib.GameProfile")));
            System.out.println(jarPath.replaceAll(".*/", "") + " " + method + " 命中=" + td.getDeclaredMethods().filter(m).size());
            new ByteBuddy().redefine(td, loc).visit(Advice.to(PeerSkinAdvice.class).on(m)).make();
            System.out.println("  WEAVED OK");
        } catch (Throwable t) {
            System.out.println("  FAIL: " + t);
        }
    }
}
