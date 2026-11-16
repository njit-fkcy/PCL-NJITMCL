import club.njitmcl.agent.TextureDomainAdvice;
import net.bytebuddy.ByteBuddy;
import net.bytebuddy.asm.Advice;
import net.bytebuddy.description.type.TypeDescription;
import net.bytebuddy.dynamic.ClassFileLocator;
import net.bytebuddy.matcher.ElementMatchers;
import net.bytebuddy.pool.TypePool;
import java.io.File;

public class WeaveDomain {
    public static void main(String[] a) throws Exception {
        checkLegacy();
        check("C:/Users/m1520/AppData/Roaming/.minecraft/libraries/com/mojang/authlib/10.0.77/authlib-10.0.77.jar",
                "com.mojang.authlib.services.MinecraftServicesDiscoveryService");
        check("C:/Users/m1520/AppData/Roaming/.minecraft/libraries/com/mojang/authlib/4.0.43/authlib-4.0.43.jar",
                "com.mojang.authlib.yggdrasil.TextureUrlChecker");
        check("C:/Users/m1520/AppData/Roaming/.minecraft/libraries/com/mojang/authlib/7.0.61/authlib-7.0.61.jar",
                "com.mojang.authlib.yggdrasil.TextureUrlChecker");
        check("C:/Users/m1520/AppData/Roaming/.minecraft/libraries/com/mojang/authlib/7.0.63/authlib-7.0.63.jar",
                "com.mojang.authlib.yggdrasil.TextureUrlChecker");
    }

    static void checkLegacy() {
        try {
            File jar = new File("C:/Users/m1520/AppData/Roaming/.minecraft/libraries/com/mojang/authlib/1.5.21/authlib-1.5.21.jar");
            ClassFileLocator loc = new ClassFileLocator.Compound(ClassFileLocator.ForJarFile.of(jar),
                    ClassFileLocator.ForJarFile.of(new File("C:/Users/m1520/AppData/Roaming/.minecraft/libraries/org/apache/logging/log4j/log4j-api/2.0-beta9/log4j-api-2.0-beta9.jar")),
                    ClassFileLocator.ForJarFile.of(new File("C:/Users/m1520/AppData/Roaming/.minecraft/libraries/com/google/guava/guava/15.0/guava-15.0.jar")),
                    ClassFileLocator.ForClassLoader.ofSystemLoader());
            TypePool pool = new TypePool.Default.WithLazyResolution(new TypePool.CacheProvider.Simple(), loc,
                    TypePool.Default.ReaderMode.FAST);
            TypeDescription td = pool.describe("com.mojang.authlib.yggdrasil.YggdrasilMinecraftSessionService").resolve();
            var m = ElementMatchers.named("isWhitelistedDomain")
                    .and(ElementMatchers.takesArguments(1))
                    .and(ElementMatchers.returns(ElementMatchers.named("boolean")));
            System.out.println("legacy 命中=" + td.getDeclaredMethods().filter(m).size());
            new ByteBuddy().redefine(td, loc).visit(Advice.to(TextureDomainAdvice.class).on(m)).make();
            System.out.println("  WEAVED OK (1.5.21)");
        } catch (Throwable t) {
            System.out.println("  FAIL: " + t);
        }
    }

    static void check(String jarPath, String cls) {
        try {
            File jar = new File(jarPath);
            ClassFileLocator loc = new ClassFileLocator.Compound(ClassFileLocator.ForJarFile.of(jar),
                    ClassFileLocator.ForClassLoader.ofSystemLoader());
            TypePool pool = new TypePool.Default.WithLazyResolution(new TypePool.CacheProvider.Simple(), loc,
                    TypePool.Default.ReaderMode.FAST);
            TypeDescription td = pool.describe(cls).resolve();
            var m = ElementMatchers.named("isAllowedTextureDomain")
                    .and(ElementMatchers.takesArguments(1))
                    .and(ElementMatchers.returns(ElementMatchers.named("boolean")));
            int hits = td.getDeclaredMethods().filter(m).size();
            System.out.println(cls + "  命中=" + hits + "  " + (hits > 0 ? td.getDeclaredMethods().filter(m).getOnly() : ""));
            new ByteBuddy().redefine(td, loc).visit(Advice.to(TextureDomainAdvice.class).on(m)).make();
            System.out.println("  WEAVED OK");
        } catch (Throwable t) {
            System.out.println("  FAIL: " + t);
        }
    }
}
