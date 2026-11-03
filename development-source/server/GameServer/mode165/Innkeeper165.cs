using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>
    /// The innkeeper of a BotRealm traveler's inn (owner 2026-10-30): talking to them opens the companion hiring
    /// window (Hire165), where companions are hired, summoned, dismissed and released. Placed only in the BotRealm database
    /// (tools/claude-version/botrealm/build_inns165.py).
    /// </summary>
    public class Innkeeper165 : GameNPC
    {
        public override bool AddToWorld()
        {
            if (!base.AddToWorld())
                return false;
            InnQuestMarkers165.Register(this);
            return true;
        }

        public override bool RemoveFromWorld()
        {
            InnQuestMarkers165.Unregister(this);
            return base.RemoveFromWorld();
        }

        /// <summary>The turn-in icon while the player's "The Traveler's Inn" quest is open (InnQuestMarkers165).</summary>
        public override eQuestIndicator GetQuestIndicator(GamePlayer player) =>
            InnQuestMarkers165.TurnsInFor(this, player) ? eQuestIndicator.Finish : base.GetQuestIndicator(player);

        public override bool Interact(GamePlayer player)
        {
            if (!base.Interact(player))
                return false;
            TurnTo(player, 5000);
            if (!Classic165Mode.IsOn)
                return true;
            player.Out.SendMessage($"{GetName(0, true)} says, \"Welcome, {player.Name}. Looking for company on the road? " +
                "Pick who you'd like, and I'll send them along with you.\"", eChatType.CT_Say, eChatLoc.CL_ChatWindow);
            Hire165.OpenInnkeeper(player, this);
            return true;
        }
    }
}
