import club.njitmcl.agent.SkinFutureAdvice;
import net.bytebuddy.ByteBuddy;
import net.bytebuddy.asm.Advice;
import net.bytebuddy.matcher.ElementMatchers;

public class FutWeave {
    public static void main(String[] a) throws Exception {
        try {
            new ByteBuddy().redefine(FutStub.class)
                    .visit(Advice.to(SkinFutureAdvice.class).on(ElementMatchers.named("registerTextures")))
                    .make()
                    .saveIn(new java.io.File("probe-src/woven"));
            System.out.println("WEAVED OK");
        } catch (Throwable t) {
            System.out.println("WEAVE FAIL: " + t);
        }
    }
}
