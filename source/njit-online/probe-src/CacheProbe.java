import club.njitmcl.agent.PeerSkin;
import club.njitmcl.agent.SkinCache;
import com.mojang.authlib.GameProfile;
import java.util.*;

public class CacheProbe {
    public static void main(String[] a) throws Exception {
        String id = "aaaabbbbccccddddeeeeffff00001111";
        Map<String, Object> p = new LinkedHashMap<String, Object>();
        p.put("name", "textures");
        p.put("value", "eyJ0ZXh0dXJlcyI6eyJTS0lOIjp7InVybCI6Imh0dHA6Ly94In19fQ==");
        p.put("signature", "c2ln");
        List<Object> list = new ArrayList<Object>();
        list.add(p);
        SkinCache.write(id, list);
        System.out.println("cache read = " + (SkinCache.read(id) == null ? "null" : SkinCache.read(id).size() + " 条"));

        GameProfile profile = new GameProfile(
                UUID.fromString("aaaa" + "bbbb" + "-cccc-dddd-eeee-ffff00001111"), "Cached");
        System.out.println("before = " + profile.getProperties().size());
        PeerSkin.fill(profile);
        System.out.println("after  = " + profile.getProperties().size()
                + " keys=" + profile.getProperties().asMap().keySet());
    }
}
