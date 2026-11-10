using System;
using System.Collections.Generic;
using System.IO;
using SkiaSharp;

namespace OpenCivOne.Graphics
{
	/// <summary>
	/// An isometric view of the map, in the rotated diamond layout: map cell (x, y) is drawn as a diamond whose centre is
	/// at ((x - y) * TileWidth / 2, (x + y) * TileHeight / 2), so every cell touches its four game neighbours along an
	/// edge and the east-west wrap runs diagonally. The ground is one inked texture for most land, told apart mostly by
	/// what stands on it; trees, peaks, hills, dunes, reeds, stones and ice are sprites drawn back to front with fields,
	/// rivers and roads under them, then cities with their plates and flags, units with their banners, and mist over
	/// unexplored land. The art comes from a theme's hd/iso folder (see tools/hdart/iso_assets.py).
	/// </summary>
	public sealed class IsoView
	{
		public const int TileWidth = 80, TileHeight = 40;
		private const int MapWidth = 80, MapHeight = 50, TextureSize = 400;
		// sizes are given for 192-pixel tiles, as the art was laid out
		private const float S = TileWidth / 192f;
		private const int Desert = 0, Plains = 1, Grassland = 2, Forest = 3, Hills = 4, Mountains = 5, Tundra = 6, Arctic = 7,
			Swamp = 8, Jungle = 9, Water = 10, River = 11;
		private const int Grass = 0, Sand = 1, Snow = 2, Sea = 3, Fog = 4;
		private static readonly string[] ResourceNames = { "oasis", "horses", "wheat", "pheasant", "coal", "gold", "caribou", "seal", "peat", "gems", "fish" };
		private static readonly string[] UnitNames = { "settlers", "militia", "phalanx", "legion", "musketeers", "riflemen", "cavalry", "knights", "catapult",
			"cannon", "chariot", "armor", "mech_infantry", "artillery", "fighter", "bomber", "trireme", "sail", "frigate", "ironclad", "cruiser", "battleship",
			"submarine", "carrier", "transport", "nuclear", "diplomat", "caravan" };
		// the ground of each terrain: which texture, and how its colour is nudged
		private static readonly int[] GroundOf = { Sand, Grass, Grass, Grass, Grass, Grass, Grass, Snow, Grass, Grass, Sea, Grass };
		// the inked grass is olive; each terrain shifts it far enough to be told apart at a glance: grassland green, plains
		// dry gold, hills ochre-olive, tundra cold grey-lilac, swamp dark teal-olive
		private static readonly float[][] Tint = {
			new[] { 1f, 1f, 1f }, new[] { 1.38f, 1.20f, 0.92f }, new[] { 0.90f, 1.05f, 0.86f }, new[] { 0.80f, 0.94f, 0.84f },
			new[] { 1.10f, 0.98f, 0.82f }, new[] { 0.96f, 0.95f, 0.92f }, new[] { 1.10f, 1.06f, 1.95f }, new[] { 1f, 1f, 1f },
			new[] { 0.62f, 0.78f, 1.06f }, new[] { 0.82f, 1.02f, 0.84f }, new[] { 1f, 1f, 1f }, new[] { 0.90f, 1.05f, 0.86f } };

		private readonly int width, height;
		private readonly uint[] grass, sand, snow, sea, shallow;

		// the painted crop plots (seen from above, FieldPlot pixels square), mapped onto each plot of a field
		private const int FieldPlot = 32;
		private readonly List<uint[]> fieldPlots = new List<uint[]>();
		private readonly Dictionary<string, List<SKBitmap>> sets = new Dictionary<string, List<SKBitmap>>();
		private readonly Dictionary<string, SKBitmap> objects = new Dictionary<string, SKBitmap>();
		private uint[] pixels;
		private long signature = long.MinValue;

		/// <summary>
		/// What the view shows, read from the game: terrain id, whether the human has explored a cell, its visible
		/// improvements (TerrainImprovementFlagsEnum), whether it has a special resource or a hut; and the visible cities and units
		/// </summary>
		public sealed class Scene
		{
			public Func<int, int, int> Terrain = (x, y) => Water;
			public Func<int, int, bool> Explored = (x, y) => true;
			public Func<int, int, int> Improvements = (x, y) => 0;
			public Func<int, int, int> Feature = (x, y) => 0;
			public List<(int X, int Y, string Name, int Size, uint Colour)> Cities = new List<(int, int, string, int, uint)>();
			public List<(int X, int Y, int Type, uint Colour)> Units = new List<(int, int, int, uint)>();
		}

		private IsoView(int width, int height, uint[] grass, uint[] sand, uint[] snow, uint[] sea, uint[] shallow)
		{
			this.width = width;
			this.height = height;
			this.grass = grass;
			this.sand = sand;
			this.snow = snow;
			this.sea = sea;
			this.shallow = shallow;
			this.pixels = new uint[width * height];
		}

		public int Width => this.width;

		public int Height => this.height;

		/// <summary>
		/// The view's pixels after the last Render
		/// </summary>
		public uint[] Pixels => this.pixels;

