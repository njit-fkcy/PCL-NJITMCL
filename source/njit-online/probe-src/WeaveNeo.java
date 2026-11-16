import club.njitmcl.agent.AuthFlagInlineAdvice;
import club.njitmcl.agent.ValidatorInlineAdvice;
import net.bytebuddy.ByteBuddy;
import net.bytebuddy.asm.Advice;
import net.bytebuddy.description.type.TypeDescription;
import net.bytebuddy.dynamic.ClassFileLocator;
import net.bytebuddy.matcher.ElementMatchers;
import net.bytebuddy.pool.TypePool;
import java.io.File;

public class WeaveNeo {
    public static void main(String[] a) throws Exception {
        File jar = new File("C:/Users/m1520/AppData/Roaming/.minecraft/libraries/net/minecraft/client/1.21.4-20241203.161809/client-1.21.4-20241203.161809-srg.jar");
        if (!jar.isFile()) {
            System.out.println("no jar at " + jar);
            return;
        }
        ClassFileLocator loc = new ClassFileLocator.Compound(ClassFileLocator.ForJarFile.of(jar),
                ClassFileLocator.ForClassLoader.ofSystemLoader());
        TypePool pool = new TypePool.Default.WithLazyResolution(new TypePool.CacheProvider.Simple(), loc,
                TypePool.Default.ReaderMode.FAST);

        TypeDescription sv = pool.describe("net.minecraft.util.SignatureValidator").resolve();
        var m = ElementMatchers.returns(ElementMatchers.named("boolean"))
                .and(ElementMatchers.not(ElementMatchers.isEquals()))
                .and(ElementMatchers.not(ElementMatchers.isHashCode()));
        System.out.println("SignatureValidator 命中=" + sv.getDeclaredMethods().filter(m).size());
        new ByteBuddy().redefine(sv, loc).visit(Advice.to(ValidatorInlineAdvice.class).on(m)).make();
        System.out.println("  穷版 WEAVED OK");

        TypeDescription mcs = pool.describe("net.minecraft.server.MinecraftServer").resolve();
        var auth = ElementMatchers.named("usesAuthentication").and(ElementMatchers.takesArguments(0));
        System.out.println("MinecraftServer.usesAuthentication 命中=" + mcs.getDeclaredMethods().filter(auth).size());
        new ByteBuddy().redefine(mcs, loc).visit(Advice.to(AuthFlagInlineAdvice.class).on(auth)).make();
        System.out.println("  穷版 WEAVED OK");
    }
}
