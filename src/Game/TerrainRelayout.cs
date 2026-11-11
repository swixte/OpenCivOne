namespace OpenCivOne
{
	/// <summary>
	/// Re-lays the land terrain of a map made by the classic generator from smooth fields, so it reads as real
	/// geography: coastlines, continents, lakes and rivers stay exactly as generated, and every land terrain keeps its
	/// exact number of tiles (arctic only where it may stand, at the poles; the rest of its tiles shared out among the
	/// other terrains in proportion), but where each terrain goes follows elevation, moisture and temperature: mountain
	/// ranges along ridges with hills on their flanks, tundra in the cold, desert in the dry tropics, jungle and swamp in
	/// the wet tropics and lowlands, forest where it is wet, grassland and plains between. It uses no randomness of its
	/// own; its noise is seeded from the map, so the same map always gives the same result.
	/// </summary>
	public static class TerrainRelayout
	{
		private const int Desert = 0, Plains = 1, Grassland = 2, Forest = 3, Hills = 4, Mountains = 5, Tundra = 6, Arctic = 7,
			Swamp = 8, Jungle = 9, Water = 10, River = 11;
		private const int Width = 80, Height = 50;

		private static readonly (int X, int Y)[] Four = { (1, 0), (-1, 0), (0, 1), (0, -1) };
		private static readonly (int X, int Y)[] Eight = { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) };

		public static void Apply(int[,] map)
		{
			int seed = 17;

			for (int x = 0; x < Width; x++)
				for (int y = 0; y < Height; y++)
					seed = unchecked((seed * 31) + map[x, y]);

			var cells = new List<(int X, int Y)>();
			int[] count = new int[12];

			for (int y = 0; y < Height; y++)
			{
				for (int x = 0; x < Width; x++)
				{
					int t = map[x, y];

					// the outermost rows become the polar caps after this, so they are left as they are
					if (t == Water || t == River || y == 0 || y == Height - 1)
						continue;

					cells.Add((x, y));
					count[t]++;
				}
			}

			if (cells.Count == 0)
				return;

			// how far each land cell is from the sea, for elevation that rises inland
			float[,] inland = CoastDistance(map);

			// the fields: elevation (ridged, rising inland), moisture (wetter by water and rivers), temperature (by
			// latitude, cooler with height)
			var elevation = new Dictionary<(int, int), float>();
			var moisture = new Dictionary<(int, int), float>();
			var temperature = new Dictionary<(int, int), float>();

			foreach ((int x, int y) in cells)
			{
				float ridge = 1 - MathF.Abs((Fbm(x, y, 16, seed + 1) * 2) - 1);
				float e = (ridge * ridge * 0.65f) + (Fbm(x, y, 16, seed + 2) * 0.2f) + (MathF.Min(inland[x, y], 5) / 5 * 0.25f);
				elevation[(x, y)] = e;

				float wet = 0;

				for (int dy = -2; dy <= 2; dy++)
					for (int dx = -2; dx <= 2; dx++)
						if (y + dy >= 0 && y + dy < Height && map[Wrap(x + dx), y + dy] is Water or River)
							wet += 1f / (1 + Math.Abs(dx) + Math.Abs(dy));

				// broad wet and dry regions, with smaller variations inside them so no island is all one kind
				moisture[(x, y)] = (Fbm(x, y, 16, seed + 3) * 0.6f) + (Fbm(x, y, 8, seed + 6) * 0.3f) + (MathF.Min(wet, 3) / 3 * 0.1f);
				temperature[(x, y)] = 1 - Latitude(y) + ((Fbm(x, y, 16, seed + 4) - 0.5f) * 0.12f) - (e * 0.18f);
			}

			// the number of tiles of each terrain: arctic only at the poles, its other tiles shared out in proportion
			int polar = cells.Count(c => c.Y < 3 || c.Y > Height - 4);
			int arctic = Math.Min(count[Arctic], polar);
			int spare = count[Arctic] - arctic;
			int[] target = (int[])count.Clone();
			target[Arctic] = arctic;
			int others = cells.Count - count[Arctic];

			// arctic that may not stay is shared out among the other terrains in proportion to their counts, so each
			// keeps its share of the land that is not ice
			if (spare > 0 && others > 0)
			{
				int given = 0;
				int[] kinds = { Desert, Plains, Grassland, Forest, Hills, Mountains, Tundra, Swamp, Jungle };
				var remainders = new List<(double Fraction, int Kind)>();

				foreach (int k in kinds)
				{
					double exact = (double)count[k] * spare / others;
					int share = (int)exact;
					target[k] += share;
					given += share;
					remainders.Add((exact - share, k));
				}

				foreach (var r in remainders.OrderByDescending(r => r.Fraction).ThenBy(r => r.Kind).Take(spare - given))
					target[r.Kind]++;
			}

			// rivers rise in high ground: the land around each river's source is lifted
			for (int y = 0; y < Height; y++)
			{
				for (int x = 0; x < Width; x++)
				{
					if (map[x, y] != River)
						continue;

					int rivers = 0;
					bool sea = false;

					foreach ((int dx, int dy) in Four)
					{
						if (y + dy < 0 || y + dy >= Height)
							continue;

						rivers += map[Wrap(x + dx), y + dy] == River ? 1 : 0;
						sea |= map[Wrap(x + dx), y + dy] == Water;
					}

					if (rivers > 1 || sea)
						continue;

					foreach ((int dx, int dy) in Eight)
						if (elevation.ContainsKey((Wrap(x + dx), y + dy)))
							elevation[(Wrap(x + dx), y + dy)] += 0.35f;
				}
			}

			var free = new HashSet<(int, int)>(cells);
			var result = new Dictionary<(int, int), int>();

			void Take(int kind, int n, Func<(int X, int Y), float> score, Func<(int X, int Y), bool>? allowed = null)
			{
				var picks = free.Where(c => allowed == null || allowed(c)).OrderByDescending(score).ThenBy(c => c.Item2).ThenBy(c => c.Item1).Take(n).ToList();

				foreach (var c in picks)
				{
					result[c] = kind;
					free.Remove(c);
				}

				// any that could not be placed where they may stand go to grassland and plains below
				if (picks.Count < n)
				{
					target[Grassland] += (n - picks.Count + 1) / 2;
					target[Plains] += (n - picks.Count) / 2;
				}
			}

			bool Tropic((int X, int Y) c) => Latitude(c.Y) <= 0.6f;

			// the order matters: each terrain takes the cells that suit it best of those still free
			Take(Arctic, target[Arctic], c => -temperature[c], c => c.Y < 3 || c.Y > Height - 4);
			Take(Mountains, target[Mountains], c => elevation[c]);
			Take(Hills, target[Hills], c => elevation[c] + (NearKind(result, c, Mountains) * 0.2f));
			Take(Tundra, target[Tundra], c => -temperature[c]);
			Take(Desert, target[Desert], c => temperature[c] - (moisture[c] * 1.4f), Tropic);
			Take(Jungle, target[Jungle], c => temperature[c] + (moisture[c] * 1.2f) - (NearKind(result, c, Desert) * 0.5f), Tropic);
			Take(Swamp, target[Swamp], c => moisture[c] - (elevation[c] * 1.2f) + (Latitude(c.Y) < 0.75f ? 0 : -1));
			Take(Forest, target[Forest], c => moisture[c] + (Fbm(c.X, c.Y, 16, seed + 5) * 0.3f) + (Latitude(c.Y) * 0.5f));
			Take(Grassland, target[Grassland], c => moisture[c] + (temperature[c] * 0.2f));

			foreach (var c in free.ToList())
				result[c] = Plains;

			// jungle never beside desert or arctic, desert never beside arctic or swamp: swap such a jungle or desert
			// tile with the nearest plains or grassland tile that is free of the conflict, so the counts stay exact
			for (int pass = 0; pass < 3; pass++)
			{
				foreach (var c in cells)
				{
					int t = result[c];

					if (!Conflict(result, c, t))
						continue;

					var swap = cells.Where(o => result[o] is Plains or Grassland && (t != Desert && t != Jungle || Tropic(o)) && !Conflict(result, o, t))
						.OrderBy(o => Distance(c, o)).FirstOrDefault((-1, -1));

					if (swap.Item1 < 0)
						continue;

					result[c] = result[swap];
					result[swap] = t;
				}
			}

			Consolidate(result, cells);
			JoinLoneTiles(map, result, cells);
			BreakUpLargeRegions(map, result, cells, seed);
			JoinLoneTiles(map, result, cells);

			foreach (var c in cells)
				map[c.X, c.Y] = result[c];
		}

		// gathers stray tiles into their neighbours' regions by swapping pairs, so every terrain keeps its exact count: a
		// tile that has few of its own kind around it changes places with a nearby tile of the kind around it that would
		// itself be better placed where the first one is; a swap is made only when it adds to how many tiles touch their
		// own kind, and never breaks the latitude limits or the rules on neighbours
		private static void Consolidate(Dictionary<(int, int), int> result, List<(int X, int Y)> cells)
		{
			int Same((int X, int Y) c, int kind, (int, int)? ignore = null)
			{
				int n = 0;

				// neighbours sharing an edge count double: they are what joins tiles into one region
				foreach ((int dx, int dy) in Eight)
				{
					var o = (Wrap(c.X + dx), c.Y + dy);

					if (o != ignore && result.TryGetValue(o, out int k) && k == kind)
						n += dx == 0 || dy == 0 ? 2 : 1;
				}

				return n;
			}

			bool Fits(int kind, (int X, int Y) c) => kind switch
			{
				Arctic => c.Y < 3 || c.Y > Height - 4,
				Desert or Jungle => Latitude(c.Y) <= 0.6f,
				_ => true,
			};

			for (int pass = 0; pass < 12; pass++)
			{
				int swaps = 0;

				foreach (var a in cells)
				{
					int ta = result[a];

					if (ta == Arctic || Same(a, ta) > 5)
						continue;

					// the kind most common around it
					int[] votes = new int[12];

					foreach ((int dx, int dy) in Eight)
						if (result.TryGetValue((Wrap(a.X + dx), a.Y + dy), out int k))
							votes[k]++;

					int tb = Array.IndexOf(votes, votes.Max());

					if (tb == ta || tb == Arctic || !Fits(tb, a))
						continue;

					(int, int) best = (-1, -1);
					int bestGain = 0;

					for (int dy = -6; dy <= 6; dy++)
					{
						for (int dx = -6; dx <= 6; dx++)
						{
							var b = (Wrap(a.X + dx), a.Y + dy);

							if (!result.TryGetValue(b, out int k) || k != tb || !Fits(ta, b))
								continue;

							int before = Same(a, ta) + Same(b, tb);
							int after = Same(a, tb, b) + Same(b, ta, a);
							int gain = after - before;

							if (gain > bestGain)
							{
								bestGain = gain;
								best = b;
							}
						}
					}

					if (best.Item1 < 0)
						continue;

					result[a] = tb;
					result[best] = ta;

					if (ConflictAround(result, a) || ConflictAround(result, best))
					{
						result[a] = ta;
						result[best] = tb;
						continue;
					}

					swaps++;
				}

				if (swaps == 0)
					break;
			}
		}

		// no region too large to read as more than one colour, and no landmass of one terrain: a region larger than about
		// 26 tiles or a third of its landmass, and a landmass of more than 8 tiles with one terrain (more than 20 with
		// fewer than three), give a compact patch of their cells to another terrain, swapping places with an equal patch
		// of that terrain from a large region elsewhere, so every count stays exact and neither patch is left as specks
		private static void BreakUpLargeRegions(int[,] map, Dictionary<(int, int), int> result, List<(int X, int Y)> cells, int seed)
		{
			// the landmasses: land joined by edges, rivers included as land
			var landOf = new Dictionary<(int, int), int>();
			var landSize = new List<int>();

			foreach (var c in cells)
			{
				if (landOf.ContainsKey(c))
					continue;

				int id = landSize.Count, n = 0;
				var stack = new Stack<(int, int)>();
				stack.Push(c);
				landOf[c] = id;

				while (stack.Count > 0)
				{
					(int x, int y) = stack.Pop();
					n++;

					foreach ((int dx, int dy) in Four)
					{
						var o = (Wrap(x + dx), y + dy);

						if (o.Item2 >= 1 && o.Item2 < Height - 1 && !landOf.ContainsKey(o) && map[o.Item1, o.Item2] != Water)
						{
							landOf[o] = id;
							stack.Push(o);
						}
					}
				}

				landSize.Add(n);
			}

			bool Fits(int kind, (int X, int Y) c) => kind switch
			{
				Arctic => c.Y < 3 || c.Y > Height - 4,
				Desert or Jungle => Latitude(c.Y) <= 0.6f,
				_ => true,
			};

			List<List<(int X, int Y)>> Regions()
			{
				var seen = new HashSet<(int, int)>();
				var all = new List<List<(int X, int Y)>>();

				foreach (var c in cells)
				{
					if (!seen.Add(c))
						continue;

					var region = new List<(int X, int Y)>();
					var stack = new Stack<(int X, int Y)>();
					stack.Push(c);

					while (stack.Count > 0)
					{
						var p = stack.Pop();
						region.Add(p);

						foreach ((int dx, int dy) in Four)
						{
							var o = (Wrap(p.X + dx), p.Y + dy);

							if (result.TryGetValue(o, out int k) && k == result[c] && seen.Add(o))
								stack.Push(o);
						}
					}

					all.Add(region);
				}

				return all;
			}

			// a patch of n cells of a region, grown from a seed cell: nearest first, by a distance roughened with noise, so
			// the patch has an irregular, natural outline rather than a diamond's
			List<(int X, int Y)> Patch(List<(int X, int Y)> region, (int X, int Y) start, int n)
			{
				var inRegion = new HashSet<(int, int)>(region.Select(r => (r.X, r.Y)));
				var patch = new List<(int X, int Y)>();
				var seen = new HashSet<(int, int)> { start };
				var queue = new PriorityQueue<(int X, int Y), float>();
				queue.Enqueue(start, 0);

				while (queue.Count > 0 && patch.Count < n)
				{
					var p = queue.Dequeue();
					patch.Add(p);

					foreach ((int dx, int dy) in Four)
					{
						var o = (Wrap(p.X + dx), p.Y + dy);

						if (inRegion.Contains(o) && seen.Add(o))
						{
							float d = MathF.Sqrt(Distance(o, start)) + (Hash(o.Item1, o.Item2, seed + 11) * 1.6f);
							queue.Enqueue(o, d);
						}
					}
				}

				return patch;
			}

			var tried = new HashSet<(int, int)>();

			for (int round = 0; round < 80; round++)
			{
				var regions = Regions();
				List<(int X, int Y)>? target = null;
				int patchSize = 0;

				// the worst offender first: the largest region over its limit, else a landmass with too few terrains
				foreach (var r in regions.OrderByDescending(r => r.Count))
				{
					int land = landSize[landOf[r[0]]];
					int limit = Math.Max(6, Math.Min(26, land / 3));

					if (r.Count > limit && result[r[0]] is not (Arctic or Hills or Mountains) && !tried.Contains(r.Min()))
					{
						target = r;
						patchSize = Math.Clamp(r.Count / 3, 4, 10);
						break;
					}
				}

				if (target == null)
				{
					foreach (var group in cells.GroupBy(c => landOf[c]))
					{
						int size = landSize[group.Key], kinds = group.Select(c => result[c]).Distinct().Count();

						if ((size > 8 && kinds < 2) || (size > 20 && kinds < 3))
						{
							var largest = regions.Where(r => landOf[r[0]] == group.Key && result[r[0]] is not (Arctic or Hills or Mountains)).OrderByDescending(r => r.Count).FirstOrDefault();

							if (largest != null && largest.Count >= 6 && !tried.Contains(largest.Min()))
							{
								target = largest;
								patchSize = Math.Clamp(size / 4, 3, 8);
								break;
							}
						}
					}
				}

				if (target == null)
					break;

				int kindA = result[target[0]];

				// the patch: grown from a cell picked by the noise, away from the region's first cell, so patches differ
				var startA = target[(int)(Hash(target.Count, round, seed + 9) * target.Count) % target.Count];
				var patchA = Patch(target, startA, patchSize);

				// the donor: a large enough region of another terrain that may stand where patch A is, nearest first
				bool swapped = false;

				// (relief keeps its place: hills and mountains are never moved, so ranges, foothills and the high ground
				// rivers rise in stay as they are; and a donor gives a patch only if three times its size, so what is
				// left of it does not fall into fragments)
				foreach (var donor in regions.Where(r => result[r[0]] != kindA && result[r[0]] is not (Arctic or Hills or Mountains) && r.Count >= patchA.Count * 3 && r != target)
					.OrderBy(r => Distance(r[0], startA)))
				{
					int kindB = result[donor[0]];

					if (!patchA.All(c => Fits(kindB, c)))
						continue;

					var patchB = Patch(donor, donor[(int)(Hash(donor.Count, round, seed + 10) * donor.Count) % donor.Count], patchA.Count);

					if (patchB.Count != patchA.Count || !patchB.All(c => Fits(kindA, c)))
						continue;

					foreach (var c in patchA)
						result[c] = kindB;

					foreach (var c in patchB)
						result[c] = kindA;

					if (patchA.Concat(patchB).Any(c => ConflictAround(result, c)))
					{
						foreach (var c in patchA)
							result[c] = kindA;

						foreach (var c in patchB)
							result[c] = kindB;

						continue;
					}

					swapped = true;
					break;
				}

				// a region with no partner is left as it is, and the others still get their turn
				if (!swapped)
					tried.Add(target.Min());
			}
		}

		// a last pass on tiles that share no edge with their own kind: each changes places with a nearby tile whose
		// kind suits its own spot, wherever that leaves fewer such lone tiles around both of them
		private static void JoinLoneTiles(int[,] map, Dictionary<(int, int), int> result, List<(int X, int Y)> cells)
		{
			bool ByRiver((int X, int Y) c)
			{
				foreach ((int dx, int dy) in Eight)
				{
					int ny = c.Y + dy;

					if (ny >= 0 && ny < Height && map[Wrap(c.X + dx), ny] == River)
						return true;
				}

				return false;
			}

			// a tile in a piece of one or two tiles of its kind (joined by edges)
			bool Lone((int X, int Y) c)
			{
				if (!result.TryGetValue(c, out int t))
					return false;

				(int, int)? partner = null;

				foreach ((int dx, int dy) in Four)
				{
					var o = (Wrap(c.X + dx), c.Y + dy);

					if (result.TryGetValue(o, out int k) && k == t)
					{
						if (partner != null)
							return false;

						partner = o;
					}
				}

				if (partner == null)
					return true;

				(int px, int py) = partner.Value;

				foreach ((int dx, int dy) in Four)
				{
					var o = (Wrap(px + dx), py + dy);

					if (o != c && result.TryGetValue(o, out int k) && k == t)
						return false;
				}

				return true;
			}

			int LoneAround((int X, int Y) c)
			{
				int n = 0;

				for (int dy = -1; dy <= 1; dy++)
					for (int dx = -1; dx <= 1; dx++)
						n += Lone((Wrap(c.X + dx), c.Y + dy)) ? 1 : 0;

				return n;
			}

			bool Fits(int kind, (int X, int Y) c) => kind switch
			{
				Arctic => c.Y < 3 || c.Y > Height - 4,
				Desert or Jungle => Latitude(c.Y) <= 0.6f,
				_ => true,
			};

			for (int pass = 0; pass < 4; pass++)
			{
				foreach (var a in cells)
				{
					// (hills and mountains beside a river keep their place: they are the high ground it rises in)
					if (!Lone(a) || result[a] == Arctic || (result[a] is Hills or Mountains && ByRiver(a)))
						continue;

					int ta = result[a];
					(int, int) best = (-1, -1);
					int bestGain = 0;

					for (int dy = -7; dy <= 7; dy++)
					{
						for (int dx = -7; dx <= 7; dx++)
						{
							var b = (Wrap(a.X + dx), a.Y + dy);

							if (!result.TryGetValue(b, out int tb) || tb == ta || tb == Arctic || (tb is Hills or Mountains && ByRiver(b)) || !Fits(ta, b) || !Fits(tb, a))
								continue;

							int before = LoneAround(a) + LoneAround(b);
							result[a] = tb;
							result[b] = ta;
							int after = LoneAround(a) + LoneAround(b);
							bool bad = ConflictAround(result, a) || ConflictAround(result, b);
							result[a] = ta;
							result[b] = tb;

							if (!bad && before - after > bestGain)
							{
								bestGain = before - after;
								best = b;
							}
						}
					}

					if (best.Item1 >= 0)
					{
						result[a] = result[best];
						result[best] = ta;
					}
				}
			}
		}

		private static bool ConflictAround(Dictionary<(int, int), int> result, (int X, int Y) c)
		{
			if (Conflict(result, c, result[c]))
				return true;

			foreach ((int dx, int dy) in Eight)
			{
				var o = (Wrap(c.X + dx), c.Y + dy);

				if (result.TryGetValue(o, out int k) && Conflict(result, o, k))
					return true;
			}

			return false;
		}

		private static bool Conflict(Dictionary<(int, int), int> result, (int X, int Y) c, int t)
		{
			if (t != Jungle && t != Desert)
				return false;

			foreach ((int dx, int dy) in Eight)
			{
				var n = (Wrap(c.X + dx), c.Y + dy);

				if (!result.TryGetValue(n, out int k) || n == c)
					continue;

				if (t == Jungle && (k == Desert || k == Arctic))
					return true;

				if (t == Desert && (k == Arctic || k == Swamp))
					return true;
			}

			return false;
		}

		private static float NearKind(Dictionary<(int, int), int> result, (int X, int Y) c, int kind)
		{
			int n = 0;

			foreach ((int dx, int dy) in Eight)
				if (result.TryGetValue((Wrap(c.X + dx), c.Y + dy), out int k) && k == kind)
					n++;

			return n;
		}

		private static int Distance((int X, int Y) a, (int X, int Y) b)
		{
			int dx = Math.Abs(a.X - b.X);
			return Math.Min(dx, Width - dx) + Math.Abs(a.Y - b.Y);
		}

		private static float[,] CoastDistance(int[,] map)
		{
			float[,] d = new float[Width, Height];
			var queue = new Queue<(int, int)>();

			for (int y = 0; y < Height; y++)
			{
				for (int x = 0; x < Width; x++)
				{
					d[x, y] = map[x, y] == Water ? 0 : float.MaxValue;

					if (map[x, y] == Water)
						queue.Enqueue((x, y));
				}
			}

			while (queue.Count > 0)
			{
				(int x, int y) = queue.Dequeue();

				foreach ((int dx, int dy) in Four)
				{
					int nx = Wrap(x + dx), ny = y + dy;

					if (ny >= 0 && ny < Height && d[nx, ny] > d[x, y] + 1)
					{
						d[nx, ny] = d[x, y] + 1;
						queue.Enqueue((nx, ny));
					}
				}
			}

			return d;
		}

		private static int Wrap(int x) => ((x % Width) + Width) % Width;

		private static float Latitude(int y) => MathF.Abs(y - ((Height - 1) / 2f)) / ((Height - 1) / 2f);

		private static float Hash(int x, int y, int seed)
		{
			unchecked
			{
				uint h = ((uint)x * 374761393u) + ((uint)y * 668265263u) + ((uint)seed * 1013904223u);
				h = (h ^ (h >> 15)) * 2246822519u;
				h = (h ^ (h >> 13)) * 3266489917u;
				h ^= h >> 16;
				return (h & 0xffffff) / (float)0x1000000;
			}
		}

		// smooth value noise that wraps around the world's width
		private static float Noise(float x, float y, int seed, int period)
		{
			int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
			float fx = x - x0, fy = y - y0;
			fx = fx * fx * (3 - (2 * fx));
			fy = fy * fy * (3 - (2 * fy));
			int xa = ((x0 % period) + period) % period, xb = (xa + 1) % period;
			float a = Hash(xa, y0, seed), b = Hash(xb, y0, seed), c = Hash(xa, y0 + 1, seed), d = Hash(xb, y0 + 1, seed);
			return (a + ((b - a) * fx)) + (((c + ((d - c) * fx)) - (a + ((b - a) * fx))) * fy);
		}

		// noise at three octaves on cells of the given size, wrapping cleanly around the world (the size divides 80)
		private static float Fbm(int x, int y, int size, int seed)
		{
			float total = 0, amp = 0.55f, norm = 0;

			for (int o = 0; o < 2; o++)
			{
				int s = Math.Max(1, size >> o);
				total += Noise(x / (float)s, y / (float)s, seed + (o * 101), Width / s) * amp;
				norm += amp;
				amp *= 0.5f;
			}

			return total / norm;
		}
	}
}