		/// <summary>
		/// Loads the art from a theme's hd folder (its iso and painted folders); null if it has no isometric art
		/// </summary>
		public static IsoView? Load(string hdFolder, int width, int height)
		{
			string iso = Path.Combine(hdFolder, "iso");

			if (!Directory.Exists(Path.Combine(iso, "ground")))
				return null;

			uint[] ReadTexture(string path, float[] mean, float contrast)
			{
				using SKBitmap? image = SKBitmap.Decode(path);

				if (image == null)
					return new uint[TextureSize * TextureSize];

				using SKBitmap sized = image.Resize(new SKImageInfo(TextureSize, TextureSize), new SKSamplingOptions(SKCubicResampler.Mitchell));
				uint[] result = new uint[TextureSize * TextureSize];
				float lumMean = 0;
				float[] lum = new float[result.Length];

				for (int i = 0; i < result.Length; i++)
				{
					SKColor c = sized.GetPixel(i % TextureSize, i / TextureSize);
					lum[i] = (c.Red + c.Green + c.Blue) / 3f;
					lumMean += lum[i];

					if (mean.Length == 0)
						result[i] = 0xff000000 | ((uint)c.Red << 16) | ((uint)c.Green << 8) | c.Blue;
				}

				if (mean.Length == 0)
					return result;

				lumMean /= result.Length;

				// a calmer version: the mean colour, with the texture's detail at a fraction of its contrast
				for (int i = 0; i < result.Length; i++)
				{
					float d = (lum[i] - lumMean) * contrast;
					result[i] = Pack(mean[0] + d, mean[1] + d, mean[2] + d);
				}

				return result;
			}

			string painted = Path.Combine(hdFolder, "painted", "ground");
			IsoView view = new IsoView(width, height,
				ReadTexture(Path.Combine(iso, "ground", "grass.png"), Array.Empty<float>(), 1),
				ReadTexture(Path.Combine(iso, "ground", "sand.png"), Array.Empty<float>(), 1),
				ReadTexture(Path.Combine(painted, "snow.png"), new[] { 236f, 234f, 224f }, 1f),
				ReadTexture(Path.Combine(painted, "ocean.png"), new[] { 54f, 102f, 122f }, 0.12f),
				ReadTexture(Path.Combine(painted, "shallow.png"), new[] { 84f, 142f, 150f }, 0.35f));

			string plotFolder = Path.Combine(iso, "fieldplots");

			if (Directory.Exists(plotFolder))
			{
				string[] files = Directory.GetFiles(plotFolder, "*.png");
				Array.Sort(files, (a, b) => int.Parse(Path.GetFileNameWithoutExtension(a)).CompareTo(int.Parse(Path.GetFileNameWithoutExtension(b))));

				foreach (string file in files)
				{
					using SKBitmap? b = SKBitmap.Decode(file);

					if (b == null)
						continue;

					using SKBitmap sized = b.Resize(new SKImageInfo(FieldPlot, FieldPlot), new SKSamplingOptions(SKCubicResampler.Mitchell));
					uint[] px = new uint[FieldPlot * FieldPlot];

					for (int i = 0; i < px.Length; i++)
					{
						SKColor c = sized.GetPixel(i % FieldPlot, i / FieldPlot);
						// lifted to the mockup's sunlit yellow-greens: the painted sheet comes out darker, with more soil
						px[i] = Pack(c.Red * 1.36f, c.Green * 1.42f, c.Blue * 1.4f);
					}

					view.fieldPlots.Add(px);
				}
			}

			// every set at one scale, so a single tree stays smaller than a clump: its widest sprite gets the width given
			(string Set, float Width)[] scales = {
				("trees", 160 * S), ("jungle", 160 * S), ("peaks", 240 * S), ("hills", 145 * S), ("dunes", 90 * S), ("reeds", 62 * S),
				("tundra", 42 * S), ("ice", 70 * S), ("tufts", 45 * S), ("hamlet", 102 * S), ("town", TileWidth * 1.25f), ("plots", TileWidth * 1.08f) };

			foreach ((string set, float target) in scales)
			{
				string folder = Path.Combine(iso, set);
				List<SKBitmap> loaded = new List<SKBitmap>();

				if (Directory.Exists(folder))
				{
					string[] files = Directory.GetFiles(folder, "*.png");
					Array.Sort(files, (a, b) => int.Parse(Path.GetFileNameWithoutExtension(a)).CompareTo(int.Parse(Path.GetFileNameWithoutExtension(b))));
					List<SKBitmap> raw = new List<SKBitmap>();

					foreach (string file in files)
					{
						SKBitmap? b = SKBitmap.Decode(file);

						if (b != null)
							raw.Add(b);
					}

					int widest = 1;

					foreach (SKBitmap b in raw)
						widest = Math.Max(widest, b.Width);

					foreach (SKBitmap b in raw)
					{
						float k = target / widest;

						if (set == "plots")
							loaded.Add(b.Resize(new SKImageInfo((int)(TileWidth * 1.08f), (int)(TileHeight * 1.08f)), new SKSamplingOptions(SKCubicResampler.Mitchell)));
						else
							loaded.Add(b.Resize(new SKImageInfo(Math.Max(1, (int)(b.Width * k)), Math.Max(1, (int)(b.Height * k))), new SKSamplingOptions(SKCubicResampler.Mitchell)));

						b.Dispose();
					}
				}

				view.sets[set] = loaded;
			}

			// the small lone trees of open land: the first few of the tree set, smaller
			List<SKBitmap> lone = new List<SKBitmap>();

			for (int i = 0; i < Math.Min(4, view.sets["trees"].Count); i++)
			{
				SKBitmap b = view.sets["trees"][i];
				float k = 0.5f;
				lone.Add(b.Resize(new SKImageInfo(Math.Max(1, (int)(b.Width * k)), Math.Max(1, (int)(b.Height * k))), new SKSamplingOptions(SKCubicResampler.Mitchell)));
			}

			view.sets["lone"] = lone;

			// the scattered bushes and lone trees in the ground's olive family, not blue-green, so they do not speckle it
			foreach (string set in new[] { "tufts", "lone" })
			{
				List<SKBitmap> toned = new List<SKBitmap>();

				foreach (SKBitmap b in view.sets[set])
				{
					SKBitmap t = new SKBitmap(b.Width, b.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
					using SKCanvas c = new SKCanvas(t);
					c.Clear(SKColors.Transparent);
					const float k = 0.62f, l = 1 - k;
					using SKPaint warm = new SKPaint { ColorFilter = SKColorFilter.CreateColorMatrix(new float[] {
						(0.3f * l) + k, 0.59f * l, 0.11f * l, 0, 0.05f,
						0.3f * l, (0.59f * l) + k, 0.11f * l, 0, 0.03f,
						0.3f * l, 0.59f * l, (0.11f * l) + (k * 0.8f), 0, 0,
						0, 0, 0, 1, 0 }) };
					c.DrawBitmap(b, 0, 0, warm);
					toned.Add(t);
					b.Dispose();
				}

				view.sets[set] = toned;
			}

			// irrigated fields: the plots ripened to gold, so they stand out on green grassland
			List<SKBitmap> gold = new List<SKBitmap>();

			foreach (SKBitmap b in view.sets["plots"])
			{
				SKBitmap g = new SKBitmap(b.Width, b.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
				using SKCanvas c = new SKCanvas(g);
				c.Clear(SKColors.Transparent);
				using SKPaint ripe = new SKPaint { ColorFilter = SKColorFilter.CreateColorMatrix(new float[] {
					1.12f, 0.10f, 0, 0, 0.02f,
					0.06f, 0.98f, 0, 0, 0.01f,
					0, 0, 0.62f, 0, 0,
					0, 0, 0, 1, 0 }) };
				c.DrawBitmap(b, 0, 0, ripe);
				gold.Add(g);
			}

			view.sets["gold"] = gold;

			// units, resources and huts from the theme's painted objects
			string objectsFolder = Path.Combine(hdFolder, "painted", "objects");

			foreach (string name in Array.ConvertAll(UnitNames, n => "unit_" + n))
				view.LoadObject(objectsFolder, name, height: 56);

			// resources, huts and mines a little smaller than a unit, so a busy map is not dominated by them
			foreach (string name in Array.ConvertAll(ResourceNames, n => "res_" + n))
				view.LoadObject(objectsFolder, name, height: 34, halo: true);

			view.LoadObject(objectsFolder, "res_fish", height: 26);

			if (view.objects.Remove("res_fish", out SKBitmap? small))
				view.objects["fish"] = small;

			view.LoadObject(objectsFolder, "res_fish", height: 34, halo: true);
			view.LoadObject(objectsFolder, "hut", height: 40, halo: true);
			view.LoadObject(objectsFolder, "mine", height: 32, halo: true);
			view.LoadObject(objectsFolder, "fortress", height: 46, halo: true);

			// the fortress in grey stone, so it is not taken for the hut's thatch
			if (view.objects.TryGetValue("fortress", out SKBitmap? fortress))
			{
				SKBitmap stone = new SKBitmap(fortress.Width, fortress.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
				using SKCanvas c = new SKCanvas(stone);
				c.Clear(SKColors.Transparent);
				using SKPaint grey = new SKPaint { ColorFilter = SKColorFilter.CreateColorMatrix(new float[] {
					0.28f, 0.50f, 0.10f, 0, 0,
					0.28f, 0.50f, 0.10f, 0, 0,
					0.26f, 0.48f, 0.10f, 0, 0,
					0, 0, 0, 1, 0 }) };
				c.DrawBitmap(fortress, 0, 0, grey);
				fortress.Dispose();
				view.objects["fortress"] = stone;
			}

			// pollution: a drawn patch of grey-brown smog
			SKBitmap smog = new SKBitmap(56, 30, SKColorType.Bgra8888, SKAlphaType.Premul);

			using (SKCanvas sc = new SKCanvas(smog))
			{
				sc.Clear(SKColors.Transparent);
				using SKPaint puff = new SKPaint { IsAntialias = true, Color = new SKColor(84, 72, 60, 150) };
				using SKPaint rim = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, Color = new SKColor(40, 34, 28, 200) };

				foreach ((float px, float py, float pr) in new[] { (14f, 18f, 9f), (27f, 13f, 11f), (41f, 18f, 9f), (28f, 21f, 8f) })
				{
					sc.DrawCircle(px, py, pr, puff);
					sc.DrawCircle(px, py, pr, rim);
				}
			}

			view.objects["smog"] = smog;

			return view;
		}

		private void LoadObject(string folder, string name, int height, bool halo = false)
		{
			string path = Path.Combine(folder, name + ".png");

			if (!File.Exists(path))
				return;

			using SKBitmap? b = SKBitmap.Decode(path);

			if (b == null)
				return;

			float k = (float)height / b.Height;
			SKBitmap sized = b.Resize(new SKImageInfo(Math.Max(1, (int)(b.Width * k)), height), new SKSamplingOptions(SKCubicResampler.Mitchell));

			if (!halo)
			{
				this.objects[name] = sized;
				return;
			}

			// a dark key line and a cream halo around it, so it reads over any ground and in front of trees
			SKBitmap ringed = new SKBitmap(sized.Width + 8, sized.Height + 8, SKColorType.Bgra8888, SKAlphaType.Premul);

			using (SKCanvas c = new SKCanvas(ringed))
			using (SKPaint cream = new SKPaint { ColorFilter = SKColorFilter.CreateBlendMode(new SKColor(226, 214, 184, 200), SKBlendMode.SrcIn) })
			using (SKPaint key = new SKPaint { ColorFilter = SKColorFilter.CreateBlendMode(new SKColor(40, 34, 28), SKBlendMode.SrcIn) })
			{
				c.Clear(SKColors.Transparent);

				for (int dy = -2; dy <= 2; dy++)
					for (int dx = -2; dx <= 2; dx++)
						if ((dx * dx) + (dy * dy) <= 5)
							c.DrawBitmap(sized, 4 + dx, 4 + dy, cream);

				for (int dy = -1; dy <= 1; dy++)
					for (int dx = -1; dx <= 1; dx++)
						c.DrawBitmap(sized, 4 + dx, 4 + dy, key);

				c.DrawBitmap(sized, 4, 4);
			}

			sized.Dispose();
			this.objects[name] = ringed;
		}

		private static uint Pack(float r, float g, float b)
		{
			return 0xff000000 | ((uint)Math.Clamp(r, 0, 255) << 16) | ((uint)Math.Clamp(g, 0, 255) << 8) | (uint)Math.Clamp(b, 0, 255);
		}

		private static int Wrap(int x) => ((x % MapWidth) + MapWidth) % MapWidth;

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

		// value noise over map cells, `perCell` features per cell, repeating around the world east-west
		private static float Noise(float gx, float gy, float perCell, int seed)
		{
			float x = gx * perCell, y = gy * perCell;
			int period = (int)(MapWidth * perCell);
			int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
			float fx = x - ix, fy = y - iy;
			float u = fx * fx * (3 - (2 * fx)), v = fy * fy * (3 - (2 * fy));
			int x0 = ((ix % period) + period) % period, x1 = (x0 + 1) % period;
			float a = Hash(x0, iy, seed), b = Hash(x1, iy, seed), c = Hash(x0, iy + 1, seed), d = Hash(x1, iy + 1, seed);
			return a + ((b - a) * u) + ((c - a) * v) + ((a - b - c + d) * u * v);
		}

		/// <summary>
		/// Paints the view centred on map position (cx, cy) in cells, unless nothing it shows has changed since the
		/// last time. Returns true if the pixels changed.
		/// </summary>
		public bool Render(float cx, float cy, Scene scene)
		{
			int range = (this.width / TileWidth) + (this.height / TileHeight) + 3;
			int ccx = (int)MathF.Floor(cx), ccy = (int)MathF.Floor(cy);
			long sig = 17;

			unchecked
			{
				sig = (sig * 31) + (long)(cx * 16) + ((long)(cy * 16) << 20);

				for (int y = ccy - range; y <= ccy + range; y++)
				{
					for (int x = ccx - range; x <= ccx + range; x++)
					{
						if (y < 0 || y >= MapHeight)
							continue;

						int wx = Wrap(x);
						sig = (sig * 31) + scene.Terrain(wx, y);
						sig = (sig * 7) + (scene.Explored(wx, y) ? 1 : 0);
						sig = (sig * 131) + scene.Improvements(wx, y);
						sig = (sig * 5) + scene.Feature(wx, y);
					}
				}

				foreach (var c in scene.Cities)
					sig = (sig * 37) + (c.X * 1000) + (c.Y * 10) + c.Size + c.Name.GetHashCode() + c.Colour;

				foreach (var u in scene.Units)
					sig = (sig * 41) + (u.X * 1000) + (u.Y * 10) + u.Type + u.Colour;
			}

			if (sig == this.signature)
				return false;

			this.signature = sig;
			this.Paint(cx, cy, scene, range);

			return true;
		}

		private void Paint(float cx, float cy, Scene scene, int range)
		{
			int w = this.width, h = this.height;
			float halfW = TileWidth / 2f, halfH = TileHeight / 2f;

			int TerrainAt(int x, int y)
			{
				if (y < 0 || y >= MapHeight)
					return -1;

				int t = scene.Terrain(Wrap(x), y);
				return (t < 0 || t > River) ? Grassland : t;
			}

			bool ExploredAt(int x, int y) => y >= 0 && y < MapHeight && scene.Explored(Wrap(x), y);

			// pass 1: the ground of every pixel, the terrain its nearby cells vote for (hard edges, rounded shapes), and how
			// much it is under mist
			int[] kind = new int[w * h];
			float[] fog = new float[w * h];
			uint[] img = new uint[w * h];
			Span<float> votes = stackalloc float[13];

			for (int py = 0; py < h; py++)
			{
				for (int px = 0; px < w; px++)
				{
					float u = (px - (w / 2f)) / halfW, v = (py - (h / 2f)) / halfH;
					float gx0 = cx + ((u + v) / 2), gy0 = cy + ((v - u) / 2);
					// a little wobble, fixed to the world, so terrain edges are organic and do not swim when the view moves
					float gx = gx0 + ((Noise(gx0, gy0, 0.8f, 1) - 0.5f) * 0.6f) + ((Noise(gx0, gy0, 2.8f, 3) - 0.5f) * 0.18f);
					float gy = gy0 + ((Noise(gx0, gy0, 0.8f, 2) - 0.5f) * 0.6f) + ((Noise(gx0, gy0, 2.8f, 4) - 0.5f) * 0.18f);
					int ix = (int)MathF.Floor(gx), iy = (int)MathF.Floor(gy);
					votes.Clear();
					float fogVote = 0, total = 0;

					for (int dj = -1; dj <= 1; dj++)
					{
						for (int di = -1; di <= 1; di++)
						{
							int x = ix + di, y = iy + dj;
							float dx = gx - (x + 0.5f), dy = gy - (y + 0.5f);
							float wgt = 1.2f - (MathF.Sqrt((dx * dx) + (dy * dy)) * 1.15f);

							if (wgt <= 0)
								continue;

							wgt = wgt * wgt * wgt;
							// past the poles the ice cap carries on, rather than a dark void at the world's edge
							bool offMap = y < 0 || y >= MapHeight;
							int t = offMap ? Arctic : TerrainAt(x, y);
							bool known = offMap || (t >= 0 && ExploredAt(x, y));
							total += wgt;

							if (!known)
								fogVote += wgt;
							else
								votes[t] += wgt;
						}
					}

					int best = Water;
					float bestVote = -1;

					for (int t = 0; t <= River; t++)
					{
						if (votes[t] > bestVote)
						{
							bestVote = votes[t];
							best = t;
						}
					}

					int i = (py * w) + px;
					fog[i] = total > 0 ? fogVote / total : 1;
					int g = GroundOf[best];
					kind[i] = g;
					// the texture is fixed to the world's isometric positions; 400 divides the world's width, so it wraps cleanly
					int tx = (int)MathF.Floor((gx0 - gy0) * halfW), ty = (int)MathF.Floor((gx0 + gy0) * halfH);
					int ti = ((((ty % TextureSize) + TextureSize) % TextureSize) * TextureSize) + (((tx % TextureSize) + TextureSize) % TextureSize);
					uint c = g switch { Sand => this.sand[ti], Snow => this.snow[ti], Sea => this.sea[ti], _ => this.grass[ti] };
					float[] tint = Tint[best];
					float cr = ((c >> 16) & 0xff) * tint[0], cg = ((c >> 8) & 0xff) * tint[1], cb = (c & 0xff) * tint[2];

					// the land toned to the mockup's khaki-olive: a quarter less saturated, warmed towards yellow
					if (g != Sea && g != Snow)
					{
						float grey = (cr + cg + cb) / 3;
						cr = (grey + ((cr - grey) * 0.74f) + 10) * 1.04f;
						cg = (grey + ((cg - grey) * 0.74f) + 1) * 1.04f;
						cb = (grey + ((cb - grey) * 0.74f) + 6) * 1.04f;
					}

					img[i] = Pack(cr, cg, cb);

					if (g == Sea)
						kind[i] = Sea;
				}
			}

			// fields: irrigated land, and some of the worked land around cities, painted into the ground in pass 2
			var cityCells = new HashSet<(int, int)>();

			foreach (var c in scene.Cities)
				cityCells.Add((c.X, c.Y));

			bool CityCell(int x, int y) => cityCells.Contains((Wrap(x), y));

			bool FieldCell(int x, int y)
			{
				int t = TerrainAt(x, y);

				if (!ExploredAt(x, y) || CityCell(x, y) || (t != Grassland && t != Plains && t != Desert && t != River && t != Hills))
					return false;

				if ((scene.Improvements(Wrap(x), y) & 0x2) != 0)
					return true;

				if (t == Desert || t == Hills)
					return false;

				for (int dy = -1; dy <= 1; dy++)
					for (int dx = -1; dx <= 1; dx++)
						if (CityCell(x + dx, y + dy))
							return Hash(Wrap(x), y, 21) < 0.6f;

				return false;
			}

			var fieldCache = new Dictionary<(int, int), bool>();

			bool IsFieldCell(int x, int y)
			{
				if (!fieldCache.TryGetValue((x, y), out bool f))
					fieldCache[(x, y)] = f = FieldCell(x, y);

				return f;
			}

			// pass 2: the shallows, foam, coast line, beach, and an ink line where two kinds of ground meet
			float[] seaDist = Distance(kind, w, h, k => k != Sea);
			float[] snowDist = Distance(kind, w, h, k => k != Snow);
			float[] landDist = Distance(kind, w, h, k => k == Sea);

			for (int py = 0; py < h; py++)
			{
				for (int px = 0; px < w; px++)
				{
					int i = (py * w) + px;
					uint c = img[i];
					float r = (c >> 16) & 0xff, g = (c >> 8) & 0xff, b = c & 0xff;
					float u = (px - (w / 2f)) / halfW, v = (py - (h / 2f)) / halfH;
					float gx0 = cx + ((u + v) / 2), gy0 = cy + ((v - u) / 2);
					int tx = (int)MathF.Floor((gx0 - gy0) * halfW), ty = (int)MathF.Floor((gx0 + gy0) * halfH);
					int ti = ((((ty % TextureSize) + TextureSize) % TextureSize) * TextureSize) + (((tx % TextureSize) + TextureSize) % TextureSize);

					if (kind[i] == Sea)
					{
						float band = Math.Clamp(1 - ((seaDist[i] - 2) / 22), 0, 1);
						uint s = this.shallow[ti];
						r += (((s >> 16) & 0xff) - r) * band;
						g += (((s >> 8) & 0xff) - g) * band;
						b += ((s & 0xff) - b) * band;

						if (MathF.Abs(seaDist[i] - 4) < 1.1f && Noise(gx0, gy0, 9, 5) > 0.45f)
						{
							r = (r * 0.35f) + (236 * 0.65f);
							g = (g * 0.35f) + (232 * 0.65f);
							b = (b * 0.35f) + (214 * 0.65f);
						}
					}
					else if (kind[i] == Snow && snowDist[i] < 9)
					{
						// blue-grey shading inside the edge of the ice, and broken drift lines
						float edgeShade = 1 - (snowDist[i] / 9);
						r += (176 - r) * edgeShade * 0.55f;
						g += (188 - g) * edgeShade * 0.55f;
						b += (206 - b) * edgeShade * 0.55f;
					}
					else if (landDist[i] < 3.5f)
					{
						r = (r * 0.55f) + (214 * 0.45f);
						g = (g * 0.55f) + (198 * 0.45f);
						b = (b * 0.55f) + (150 * 0.45f);
					}

					// fields, as in the mockup: each field cell divided into 3 x 3 plots along the cell's axes, yellow-greens from
					// the ground's olive family, each plot crossed by a dark midrib and leaf-vein rows, edged by a dark hedge line
					// inside a grey-cream lane; neighbouring field cells share their lanes, so fields join into one patchwork
					if (kind[i] != Sea)
					{
						int fcx = (int)MathF.Floor(gx0), fcy = (int)MathF.Floor(gy0);

						if (IsFieldCell(fcx, fcy))
						{
							// two or three plots across each cell, chosen per column and per row, so the lanes run on unbroken
							// through the field but the plots are not all one size
							const float cellPx = 35.8f; // a cell's width across, in screen pixels
							int nu = Hash(Wrap(fcx), 0, 61) < 0.5f ? 2 : 3, nv = Hash(0, fcy, 62) < 0.5f ? 2 : 3;
							float pxU = cellPx / nu, pxV = cellPx / nv;
							float fu = (gx0 - fcx) * nu, fv = (gy0 - fcy) * nv;
							int pu = Math.Min(nu - 1, (int)fu), pv = Math.Min(nv - 1, (int)fv);
							float su = ((fu - pu) * 2) - 1, sv = ((fv - pv) * 2) - 1;
							float lane = MathF.Min((1 - MathF.Abs(su)) * pxU, (1 - MathF.Abs(sv)) * pxV) / 2;
							int plotX = (Wrap(fcx) * 3) + pu, plotY = (fcy * 3) + pv;
							float pick = Hash(plotX, plotY, 51);
							(float pr, float pg, float pb) = pick < 0.3f ? (126f, 140f, 54f) : pick < 0.55f ? (110f, 128f, 46f) : pick < 0.8f ? (136f, 146f, 60f) : (102f, 122f, 42f);
							float vein, rad = MathF.Sqrt((su * su) + (sv * sv));

							if (Hash(plotX, plotY, 52) < 0.6f)
							{
								// leaves radiating from the middle on a faint cross, as the mockup's plots
								float ang = MathF.Atan2(sv, su) * 4f / 3.14159f;
								float spoke = MathF.Abs((ang * 2) - MathF.Round(ang * 2));
								float cross = MathF.Min(MathF.Abs(su) * pxU, MathF.Abs(sv) * pxV) / 2;
								vein = (MathF.Max(0, 1 - (spoke / 0.3f)) * MathF.Min(1, rad * 2.5f) * 0.8f) + (cross < 0.6f ? 0.5f : 0);
							}
							else
							{
								// soft crop rows along one axis
								float along = (Hash(plotX, plotY, 53) < 0.5f ? su * pxU : sv * pxV) / 2 / 2.6f;
								vein = MathF.Max(0, 1 - (MathF.Abs(along - MathF.Round(along)) / 0.36f)) * 0.9f;
							}

							// sunlit in the middle, darker toward the hedges; the ground's own marks show through, mottling it
							float detail = ((r + g + b) / 3 / 120f) - 1;
							float shade = (1 + (MathF.Max(0, 0.35f - vein) * 0.6f) - (MathF.Min(1, vein) * 0.52f)) * (1.1f - (rad * 0.14f));
							float fr = (pr * shade) + (detail * 18), fg = (pg * shade) + (detail * 18), fb = (pb * shade) + (detail * 10);

							// with the theme's painted crop plots, each plot shows one of them, turned one of four ways
							if (this.fieldPlots.Count > 0)
							{
								uint[] tex = this.fieldPlots[(int)(Hash(plotX, plotY, 54) * this.fieldPlots.Count) % this.fieldPlots.Count];
								int turn = (int)(Hash(plotX, plotY, 55) * 4) & 3;
								float tu = (su + 1) / 2, tv = (sv + 1) / 2;
								(tu, tv) = turn switch { 1 => (1 - tv, tu), 2 => (1 - tu, 1 - tv), 3 => (tv, 1 - tu), _ => (tu, tv) };
								float qx = Math.Clamp((tu * FieldPlot) - 0.5f, 0, FieldPlot - 1.001f), qy = Math.Clamp((tv * FieldPlot) - 0.5f, 0, FieldPlot - 1.001f);
								int x0 = (int)qx, y0 = (int)qy;
								float ax = qx - x0, ay = qy - y0;
								uint c00 = tex[(y0 * FieldPlot) + x0], c10 = tex[(y0 * FieldPlot) + x0 + 1], c01 = tex[((y0 + 1) * FieldPlot) + x0], c11 = tex[((y0 + 1) * FieldPlot) + x0 + 1];
								float Mix(int sh) => ((((c00 >> sh) & 0xff) * (1 - ax)) + (((c10 >> sh) & 0xff) * ax)) * (1 - ay) + ((((c01 >> sh) & 0xff) * (1 - ax)) + (((c11 >> sh) & 0xff) * ax)) * ay;
								fr = Mix(16);
								fg = Mix(8);
								fb = Mix(0);
							}

							// a thin grey-cream lane, edged by a dark hedge line on the plot's side, both antialiased so they do
							// not break into steps
							// the pale lanes run only between cells; the plots inside a cell are parted by a thin hedge alone, so
							// from a distance a field reads as blocks of plots rather than a grid
							float ex = gx0 - fcx, ey = gy0 - fcy;
							float cellEdge = MathF.Min(MathF.Min(ex, 1 - ex), MathF.Min(ey, 1 - ey)) * cellPx;
							bool outer = cellEdge <= lane + 0.01f;
							float laneW = outer ? Math.Clamp(1.0f - lane, 0, 1) : 0;
							float hedgeW = (outer ? Math.Clamp(1.9f - lane, 0, 1) : Math.Clamp(1.1f - lane, 0, 1) * 0.85f) * (1 - laneW);
							float plotW = 1 - laneW - hedgeW;
							fr = (fr * plotW) + (172 * laneW) + (54 * hedgeW);
							fg = (fg * plotW) + (162 * laneW) + (66 * hedgeW);
							fb = (fb * plotW) + (116 * laneW) + (24 * hedgeW);

							r = fr;
							g = fg;
							b = fb;
						}
					}

					bool edge = (px + 1 < w && kind[i + 1] != kind[i]) || (py + 1 < h && kind[i + w] != kind[i]);

					if (edge)
					{
						r = (r * 0.25f) + (40 * 0.75f);
						g = (g * 0.25f) + (34 * 0.75f);
						b = (b * 0.25f) + (28 * 0.75f);
					}

					img[i] = Pack(r, g, b);
				}
			}

			using SKBitmap bitmap = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
			System.Runtime.InteropServices.Marshal.Copy(Array.ConvertAll(img, x => unchecked((int)x)), 0, bitmap.GetPixels(), img.Length);
			using SKCanvas canvas = new SKCanvas(bitmap);

			SKPoint Screen(float x, float y) => new SKPoint((w / 2f) + ((x - cx - (y - cy)) * halfW), (h / 2f) + ((x - cx + (y - cy)) * halfH));

			// the cells to draw objects for, with x unwrapped near the view's centre
			List<(int X, int Y)> cells = new List<(int, int)>();

			for (int y = (int)cy - range; y <= (int)cy + range; y++)
			{
				for (int x = (int)cx - range; x <= (int)cx + range; x++)
				{
					if (y < 0 || y >= MapHeight)
						continue;

					SKPoint p = Screen(x + 0.5f, y + 0.5f);

					if (p.X > -200 && p.X < w + 200 && p.Y > -200 && p.Y < h + 260)
						cells.Add((x, y));
				}
			}

			var cityAt = new Dictionary<(int, int), (string Name, int Size, uint Colour)>();

			foreach (var c in scene.Cities)
				cityAt[(c.X, c.Y)] = (c.Name, c.Size, c.Colour);

			bool IsCity(int x, int y) => cityAt.ContainsKey((Wrap(x), y));
			int Improvements(int x, int y) => (y < 0 || y >= MapHeight) ? 0 : scene.Improvements(Wrap(x), y);

			var hamletsNearCities = new List<(float Base, float X, SKBitmap Bitmap)>();

			// fields: irrigated land, and the worked land around cities
			foreach ((int x, int y) in cells)
			{
				int t = TerrainAt(x, y);

				if (!ExploredAt(x, y) || IsCity(x, y) || (t != Grassland && t != Plains && t != Desert && t != River))
					continue;

				bool irrigated = (Improvements(x, y) & 0x2) != 0;
				bool nearCity = false;

				for (int dy = -1; dy <= 1 && !nearCity; dy++)
					for (int dx = -1; dx <= 1 && !nearCity; dx++)
						nearCity = IsCity(x + dx, y + dy);

				if (nearCity && t != Desert && Hash(Wrap(x), y, 23) < 0.3f)
				{
					List<SKBitmap> hamlets = this.sets["hamlet"];

					if (hamlets.Count > 0)
					{
						SKBitmap b = hamlets[(int)(Hash(Wrap(x), y, 24) * hamlets.Count) % hamlets.Count];
						SKPoint c = Screen(x + 0.3f + (Hash(Wrap(x), y, 25) * 0.4f), y + 0.3f + (Hash(Wrap(x), y, 26) * 0.4f));
						hamletsNearCities.Add((c.Y, c.X, b));
					}
				}

			}

			// swamp pools and pollution stains lie on the ground
			using (SKPaint pool = new SKPaint { IsAntialias = true, Color = new SKColor(52, 84, 92) }, poolEdge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, Color = new SKColor(34, 40, 36) },
				sludge = new SKPaint { IsAntialias = true, Color = new SKColor(58, 48, 36, 215) }, sludgeEdge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f, Color = new SKColor(30, 24, 18) },
				dead = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, Color = new SKColor(150, 128, 88) })
			{
				foreach ((int x, int y) in cells)
				{
					if (!ExploredAt(x, y))
						continue;

					if (TerrainAt(x, y) == Swamp)
					{
						for (int n = 0; n < 3; n++)
						{
							SKPoint c = Screen(x + 0.2f + (Hash(Wrap(x), y, 600 + n) * 0.6f), y + 0.2f + (Hash(Wrap(x), y, 700 + n) * 0.6f));
							SKRect r = new SKRect(c.X - 9, c.Y - 3.5f, c.X + 9, c.Y + 3.5f);
							canvas.DrawOval(r, pool);
							canvas.DrawOval(r, poolEdge);
						}
					}

				}
			}

