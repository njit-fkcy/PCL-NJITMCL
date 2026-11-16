import club.njitmcl.agent.PlayerEntryAdvice;
import club.njitmcl.agent.SkinRenderAdvice;
import net.bytebuddy.ByteBuddy;
import net.bytebuddy.asm.Advice;
import net.bytebuddy.description.type.TypeDescription;
import net.bytebuddy.dynamic.ClassFileLocator;
import net.bytebuddy.matcher.ElementMatchers;
import net.bytebuddy.pool.TypePool;
import java.io.File;

public class WeaveTwo {
    static ClassFileLocator loc;
    static TypePool pool;

    public static void main(String[] a) throws Exception {
        File jar = new File("C:/Users/m1520/AppData/Roaming/.minecraft/libraries/net/minecraftforge/forge/26.3-66.1.5/forge-26.3-66.1.5-client.jar");
        File uni = new File("C:/Users/m1520/AppData/Roaming/.minecraft/libraries/net/minecraftforge/forge/26.3-66.1.5/forge-26.3-66.1.5-universal.jar");
        File auth = new File("C:/Users/m1520/AppData/Roaming/.minecraft/libraries/com/mojang/authlib/10.0.77/authlib-10.0.77.jar");
        loc = new ClassFileLocator.Compound(
                ClassFileLocator.ForJarFile.of(jar),
                ClassFileLocator.ForJarFile.of(uni),
                ClassFileLocator.ForJarFile.of(auth),
                ClassFileLocator.ForJarFile.of(new File("C:/Users/m1520/AppData/Roaming/.minecraft/libraries/it/unimi/dsi/fastutil/8.5.18/fastutil-8.5.18.jar")),
                ClassFileLocator.ForClassLoader.ofSystemLoader());
        pool = new TypePool.Default.WithLazyResolution(new TypePool.CacheProvider.Simple(), loc,
                TypePool.Default.ReaderMode.FAST);

        check("net.minecraft.network.protocol.game.ClientboundPlayerInfoUpdatePacket$Entry",
                ElementMatchers.isConstructor().and(ElementMatchers.takesArguments(9)),
                PlayerEntryAdvice.class);

        check("net.minecraft.client.player.AbstractClientPlayer",
                ElementMatchers.takesArguments(0).and(ElementMatchers.returns(
                        ElementMatchers.named("net.minecraft.world.entity.player.PlayerSkin"))),
                SkinRenderAdvice.class);
    }

    static void check(String name, net.bytebuddy.matcher.ElementMatcher<net.bytebuddy.description.method.MethodDescription> m, Class<?> advice) {
        try {
            TypeDescription td = pool.describe(name).resolve();
            int hits = td.getDeclaredMethods().filter(m).size();
            System.out.println(name + "  命中=" + hits);
            new ByteBuddy().redefine(td, loc).visit(Advice.to(advice).on(m)).make();
            System.out.println("  WEAVED OK");
        } catch (Throwable t) {
            System.out.println("  FAIL: " + t);
        }
    }
}
