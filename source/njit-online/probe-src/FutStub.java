import java.util.concurrent.CompletableFuture;

public class FutStub {
    public CompletableFuture<String> registerTextures(Object a, Object b) {
        return CompletableFuture.completedFuture("done-value");
    }
    public static void main(String[] x) throws Exception {
        System.out.println(new FutStub().registerTextures("a", "b").get());
        Thread.sleep(300);
    }
}
