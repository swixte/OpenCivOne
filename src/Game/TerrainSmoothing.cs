namespace OpenCivOne
{
	/// <summary>
	/// An optional pass after the classic map generator (off by default, see GlobalPreferences.TerrainSmoothing) that
	/// gathers the generator's scattered single tiles into coherent regions, keeps terrain within sensible latitudes and
	/// gathers lone mountains into ranges, while water and rivers stay exactly as generated and every land terrain keeps
	/// about the number of tiles it had, so the game's balance is unchanged. It uses no randomness: the same map always
	/// gives the same result.
	/// </summary>
	public static class TerrainSmoothing
	{
		private const int Desert = 0, Plains = 1, Grassland = 2, Forest = 3, Hills = 4, Mountains = 5, Tundra = 6, Arctic = 7,
			Swamp = 8, Jungle = 9, Water = 10, River = 11;
		private const int Width = 80, Height = 50;

		private static readonly (int X, int Y)[] Eight = { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) };

		/// <summary>
		/// Smooths the terrain of a map held as terrain values (TerrainTypeEnum as int), in place
		/// </summary>
		public static void Apply(int[,] map)
		{
			int[] original = Count(map);

			for (int y = 0; y < Height; y++)
				for (int x = 0; x < Width; x++)
					if (Movable(map[x, y]) && !Allowed(map[x, y], y))
						map[x, y] = Substitute(map[x, y], y);

			for (int pass = 0; pass < 4; pass++)
				MajorityPass(map);

			GatherMountains(map);
			AddFoothills(map);
			RemoveForbiddenNeighbours(map);
			Rebalance(map, original);
			RemoveForbiddenNeighbours(map);
		}

		/// <summary>
		/// After the polar caps are drawn: a single tundra tile on the polar rows (no tundra among its eight neighbours)
		/// becomes ice. Land stays land, so coastlines, continents and rivers are untouched.
		/// </summary>
		public static void CleanPolarRows(int[,] map)
		{
			var lone = new List<(int X, int Y)>();

			foreach (int y in new[] { 0, 1, Height - 2, Height - 1 })
				for (int x = 0; x < Width; x++)
					if (map[x, y] == Tundra && !Around(map, x, y).Contains(Tundra))
						lone.Add((x, y));

			// each speck turned to ice gives its tile back at the edge of a real tundra region on the polar rows, so the
			// amount of tundra stays the same
			foreach ((int x, int y) in lone)
			{
				map[x, y] = Arctic;
				(int X, int Y) best = (-1, -1);
				int bestScore = 0;

				foreach (int ry in new[] { 0, 1, Height - 2, Height - 1 })
				{
					for (int rx = 0; rx < Width; rx++)
					{
						if (map[rx, ry] != Arctic || (rx, ry) == (x, y))
							continue;

						// beside tundra, edge neighbours counting double, so the specks gather into strips
						int score = 0;

						foreach ((int dx, int dy) in Eight)
						{
							int ny = ry + dy;

							if (ny >= 0 && ny < Height && map[Wrap(rx + dx), ny] == Tundra)
								score += dx == 0 || dy == 0 ? 2 : 1;
						}

						if (score > bestScore)
						{
							bestScore = score;
							best = (rx, ry);
						}
					}
				}

				if (best.X >= 0)
					map[best.X, best.Y] = Tundra;
			}
		}

		// water and rivers are never changed, nor turned into
		private static bool Movable(int t) => t != Water && t != River;

		private static int Wrap(int x) => ((x % Width) + Width) % Width;

		private static float Latitude(int y) => MathF.Abs(y - ((Height - 1) / 2f)) / ((Height - 1) / 2f);

		// arctic only within three rows of a pole; desert and jungle only within 60% of the way from the equator
		private static bool Allowed(int t, int y)
		{
			if (t == Arctic)
				return y < 3 || y > Height - 4;

			if (t == Desert || t == Jungle)
				return Latitude(y) <= 0.6f;

			return true;
		}

		private static int Substitute(int t, int y) => t switch
		{
			Arctic => Tundra,
			Desert => Plains,
			Jungle => Latitude(y) > 0.75f ? Forest : Grassland,
			_ => t,
		};

		private static int[] Count(int[,] map)
		{
			int[] count = new int[12];

			foreach (int t in map)
				count[t]++;

			return count;
		}

		private static IEnumerable<int> Around(int[,] map, int x, int y)
		{
			foreach ((int dx, int dy) in Eight)
			{
				int ny = y + dy;

				if (ny >= 0 && ny < Height)
					yield return map[Wrap(x + dx), ny];
			}
		}

		// a land tile that shares its type with at most one of its eight neighbours takes the type most of its land
		// neighbours have, where that type may stand at its latitude
		private static void MajorityPass(int[,] map)
		{
			int[,] next = (int[,])map.Clone();

			for (int y = 0; y < Height; y++)
			{
				for (int x = 0; x < Width; x++)
				{
					int t = map[x, y];

					if (!Movable(t))
						continue;

					int[] votes = new int[12];

					foreach (int n in Around(map, x, y))
						votes[n]++;

					if (votes[t] > 1)
						continue;

					int best = t, bestVotes = votes[t];

					for (int c = 0; c < 12; c++)
					{
						if (Movable(c) && votes[c] > bestVotes && Allowed(c, y))
						{
							best = c;
							bestVotes = votes[c];
						}
					}

					next[x, y] = best;
				}
			}

			Array.Copy(next, map, map.Length);
		}

		// a mountain with no other mountain within two cells becomes hills, so mountains stand in ranges
		private static void GatherMountains(int[,] map)
		{
			var lone = new List<(int, int)>();

			for (int y = 0; y < Height; y++)
			{
				for (int x = 0; x < Width; x++)
				{
					if (map[x, y] != Mountains)
						continue;

					bool near = false;

					for (int dy = -2; dy <= 2 && !near; dy++)
						for (int dx = -2; dx <= 2 && !near; dx++)
							near = (dx != 0 || dy != 0) && y + dy >= 0 && y + dy < Height && map[Wrap(x + dx), y + dy] == Mountains;

					if (!near)
						lone.Add((x, y));
				}
			}

			foreach ((int x, int y) in lone)
				map[x, y] = Hills;
		}

		// every mountain range gets hills on at least one side, and every river rises beside hills or mountains: the
		// lowland tile beside it with the most land around it becomes hills (the balance pass makes up the counts)
		private static void AddFoothills(int[,] map)
		{
			var seen = new bool[Width, Height];

			for (int y = 0; y < Height; y++)
			{
				for (int x = 0; x < Width; x++)
				{
					if (map[x, y] != Mountains || seen[x, y])
						continue;

					// the range: mountains joined across diagonals too
					var range = new List<(int X, int Y)>();
					var stack = new Stack<(int, int)>();
					stack.Push((x, y));
					seen[x, y] = true;

					while (stack.Count > 0)
					{
						(int cx, int cy) = stack.Pop();
						range.Add((cx, cy));

						foreach ((int dx, int dy) in Eight)
						{
							int nx = Wrap(cx + dx), ny = cy + dy;

							if (ny >= 0 && ny < Height && !seen[nx, ny] && map[nx, ny] == Mountains)
							{
								seen[nx, ny] = true;
								stack.Push((nx, ny));
							}
						}
					}

					if (!range.Exists(c => Around(map, c.X, c.Y).Contains(Hills)))
						MakeHillBeside(map, range);
				}
			}

			for (int y = 0; y < Height; y++)
			{
				for (int x = 0; x < Width; x++)
				{
					if (map[x, y] != River)
						continue;

					int rivers = 0;
					bool sea = false;

					foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
					{
						int ny = y + dy;

						if (ny >= 0 && ny < Height)
						{
							rivers += map[Wrap(x + dx), ny] == River ? 1 : 0;
							sea |= map[Wrap(x + dx), ny] == Water;
						}
					}

					var around = Around(map, x, y).ToList();

					if (rivers <= 1 && !sea && !around.Contains(Hills) && !around.Contains(Mountains))
						MakeHillBeside(map, new List<(int X, int Y)> { (x, y) });
				}
			}
		}

		private static void MakeHillBeside(int[,] map, List<(int X, int Y)> cells)
		{
			int bestX = -1, bestY = -1, bestScore = -1;

			foreach ((int cx, int cy) in cells)
			{
				foreach ((int dx, int dy) in Eight)
				{
					int nx = Wrap(cx + dx), ny = cy + dy;

					if (ny < 0 || ny >= Height)
						continue;

					int t = map[nx, ny];

					if (!Movable(t) || t == Mountains || t == Arctic)
						continue;

					int score = Around(map, nx, ny).Count(Movable) + (t is Plains or Grassland or Tundra ? 4 : 0);

					if (score > bestScore)
					{
						bestScore = score;
						bestX = nx;
						bestY = ny;
					}
				}
			}

			if (bestX >= 0)
				map[bestX, bestY] = Hills;
		}

		// no jungle beside desert or arctic, no desert beside arctic or swamp
		private static void RemoveForbiddenNeighbours(int[,] map)
		{
			for (int y = 0; y < Height; y++)
			{
				for (int x = 0; x < Width; x++)
				{
					int t = map[x, y];
					var around = Around(map, x, y).ToList();

					if (t == Jungle && (around.Contains(Desert) || around.Contains(Arctic)))
						map[x, y] = Grassland;
					else if (t == Desert && (around.Contains(Arctic) || around.Contains(Swamp)))
						map[x, y] = Plains;
				}
			}
		}

		// brings each land terrain back to about the number of tiles it had (within 2% of the land), growing the short
		// ones at the edges of their own regions over terrain that has too many, so no new single tiles appear; a
		// terrain with nothing left to grow from is seeded where it fits best (swamp by water, jungle and desert towards
		// the equator, tundra and arctic towards the poles) and grows from there
		private static void Rebalance(int[,] map, int[] original)
		{
			int land = 0;

			for (int t = 0; t < 12; t++)
				if (Movable(t))
					land += original[t];

			int tolerance = 2;
			var stuck = new HashSet<int>();

			for (int round = 0; round < 3000; round++)
			{
				int[] now = Count(map);
				int shortest = -1, deficit = tolerance;

				for (int t = 0; t < 12; t++)
				{
					if (Movable(t) && !stuck.Contains(t) && original[t] - now[t] > deficit)
					{
						deficit = original[t] - now[t];
						shortest = t;
					}
				}

				if (shortest < 0)
					break;

				if (!Place(map, shortest, now, original, grow: true) && !Place(map, shortest, now, original, grow: false))
					stuck.Add(shortest);
			}
		}

		// puts one tile of a terrain that is short: beside its own kind when growing, else as a seed where it fits
		private static bool Place(int[,] map, int kind, int[] now, int[] original, bool grow)
		{
			int bestX = -1, bestY = -1;
			float bestScore = 0;

			for (int y = 0; y < Height; y++)
			{
				if (!Allowed(kind, y))
					continue;

				for (int x = 0; x < Width; x++)
				{
					int t = map[x, y];

					if (!Movable(t) || t == kind || now[t] <= original[t])
						continue;

					var around = Around(map, x, y).ToList();

					if (kind == Jungle && (around.Contains(Desert) || around.Contains(Arctic)))
						continue;

					if (kind == Desert && (around.Contains(Arctic) || around.Contains(Swamp)))
						continue;

					int same = around.Count(n => n == kind);
					float score;

					if (grow)
					{
						if (same == 0)
							continue;

						// beside as much of its own kind as possible, taking from the middle of a donor region rather
						// than leaving a lone tile of the donor behind
						score = (same * 4) + around.Count(n => n == t);
					}
					else
					{
						float lat = Latitude(y);
						score = 1 + around.Count(n => n == t) + kind switch
						{
							Swamp => around.Count(n => n == Water || n == River) * 3,
							Jungle or Desert => (1 - lat) * 10,
							Tundra or Arctic => lat * 10,
							Forest => around.Count(n => n == Grassland) * 2,
							Mountains => around.Count(n => n == Hills) * 3,
							Hills => around.Count(n => n == Mountains) * 3,
							_ => 0,
						};
					}

					if (score > bestScore)
					{
						bestScore = score;
						bestX = x;
						bestY = y;
					}
				}
			}

			if (bestX < 0)
				return false;

			map[bestX, bestY] = kind;
			return true;
		}
	}
}
