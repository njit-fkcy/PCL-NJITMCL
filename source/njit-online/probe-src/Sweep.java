import net.bytebuddy.description.type.TypeDescription;
import net.bytebuddy.dynamic.ClassFileLocator;
import net.bytebuddy.matcher.ElementMatcher;
import net.bytebuddy.matcher.ElementMatchers;
import net.bytebuddy.pool.TypePool;
import java.io.File;
import java.util.*;

public class Sweep {
    static final ElementMatcher<TypeDescription> VERIFIER = ElementMatchers.isInterface()
        .and(ElementMatchers.declaresMethod(ElementMatchers.takesArguments(2)
            .and(ElementMatchers.takesArgument(0, ElementMatchers.nameEndsWith("ServicesKeySet")))));

    public static void main(String[] a) throws Exception {
        String base = "C:/Users/m1520/AppData/Roaming/.minecraft/versions/";
        String[][] cases = {
            {"1.20.1", "apj"}, {"1.21.11", "bgw"},
            {"26.1.2-Forge_64.1.6", "net/minecraft/util/SignatureValidator"},
            {"26.3-Forge_66.1.5", "net/minecraft/util/SignatureValidator"}
        };
        for (String[] c : cases) {
            File jar = null;
            File dir = new File(base + c[0]);
            for (File f : dir.listFiles()) if (f.getName().endsWith(".jar") && !f.getName().contains("natives")) jar = f;
            ClassFileLocator loc = ClassFileLocator.ForJarFile.of(jar);
            TypePool pool = new TypePool.Default.WithLazyResolution(new TypePool.CacheProvider.Simple(), loc,
                    TypePool.Default.ReaderMode.FAST);
            TypeDescription td = pool.describe(c[1].replace('/', '.')).resolve();
            System.out.println(c[0] + "  " + c[1] + "  match=" + VERIFIER.matches(td));
        }
    }
}