			// rivers and roads are routed as smooth splines: each cell of the network gets a node near its centre, nudged a
			// little so no route runs dead straight, and each link between two nodes is a cubic whose tangent runs on
			// through the node, so the route bends from one link into the next without a corner
			static SKPoint Unit(float x, float y)
			{
				float l = MathF.Max(0.001f, MathF.Sqrt((x * x) + (y * y)));
				return new SKPoint(x / l, y / l);
			}

			// the tangent at node p, leaving towards q: through a node with two links it follows the line from the other
			// link's node to q; at an end or a junction it points straight at q
			SKPoint Tangent(SKPoint p, SKPoint q, List<SKPoint> links)
			{
				SKPoint toQ = Unit(q.X - p.X, q.Y - p.Y);

				// a junction keeps its through route smooth: the link most nearly opposite this one carries on through the
				// node, and a branch meets that route head on
				SKPoint? through = null;
				float best = links.Count == 2 ? 2 : -0.3f;

				foreach (SKPoint o in links)
				{
					if (MathF.Abs(o.X - q.X) + MathF.Abs(o.Y - q.Y) < 0.01f)
						continue;

					SKPoint toO = Unit(o.X - p.X, o.Y - p.Y);
					float dot = (toQ.X * toO.X) + (toQ.Y * toO.Y);

					if (dot < best)
					{
						best = dot;
						through = o;
					}
				}

				return through is SKPoint t ? Unit(q.X - t.X, q.Y - t.Y) : toQ;
			}

