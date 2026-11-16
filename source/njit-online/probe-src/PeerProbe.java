import club.njitmcl.agent.AgentState;
import club.njitmcl.agent.PeerSkin;
import com.mojang.authlib.GameProfile;
import java.util.UUID;

public class PeerProbe {
    public static void main(String[] a) throws Exception {
        AgentState.mode = AgentState.ModeDualMojangFirst;
        GameProfile online = new GameProfile(UUID.fromString("bd366481-9827-4cc8-b6bf-b68864d20627"), "ImQiez");
        System.out.println("正版玩家 before = " + online.getProperties().size());
        PeerSkin.fill(online);
        System.out.println("正版玩家 after  = " + online.getProperties().size()
                + " keys=" + online.getProperties().asMap().keySet());

        GameProfile offline = new GameProfile(UUID.randomUUID(), "Stranger");
        PeerSkin.fill(offline);
        System.out.println("离线陌生人 after = " + offline.getProperties().size() + "（应为 0）");

        AgentState.mode = AgentState.ModeMojang;
        GameProfile other = new GameProfile(UUID.fromString("bd366481-9827-4cc8-b6bf-b68864d20627"), "ImQiez");
        PeerSkin.fill(other);
        System.out.println("正版模式 after   = " + other.getProperties().size() + "（应为 0）");
    }
}
