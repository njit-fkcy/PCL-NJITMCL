import club.njitmcl.agent.BridgeLoginAdvice;
import net.bytebuddy.ByteBuddy;
import net.bytebuddy.asm.Advice;
import net.bytebuddy.description.type.TypeDescription;
import net.bytebuddy.dynamic.ClassFileLocator;
import net.bytebuddy.matcher.ElementMatchers;
import net.bytebuddy.pool.TypePool;
import java.io.File;

public class WeaveAuth18 {
    public static void main(String[] a) throws Exception {
        File jar = new File("C:/Users/m1520/AppData/Roaming/.minecraft/libraries/com/mojang/authlib/3.3.39/authlib-3.3.39.jar");
        File log4j = new File("C:/Users/m1520/AppData/Roaming/.minecraft/libraries/org/apache/logging/log4j/log4j-api/2.17.1/log4j-api-2.17.1.jar");
        ClassFileLocator loc = new ClassFileLocator.Compound(
                ClassFileLocator.ForJarFile.of(jar),
                ClassFileLocator.ForClassLoader.ofSystemLoader());
        TypePool pool = new TypePool.Default.WithLazyResolution(new TypePool.CacheProvider.Simple(), loc,
                TypePool.Default.ReaderMode.FAST);
        TypeDescription td = pool.describe("com.mojang.authlib.yggdrasil.YggdrasilMinecraftSessionService").resolve();

        var hasJoined = ElementMatchers.named("hasJoinedServer");
        System.out.println("hasJoinedServer 命中 = " + td.getDeclaredMethods().filter(hasJoined).size());
        for (var m : td.getDeclaredMethods().filter(hasJoined)) {
            System.out.println("   " + m);
        }
        var domain = ElementMatchers.named("isWhitelistedDomain").and(ElementMatchers.takesArguments(1));
        System.out.println("isWhitelistedDomain 命中 = " + td.getDeclaredMethods().filter(domain).size());

        try {
            new ByteBuddy().redefine(td, loc)
                    .visit(Advice.to(BridgeLoginAdvice.class).on(hasJoined))
                    .make();
            System.out.println("WEAVED OK (hasJoinedServer)");
        } catch (Throwable t) {
            System.out.println("WEAVE FAIL: " + t);
            Throwable c = t.getCause();
            while (c != null) { System.out.println("   cause: " + c); c = c.getCause(); }
        }
    }
}
