import club.njitmcl.agent.*;
import net.bytebuddy.ByteBuddy;
import net.bytebuddy.asm.Advice;
import net.bytebuddy.description.type.TypeDescription;
import net.bytebuddy.dynamic.ClassFileLocator;
import net.bytebuddy.matcher.ElementMatchers;
import net.bytebuddy.pool.TypePool;
import java.io.File;

public class WeaveSkin {
    public static void main(String[] a) throws Exception {
        File jar = new File("C:/Users/m1520/AppData/Roaming/.minecraft/versions/26.3-Forge_66.1.5/26.3-Forge_66.1.5.jar");
        ClassFileLocator loc = ClassFileLocator.ForJarFile.of(jar);
        TypePool pool = new TypePool.Default.WithLazyResolution(new TypePool.CacheProvider.Simple(), loc,
                TypePool.Default.ReaderMode.FAST);
        TypeDescription td = pool.describe("net.minecraft.world.entity.player.PlayerSkin").resolve();
        var m = ElementMatchers.named("secure")
                .and(ElementMatchers.takesArguments(0))
                .and(ElementMatchers.returns(ElementMatchers.named("boolean")));
        System.out.println("命中方法数 = " + td.getDeclaredMethods().filter(m).size());
        try {
            new ByteBuddy().redefine(td, loc)
                    .visit(Advice.to(SkinSecureFlagAdvice.class).on(m))
                    .make();
            System.out.println("WEAVED OK");
        } catch (Throwable t) {
            System.out.println("WEAVE FAIL: " + t);
        }
    }
}
