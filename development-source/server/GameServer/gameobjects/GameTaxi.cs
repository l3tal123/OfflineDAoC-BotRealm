using DOL.AI.Brain;

namespace DOL.GS
{
	/// <summary>
	/// 
	/// </summary>
	public class GameTaxi : GameNPC
	{
		public GameTaxi() : base()
		{
			Model = 449;
			MaxSpeedBase = 650;
			Size = 50;
			Level = 55;
			Name = "horse";
			Flags |= eFlags.DONTSHOWNAME;   // a stable horse shows no name over it (owner 2026-10-31)
			BlankBrain brain = new BlankBrain();
			SetOwnBrain(brain);
		}
		
		public GameTaxi(INpcTemplate templateid) : base(templateid)
		{
			Flags |= eFlags.DONTSHOWNAME;
			BlankBrain brain = new BlankBrain();
			SetOwnBrain(brain);
		}

		/// <summary>
		/// The stable horse size for a rider's race (small races ride smaller horses). Used for players' and gamebots'
		/// horses alike (owner 2026-11-01: an Inconnu bot rode a bigger horse than a Briton player).
		/// </summary>
		public static byte SizeForRace(short race)
		{
			return (eRace)race switch
			{
				eRace.Lurikeen => 38,
				eRace.Kobold => 38,
				eRace.Dwarf => 42,
				eRace.Inconnu => 45,
				eRace.Frostalf => 48,
				eRace.Shar => 48,
				eRace.Briton => 50,
				eRace.Saracen => 48,
				eRace.Celt => 50,
				eRace.Valkyn => 52,
				eRace.Avalonian => 52,
				eRace.Highlander => 55,
				eRace.Norseman => 50,
				eRace.Elf => 52,
				eRace.Sylvan => 55,
				eRace.Firbolg => 62,
				eRace.HalfOgre => 62,
				eRace.AlbionMinotaur => 65,
				eRace.MidgardMinotaur => 65,
				eRace.HiberniaMinotaur => 65,
				eRace.Troll => 67,
				_ => 50,
			};
		}

		public override bool IsVisibleToPlayers => true;

		public override int MAX_PASSENGERS
		{
			get
			{
				return 1;
			}
		}

		public override int SLOT_OFFSET
		{
			get
			{
				return 0;
			}
		}
	}
}
