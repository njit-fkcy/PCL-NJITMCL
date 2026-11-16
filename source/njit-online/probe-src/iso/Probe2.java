public class Probe2 {
    public static String run() {
        return "mode=" + club.njitmcl.agent.AgentState.mode()
                + " mua=" + club.njitmcl.agent.AgentState.muaApi()
                + " debug=" + club.njitmcl.agent.AgentState.debug();
    }
}
