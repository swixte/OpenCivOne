namespace OpenCivOne.Graphics
{
	/// <summary>
	/// A painted view of the map window, shown over the classic map the game draws (see GBitmap.Overlay): the isometric
	/// view (IsoView), or the top-down view of a painted map's towns and units (TopView)
	/// </summary>
	public interface IMapView
	{
		/// <summary>
		/// The size of the view in HD pixels
		/// </summary>
		int Width { get; }

		int Height { get; }

		/// <summary>
		/// The last rendered view, 0xAARRGGBB; premultiplied by alpha when Blends
		/// </summary>
		uint[] Pixels { get; }

		/// <summary>
		/// Whether the view is drawn over the classic map's HD pixels (only its objects; the classic ground and water show
		/// where it is clear) rather than covering the whole window
		/// </summary>
		bool Blends { get; }

		/// <summary>
		/// Whether the view draws the cities itself, so the classic map leaves out the city squares, sizes and names
		/// </summary>
		bool DrawsCities { get; }

		/// <summary>
		/// Whether the view draws units of a type itself, so the classic map leaves them out
		/// </summary>
		bool DrawsUnit(int type);

		/// <summary>
		/// Whether the view draws the huts and special resources itself, so the classic map leaves them out
		/// </summary>
		bool DrawsFeatures { get; }

		/// <summary>
		/// Renders the view centred on a point in map cells; false when nothing it shows changed
		/// </summary>
		bool Render(float cx, float cy, IsoView.Scene scene);

		uint[]? MoveFrame(float t, (int X, int Y) from, (int X, int Y) to);

		void EndMove();

		bool HasBattleArt { get; }

		uint[] BattleFrame(float t, (int X, int Y) attacker, (int X, int Y) defender, bool attackerWins, int seed);

		void EndBattle();

		/// <summary>
		/// The map cell shown under a point of the view, in HD pixels, for a view centred on (cx, cy)
		/// </summary>
		(int X, int Y) CellAt(float px, float py, float cx, float cy);
	}
}
