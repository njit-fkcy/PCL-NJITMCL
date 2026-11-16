import club.njitmcl.agent.AvatarTextureAdvice;
import net.bytebuddy.ByteBuddy;
import net.bytebuddy.asm.Advice;
import net.bytebuddy.description.type.TypeDescription;
import net.bytebuddy.dynamic.ClassFileLocator;
import net.bytebuddy.matcher.ElementMatchers;
import net.bytebuddy.pool.TypePool;
import java.io.File;

public class WeaveAvatar {
    public static void main(String[] a) throws Exception {
        File jar = new File("C:/Users/m1520/AppData/Roaming/.minecraft/libraries/net/minecraftforge/forge/26.3-66.1.5/forge-26.3-66.1.5-client.jar");
        ClassFileLocator loc = new ClassFileLocator.Compound(ClassFileLocator.ForJarFile.of(jar), ClassFileLocator.ForClassLoader.ofSystemLoader());
        TypePool pool = new TypePool.Default.WithLazyResolution(new TypePool.CacheProvider.Simple(), loc,
                TypePool.Default.ReaderMode.FAST);
        TypeDescription td = pool.describe("net.minecraft.client.renderer.entity.player.AvatarRenderer").resolve();
        var m = ElementMatchers.takesArguments(1)
                .and(ElementMatchers.takesArgument(0, ElementMatchers.nameEndsWith("AvatarRenderState")))
                .and(ElementMatchers.returns(ElementMatchers.nameEndsWith("Identifier")));
        System.out.println("命中方法数 = " + td.getDeclaredMethods().filter(m).size());
        for (var x : td.getDeclaredMethods().filter(m)) System.out.println("  " + x);
        try {
            new ByteBuddy().redefine(td, loc)
                    .visit(Advice.to(AvatarTextureAdvice.class).on(m))
                    .make();
            System.out.println("WEAVED OK");
        } catch (Throwable t) {
            System.out.println("WEAVE FAIL: " + t);
            Throwable c = t.getCause();
            while (c != null) { System.out.println("  cause: " + c); c = c.getCause(); }
        }
    }
}