			// samples the link from a to b, with a sideways meander of the given amplitude that is still at both ends
			SKPoint[] Link(SKPoint a, SKPoint b, List<SKPoint> aLinks, List<SKPoint> bLinks, float meander, int samples)
			{
				float len = MathF.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y)));
				SKPoint ta = Tangent(a, b, aLinks), tb = Tangent(b, a, bLinks);
				SKPoint c1 = new SKPoint(a.X + (ta.X * len / 3), a.Y + (ta.Y * len / 3)), c2 = new SKPoint(b.X + (tb.X * len / 3), b.Y + (tb.Y * len / 3));
				SKPoint n = Unit(-(b.Y - a.Y), b.X - a.X);
				var pts = new SKPoint[samples + 1];

				for (int i = 0; i <= samples; i++)
				{
					float t = i / (float)samples, u = 1 - t;
					float x = (u * u * u * a.X) + (3 * u * u * t * c1.X) + (3 * u * t * t * c2.X) + (t * t * t * b.X);
					float y = (u * u * u * a.Y) + (3 * u * u * t * c1.Y) + (3 * u * t * t * c2.Y) + (t * t * t * b.Y);
					float off = meander * MathF.Sin(t * 6.2832f) * MathF.Sin(t * 3.1416f);
					pts[i] = new SKPoint(x + (n.X * off), y + (n.Y * off));
				}

				return pts;
			}

			// a ribbon whose width runs from wa to wb along the points, with round ends so links join smoothly
			SKPath Ribbon(SKPoint[] pts, float wa, float wb)
			{
				SKPath path = new SKPath();
				int last = pts.Length - 1;
				var left = new SKPoint[pts.Length];
				var right = new SKPoint[pts.Length];

				for (int i = 0; i <= last; i++)
				{
					SKPoint d = Unit(pts[Math.Min(last, i + 1)].X - pts[Math.Max(0, i - 1)].X, pts[Math.Min(last, i + 1)].Y - pts[Math.Max(0, i - 1)].Y);
					float half = (wa + ((wb - wa) * i / last)) / 2;
					left[i] = new SKPoint(pts[i].X - (d.Y * half), pts[i].Y + (d.X * half));
					right[i] = new SKPoint(pts[i].X + (d.Y * half), pts[i].Y - (d.X * half));
				}

				path.MoveTo(left[0]);

				for (int i = 1; i <= last; i++)
					path.LineTo(left[i]);

				for (int i = last; i >= 0; i--)
					path.LineTo(right[i]);

				path.Close();
				path.AddCircle(pts[0].X, pts[0].Y, wa / 2);
				path.AddCircle(pts[last].X, pts[last].Y, wb / 2);
				return path;
			}

			var riverCourses = new List<(SKPoint[] Pts, float Wa, float Wb)>();
			var riverNodes = new Dictionary<(int, int), SKPoint>();

			// rivers, as in the mockup: a muted grey-blue water course that meanders, narrow at its source and wider
			// downstream, over a pale bank and a thin dark edge so it looks cut into the land, with a few ripple marks
			{
				bool IsRiver(int x, int y) => TerrainAt(x, y) == River && ExploredAt(x, y);
				var nodes = riverNodes;
				var links = new Dictionary<(int, int), List<SKPoint>>();
				var mouths = new List<((int X, int Y) From, SKPoint To)>();
				(int, int)[] four = { (1, 0), (0, 1), (-1, 0), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1) };

				foreach ((int x, int y) in cells)
					if (IsRiver(x, y))
						nodes[(x, y)] = Screen(x + 0.5f + ((Hash(Wrap(x), y, 33) - 0.5f) * 0.3f), y + 0.5f + ((Hash(Wrap(x), y, 34) - 0.5f) * 0.3f));

				// the river cells are joined as a tree, never in loops: where river cells lie side by side the links that
				// would close a ring are left out (straight links are taken before diagonal ones), so a river is always
				// one course with branches, not a ladder
				var parent = new Dictionary<(int, int), (int, int)>();

				(int, int) Root((int, int) c)
				{
					while (parent.TryGetValue(c, out var p) && p != c)
						c = p;

					return c;
				}

				var keep = new HashSet<((int, int), (int, int))>();
				var order = new List<(int X, int Y)>(nodes.Keys);
				order.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));

				foreach (bool diagonal in new[] { false, true })
				{
					foreach ((int x, int y) in order)
					{
						foreach ((int dx, int dy) in four)
						{
							if ((dx != 0 && dy != 0) != diagonal || dy < 0 || (dy == 0 && dx < 0) || !nodes.ContainsKey((x + dx, y + dy)))
								continue;

							if (diagonal && (IsRiver(x + dx, y) || IsRiver(x, y + dy) || TerrainAt(x + dx, y) == Water || TerrainAt(x, y + dy) == Water))
								continue;

							(int, int) ra = Root((x, y)), rb = Root((x + dx, y + dy));

							if (ra == rb)
								continue;

							parent[ra] = rb;
							keep.Add(((x, y), (x + dx, y + dy)));
							keep.Add(((x + dx, y + dy), (x, y)));
						}
					}
				}

				var mouthed = new HashSet<(int, int)>();

				foreach ((int x, int y) in order)
				{
					var list = new List<SKPoint>();
					var seaSides = new List<(int, int)>();
					float upX = 0, upY = 0; // the direction the river flows in here: away from its other cells

					foreach ((int dx, int dy) in four)
					{
						if (dx != 0 && dy != 0 && (IsRiver(x + dx, y) || IsRiver(x, y + dy) || TerrainAt(x + dx, y) == Water || TerrainAt(x, y + dy) == Water))
							continue;

						if (nodes.TryGetValue((x + dx, y + dy), out SKPoint q) && keep.Contains(((x, y), (x + dx, y + dy))))
						{
							list.Add(q);
							upX -= dx;
							upY -= dy;
						}
						else if (!nodes.ContainsKey((x + dx, y + dy)) && TerrainAt(x + dx, y + dy) == Water && dx * dy == 0)
						{
							seaSides.Add((dx, dy));
						}
					}

					// one mouth to a cell, never a delta around an island: on the side of the sea that carries the river's
					// flow on, so it does not hook back before it reaches the shore
					// (and one mouth to a river, so it never reaches the sea by two ways round)
					if (seaSides.Count > 0 && mouthed.Add(Root((x, y))))
					{
						(int sx, int sy) = seaSides[0];
						float bestDot = float.MinValue;

						foreach ((int dx, int dy) in seaSides)
						{
							float dot = (dx * upX) + (dy * upY);

							if (dot > bestDot)
							{
								bestDot = dot;
								(sx, sy) = (dx, dy);
							}
						}

						SKPoint mouth = Screen(x + 0.5f + (sx * 1.05f), y + 0.5f + (sy * 1.05f));
						list.Add(mouth);
						mouths.Add(((x, y), mouth));
					}

					links[(x, y)] = list;
				}

				// how far downstream each cell is from the sea, through the river
				var toSea = new Dictionary<(int, int), int>();
				var queue = new Queue<(int, int)>();

				foreach (var m in mouths)
					if (toSea.TryAdd(m.From, 0))
						queue.Enqueue(m.From);

				while (queue.Count > 0)
				{
					(int x, int y) = queue.Dequeue();

					foreach ((int dx, int dy) in four)
						if (nodes.ContainsKey((x + dx, y + dy)) && links[(x, y)].Contains(nodes[(x + dx, y + dy)]) && toSea.TryAdd((x + dx, y + dy), toSea[(x, y)] + 1))
							queue.Enqueue((x + dx, y + dy));
				}

				float Width(int x, int y) => toSea.TryGetValue((x, y), out int d) ? MathF.Max(3.4f, 6.6f - (d * 0.4f)) : 4.2f;
				var courses = riverCourses;

				foreach (((int x, int y), SKPoint a) in nodes)
				{
					foreach ((int dx, int dy) in four)
					{
						if (dy < 0 || (dy == 0 && dx < 0) || !nodes.TryGetValue((x + dx, y + dy), out SKPoint b) || !links[(x, y)].Contains(b))
							continue;

						float amp = (Hash(Wrap(x) * 2 + dx, (y * 2) + dy, 35) < 0.5f ? -1 : 1) * 3.2f;
						courses.Add((Link(a, b, links[(x, y)], links[(x + dx, y + dy)], amp, 16), Width(x, y), Width(x + dx, y + dy)));
					}
				}

				foreach (var m in mouths)
				{
					float wd = Width(m.From.X, m.From.Y);
					// the mouth opens out into the sea; everything past the shore is cut away below
					courses.Add((Link(nodes[m.From], m.To, links[m.From], new List<SKPoint>(), 0, 12), wd, wd + 7));
				}

				using SKPaint bank = new SKPaint { IsAntialias = true, Color = new SKColor(212, 202, 162) };
				using SKPaint edgeInk = new SKPaint { IsAntialias = true, Color = new SKColor(42, 52, 60) };
				using SKPaint water = new SKPaint { IsAntialias = true, Color = new SKColor(66, 112, 142) };
				using SKPaint ripple = Stroke(new SKColor(132, 168, 186), 0.9f);

				foreach (var c in courses)
				{
					using SKPath p = Ribbon(c.Pts, c.Wa + 4.2f, c.Wb + 4.2f);
					canvas.DrawPath(p, bank);
				}

				foreach (var c in courses)
				{
					using SKPath p = Ribbon(c.Pts, c.Wa + 1.5f, c.Wb + 1.5f);
					canvas.DrawPath(p, edgeInk);
				}

				foreach (var c in courses)
				{
					using SKPath p = Ribbon(c.Pts, c.Wa, c.Wb);
					canvas.DrawPath(p, water);
				}

				// short ripple marks along the water, a little off its middle
				foreach (var c in courses)
				{
					for (int i = 2; i + 2 < c.Pts.Length; i += 5)
					{
						SKPoint a = c.Pts[i], b = c.Pts[i + 2];
						SKPoint d = Unit(b.X - a.X, b.Y - a.Y);
						float side = (i % 2 == 0 ? 1 : -1) * 0.18f * (c.Wa + c.Wb) / 2;
						canvas.DrawLine(a.X - (d.Y * side), a.Y + (d.X * side), b.X - (d.Y * side), b.Y + (d.X * side), ripple);
					}
				}

				// the river stops at the shore: over the sea the ground as painted comes back, so the mouth opens cleanly
				// into the shallows, its banks ending at the beach, instead of a tube lying on the water
				if (mouths.Count > 0)
				{
					canvas.Flush();
					int[] shore = new int[w * h];
					System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(), shore, 0, shore.Length);

					// the river's water fans out a little into the shallows at its mouth, fading as it goes
					var plume = new List<SKPoint>();

					foreach (var m in mouths)
						plume.Add(m.To);

					for (int i = 0; i < shore.Length; i++)
					{
						if (kind[i] != Sea)
							continue;

						uint c = img[i];
						float px = i % w, py = i / w, fade = 0;

						foreach (SKPoint m in plume)
						{
							float ddx = (px - m.X) / 1.6f, ddy = py - m.Y;
							fade = MathF.Max(fade, 1 - (MathF.Sqrt((ddx * ddx) + (ddy * ddy)) / 15));
						}

						fade = MathF.Max(0, fade) * Math.Clamp(1 - (seaDist[i] / 12), 0, 1) * 0.6f;
						float cr = (c >> 16) & 0xff, cg = (c >> 8) & 0xff, cb = c & 0xff;
						shore[i] = unchecked((int)Pack(cr + ((66 - cr) * fade), cg + ((112 - cg) * fade), cb + ((142 - cb) * fade)));
					}

					System.Runtime.InteropServices.Marshal.Copy(shore, 0, bitmap.GetPixels(), shore.Length);
				}
			}

			// the points every road, railroad and river passes through, so trees and scrub that would hide them are left out
			var routePoints = new List<SKPoint>();

			foreach (var c in riverCourses)
				routePoints.AddRange(c.Pts);
			var overMountains = new List<(SKPoint[] Pts, bool Rail)>();

			// roads and railroads, as in the mockup: a cream path with a thin darker edge on both sides, bending in smooth
			// curves through the network. A road runs to its city's walls, ending at a gate on the side it comes from, so
			// a city can be entered from every side. Where three or more meet there is a small open junction; where a
			// road crosses a river, a bridge. Railroads are a gravel bed with sleepers and two rails. Diagonal links are
			// left out where the two cells beside them already join the ends, so routes never form a lattice.
			{
				bool Road(int x, int y) => ExploredAt(x, y) && ((Improvements(x, y) & 0x19) != 0 || CityCell(x, y));
				bool Rail(int x, int y) => ExploredAt(x, y) && ((Improvements(x, y) & 0x10) != 0 || CityCell(x, y));
				(int, int)[] eight = { (1, 0), (0, 1), (-1, 0), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1) };
				var nodes = new Dictionary<(int, int), SKPoint>();
				var links = new Dictionary<(int, int), List<(int, int)>>();

				foreach ((int x, int y) in cells)
				{
					if (!Road(x, y))
						continue;

					float j = CityCell(x, y) ? 0 : 0.22f;
					float ox = (Hash(Wrap(x), y, 43) - 0.5f) * j, oy = (Hash(Wrap(x), y, 44) - 0.5f) * j;

					// on a river the road keeps to one bank, clear of the water, and crosses it only on its way out
					// (the bank facing the roads it joins, so a road along a river stays on one side of it)
					if (!CityCell(x, y) && riverNodes.ContainsKey((x, y)))
					{
						var rn = new List<(int X, int Y)>();
						float ax = 0, ay = 0;

						foreach ((int dx, int dy) in eight)
						{
							if (riverNodes.ContainsKey((x + dx, y + dy)) && (dx == 0 || dy == 0))
								rn.Add((dx, dy));

							if (Road(x + dx, y + dy) && !riverNodes.ContainsKey((x + dx, y + dy)))
							{
								ax += dx;
								ay += dy;
							}
						}

						float tx = rn.Count >= 2 ? rn[1].X - rn[0].X : rn.Count == 1 ? rn[0].X : 1;
						float ty = rn.Count >= 2 ? rn[1].Y - rn[0].Y : rn.Count == 1 ? rn[0].Y : 0;
						SKPoint perp = Unit(-ty, tx);
						float dot = (ax * perp.X) + (ay * perp.Y);
						float side = dot > 0.01f ? 1 : dot < -0.01f ? -1 : (perp.X + perp.Y >= 0 ? 1 : -1);
						ox = perp.X * 0.3f * side;
						oy = perp.Y * 0.3f * side;
					}

					nodes[(x, y)] = Screen(x + 0.5f + ox, y + 0.5f + oy);
				}

				foreach (((int x, int y), SKPoint _) in nodes)
				{
					var list = new List<(int, int)>();

					foreach ((int dx, int dy) in eight)
					{
						if (!nodes.ContainsKey((x + dx, y + dy)))
							continue;

						if (dx != 0 && dy != 0 && (Road(x + dx, y) || Road(x, y + dy)))
							continue;

						// two cities side by side are not joined by a road of their own
						if (CityCell(x, y) && CityCell(x + dx, y + dy))
							continue;

						list.Add((x + dx, y + dy));
					}

					links[(x, y)] = list;
				}

				// where a link meets a city it ends at the gate: on the city's edge, on the side the link comes from
				SKPoint End((int X, int Y) cell, (int X, int Y) other)
				{
					if (!CityCell(cell.X, cell.Y))
						return nodes[cell];

					return Screen(cell.X + 0.5f + ((other.X - cell.X) * 0.36f), cell.Y + 0.5f + ((other.Y - cell.Y) * 0.36f));
				}

				// the tangents at a node come from its other links; a gate has none, so the road arrives head on
				List<SKPoint> Around((int X, int Y) cell) => CityCell(cell.X, cell.Y) ? new List<SKPoint>() : links[cell].ConvertAll(k => nodes[k]);
				var routes = new List<(SKPoint[] Pts, bool Rail, bool Mountain)>();

				foreach (((int x, int y), List<(int, int)> list) in links)
				{
					foreach ((int bx, int by) in list)
					{
						if (by < y || (by == y && bx < x))
							continue;

						SKPoint a = End((x, y), (bx, by)), b = End((bx, by), (x, y));
						List<SKPoint> aAround = Around((x, y)), bAround = Around((bx, by));

						// a gate's road leaves the wall straight, towards the next node
						SKPoint[] pts = Link(a, b, aAround, bAround, 0, 14);
						int ta = TerrainAt(x, y), tb = TerrainAt(bx, by);
						bool rail = Rail(x, y) && Rail(bx, by) && !(CityCell(x, y) && CityCell(bx, by)) && ((Improvements(x, y) & 0x10) != 0 || (Improvements(bx, by) & 0x10) != 0);
						routes.Add((pts, rail, (ta == Mountains && !CityCell(x, y)) || (tb == Mountains && !CityCell(bx, by))));
						routePoints.AddRange(pts);
					}
				}

				// bridges: where a route crosses a river's course, a short deck spanning the water and its banks
				static bool Cross(SKPoint p1, SKPoint p2, SKPoint q1, SKPoint q2, out float t)
				{
					float d = ((p2.X - p1.X) * (q2.Y - q1.Y)) - ((p2.Y - p1.Y) * (q2.X - q1.X));
					t = 0;

					if (MathF.Abs(d) < 1e-6f)
						return false;

					t = (((q1.X - p1.X) * (q2.Y - q1.Y)) - ((q1.Y - p1.Y) * (q2.X - q1.X))) / d;
					float u = (((q1.X - p1.X) * (p2.Y - p1.Y)) - ((q1.Y - p1.Y) * (p2.X - p1.X))) / d;
					return t >= 0 && t <= 1 && u >= 0 && u <= 1;
				}

				var bridges = new List<SKPoint[]>();

				foreach (var r in routes)
				{
					for (int i = 1; i < r.Pts.Length; i++)
					{
						foreach (var c in riverCourses)
						{
							for (int k = 1; k < c.Pts.Length; k++)
							{
								if (!Cross(r.Pts[i - 1], r.Pts[i], c.Pts[k - 1], c.Pts[k], out float t))
									continue;

								SKPoint at = new SKPoint(r.Pts[i - 1].X + ((r.Pts[i].X - r.Pts[i - 1].X) * t), r.Pts[i - 1].Y + ((r.Pts[i].Y - r.Pts[i - 1].Y) * t));
								SKPoint dir = Unit(r.Pts[i].X - r.Pts[i - 1].X, r.Pts[i].Y - r.Pts[i - 1].Y);
								float span = ((c.Wa + c.Wb) / 4) + 4.4f;
								bridges.Add(new[] { new SKPoint(at.X - (dir.X * span), at.Y - (dir.Y * span)), new SKPoint(at.X + (dir.X * span), at.Y + (dir.Y * span)) });
							}
						}
					}
				}

				DrawRoutes(canvas, routes.ConvertAll(r => (r.Pts, r.Rail)), bridges);

				// through mountains the road is drawn again over the peaks, as a pass cut through them
				foreach (var r in routes)
					if (r.Mountain)
						overMountains.Add((r.Pts, r.Rail));
			}

			// everything that stands on the ground, back to front
			bool HidesRoute(float x, float baseY, SKBitmap b)
			{
				float half = b.Width * 0.42f, top = baseY - (b.Height * 0.85f);

				foreach (SKPoint r in routePoints)
					if (MathF.Abs(r.X - x) < half && r.Y > top && r.Y < baseY + 3)
						return true;

				return false;
			}

			var items = new List<(float Base, float X, SKBitmap Bitmap)>();

			foreach (var hamlet in hamletsNearCities)
				if (!HidesRoute(hamlet.X, hamlet.Base, hamlet.Bitmap))
					items.Add(hamlet);
			// mines, fortresses, resources and huts: drawn over the props, so trees and peaks never hide them
			var topLayer = new List<(float Base, float X, SKBitmap Bitmap)>();

			SKBitmap? Pick(string set, int x, int y, int n)
			{
				List<SKBitmap> list = this.sets[set];
				return list.Count == 0 ? null : list[(int)(Hash(Wrap(x), y, 100 + n) * list.Count) % list.Count];
			}

			bool busy = false;

			void Put(string set, int x, int y, int n, float margin, float lift = 0)
			{
				SKBitmap? b = Pick(set, x, y, n);

				// fields are open ground, as in the mockup: nothing grows on them but the crops
				if (b == null || (set != "peaks" && set != "hamlet" && IsFieldCell(x, y)))
					return;

				float fx = margin + (Hash(Wrap(x), y, 200 + n) * (1 - (2 * margin))), fy = margin + (Hash(Wrap(x), y, 300 + n) * (1 - (2 * margin)));

				// a cell with an improvement, a resource or a hut keeps a clearing in its middle, so they are seen
				if (busy && (((fx - 0.5f) * (fx - 0.5f)) + ((fy - 0.5f) * (fy - 0.5f))) < 0.16f)
					return;

				SKPoint p = Screen(x + fx, y + fy);

				// nothing stands in the sea: woods and scrub follow the painted shoreline, not the cells
				int sx = (int)p.X, sy = (int)p.Y;

				if (sx >= 0 && sy >= 0 && sx < w && sy < h && kind[(sy * w) + sx] == Sea)
					return;

				// roads stay in view: a tree, bush or hill whose sprite would cover one is left out
				if (set != "peaks" && HidesRoute(p.X, p.Y + lift, b))
					return;

				items.Add((p.Y + lift, p.X, b));
			}

			bool Near(int x, int y, int terrain)
			{
				for (int dy = -1; dy <= 1; dy++)
					for (int dx = -1; dx <= 1; dx++)
						if ((dx != 0 || dy != 0) && TerrainAt(x + dx, y + dy) == terrain)
							return true;

				return false;
			}

			foreach ((int x, int y) in cells)
			{
				if (!ExploredAt(x, y) || IsCity(x, y))
					continue;

				int t = TerrainAt(x, y);
				int count;
				busy = (Improvements(x, y) & 0x7c) != 0 || scene.Feature(Wrap(x), y) != 0 || ((Improvements(x, y) & 0x18) != 0 && (t == Forest || t == Jungle || t == Mountains));

				switch (t)
				{
					case Forest:
						for (int n = 0; n < 10; n++)
							Put("trees", x, y, n, 0.04f);
						break;

					case Jungle:
						for (int n = 0; n < 8; n++)
							Put("jungle", x, y, n, 0.04f);
						break;

					case Mountains:
						if (busy)
						{
							SKBitmap? peak = Pick("peaks", x, y, 0);

							if (peak != null)
							{
								SKPoint p = Screen(x + 0.3f, y + 0.3f);
								items.Add((p.Y, p.X, peak));
							}
						}
						else
						{
							Put("peaks", x, y, 0, 0.45f, 10 * S);
						}
						break;

					case Hills:
						for (int n = 0; n < 2; n++)
							Put("hills", x, y, n, 0.25f, 8 * S);
						break;

					case Desert:
						count = 2 + (int)(Hash(Wrap(x), y, 7) * 2);
						for (int n = 0; n < count; n++)
							Put("dunes", x, y, n, 0.15f);
						break;

					case Swamp:
						count = 7 + (int)(Hash(Wrap(x), y, 7) * 4);
						for (int n = 0; n < count; n++)
							Put("reeds", x, y, n, 0.1f);
						break;

					case Tundra:
						count = 5 + (int)(Hash(Wrap(x), y, 7) * 3);
						for (int n = 0; n < count; n++)
							Put("tundra", x, y, n, 0.12f);
						break;

					case Arctic:
						count = 3 + (int)(Hash(Wrap(x), y, 7) * 2);
						for (int n = 0; n < count; n++)
							Put("ice", x, y, n, 0.15f);
						break;

					case Grassland:
					case River:
						// lush: lone trees, bushes and flowers
						count = 1 + (int)(Hash(Wrap(x), y, 7) * 2.6f);
						for (int n = 0; n < count; n++)
							Put(Hash(Wrap(x), y, 400 + n) < 0.4f ? "lone" : "tufts", x, y, n, 0.1f);
						break;

					case Plains:
						// dry and open: a few tufts and stones, rarely a tree
						count = 1 + (int)(Hash(Wrap(x), y, 7) * 3);
						for (int n = 0; n < count; n++)
						{
							float r = Hash(Wrap(x), y, 400 + n);
							Put(r < 0.1f ? "lone" : r < 0.75f ? "tufts" : "tundra", x, y, n, 0.1f);
						}
						break;
				}

				// woods spill a few clumps onto open land beside them, so their edges do not follow the cells
				if ((t == Grassland || t == Plains || t == Tundra) && (Near(x, y, Forest) || Near(x, y, Jungle)))
				{
					count = 1 + (int)(Hash(Wrap(x), y, 8) * 3);
					for (int n = 0; n < count; n++)
						Put(Near(x, y, Forest) ? "trees" : "jungle", x, y, 20 + n, 0.05f);
				}

				// mines and fortresses
				int imp = Improvements(x, y);

				if ((imp & 0x4) != 0 && this.objects.TryGetValue("mine", out SKBitmap? mine))
				{
					SKPoint p = Screen(x + 0.55f, y + 0.7f);
					topLayer.Add((p.Y + 6, p.X, mine));
				}


				if ((imp & 0x20) != 0 && this.objects.TryGetValue("fortress", out SKBitmap? fort))
				{
					SKPoint p = Screen(x + 0.5f, y + 0.5f);
					topLayer.Add((p.Y + 12, p.X, fort));
				}

				// special resources and huts
				int feature = scene.Feature(Wrap(x), y);

				// fish only near land, where a city could work them, and smaller, so the open sea is not dotted with them
				bool nearLand = t != Water;

				for (int dy = -2; dy <= 2 && !nearLand; dy++)
					for (int dx = -2; dx <= 2 && !nearLand; dx++)
						nearLand = TerrainAt(x + dx, y + dy) is >= 0 and not Water;

				if ((feature & 1) != 0 && t == Water && nearLand && this.objects.TryGetValue("fish", out SKBitmap? fish))
				{
					SKPoint p = Screen(x + 0.5f, y + 0.55f);
					items.Add((p.Y + 4, p.X, fish));
				}
				else if ((feature & 1) != 0 && t != Water && t < ResourceNames.Length && this.objects.TryGetValue("res_" + ResourceNames[t], out SKBitmap? res))
				{
					SKPoint p = Screen(x + 0.6f, y + 0.6f);
					topLayer.Add((p.Y + 4, p.X, res));
				}

				if ((feature & 2) != 0 && this.objects.TryGetValue("hut", out SKBitmap? hut))
				{
					SKPoint p = Screen(x + 0.4f, y + 0.45f);
					topLayer.Add((p.Y + 4, p.X, hut));
				}
			}

			// cities and units
			foreach (var c in scene.Cities)
			{
				(int x, int y) = Nearest(c.X, c.Y, cx);
				List<SKBitmap> towns = this.sets["town"];

				if (towns.Count == 0)
					continue;

				SKBitmap town = towns[c.Size >= 8 ? 0 : c.Size >= 4 ? 1 : 2 + ((c.X + c.Y) & 1)];
				SKPoint p = Screen(x + 0.5f, y + 0.5f);
				items.Add((p.Y + (30 * S) + 6, p.X, town));
			}

			foreach (var u in scene.Units)
			{
				if (u.Type < 0 || u.Type >= UnitNames.Length || !this.objects.TryGetValue("unit_" + UnitNames[u.Type], out SKBitmap? unit))
					continue;

				(int x, int y) = Nearest(u.X, u.Y, cx);
				SKPoint p = Screen(x + 0.5f, y + 0.5f);
				items.Add((p.Y + 14, p.X, unit));
			}

			items.Sort((a, b) => a.Base.CompareTo(b.Base));
			topLayer.Sort((a, b) => a.Base.CompareTo(b.Base));

			using (SKPaint sprite = new SKPaint { IsAntialias = true })
			{
				foreach (var item in items)
					canvas.DrawBitmap(item.Bitmap, item.X - (item.Bitmap.Width / 2f), item.Base - item.Bitmap.Height, sprite);

				DrawRoutes(canvas, overMountains, new List<SKPoint[]>());

				// pollution (parked: small and neutral for now): a small dark patch at the tile's centre
				using (SKPaint patch = new SKPaint { IsAntialias = true, Color = new SKColor(60, 52, 42, 170) })
				{
					foreach ((int x, int y) in cells)
					{
						if ((Improvements(x, y) & 0x40) == 0 || !ExploredAt(x, y))
							continue;

						SKPoint c = Screen(x + 0.5f, y + 0.5f);
						canvas.DrawOval(new SKRect(c.X - 9, c.Y - 4, c.X + 9, c.Y + 4), patch);
					}
				}

				foreach (var item in topLayer)
					canvas.DrawBitmap(item.Bitmap, item.X - (item.Bitmap.Width / 2f), item.Base - item.Bitmap.Height, sprite);
			}

			// unexplored land: dark paper with bands of mist, over everything, with a soft edge
			canvas.Flush();
			uint[] drawn = new uint[w * h];
			int[] raw = new int[w * h];
			System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(), raw, 0, raw.Length);

			for (int py = 0; py < h; py++)
			{
				for (int px = 0; px < w; px++)
				{
					int i = (py * w) + px;
					uint c = (uint)raw[i];
					float f = Math.Clamp((fog[i] - 0.3f) / 0.4f, 0, 1);

					if (f > 0)
					{
						float u = (px - (w / 2f)) / halfW, v = (py - (h / 2f)) / halfH;
						float gx0 = cx + ((u + v) / 2), gy0 = cy + ((v - u) / 2);
						float wy = (gx0 + gy0) * halfH, wx = (gx0 - gy0) * halfW;
						bool mist = ((((wy + (MathF.Sin(wx / 70) * 9)) % 46) + 46) % 46) < 15 && Noise(gx0, gy0, 0.9f, 9) > 0.45f;
						float fr = mist ? 52 : 34, fg = mist ? 52 : 34, fb = mist ? 64 : 42;
						c = Pack((((c >> 16) & 0xff) * (1 - f)) + (fr * f), (((c >> 8) & 0xff) * (1 - f)) + (fg * f), ((c & 0xff) * (1 - f)) + (fb * f));
					}

					drawn[i] = c | 0xff000000;
				}
			}

			System.Runtime.InteropServices.Marshal.Copy(Array.ConvertAll(drawn, x => unchecked((int)x)), 0, bitmap.GetPixels(), drawn.Length);

			// banners on units, flags and plates on cities
			foreach (var u in scene.Units)
			{
				(int x, int y) = Nearest(u.X, u.Y, cx);
				SKPoint p = Screen(x + 0.5f, y + 0.5f);
				Flag(canvas, p.X - 14, p.Y - 26, 10, u.Colour, 1.9f);
			}

			using SKFont font = new SKFont(SKTypeface.FromFamilyName("DejaVu Serif", SKFontStyle.Bold) ?? SKTypeface.Default, 13);
			using SKPaint fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
			using SKPaint outline = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, Color = new SKColor(40, 34, 28) };

			foreach (var c in scene.Cities)
			{
				(int x, int y) = Nearest(c.X, c.Y, cx);
				SKPoint p = Screen(x + 0.5f, y + 0.5f);
				// only cities in view get a plate
				if (p.X < -40 || p.X > w + 40 || p.Y < -40 || p.Y > h + 40)
					continue;

				Flag(canvas, p.X + 14, p.Y - 30, 13, c.Colour, 2.3f);
				string size = c.Size.ToString();
				float nameWidth = font.MeasureText(c.Name);
				float box = 18, left = Math.Clamp(p.X - ((nameWidth + box + 12) / 2), 4, w - (nameWidth + box + 12) - 4), top = Math.Clamp(p.Y + 18, 4, h - box - 4);
				fill.Color = new SKColor(c.Colour | 0xff000000);
				canvas.DrawRect(left, top, box, box, fill);
				canvas.DrawRect(left, top, box, box, outline);
				fill.Color = new SKColor(240, 230, 204);
				canvas.DrawRect(left + box, top, nameWidth + 12, box, fill);
				canvas.DrawRect(left + box, top, nameWidth + 12, box, outline);
				fill.Color = new SKColor(244, 236, 216);
				canvas.DrawText(size, left + (box / 2) - (font.MeasureText(size) / 2), top + 13.5f, font, fill);
				fill.Color = new SKColor(40, 34, 28);
				canvas.DrawText(c.Name, left + box + 6, top + 13.5f, font, fill);
			}

			canvas.Flush();
			int[] result = new int[w * h];
			System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(), result, 0, result.Length);
			uint[] next = new uint[w * h];

			for (int i = 0; i < next.Length; i++)
				next[i] = (uint)result[i] | 0xff000000;

			this.pixels = next;
		}

		// the copy of a map cell nearest the view's centre, across the world's wrap
		private static (int X, int Y) Nearest(int x, int y, float cx)
		{
			return (x + ((int)MathF.Round((cx - x) / MapWidth) * MapWidth), y);
		}

		// draws roads (a darker edge, then a cream path over it) and railroads (a gravel bed, sleepers, two rails), then
		// bridges over them where they cross a river: a timber deck with dark parapets
		private static void DrawRoutes(SKCanvas canvas, List<(SKPoint[] Pts, bool Rail)> routes, List<SKPoint[]> bridges)
		{
			using SKPaint edge = Stroke(new SKColor(92, 74, 46), 5.0f), path = Stroke(new SKColor(218, 202, 154), 3.3f);
			using SKPaint bed = Stroke(new SKColor(70, 62, 52), 5.6f), gravel = Stroke(new SKColor(134, 122, 104), 4.2f);
			using SKPaint sleeper = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.1f, Color = new SKColor(70, 52, 36) };
			using SKPaint steel = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.9f, Color = new SKColor(40, 38, 40), StrokeJoin = SKStrokeJoin.Round };

			SKPath Poly(SKPoint[] pts)
			{
				SKPath p = new SKPath();
				p.AddPoly(pts, false);
				return p;
			}

			foreach (var r in routes)
			{
				using SKPath p = Poly(r.Pts);
				canvas.DrawPath(p, r.Rail ? bed : edge);
			}

			foreach (var r in routes)
			{
				using SKPath p = Poly(r.Pts);

				if (!r.Rail)
				{
					canvas.DrawPath(p, path);
					continue;
				}

				canvas.DrawPath(p, gravel);

				// sleepers across the bed every few pixels, then the two rails along it
				float carry = 0;
				var left = new SKPoint[r.Pts.Length];
				var right = new SKPoint[r.Pts.Length];

				for (int i = 0; i < r.Pts.Length; i++)
				{
					SKPoint a = r.Pts[Math.Max(0, i - 1)], b = r.Pts[Math.Min(r.Pts.Length - 1, i + 1)];
					float dx = b.X - a.X, dy = b.Y - a.Y, l = MathF.Max(0.001f, MathF.Sqrt((dx * dx) + (dy * dy)));
					float nx = -dy / l, ny = dx / l;
					left[i] = new SKPoint(r.Pts[i].X + (nx * 1.3f), r.Pts[i].Y + (ny * 1.3f));
					right[i] = new SKPoint(r.Pts[i].X - (nx * 1.3f), r.Pts[i].Y - (ny * 1.3f));

					if (i == 0)
						continue;

					float sx = r.Pts[i].X - r.Pts[i - 1].X, sy = r.Pts[i].Y - r.Pts[i - 1].Y;
					carry += MathF.Sqrt((sx * sx) + (sy * sy));

					while (carry >= 3.2f)
					{
						carry -= 3.2f;
						float t = 1 - (carry / MathF.Max(0.001f, MathF.Sqrt((sx * sx) + (sy * sy))));
						float px = r.Pts[i - 1].X + (sx * t), py = r.Pts[i - 1].Y + (sy * t);
						canvas.DrawLine(px + (nx * 2.4f), py + (ny * 2.4f), px - (nx * 2.4f), py - (ny * 2.4f), sleeper);
					}
				}

				using SKPath lp = Poly(left), rp = Poly(right);
				canvas.DrawPath(lp, steel);
				canvas.DrawPath(rp, steel);
			}

			// a bridge: the road carries straight on over the water, with a thin dark parapet rail along each side that
			// flares out a little at both ends, onto the banks
			using SKPaint parapet = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, Color = new SKColor(46, 36, 28), StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
			using SKPaint shade = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.2f, Color = new SKColor(30, 44, 56, 110), StrokeCap = SKStrokeCap.Butt };

			foreach (SKPoint[] b in bridges)
			{
				SKPoint a = b[0], z = b[^1];
				float dx = z.X - a.X, dy = z.Y - a.Y, l = MathF.Max(0.001f, MathF.Sqrt((dx * dx) + (dy * dy)));
				float ux = dx / l, uy = dy / l, nx = -uy, ny = ux;
				const float off = 2.8f, flare = 1.6f;

				// the bridge's shadow on the water, on its lower side
				float sx = ny >= 0 ? nx : -nx, sy = ny >= 0 ? ny : -ny;
				canvas.DrawLine(a.X + (ux * 2) + (sx * 3.6f), a.Y + (uy * 2) + (sy * 3.6f), z.X - (ux * 2) + (sx * 3.6f), z.Y - (uy * 2) + (sy * 3.6f), shade);

				foreach (float side in new[] { 1f, -1f })
				{
					using SKPath rail = new SKPath();
					rail.MoveTo(a.X - (ux * 1.6f) + (nx * side * (off + flare)), a.Y - (uy * 1.6f) + (ny * side * (off + flare)));
					rail.LineTo(a.X + (ux * 1.4f) + (nx * side * off), a.Y + (uy * 1.4f) + (ny * side * off));
					rail.LineTo(z.X - (ux * 1.4f) + (nx * side * off), z.Y - (uy * 1.4f) + (ny * side * off));
					rail.LineTo(z.X + (ux * 1.6f) + (nx * side * (off + flare)), z.Y + (uy * 1.6f) + (ny * side * (off + flare)));
					canvas.DrawPath(rail, parapet);
				}
			}
		}

		private static SKPaint Stroke(SKColor colour, float width)
		{
			return new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width, Color = colour, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
		}

		private static void Flag(SKCanvas canvas, float x, float y, float size, uint colour, float pole)
		{
			using SKPaint ink = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(1.5f, size / 6), Color = new SKColor(40, 34, 28) };
			using SKPaint cloth = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(colour | 0xff000000) };
			float top = y - (size * pole);
			canvas.DrawLine(x, y, x, top, ink);
			using SKPath path = new SKPath();
			path.MoveTo(x, top);
			path.LineTo(x + (size * 1.2f), top + (size * 0.12f));
			path.LineTo(x + size, top + (size * 0.45f));
			path.LineTo(x + (size * 1.2f), top + (size * 0.8f));
			path.LineTo(x, top + (size * 0.7f));
			path.Close();
			canvas.DrawPath(path, cloth);
			ink.StrokeWidth = 1.2f;
			canvas.DrawPath(path, ink);
		}

		// distance from each pixel to the nearest pixel that is a seed, by a two pass chamfer transform
		private static float[] Distance(int[] kind, int w, int h, Func<int, bool> seed)
		{
			const float Diagonal = 1.4142f;
			float[] d = new float[w * h];

			for (int i = 0; i < d.Length; i++)
				d[i] = seed(kind[i]) ? 0 : 1e6f;

			for (int y = 0; y < h; y++)
			{
				for (int x = 0; x < w; x++)
				{
					int i = (y * w) + x;
					if (x > 0) d[i] = Math.Min(d[i], d[i - 1] + 1);
					if (y > 0) d[i] = Math.Min(d[i], d[i - w] + 1);
					if (x > 0 && y > 0) d[i] = Math.Min(d[i], d[i - w - 1] + Diagonal);
					if (x < w - 1 && y > 0) d[i] = Math.Min(d[i], d[i - w + 1] + Diagonal);
				}
			}

			for (int y = h - 1; y >= 0; y--)
			{
				for (int x = w - 1; x >= 0; x--)
				{
					int i = (y * w) + x;
					if (x < w - 1) d[i] = Math.Min(d[i], d[i + 1] + 1);
					if (y < h - 1) d[i] = Math.Min(d[i], d[i + w] + 1);
					if (x < w - 1 && y < h - 1) d[i] = Math.Min(d[i], d[i + w + 1] + Diagonal);
					if (x > 0 && y < h - 1) d[i] = Math.Min(d[i], d[i + w - 1] + Diagonal);
				}
			}

			return d;
		}

		/// <summary>
		/// The map cell under a point of the view, for a view centred on (cx, cy); y may fall outside the map
		/// </summary>
		public (int X, int Y) CellAt(float px, float py, float cx, float cy)
		{
			float u = (px - (this.width / 2f)) / (TileWidth / 2f), v = (py - (this.height / 2f)) / (TileHeight / 2f);
			return (Wrap((int)MathF.Floor(cx + ((u + v) / 2))), (int)MathF.Floor(cy + ((v - u) / 2)));
		}
	}
}
