public class ApjProbe {
    public static void main(String[] a) throws Exception {
        Class<?> c = Class.forName("apj");
        System.out.println("loaded " + c.getName() + " iface=" + c.isInterface());
        Thread.sleep(4000);
        System.out.println("done");
    }
}
