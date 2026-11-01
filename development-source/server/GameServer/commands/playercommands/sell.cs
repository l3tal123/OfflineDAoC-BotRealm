using System;
using DOL.Database;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;

namespace DOL.GS.Commands
{
	[CmdAttribute(
		"&sell",
		ePrivLevel.Player,
		"Sell items to a targeted merchant.  Specify a single bag, a range or all",
		"Use: /sell 4 to sell all items bag 4",
		"/sell 2-3 to sell all items in bags 2 and 3",
		"/sell all to sell everything in your bags (add nopot to keep potions)")]
	public class SellCommandHandler : AbstractCommandHandler, ICommandHandler
	{
		private const int BagCount = 5;
		private const int SlotsPerBag = 8;

		// Freeshard convenience items that must never be sold. The True 1.65 content removes them; this is the
		// second line of defence the command has always had.
		private static readonly string[] NeverSoldPackageIds = ["AtlasXPItem", "atlas_orbs_item", "atlas_potion"];

		public void OnCommand(GameClient client, string[] args)
		{
			GamePlayer player = client.Player;

			if (player == null || player.Inventory == null || args.Length < 2 ||
			    !TryParseSlotRange(args[1], out eInventorySlot first, out eInventorySlot last))
			{
				client.Out.SendMessage("Use: /sell <bag>, /sell <bag1-bag2>, /sell all", eChatType.CT_System, eChatLoc.CL_SystemWindow);
				return;
			}

			GameMerchant merchant = player.TargetObject as GameMerchant;
			GameGuardMerchant guardMerchant = player.TargetObject as GameGuardMerchant;
			if (merchant == null && guardMerchant == null)
			{
				client.Out.SendMessage("You must target a merchant.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
				return;
			}

			string merchantName = player.TargetObject.GetName(0, true);
			if (!player.TargetObject.IsWithinRadius(player, ServerProperties.Properties.WORLD_PICKUP_DISTANCE))
			{
				client.Out.SendMessage($"You are too far away from {merchantName}.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
				return;
			}

			bool skipPotions = Array.Exists(args, arg => string.Equals(arg, "nopot", StringComparison.OrdinalIgnoreCase));
			int sold = 0;
			long earned = 0;

			// GetItemRange returns a snapshot, so removing a sold item cannot disturb the loop.
			foreach (DbInventoryItem item in player.Inventory.GetItemRange(first, last))
			{
				if (item == null || Array.IndexOf(NeverSoldPackageIds, item.PackageID) >= 0)
					continue;
				if (skipPotions && item.Object_Type == 41)
					continue;

				// Silent appraisal: 0 means the merchant will not buy it (no-drop, quest item, or it doesn't trade in
				// that kind of item), so it stays where it is instead of producing one refusal message per item.
				long value = merchant != null
					? merchant.OnPlayerAppraise(player, item, true)
					: guardMerchant.OnPlayerAppraise(player, item, true);
				if (value <= 0)
					continue;

				int slot = item.SlotPosition;
				if (merchant != null)
					merchant.OnPlayerSell(player, item);
				else
					guardMerchant.OnPlayerSell(player, item);

				// The sale succeeded when the item is no longer in the slot it was taken from.
				if (player.Inventory.GetItem((eInventorySlot)slot) == null)
				{
					sold++;
					earned += value;
				}
			}

			if (sold == 0)
			{
				client.Out.SendMessage($"There was nothing in those bags that {merchantName} would buy.", eChatType.CT_Merchant, eChatLoc.CL_SystemWindow);
				return;
			}

			client.Out.SendMessage($"Sold {sold} item{(sold == 1 ? string.Empty : "s")} to {merchantName} for {Money.GetString(earned)}.", eChatType.CT_Merchant, eChatLoc.CL_SystemWindow);
		}

		/// <summary>Maps "/sell all", "/sell 4" and "/sell 2-3" to the backpack slots they name.</summary>
		private static bool TryParseSlotRange(string argument, out eInventorySlot first, out eInventorySlot last)
		{
			first = eInventorySlot.Invalid;
			last = eInventorySlot.Invalid;

			if (string.IsNullOrWhiteSpace(argument))
				return false;

			if (argument.Equals("all", StringComparison.OrdinalIgnoreCase))
			{
				first = eInventorySlot.FirstBackpack;
				last = eInventorySlot.LastBackpack;
				return true;
			}

			string[] bounds = argument.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			if (bounds.Length is < 1 or > 2 || !TryParseBag(bounds[0], out int firstBag))
				return false;

			int lastBag;
			if (bounds.Length == 1)
				lastBag = firstBag;
			else if (!TryParseBag(bounds[1], out lastBag))
				return false;

			if (firstBag > lastBag)
				(firstBag, lastBag) = (lastBag, firstBag);

			first = eInventorySlot.FirstBackpack + (firstBag - 1) * SlotsPerBag;
			last = eInventorySlot.FirstBackpack + (lastBag - 1) * SlotsPerBag + SlotsPerBag - 1;
			return true;
		}

		private static bool TryParseBag(string text, out int bag) =>
			int.TryParse(text, out bag) && bag is >= 1 and <= BagCount;
	}
}
