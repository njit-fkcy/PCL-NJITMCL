import club.njitmcl.agent.*;
import net.bytebuddy.ByteBuddy;
import net.bytebuddy.asm.Advice;
import net.bytebuddy.description.type.TypeDescription;
import net.bytebuddy.dynamic.ClassFileLocator;
import net.bytebuddy.matcher.ElementMatchers;
import net.bytebuddy.pool.TypePool;
import java.io.File;

public class Weave263 {
    public static void main(String[] a) throws Exception {
        File jar = new File("C:/Users/m1520/AppData/Roaming/.minecraft/versions/26.3-Forge_66.1.5/26.3-Forge_66.1.5.jar");
        ClassFileLocator loc = ClassFileLocator.ForJarFile.of(jar);
        TypePool pool = new TypePool.Default.WithLazyResolution(
                new TypePool.CacheProvider.Simple(), loc, TypePool.Default.ReaderMode.FAST);
        TypeDescription td = pool.describe("net.minecraft.client.resources.SkinManager").resolve();

        var getMatcher = ElementMatchers.takesArguments(1)
                .and(ElementMatchers.takesArgument(0, ElementMatchers.named("com.mojang.authlib.GameProfile")));
        var lookupMatcher = ElementMatchers.takesArguments(2)
                .and(ElementMatchers.takesArgument(0, ElementMatchers.named("com.mojang.authlib.GameProfile")))
                .and(ElementMatchers.takesArgument(1, ElementMatchers.is(boolean.class)));

        System.out.println("get 命中方法数 = " + td.getDeclaredMethods().filter(getMatcher).size());
        System.out.println("createLookup 命中方法数 = " + td.getDeclaredMethods().filter(lookupMatcher).size());

        try {
            new ByteBuddy()
                    .redefine(td, loc)
                    .visit(Advice.to(TraceAdvice.class).on(ElementMatchers.isConstructor()))
                    .visit(Advice.to(SkinProfileAdvice.class).on(getMatcher))
                    .visit(Advice.to(SkinSecureAdvice.class).on(lookupMatcher))
                    .make();
            System.out.println("WEAVED OK");
        } catch (Throwable t) {
            System.out.println("WEAVE FAIL: " + t);
            Throwable c = t.getCause();
            while (c != null) { System.out.println("  cause: " + c); c = c.getCause(); }
        }
    }
}
