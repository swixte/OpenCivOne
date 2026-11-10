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

			foreach (string name in Array.ConvertAll(ResourceNames, n => "res_" + n))
				view.LoadObject(objectsFolder, name, height: 44, halo: true);

			view.LoadObject(objectsFolder, "hut", height: 48, halo: true);
			view.LoadObject(objectsFolder, "mine", height: 44, halo: true);
			view.LoadObject(objectsFolder, "fortress", height: 46, halo: true);

			// the fortress in grey stone, so it is not taken for the hut's thatch
			if (view.objects.TryGetValue("fortress", out SKBitmap? fortress))
			{
				SKBitmap stone = new SKBitmap(fortress.Width, fortress.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
				using SKCanvas c = new SKCanvas(stone);
				c.Clear(SKColors.Transparent);
				using SKPaint grey = new SKPaint { ColorFilter = SKColorFilter.CreateColorMatrix(new float[] {
					0.30f, 0.50f, 0.12f, 0, -0.02f,
					0.30f, 0.50f, 0.12f, 0, -0.01f,
					0.32f, 0.52f, 0.16f, 0, 0.02f,
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
							int t = TerrainAt(x, y);
							bool known = t >= 0 && ExploredAt(x, y);
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
					img[i] = Pack(((c >> 16) & 0xff) * tint[0], ((c >> 8) & 0xff) * tint[1], (c & 0xff) * tint[2]);

					if (g == Sea)
						kind[i] = Sea;
				}
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

				if (irrigated || (nearCity && t != Desert && Hash(Wrap(x), y, 21) < 0.7f))
				{
					List<SKBitmap> plots = this.sets[irrigated ? "gold" : "plots"];

					if (plots.Count == 0)
						continue;

					SKBitmap p = plots[(int)(Hash(Wrap(x), y, 22) * plots.Count)];
					SKPoint c = Screen(x + 0.5f, y + 0.5f);
					canvas.DrawBitmap(p, c.X - (p.Width / 2f), c.Y - (p.Height / 2f));
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

			// rivers: from cell to cell through a bent midpoint, and on into the sea; each layer drawn for every piece
			// before the next, so the water is one continuous ribbon
			List<SKPath> riverPaths = new List<SKPath>();

			foreach ((int x, int y) in cells)
			{
				if (TerrainAt(x, y) != River || !ExploredAt(x, y))
					continue;

				foreach ((int dx, int dy) in new[] { (1, 0), (0, 1), (-1, 0), (0, -1) })
				{
					int t = TerrainAt(x + dx, y + dy);
					bool toSea = t == Water;

					if (!(t == River && (dx > 0 || dy > 0)) && !toSea)
						continue;

					SKPoint a = Screen(x + 0.5f, y + 0.5f), b = Screen(x + 0.5f + (dx * (toSea ? 0.62f : 1)), y + 0.5f + (dy * (toSea ? 0.62f : 1)));
					float bend = (Hash(Wrap(x) * 2 + dx, (y * 2) + dy, 31) - 0.5f) * 30;
					SKPoint m = new SKPoint(((a.X + b.X) / 2) - ((b.Y - a.Y) / 40 * bend), ((a.Y + b.Y) / 2) + ((b.X - a.X) / 40 * bend));
					SKPath path = new SKPath();
					path.MoveTo(a);
					path.QuadTo(m, b);
					riverPaths.Add(path);
				}
			}

			using (SKPaint bank = Stroke(new SKColor(214, 204, 168), 9), ink = Stroke(new SKColor(34, 40, 56), 6.5f), water = Stroke(new SKColor(64, 124, 190), 4.4f))
			{
				foreach (SKPath path in riverPaths)
					canvas.DrawPath(path, bank);

				foreach (SKPath path in riverPaths)
					canvas.DrawPath(path, ink);

				foreach (SKPath path in riverPaths)
				{
					canvas.DrawPath(path, water);
					path.Dispose();
				}
			}

			var overRoads = new List<(SKPoint A, SKPoint M, SKPoint B, bool Rail)>();

			// roads and railroads: a cream path with a dark edge between neighbouring road cells and cities
			using (SKPaint edge = Stroke(new SKColor(70, 56, 36), 6), path = Stroke(new SKColor(222, 204, 158), 3.8f), rail = Stroke(new SKColor(60, 52, 44), 1.4f))
			{
				bool Road(int x, int y) => (Improvements(x, y) & 0x19) != 0 && ExploredAt(x, y);

				foreach ((int x, int y) in cells)
				{
					if (!Road(x, y))
						continue;

					foreach ((int dx, int dy) in new[] { (1, 0), (0, 1), (1, 1), (1, -1) })
					{
						if (!Road(x + dx, y + dy))
							continue;

						SKPoint a = Screen(x + 0.5f, y + 0.5f), b = Screen(x + dx + 0.5f, y + dy + 0.5f);
						float bend = (Hash(Wrap(x) * 3 + dx, (y * 3) + dy, 41) - 0.5f) * 10;
						SKPoint m = new SKPoint(((a.X + b.X) / 2) + bend, ((a.Y + b.Y) / 2) + (bend * 0.4f));
						using SKPath p = new SKPath();
						p.MoveTo(a);
						p.QuadTo(m, b);
						canvas.DrawPath(p, edge);
						canvas.DrawPath(p, path);
						bool railed = (Improvements(x, y) & 0x10) != 0 && (Improvements(x + dx, y + dy) & 0x10) != 0;

						if (railed)
							canvas.DrawPath(p, rail);

						// through woods and mountains the road is drawn again over the trees and peaks, as a cut through them
						int ta = TerrainAt(x, y), tb = TerrainAt(x + dx, y + dy);

						if (ta == Forest || ta == Jungle || ta == Mountains || tb == Forest || tb == Jungle || tb == Mountains)
							overRoads.Add((a, m, b, railed));
					}
				}
			}

			// everything that stands on the ground, back to front
			var items = new List<(float Base, float X, SKBitmap Bitmap)>(hamletsNearCities);
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

				if (b == null)
					return;

				float fx = margin + (Hash(Wrap(x), y, 200 + n) * (1 - (2 * margin))), fy = margin + (Hash(Wrap(x), y, 300 + n) * (1 - (2 * margin)));

				// a cell with an improvement, a resource or a hut keeps a clearing in its middle, so they are seen
				if (busy && (((fx - 0.5f) * (fx - 0.5f)) + ((fy - 0.5f) * (fy - 0.5f))) < 0.16f)
					return;

				SKPoint p = Screen(x + fx, y + fy);
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
						count = 2 + (int)(Hash(Wrap(x), y, 7) * 3);
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

				if ((feature & 1) != 0 && t < ResourceNames.Length && this.objects.TryGetValue("res_" + ResourceNames[t], out SKBitmap? res))
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

				using (SKPaint edge = Stroke(new SKColor(70, 56, 36), 6), path = Stroke(new SKColor(222, 204, 158), 3.8f), rail = Stroke(new SKColor(60, 52, 44), 1.4f))
				{
					foreach (var r in overRoads)
					{
						using SKPath p = new SKPath();
						p.MoveTo(r.A);
						p.QuadTo(r.M, r.B);
						canvas.DrawPath(p, edge);
						canvas.DrawPath(p, path);

						if (r.Rail)
							canvas.DrawPath(p, rail);
					}
				}

				// pollution: a wide irregular smear of sludge over the tile, over the props, with dead stalks
				using (SKPaint sludge = new SKPaint { IsAntialias = true, Color = new SKColor(56, 46, 34, 200) },
					sludgeEdge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f, Color = new SKColor(28, 22, 16) },
					oil = new SKPaint { IsAntialias = true, Color = new SKColor(34, 28, 22, 220) },
					dead = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f, Color = new SKColor(160, 138, 96) })
				{
					foreach ((int x, int y) in cells)
					{
						if ((Improvements(x, y) & 0x40) == 0 || !ExploredAt(x, y))
							continue;

						SKPoint c = Screen(x + 0.5f, y + 0.5f);
						using SKPath stain = new SKPath();

						for (int k = 0; k <= 16; k++)
						{
							float a = k / 16f * MathF.PI * 2, rr = 0.7f + (Hash(Wrap(x) + k, y, 800) * 0.4f);
							SKPoint q = new SKPoint(c.X + (MathF.Cos(a) * halfW * 0.82f * rr), c.Y + (MathF.Sin(a) * halfH * 0.82f * rr));

							if (k == 0)
								stain.MoveTo(q);
							else
								stain.LineTo(q);
						}

						stain.Close();
						canvas.DrawPath(stain, sludge);
						canvas.DrawPath(stain, sludgeEdge);

						for (int k = 0; k < 4; k++)
							canvas.DrawOval(new SKRect(c.X - 18 + (k * 10), c.Y - 4 + ((k & 1) * 5), c.X - 10 + (k * 10), c.Y + ((k & 1) * 5)), oil);

						for (int k = 0; k < 6; k++)
						{
							float sx = c.X - 22 + (k * 9), sy = c.Y - 3 + ((k % 3) * 4);
							canvas.DrawLine(sx, sy, sx + 3, sy - 9, dead);
							canvas.DrawLine(sx + 3, sy - 9, sx + 6, sy - 6, dead);
						}
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
