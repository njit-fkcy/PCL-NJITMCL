import club.njitmcl.agent.SkinDebugAdvice;
import net.bytebuddy.ByteBuddy;
import net.bytebuddy.asm.Advice;
import net.bytebuddy.matcher.ElementMatchers;
import java.util.*;

public class WeaveCheck {
    public Map<String, String> getTextures(Object p, boolean s) { return new HashMap<String, String>(); }

    public static void main(String[] a) {
        try {
            new ByteBuddy().redefine(WeaveCheck.class)
                    .visit(Advice.to(SkinDebugAdvice.class).on(ElementMatchers.named("getTextures")))
                    .make();
            System.out.println("WEAVE OK");
        } catch (Throwable t) {
            System.out.println("WEAVE FAIL: " + t);
        }
    }
}
