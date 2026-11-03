using System;
using System.Collections.Generic;
using DOL.Database;
using DOL.GS.PacketHandler;

namespace DOL.GS.Quests
{
    /// <summary>
    /// BotRealm (plan section 4, epic quests): helpers for the three level 50 finales that run as the period quests
    /// in BotRealm, rebuilt from the period walkthroughs (Camelot Warcry spoilers, Allakhazam quest pages):
    /// Shadows50 = Lord of Deceit (Captain Rhodri, Lord Elidyn), Academy50 = Symbol of the Broken (started by Veteran
    /// Guardsman Esmond at Cornwall Station), Harmony50 = Last Heir (Cailean, Donnchadh, Chieftess Niamh, Talriese).
    /// Normal mode never calls these.
    /// </summary>
    public static class Epics165
    {
        /// <summary>The experience every scripted level 50 finale already gives (kept as the server's number).</summary>
        public const long FinaleXp = 1937768448;

        /// <summary>Has the player finished the earlier part of the epic line, the classic data quest of that name and level
        /// (the 43/45/47-48 parts are DataQuest rows that all carry the line's name)?</summary>
        public static bool FinishedPart(GamePlayer player, string questName, int minLevel)
        {
            foreach (AbstractQuest quest in player.GetFinishedQuests())
            {
                if (quest is DataQuest dataQuest && dataQuest.DBDataQuest != null && dataQuest.DBDataQuest.MinLevel == minLevel &&
                    string.Equals(dataQuest.DBDataQuest.Name, questName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>The named NPC of a realm in a region (the first one), or null.</summary>
        public static GameNPC FindNpc(string name, eRealm realm, ushort region)
        {
            foreach (GameNPC npc in WorldMgr.GetNPCsByName(name, realm))
                if (npc.CurrentRegionID == region)
                    return npc;
            return null;
        }

        /// <summary>The trainers who give the earlier part of an epic line (the StartName of its data quest rows), so the
        /// finale is offered by the same trainers the period journal sends the player to.</summary>
        public static List<GameNPC> TrainersOf(string questName, int minLevel, eRealm realm)
        {
            var found = new List<GameNPC>();
            foreach (DbDataQuest row in GameServer.Database.SelectAllObjects<DbDataQuest>())
            {
                if (row.MinLevel != minLevel || !string.Equals(row.Name, questName, StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (GameNPC npc in WorldMgr.GetNPCsByName(row.StartName, realm))
                    if (npc.CurrentRegionID == row.StartRegionID && npc is GameTrainer && !found.Contains(npc))
                        found.Add(npc);
            }
            return found;
        }

        /// <summary>Is this the player's own class trainer (the "return to your trainer" steps)?</summary>
        public static bool IsOwnTrainer(GamePlayer player, GameObject target) =>
            target is GameTrainer trainer && trainer.Realm == player.Realm && trainer.TrainedClass != eCharacterClass.Unknown &&
            trainer.CanTrain(player);

        /// <summary>Is the target the named NPC of the player's realm?</summary>
        public static bool IsNamed(GamePlayer player, GameObject target, string name) =>
            target is GameNPC npc && npc.Realm == player.Realm && npc.Name == name;

        /// <summary>The six free backpack slots a full suit of epic armour needs; tells the player when they are missing.</summary>
        public static bool HasRoomForArmour(GamePlayer player)
        {
            if (player.Inventory.IsSlotsFree(6, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack))
                return true;
            player.Out.SendMessage("You do not have enough free space in your inventory!", eChatType.CT_Important, eChatLoc.CL_SystemWindow);
            return false;
        }
    }
}
