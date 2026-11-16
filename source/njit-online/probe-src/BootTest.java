import java.io.File;
import java.io.FileInputStream;
import java.io.ByteArrayOutputStream;

/** 模拟 KnotClassLoader / securejarhandler：父加载器只有 bootstrap，看不见 app 里的代理类。 */
public class BootTest {

    static final class IsoLoader extends ClassLoader {
        private final File dir;
        IsoLoader(File dir) {
            super(null);            // 父 = bootstrap，够不到 -cp
            this.dir = dir;
        }
        @Override
        protected Class<?> findClass(String name) throws ClassNotFoundException {
            File f = new File(dir, name.replace('.', '/') + ".class");
            if (!f.isFile()) {
                throw new ClassNotFoundException(name);
            }
            try {
                FileInputStream in = new FileInputStream(f);
                ByteArrayOutputStream out = new ByteArrayOutputStream();
                byte[] buf = new byte[8192];
                int n;
                while ((n = in.read(buf)) > 0) out.write(buf, 0, n);
                in.close();
                byte[] data = out.toByteArray();
                return defineClass(name, data, 0, data.length);
            } catch (Exception e) {
                throw new ClassNotFoundException(name, e);
            }
        }
    }

    public static void main(String[] a) throws Exception {
        IsoLoader loader = new IsoLoader(new File("probe-src/iso"));
        Class<?> probe = loader.loadClass("Probe2");
        Object value = probe.getMethod("run").invoke(null);
        System.out.println("隔离加载器里调用成功：" + value);
        System.out.println("Probe2 的加载器 = " + probe.getClassLoader().getClass().getSimpleName());
        Class<?> state = Class.forName("club.njitmcl.agent.AgentState", false, loader);
        System.out.println("AgentState 来自 = " + (state.getClassLoader() == null ? "bootstrap" : state.getClassLoader()));
    }
}
