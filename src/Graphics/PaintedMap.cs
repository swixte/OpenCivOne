using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using SkiaSharp;

namespace OpenCivOne.Graphics
{
	/// <summary>
	/// Paints the world map as one picture instead of stamping a tile per cell: ground textures blend across terrain
	/// borders with soft, irregular edges, the sea deepens away from the coast and breaks into surf at it, rivers wind
	/// between cells, and trees, hills and mountains are scattered over their cells and may reach into neighbouring ones.
	/// The map is painted in chunks of 8x8 cells when first shown and painted again when the terrain under a chunk changes.
	/// Map cells then show their square of the painting at HD resolution (see MapManagement.DrawCell).
	///
	/// Art comes from the hd/painted folder: ground/NAME.png textures and SET/*.png sprites (see docs/hd-art.md).
	/// </summary>
	public class PaintedMap
	{
		public const int MapWidth = 80;
		public const int MapHeight = 50;

		// terrain ids as in TerrainTypeEnum
		private const int Desert = 0, Plains = 1, Grassland = 2, Forest = 3, Hills = 4, Mountains = 5;
		private const int Tundra = 6, Arctic = 7, Swamp = 8, Jungle = 9, Water = 10, River = 11;

		private const int ChunkCells = 8;
		private const int TextureCells = 8;

		// ground textures of each terrain, with their weights
		private static readonly (string Name, float Weight)[][] Ground = new (string, float)[][]
		{
			new[] { ("desert", 1f) },
			new[] { ("plains", 1f) },
			new[] { ("grass", 1f) },
			new[] { ("floor", 1f) },
			new[] { ("grass", 0.55f), ("plains", 0.45f) },
			new[] { ("tundra", 0.7f), ("grass", 0.3f) },
			new[] { ("tundra", 1f) },
			new[] { ("snow", 1f) },
			new[] { ("swamp", 1f) },
			new[] { ("floor", 0.6f), ("swamp", 0.4f) },
			new[] { ("grass", 1f) },
			new[] { ("grass", 1f) },
		};

		// a texture with a terrain's own name, if the art has one, paints that terrain on its own instead of the mix above
		private static readonly string[] OwnGround = { "desert", "plains", "grass", "forest", "hills", "mountains", "tundra", "snow", "swamp", "jungle", "", "" };

		// the ground textures of each terrain, for this art
		private (string Name, float Weight)[][] groundParts = Ground;

		/// <summary>
		/// How the art wants to be painted, from an optional style.txt next to its folders (lines of "name value"):
		/// clouds, the strength of the drifting cloud shadows (1); grade, the strength of the warm colour grade (1);
		/// shallows, the width of the shallow water along coasts (1); coastline, 1 for a printed coast with a dark key line
		/// and a cream beach instead of a dark rocky rim (0); print, 1 to paint the map like a woodblock print (0, see
		/// PaintPrint); pattern and seapattern, how much larger the land and sea motifs are drawn in print mode (1), and
		/// "pattern TEXTURE size" for one terrain's own texture (forest, hills, ...); paper,
		/// how much the
		/// colours lean towards the cream of the paper in print mode (0).
		/// </summary>
		private float clouds = 1, grade = 1, shallows = 1, pattern = 1, seaPattern = 1, paper = 0;

		// per terrain motif sizes in print mode ("pattern forest 1.2"), the same as pattern unless set
		private readonly float[] terrainPattern = new float[Ground.Length];
		private bool printedCoast = false, print = false;

		// for the blended (not print) map: scenery laid out as on a print (layout), a sandy beach instead of a rocky rim
		// (beach), rivers in the sea's colours with soft banks (softrivers), and trees and jungle drawn at these scales
		// (trees, jungle; jungle follows trees unless given)
		private bool layout = false, beach = false, softRivers = false;
		private float treeScale = 1, jungleScale = -1;

		// how far coasts and terrain borders wander from the grid (coastwarp, 1 as before; more makes rounder, craggier coasts)
		private float coastWarp = 1;

		// surf, how strong the broken white line off the coast is (1); beachtone, 1 for a beach of sand lightened to
		// cream, 0 for one in the desert's own sand
		private float surf = 1, beachTone = 1;

		// crag, how rough the coast's fine edge is (1); lower for smooth sandy curves
		private float cragginess = 1;

		// relief, how strongly the land is lit as a landscape with height (0, flat); variety, how much the land's colour
		// drifts from region to region, lusher in one place and drier in another (0, none)
		private float relief = 0, variety = 0;

		// topdown, 1 to see everything from straight above: woods as tree crowns seen from overhead, hills and mountains
		// as relief in the ground instead of standing pictures; mix, how far terrains reach into each other at their
		// borders, in painterly patches (0, a plain fade)
		private bool topDown = false;
		private float mix = 0;

		// sea, how strongly the open sea is painted with depth (deepening away from land), swells along the coasts and
		// glints of light (0, plain)
		private float sea = 0;

		// the height each terrain stands at, and how rough its ground is
		private static readonly float[] TerrainHeight = { 0.14f, 0.10f, 0.08f, 0.22f, 0.52f, 1.0f, 0.16f, 0.3f, 0.02f, 0.18f, -0.25f, 0.02f };
		private static readonly float[] TerrainRoughness = { 0.05f, 0.035f, 0.035f, 0.06f, 0.22f, 0.55f, 0.06f, 0.08f, 0.015f, 0.06f, 0f, 0.01f };
		// how wide land terrains blend into each other (groundblend, 1 as before; more hides cell corners)
		private float groundBlend = 1;

		private readonly Func<int, int, int> terrainAt;
		private readonly int cell;
		private readonly int worldWidth;
		private readonly int worldHeight;
		private readonly Dictionary<string, Texture> textures = new Dictionary<string, Texture>();
		private readonly Dictionary<string, List<Sprite>> sprites = new Dictionary<string, List<Sprite>>();
		// a print's resources (res_NAME) and huts (hut), painted into the map with its trees and peaks
		private readonly Dictionary<string, Sprite> features = new Dictionary<string, Sprite>();
		// a print's river: a straight painted strip (river.png), laid along each river's course
		private Sprite? riverStrip = null;

		// the resource of each terrain, by terrain id, as the objects are named
		private static readonly string[] ResourceNames = { "oasis", "horses", "wheat", "pheasant", "coal", "gold", "caribou", "seal", "peat", "gems", "fish" };

		public const int SpecialResource = 1, MinorTribeHut = 2, City = 4, RoadCorridor = 8;

		/// <summary>
		/// A mine or a fortress on the cell: the scenery leaves room for it
		/// </summary>
		public const int MineSite = 256, FortressSite = 512;

		// a city's size step (0 for a hamlet up to 5 for a great capital) in the feature bits above City: its clearing
		// grows with it
		public const int CityStepShift = 4, CityStepMask = 7 << CityStepShift;
		private readonly Dictionary<(int X, int Y), Chunk> chunks = new Dictionary<(int X, int Y), Chunk>();

		// The world is also painted on a background thread, nearest chunks first, so the map rarely waits for painting.
		// The game waits for a chunk the background thread is painting rather than paint it twice.
		private readonly object chunkLock = new object();
		private Thread? painter = null;
		private bool painterRestart = false;
		private (int X, int Y) painterCentre = (0, 0);
		private (int X, int Y)? painterChunk = null;

		private class Texture
		{
			public int Size;
			public float[] R = Array.Empty<float>(), G = Array.Empty<float>(), B = Array.Empty<float>();
		}

		private class Sprite
		{
			public int Width, Height;
			// premultiplied colour, alpha in [0, 1]
			public float[] R = Array.Empty<float>(), G = Array.Empty<float>(), B = Array.Empty<float>(), A = Array.Empty<float>();
			// softened alpha, for the cast shadow
			public float[] Soft = Array.Empty<float>();
		}

		// The terrain around a chunk, read from the map once before it is painted
		private class TerrainGrid
		{
			public const int Margin = 6;
			private readonly int left, top, width;
			private readonly int[] values;

			public TerrainGrid(PaintedMap map, int cx, int cy)
			{
				this.left = (cx * ChunkCells) - Margin;
				this.top = (cy * ChunkCells) - Margin;
				this.width = ChunkCells + (2 * Margin);
				this.values = new int[this.width * this.width];

				for (int j = 0; j < this.width; j++)
					for (int i = 0; i < this.width; i++)
						this.values[(j * this.width) + i] = map.Terrain(this.left + i, this.top + j);
			}

			public int At(int x, int y)
			{
				return this.values[((y - this.top) * this.width) + (x - this.left)];
			}
		}

		private class Chunk
		{
			public long Signature;
			public uint[] Pixels = Array.Empty<uint>();
		}

		private struct Placement
		{
			public Sprite Sprite;
			public float X, Base, Width, Height;
			public bool Flip;
			// hills and mountains in a print: the foot fades into the ground instead of ending in an edge
			public bool FadeFoot;
			// a resource of the sea, drawn over the water
			public bool OnWater;
			// how opaque it is drawn (0 means fully: the default)
			public float Fade;
			// subtle per-placement colour variation for mountain ranges (0 = unchanged)
			public float Warmth;
			public float Shade;
			// a tree crown seen from above (topdown): centred on (X, Base), Width its radius, Height its terrain
			public bool Crown;
		}

		/// <param name="terrainAt">Terrain id (TerrainTypeEnum) of a map cell, x already wrapped and y inside the map</param>
		public PaintedMap(int cellSize, Func<int, int, int> terrainAt)
		{
			this.cell = cellSize;
			this.terrainAt = terrainAt;
			this.worldWidth = MapWidth * cellSize;
			this.worldHeight = MapHeight * cellSize;
		}

		public int CellSize => this.cell;

		// a print's minimap colours by terrain id: the palette of the print's map, not the original EGA colours
		private static readonly uint[] PrintMiniMap = { 0xEAD9A6, 0xD9B45A, 0x8DB255, 0x2E5A43, 0xA08A4E, 0x6E7C8C, 0xB4A3B8, 0xF2F2EE, 0x6B6B45, 0x2F8A4E, 0x1F3A5F, 0x3F7FBF };

		/// <summary>
		/// The colour the minimap shows for a terrain in a print (RRGGBB), or 0 to keep the game's own colour
		/// </summary>
		public uint MiniMapColor(int terrain) => (this.print && terrain >= 0 && terrain < PrintMiniMap.Length) ? PrintMiniMap[terrain] : 0;

		/// <summary>
		/// What a map cell holds that a print paints into the ground: SpecialResource and MinorTribeHut flags; x already
		/// wrapped and y inside the map
		/// </summary>
		public Func<int, int, int>? FeatureAt { get; set; }

		/// <summary>
		/// Resources and huts are painted into the map, so the game should not draw them over it
		/// </summary>
		public bool PaintsFeatures => this.print && this.FeatureAt != null && this.features.Count > 0;

		/// <summary>
		/// The art is painted like a woodblock print (print 1 in its style.txt); the city size then stands at the bottom left
		/// </summary>
		public bool Print => this.print;

		/// <summary>
		/// Loads the art from an hd/painted folder, or returns null if there is none
		/// </summary>
		public static PaintedMap? Load(string folder, int cellSize, Func<int, int, int> terrainAt)
		{
			if (!Directory.Exists(Path.Combine(folder, "ground")))
				return null;

			PaintedMap map = new PaintedMap(cellSize, terrainAt);
			int textureSize = cellSize * TextureCells;

			foreach (string path in Directory.GetFiles(Path.Combine(folder, "ground"), "*.png"))
			{
				using SKBitmap? image = SKBitmap.Decode(path);

				if (image != null)
				{
					map.textures[Path.GetFileNameWithoutExtension(path).ToLowerInvariant()] = LoadTexture(image, textureSize);
				}
			}

			foreach (string set in new[] { "trees", "jungle", "hills", "mountains", "mountains_gold", "hills_coal", "jungle_gems", "peaks" })
			{
				List<Sprite> list = new List<Sprite>();
				string setFolder = Path.Combine(folder, set);

				if (Directory.Exists(setFolder))
				{
					foreach (string path in Directory.GetFiles(setFolder, "*.png"))
					{
						using SKBitmap? image = SKBitmap.Decode(path);

						if (image != null)
						{
							list.Add(LoadSprite(image));
						}
					}
				}

				map.sprites[set] = list;
			}

			string riverPath = Path.Combine(folder, "river.png");

			if (File.Exists(riverPath))
			{
				using SKBitmap? image = SKBitmap.Decode(riverPath);

				if (image != null)
					map.riverStrip = LoadSprite(image);
			}

			string objects = Path.Combine(folder, "objects");

			if (Directory.Exists(objects))
			{
				foreach (string name in ResourceNames.Select(n => "res_" + n).Append("hut"))
				{
					string path = Path.Combine(objects, name + ".png");
					using SKBitmap? image = File.Exists(path) ? SKBitmap.Decode(path) : null;

					if (image != null)
						map.features[name] = LoadSprite(image);
				}
			}

			string stylePath = Path.Combine(folder, "style.txt");

			if (File.Exists(stylePath))
			{
				foreach (string line in File.ReadAllLines(stylePath))
				{
					string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

					if (parts.Length == 3 && parts[0].ToLowerInvariant() == "pattern" &&
						float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float own))
					{
						int t = Array.IndexOf(OwnGround, parts[1].ToLowerInvariant());

						if (t >= 0)
							map.terrainPattern[t] = Math.Clamp(own, 0.25f, 8);

						continue;
					}

					if (parts.Length < 2 || !float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value))
						continue;

					switch (parts[0].ToLowerInvariant())
					{
						case "clouds": map.clouds = value; break;
						case "grade": map.grade = value; break;
						case "shallows": map.shallows = Math.Max(0.1f, value); break;
						case "coastline": map.printedCoast = value != 0; break;
						case "print": map.print = value != 0; break;
						case "layout": map.layout = value != 0; break;
						case "beach": map.beach = value != 0; break;
						case "softrivers": map.softRivers = value != 0; break;
						case "trees": map.treeScale = Math.Clamp(value, 0.15f, 2); break;
						case "jungle": map.jungleScale = Math.Clamp(value, 0.15f, 2); break;
						case "coastwarp": map.coastWarp = Math.Clamp(value, 0.5f, 2); break;
						case "groundblend": map.groundBlend = Math.Clamp(value, 0.5f, 4.5f); break;
						case "pattern": map.pattern = Math.Clamp(value, 0.25f, 8); break;
						case "seapattern": map.seaPattern = Math.Clamp(value, 0.25f, 8); break;
						case "paper": map.paper = Math.Clamp(value, 0, 1); break;
						case "surf": map.surf = Math.Clamp(value, 0, 1.5f); break;
						case "relief": map.relief = Math.Clamp(value, 0, 3); break;
						case "topdown": map.topDown = value != 0; break;
						case "mix": map.mix = Math.Clamp(value, 0, 2); break;
						case "sea": map.sea = Math.Clamp(value, 0, 2); break;
						case "variety": map.variety = Math.Clamp(value, 0, 3); break;
						case "beachtone": map.beachTone = Math.Clamp(value, 0, 1); break;
						case "crag": map.cragginess = Math.Clamp(value, 0, 1.5f); break;
					}
				}
			}

			// a texture has to repeat a whole number of times around the world, or its pattern breaks where the world wraps
			map.pattern = WholeRepeats(map.pattern);
			map.seaPattern = WholeRepeats(map.seaPattern);

			for (int t = 0; t < map.terrainPattern.Length; t++)
			{
				if (map.terrainPattern[t] > 0)
					map.terrainPattern[t] = WholeRepeats(map.terrainPattern[t]);
			}

			map.groundParts = new (string Name, float Weight)[Ground.Length][];

			for (int t = 0; t < Ground.Length; t++)
				map.groundParts[t] = (OwnGround[t] != "" && map.textures.ContainsKey(OwnGround[t])) ? new[] { (OwnGround[t], 1f) } : Ground[t];

			// every texture is in place before painting starts, so painting threads only read them
			foreach ((string Name, float Weight)[] parts in map.groundParts)
				foreach ((string name, float _) in parts)
					map.TextureOr(name);

			map.TextureOr("ocean");
			map.TextureOr("shallow");
			map.TextureOr("desert");
			map.TextureOr("plains");

			return map;
		}

		// the nearest texture scale at which a texture (TextureCells wide) repeats a whole number of times across the map
		private static float WholeRepeats(float scale)
		{
			float across = MapWidth / (float)TextureCells;

			return across / Math.Max(1, MathF.Round(across / scale));
		}

		private static Texture LoadTexture(SKBitmap image, int size)
		{
			using SKBitmap resized = image.Resize(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul), new SKSamplingOptions(SKCubicResampler.Mitchell));
			Texture texture = new Texture { Size = size, R = new float[size * size], G = new float[size * size], B = new float[size * size] };

			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					SKColor c = resized.GetPixel(x, y);
					int i = (y * size) + x;
					texture.R[i] = c.Red;
					texture.G[i] = c.Green;
					texture.B[i] = c.Blue;
				}
			}

			return texture;
		}

		private static Sprite LoadSprite(SKBitmap image)
		{
			int w = image.Width, h = image.Height;
			Sprite sprite = new Sprite { Width = w, Height = h, R = new float[w * h], G = new float[w * h], B = new float[w * h], A = new float[w * h] };

			for (int y = 0; y < h; y++)
			{
				for (int x = 0; x < w; x++)
				{
					SKColor c = image.GetPixel(x, y);
					int i = (y * w) + x;
					float a = c.Alpha / 255f;
					sprite.A[i] = a;
					sprite.R[i] = c.Red * a;
					sprite.G[i] = c.Green * a;
					sprite.B[i] = c.Blue * a;
				}
			}

			sprite.Soft = BoxBlur(sprite.A, w, h, Math.Max(1, Math.Min(w, h) / 24));
			sprite.Soft = BoxBlur(sprite.Soft, w, h, Math.Max(1, Math.Min(w, h) / 24));

			return sprite;
		}

		private static float[] BoxBlur(float[] source, int w, int h, int radius)
		{
			float[] horizontal = new float[source.Length];
			float[] result = new float[source.Length];

			for (int y = 0; y < h; y++)
			{
				for (int x = 0; x < w; x++)
				{
					float sum = 0;

					for (int k = -radius; k <= radius; k++)
						sum += source[(y * w) + Math.Clamp(x + k, 0, w - 1)];

					horizontal[(y * w) + x] = sum / ((2 * radius) + 1);
				}
			}

			for (int y = 0; y < h; y++)
			{
				for (int x = 0; x < w; x++)
				{
					float sum = 0;

					for (int k = -radius; k <= radius; k++)
						sum += horizontal[(Math.Clamp(y + k, 0, h - 1) * w) + x];

					result[(y * w) + x] = sum / ((2 * radius) + 1);
				}
			}

			return result;
		}

		/// <summary>
		/// Copies the painting of map cell (x, y) into an ARGB buffer
		/// </summary>
		public void CopyCell(int x, int y, uint[] destination, int destinationOffset, int destinationStride)
		{
			x = WrapX(x);
			y = Math.Clamp(y, 0, MapHeight - 1);

			Chunk chunk = this.GetChunk(x / ChunkCells, y / ChunkCells);
			int chunkSize = ChunkCells * this.cell;
			int source = (((y % ChunkCells) * this.cell) * chunkSize) + ((x % ChunkCells) * this.cell);

			for (int i = 0; i < this.cell; i++)
			{
				Array.Copy(chunk.Pixels, source + (i * chunkSize), destination, destinationOffset + (i * destinationStride), this.cell);
			}
		}

		/// <summary>
		/// Paints an unexplored map cell of a print: dark paper with soft bands of stylised mist (kasumi), painted from
		/// the world position so neighbouring unexplored cells join without a grid
		/// </summary>
		public void CopyFogCell(int x, int y, uint[] destination, int destinationOffset, int destinationStride)
		{
			x = WrapX(x);
			float k = this.cell / 64f;

			for (int py = 0; py < this.cell; py++)
			{
				int wy = (y * this.cell) + py;

				for (int px = 0; px < this.cell; px++)
				{
					int wx = (x * this.cell) + px;
					// kasumi: long flat bands of cloud with rounded ends, in rows, each row's clouds placed by a hash so the
					// pattern repeats around the world without a break
					float cloud = this.FogCloud(wx, wy, k);
					float fill = Smooth(0.6f * k, -0.6f * k, cloud);
					float rim = Smooth(1.4f * k, 0.3f * k, MathF.Abs(cloud + (1.2f * k)));
					float grain = (this.Noise(wx, wy, 3 * k, 53) - 0.5f) * 6;
					float r = 27 + (17 * fill) + (14 * rim) + grain, g = 28 + (17 * fill) + (14 * rim) + grain, b = 36 + (21 * fill) + (16 * rim) + grain;
					destination[destinationOffset + (py * destinationStride) + px] = 0xff000000 | ((uint)Math.Clamp(r, 0, 255) << 16) | ((uint)Math.Clamp(g, 0, 255) << 8) | (uint)Math.Clamp(b, 0, 255);
				}
			}
		}

		// signed distance (negative inside) to the nearest band of mist at a world pixel
		private float FogCloud(int wx, int wy, float k)
		{
			float row = 52 * k, half = 8.5f * k, span = 256 * k;
			int segments = Math.Max(1, (int)MathF.Round(this.worldWidth / span));
			span = this.worldWidth / (float)segments;
			float best = float.MaxValue;
			int n0 = (int)MathF.Floor(wy / row);

			for (int n = n0 - 1; n <= n0 + 1; n++)
			{
				float centreY = (n * row) + (row * 0.4f);
				float shift = Hash(n, 7, 61) * span;

				for (int d = -1; d <= 1; d++)
				{
					int seg = ((int)MathF.Floor(((wx - shift) % this.worldWidth + this.worldWidth) % this.worldWidth / span) + d + segments) % segments;
					float h1 = Hash(seg, n, 62), h2 = Hash(seg, n, 63);

					if (h2 < 0.25f)
						continue;

					float length = span * (0.35f + (0.45f * h1)), centreX = shift + (seg * span) + (span * 0.5f);
					// the nearest copy of the cloud around the world
					float dx = wx - centreX;
					dx -= MathF.Round(dx / this.worldWidth) * this.worldWidth;
					dx = Math.Max(0, MathF.Abs(dx) - (length / 2));
					// a scalloped top edge
					float top = (wy < centreY) ? MathF.Abs(MathF.Sin((wx + (n * 31)) / (11 * k))) * 3 * k : 0;
					float dy = wy - centreY;
					best = Math.Min(best, MathF.Sqrt((dx * dx) + (dy * dy)) - half - top);
				}
			}

			return best;
		}

		/// <summary>
		/// Forgets all painted chunks, for example when a different map is loaded
		/// </summary>
		public void Clear()
		{
			lock (this.chunkLock)
			{
				this.chunks.Clear();
			}
		}

		private Chunk GetChunk(int cx, int cy)
		{
			long signature = this.Signature(cx, cy);
			Chunk? chunk;

			lock (this.chunkLock)
			{
				// the background thread is painting this one: wait for it rather than paint it twice
				while (this.painterChunk == (cx, cy))
					Monitor.Wait(this.chunkLock);

				this.chunks.TryGetValue((cx, cy), out chunk);
			}

			if (chunk == null || chunk.Signature != signature)
			{
				chunk = new Chunk { Signature = signature, Pixels = this.Paint(cx, cy, true) };

				lock (this.chunkLock)
				{
					this.chunks[(cx, cy)] = chunk;
				}

				this.PaintInBackground(cx, cy);
			}

			return chunk;
		}

		/// <summary>
		/// Paints the chunks that are missing or out of date on a background thread, the nearest to (cx, cy) first
		/// </summary>
		private void PaintInBackground(int cx, int cy)
		{
			lock (this.chunkLock)
			{
				this.painterCentre = (cx, cy);

				if (this.painter != null)
				{
					this.painterRestart = true;
				}
				else
				{
					this.painter = new Thread(this.BackgroundPainter) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Painted map" };
					this.painter.Start();
				}
			}
		}

		private void BackgroundPainter()
		{
			try
			{
				while (true)
				{
					(int X, int Y) centre;

					lock (this.chunkLock)
					{
						centre = this.painterCentre;
						this.painterRestart = false;
					}

					int columns = MapWidth / ChunkCells, rows = ((MapHeight - 1) / ChunkCells) + 1;
					List<(int X, int Y)> order = new List<(int X, int Y)>();

					for (int y = 0; y < rows; y++)
						for (int x = 0; x < columns; x++)
							order.Add((x, y));

					// the world wraps around horizontally
					int Distance((int X, int Y) c)
					{
						int dx = Math.Abs(c.X - centre.X);
						return Math.Max(Math.Min(dx, columns - dx), Math.Abs(c.Y - centre.Y));
					}

					order.Sort((a, b) => Distance(a).CompareTo(Distance(b)));
					bool restart = false;

					foreach ((int x, int y) in order)
					{
						lock (this.chunkLock)
						{
							restart = this.painterRestart;
						}

						if (restart)
							break;

						long signature = this.Signature(x, y);
						Chunk? chunk;

						lock (this.chunkLock)
						{
							this.chunks.TryGetValue((x, y), out chunk);

							if (chunk != null && chunk.Signature == signature)
								continue;

							this.painterChunk = (x, y);
						}

						try
						{
							// one core at low priority, so the game itself keeps running smoothly
							Chunk painted = new Chunk { Signature = signature, Pixels = this.Paint(x, y, false) };

							lock (this.chunkLock)
							{
								if (!this.chunks.TryGetValue((x, y), out chunk) || chunk.Signature != signature)
									this.chunks[(x, y)] = painted;
							}
						}
						finally
						{
							lock (this.chunkLock)
							{
								this.painterChunk = null;
								Monitor.PulseAll(this.chunkLock);
							}
						}
					}

					lock (this.chunkLock)
					{
						if (!this.painterRestart)
						{
							this.painter = null;
							return;
						}
					}
				}
			}
			catch (Exception)
			{
				// the map could not be read just now (for example while a game loads), chunks are then painted when shown
				lock (this.chunkLock)
				{
					this.painter = null;
				}
			}
		}

		// The terrain a chunk's painting depends on: its own cells and the ones whose art can reach into it
		private long Signature(int cx, int cy)
		{
			long hash = 17;

			// every cell the chunk's painting depends on: its scenery reaches three cells sideways, and in a print the
			// shapes of terrain and forest crowns depend on the cells around those
			for (int y = (cy * ChunkCells) - 3; y < ((cy + 1) * ChunkCells) + 5; y++)
			{
				for (int x = (cx * ChunkCells) - 5; x < ((cx + 1) * ChunkCells) + 5; x++)
				{
					hash = (hash * 31) + this.Terrain(x, y);

					// a city's clearing, special minerals, and road corridors are painted whatever the map's style
					hash = (hash * 7) + (this.PaintsFeatures ? this.Feature(x, y) : (this.Feature(x, y) & (City | CityStepMask | SpecialResource | RoadCorridor | MineSite | FortressSite)));
				}
			}

			return hash;
		}

		private int WrapX(int x)
		{
			return ((x % MapWidth) + MapWidth) % MapWidth;
		}

		/// <summary>
		/// Picks an 8-way diagonal sea corner (dx, dy in {-1, +1}) when a city has no cardinal water neighbor, or (0, 0) otherwise.
		/// </summary>
		private (int Dx, int Dy) DiagonalHarborCorner(int cellX, int cellY)
		{
			bool IsSea(int cx, int cy) => cy >= 0 && cy < MapHeight && this.Terrain(cx, cy) == Water;

			if (IsSea(cellX + 1, cellY) || IsSea(cellX - 1, cellY) || IsSea(cellX, cellY + 1) || IsSea(cellX, cellY - 1))
				return (0, 0);

			(int Dx, int Dy)[] diags = { (1, 1), (-1, 1), (1, -1), (-1, -1) };
			(int Dx, int Dy) best = (0, 0);
			int bestScore = 0;

			foreach (var (dx, dy) in diags)
			{
				if (!IsSea(cellX + dx, cellY + dy))
					continue;

				int score = 1 +
					(IsSea(cellX + (2 * dx), cellY + (2 * dy)) ? 1 : 0) +
					(IsSea(cellX + (2 * dx), cellY + dy) ? 1 : 0) +
					(IsSea(cellX + dx, cellY + (2 * dy)) ? 1 : 0);

				if (score > bestScore)
				{
					bestScore = score;
					best = (dx, dy);
				}
			}

			return best;
		}

		/// <summary>
		/// Contributes a city's warp-calming zone, rounded shoreline foundation pad, and optional diagonal harbor cove
		/// so Paint() and LandFraction() agree identically on the painted shore edge.
		/// </summary>
		private void EvaluateCityShore(float wx, float wy, float k, int ci, int cj, int step, int diagDx, int diagDy,
			ref float warpCalm, ref float padLand, ref float diagCove)
		{
			float ccx = (ci + 0.5f) * this.cell, ccy = (cj + 0.5f) * this.cell;
			float townHalf = this.cell * (0.35f + (0.044f * step));
			float p = step >= 3 ? 3.6f : 2.2f;
			float ex = MathF.Abs(wx - ccx) / townHalf, ey = MathF.Abs(wy - ccy) / townHalf;
			float wob = ((this.Noise(wx, wy, 12 * k, 17) - 0.5f) * 0.14f) + ((this.Noise(wx, wy, 5 * k, 18) - 0.5f) * 0.06f);
			float dist = MathF.Pow(MathF.Pow(ex, p) + MathF.Pow(ey, p), 1f / p) + wob;
			padLand = Math.Max(padLand, Smooth(1.18f, 0.72f, dist));

			float radial = MathF.Sqrt((ex * ex) + (ey * ey));
			warpCalm = Math.Max(warpCalm, Smooth(1.80f, 0.85f, radial));

			if (diagDx != 0 && diagDy != 0)
			{
				float p0x = ccx + (diagDx * 0.42f * this.cell), p0y = ccy + (diagDy * 0.42f * this.cell);
				float p1x = ccx + (diagDx * 1.12f * this.cell), p1y = ccy + (diagDy * 1.12f * this.cell);
				float vx = p1x - p0x, vy = p1y - p0y;
				float vv = (vx * vx) + (vy * vy);
				float t = vv > 0 ? Math.Clamp((((wx - p0x) * vx) + ((wy - p0y) * vy)) / vv, 0f, 1f) : 0f;
				float dx = wx - (p0x + (t * vx)), dy = wy - (p0y + (t * vy));
				float segD = MathF.Sqrt((dx * dx) + (dy * dy));
				float coveR = (10.5f + (0.7f * step)) * k;
				float coveWob = (this.Noise(wx, wy, 7 * k, 19) - 0.5f) * 3.5f * k;
				diagCove = Math.Max(diagCove, Smooth(coveR * 1.45f, coveR * 0.55f, segD + coveWob));
			}
		}

		private bool HasSpriteSet(string name) => this.sprites.TryGetValue(name, out List<Sprite>? s) && s.Count > 0;

		/// <summary>
		/// Exact warped land weight at a continuous world pixel position, matching Paint's coast edge contour
		/// </summary>
		private float EvaluateWarpAndLand(float wx, float wy, out float rqx, out float rqy, out int ci, out int cj)
		{
			float k = this.cell / 64f;
			float warpAmount = 14 * k * this.coastWarp, coastRadius = 5 * k * this.coastWarp;
			float crag = this.coastWarp > 1 ? 0.3f * warpAmount * this.cragginess : 0;
			float ox = ((this.Noise(wx, wy, 24 * k, 1) - 0.5f) * 2 * warpAmount) + ((this.Noise(wx, wy, 8 * k, 2) - 0.5f) * 0.6f * warpAmount);
			float oy = ((this.Noise(wx, wy, 24 * k, 3) - 0.5f) * 2 * warpAmount) + ((this.Noise(wx, wy, 8 * k, 4) - 0.5f) * 0.6f * warpAmount);
			float cragX = 0, cragY = 0;

			if (crag > 0)
			{
				cragX = (this.Noise(wx, wy, 3 * k, 11) - 0.5f) * crag;
				cragY = (this.Noise(wx, wy, 3 * k, 12) - 0.5f) * crag;
				ox += cragX;
				oy += cragY;
			}

			float warpCalm = 0, padLand = 0, diagCove = 0;
			if (this.FeatureAt != null && this.layout)
			{
				int ui = (int)MathF.Floor(wx / this.cell), uj = (int)MathF.Floor(wy / this.cell);
				for (int dj = -2; dj <= 2; dj++)
				{
					for (int di = -2; di <= 2; di++)
					{
						int feat = this.Feature(ui + di, uj + dj);
						if ((feat & City) == 0)
							continue;

						int step = Math.Min((feat & CityStepMask) >> CityStepShift, 5);
						(int diagDx, int diagDy) = this.DiagonalHarborCorner(ui + di, uj + dj);
						this.EvaluateCityShore(wx, wy, k, ui + di, uj + dj, step, diagDx, diagDy, ref warpCalm, ref padLand, ref diagCove);
					}
				}

				if (warpCalm > 0)
				{
					float calmMul = 1f - (0.78f * warpCalm);
					ox *= calmMul;
					oy *= calmMul;
					cragX *= calmMul;
					cragY *= calmMul;
				}
			}

			float qx = wx + ox, qy = wy + oy;
			rqx = qx - cragX;
			rqy = qy - cragY;
			ci = (int)MathF.Floor(qx / this.cell);
			cj = (int)MathF.Floor(qy / this.cell);
			float land = 0;

			for (int dj = -1; dj <= 1; dj++)
			{
				float wyC = this.Weight(qy, cj + dj, coastRadius);

				if (wyC <= 0)
					continue;

				for (int di = -1; di <= 1; di++)
				{
					if (this.Terrain(ci + di, cj + dj) != Water)
						land += wyC * this.Weight(qx, ci + di, coastRadius);
				}
			}

			if (padLand > 0)
				land = Math.Max(land, padLand);

			if (diagCove > 0 && padLand < 0.72f)
			{
				float cut = diagCove * Smooth(0.72f, 0.25f, padLand);
				land = Math.Min(land, 1f - cut);
			}

			return land;
		}

		private float LandFraction(float wx, float wy) => this.EvaluateWarpAndLand(wx, wy, out _, out _, out _, out _);

		/// <summary>
		/// Exact painted water classification at fractional map coordinates (mapX, mapY): 0 = dry land, 1 = sea/wet shore, 2 = river/bank
		/// </summary>
		public int WaterKindAt(float mapX, float mapY)
		{
			if (mapY < 0 || mapY >= MapHeight)
				return 1;

			float wx = mapX * this.cell, wy = mapY * this.cell;
			float land = this.EvaluateWarpAndLand(wx, wy, out float rqx, out float rqy, out int ci, out int cj);

			if (land < 0.56f)
				return 1;

			if (!this.print)
			{
				float k = this.cell / 64f, bankWidth = 6.8f * k, wobble = 1f;

				if (this.softRivers)
				{
					rqx += (this.Noise(wx, wy, 48 * k, 8) - 0.5f) * 24 * k;
					rqy += (this.Noise(wx, wy, 48 * k, 9) - 0.5f) * 24 * k;
					wobble = 1.1f + (0.45f * this.Noise(wx, wy, 30 * k, 10));
				}

				float d = this.RiverDistance(this.Terrain, rqx, rqy, ci, cj) / wobble;

				if (d < bankWidth * 1.05f)
					return 2;
			}

			return 0;
		}

		/// <summary>
		/// Distance in cell-width units from a coastal cell's centre along cardinal or diagonal direction (dx, dy) to the actual painted shoreline
		/// </summary>
		public float ShoreDistance(int cellX, int cellY, int dx, int dy)
		{
			if (this.print || (dx == 0 && dy == 0))
				return 0.48f;

			float k = this.cell / 64f;
			float len = MathF.Sqrt((dx * dx) + (dy * dy));
			float ux = dx / len, uy = dy / len;
			bool diagonal = len > 1.1f;
			float wx0 = (cellX + 0.5f) * this.cell, wy0 = (cellY + 0.5f) * this.cell;
			float perpX = -uy, perpY = ux;
			float[] offsets = { -5 * k, 0f, 5 * k };
			float[] hits = new float[3];
			float step = Math.Max(1f, 1 * k), start = 10 * k, end = (diagonal ? 78f : 58f) * k;

			for (int r = 0; r < 3; r++)
			{
				float bx = wx0 + (perpX * offsets[r]), by = wy0 + (perpY * offsets[r]);
				float hit = end;

				for (float d = start; d <= end; d += step)
				{
					if (this.LandFraction(bx + (ux * d), by + (uy * d)) < 0.5f)
					{
						hit = d;
						break;
					}
				}

				hits[r] = hit;
			}

			Array.Sort(hits);
			return Math.Clamp(hits[1] / this.cell, 0.34f, diagonal ? 0.86f : 0.62f);
		}

		private int Feature(int x, int y)
		{
			return (this.FeatureAt == null || y < 0 || y >= MapHeight) ? 0 : this.FeatureAt(WrapX(x), y);
		}

		private int Terrain(int x, int y)
		{
			int terrain = this.terrainAt(WrapX(x), Math.Clamp(y, 0, MapHeight - 1));

			return (terrain < 0 || terrain > River) ? Grassland : terrain;
		}

		#region Noise and weights

		private static float Hash(int x, int y, int seed)
		{
			unchecked
			{
				uint h = ((uint)x * 374761393u) + ((uint)y * 668265263u) + ((uint)seed * 1013904223u);
				h = (h ^ (h >> 15)) * 2246822519u;
				h = (h ^ (h >> 13)) * 3266489917u;
				h ^= h >> 16;

				return h / 4294967296f;
			}
		}

		/// <summary>
		/// Evaluates a city's tight paved apron (innerApron) and organic cultivated garden fringe (outerVerge)
		/// </summary>
		private void CityClearingDetail(float x, float y, float k, out float innerApron, out float outerVerge, out int maxStep)
		{
			int ui = (int)MathF.Floor(x / this.cell), uj = (int)MathF.Floor(y / this.cell);
			innerApron = 0;
			outerVerge = 0;
			maxStep = 0;

			for (int dj = -1; dj <= 1; dj++)
			{
				for (int di = -1; di <= 1; di++)
				{
					int feature = this.Feature(ui + di, uj + dj);

					if ((feature & City) == 0)
						continue;

					int step = Math.Min((feature & CityStepMask) >> CityStepShift, 5);
					float ccx = (ui + di + 0.5f) * this.cell, ccy = (uj + dj + 0.5f) * this.cell;
					float wob = (0.22f * (this.Noise(x, y, 14 * k, 13) - 0.5f)) + (0.10f * (this.Noise(x, y, 4 * k, 14) - 0.5f));

					float halfIn = this.cell * 0.5f * (0.60f + (0.045f * step));
					float exIn = MathF.Abs(x - ccx) / halfIn, eyIn = MathF.Abs(y - ccy) / halfIn;
					float reachIn = MathF.Pow(MathF.Pow(exIn, 3) + MathF.Pow(eyIn, 3), 1f / 3f) + wob;
					float ciVal = Smooth(1.10f, 0.72f, reachIn);

					// Larger cities cultivate an irregular garden & orchard fringe that blends softly into surrounding grass
					// seen from above, a town and its fields fill its own tile and no more, so which tile it stands on is clear
					float halfOut = this.topDown ? this.cell * 0.5f * 0.96f : this.cell * 0.5f * (0.82f + (0.10f * step));
					float exOut = MathF.Abs(x - ccx) / halfOut, eyOut = MathF.Abs(y - ccy) / halfOut;
					float wobOut = this.topDown ? 0.04f * (this.Noise(x, y, 6 * k, 21) - 0.5f)
						: (0.30f * (this.Noise(x, y, 11 * k, 21) - 0.5f)) + (0.12f * (this.Noise(x, y, 3.8f * k, 22) - 0.5f));
					float shape = this.topDown ? 6f : 2.6f;
					float reachOut = MathF.Pow(MathF.Pow(exOut, shape) + MathF.Pow(eyOut, shape), 1f / shape) + wobOut;
					float coVal = this.topDown ? Smooth(1.0f, 0.88f, reachOut) : Smooth(1.18f, 0.72f, reachOut);

					if (coVal > outerVerge)
					{
						outerVerge = coVal;
						maxStep = step;
					}
					else if (ciVal > innerApron)
					{
						maxStep = Math.Max(maxStep, step);
					}

					innerApron = Math.Max(innerApron, ciVal);
				}
			}
		}

		/// <summary>
		/// How far a point (in pixels) is inside a city's clearing, 0 to 1, used to pull trees and peaks back around cities
		/// </summary>
		private float CityClearing(float x, float y, float k)
		{
			this.CityClearingDetail(x, y, k, out float innerApron, out float outerVerge, out int maxStep);
			float pullWeight = maxStep >= 3 ? 0.58f : 0.42f;
			return Math.Max(innerApron, outerVerge * pullWeight);
		}

		/// <summary>
		/// Smooth value noise in [0, 1] with features about `size` pixels across, repeating around the world horizontally
		/// </summary>
		/// <summary>
		/// The height of the land at a world position: the heights of the cells around it, blended smoothly, with
		/// painterly noise on top (ridges in mountains, rolling ground in hills, a gentle swell on open land). A function
		/// of the position only, so it joins up across chunks.
		/// </summary>
		private float HeightAt(TerrainGrid grid, float wx, float wy, float k)
		{
			// the cells' heights, joined by a smooth cubic B-spline through their centres (no steps at cell borders), read
			// at a position wandered a little so nothing lines up with the grid
			float fx = (wx / this.cell) - 0.5f + ((this.Noise(wx, wy, 90 * k, 34) - 0.5f) * 0.9f);
			float fy = (wy / this.cell) - 0.5f + ((this.Noise(wx, wy, 90 * k, 35) - 0.5f) * 0.9f);
			int ix = (int)MathF.Floor(fx), iy = (int)MathF.Floor(fy);
			float tx = fx - ix, ty = fy - iy;
			Span<float> bx = stackalloc float[4], by = stackalloc float[4];
			Spline(tx, bx);
			Spline(ty, by);
			float height = 0, rough = 0;

			for (int j = 0; j < 4; j++)
			{
				for (int i = 0; i < 4; i++)
				{
					int terrain = grid.At(ix - 1 + i, Math.Clamp(iy - 1 + j, 0, MapHeight - 1));
					float w = bx[i] * by[j];
					height += w * TerrainHeight[terrain];
					rough += w * TerrainRoughness[terrain];
				}
			}

			// ridged noise for crests, and softer noise for rolling ground
			float ridge = 1 - MathF.Abs((this.Noise(wx, wy, 34 * k, 31) * 2) - 1);
			float roll = (this.Noise(wx, wy, 70 * k, 32) * 0.6f) + (this.Noise(wx, wy, 18 * k, 33) * 0.4f);
			return height + (rough * ((ridge * ridge * 0.7f) + (roll * 0.6f)));
		}

		// the weights of a uniform cubic B-spline for the four points around t (0 to 1)
		private static void Spline(float t, Span<float> w)
		{
			float t2 = t * t, t3 = t2 * t;
			w[0] = (1 - (3 * t) + (3 * t2) - t3) / 6;
			w[1] = (4 - (6 * t2) + (3 * t3)) / 6;
			w[2] = (1 + (3 * t) + (3 * t2) - (3 * t3)) / 6;
			w[3] = t3 / 6;
		}

		private float Noise(float x, float y, float size, int seed)
		{
			int period = Math.Max(1, (int)MathF.Round(this.worldWidth / size));
			float gx = x / this.worldWidth * period;
			float gy = y / size;
			int ix = (int)MathF.Floor(gx), iy = (int)MathF.Floor(gy);
			float fx = gx - ix, fy = gy - iy;
			float u = fx * fx * (3 - (2 * fx)), v = fy * fy * (3 - (2 * fy));
			int x0 = ((ix % period) + period) % period, x1 = (x0 + 1) % period;
			float a = Hash(x0, iy, seed), b = Hash(x1, iy, seed), c = Hash(x0, iy + 1, seed), d = Hash(x1, iy + 1, seed);

			return a + ((b - a) * u) + ((c - a) * v) + ((a - b - c + d) * u * v);
		}

		// open terrains meet in crisp, inked edges, where a soft fade would look airbrushed; forest and jungle edges are
		// made by their crowns
		private static bool IsCrisp(int a, int b)
		{
			return a != Forest && b != Forest && a != Jungle && b != Jungle;
		}

		private static float Lerp(float a, float b, float t)
		{
			return a + ((b - a) * t);
		}

		private static float Smooth(float edge0, float edge1, float x)
		{
			float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);

			return t * t * (3 - (2 * t));
		}

		// Soft step from 0 to 1 across [-1, 1]
		private static float Step(float t)
		{
			return Smooth(-1, 1, t);
		}

		/// <summary>
		/// How much a pixel at p belongs to cell i along one axis, with borders softened over `radius` pixels.
		/// The weights of all cells add up to 1 at every pixel.
		/// </summary>
		private float Weight(float p, int i, float radius)
		{
			return Step((p - (i * this.cell)) / radius) - Step((p - ((i + 1) * this.cell)) / radius);
		}

		#endregion

		#region Painting

		/// <summary>
		/// How far each cell of a chunk (and its margin) is from land, in cells: 0 on land, about 1 just off a coast,
		/// more out at sea (counted out to the margin, which is as far as the chunk can see)
		/// </summary>
		private static float[] SeaDistances(TerrainGrid grid, int cx, int cy)
		{
			int width = ChunkCells + (2 * TerrainGrid.Margin), left = (cx * ChunkCells) - TerrainGrid.Margin, top = (cy * ChunkCells) - TerrainGrid.Margin;
			float[] d = new float[width * width];

			for (int j = 0; j < width; j++)
				for (int i = 0; i < width; i++)
					d[(j * width) + i] = grid.At(left + i, Math.Clamp(top + j, 0, MapHeight - 1)) == Water ? 99 : 0;

			// two chamfer passes: straight steps 1, diagonal 1.41
			for (int pass = 0; pass < 2; pass++)
			{
				for (int jj = 0; jj < width; jj++)
				{
					for (int ii = 0; ii < width; ii++)
					{
						int j = pass == 0 ? jj : width - 1 - jj, i = pass == 0 ? ii : width - 1 - ii, s = pass == 0 ? -1 : 1;
						int at = (j * width) + i;
						float best = d[at];

						if (i + s >= 0 && i + s < width) best = Math.Min(best, d[at + s] + 1);
						if (j + s >= 0 && j + s < width) best = Math.Min(best, d[at + (s * width)] + 1);
						if (i + s >= 0 && i + s < width && j + s >= 0 && j + s < width) best = Math.Min(best, d[at + (s * width) + s] + 1.41f);
						if (i - s >= 0 && i - s < width && j + s >= 0 && j + s < width) best = Math.Min(best, d[at + (s * width) - s] + 1.41f);
						d[at] = best;
					}
				}
			}

			for (int n = 0; n < d.Length; n++)
				d[n] = Math.Min(d[n], TerrainGrid.Margin);

			return d;
		}

		/// <summary>
		/// The distance from land at a world position, in cells, smoothly through the cells' distances (a cubic B-spline
		/// over their centres), so depth and swells follow the coast without showing the grid
		/// </summary>
		private float SeaDistanceAt(float[] distances, int cx, int cy, float wx, float wy)
		{
			int width = ChunkCells + (2 * TerrainGrid.Margin);
			float fx = (wx / this.cell) - 0.5f - ((cx * ChunkCells) - TerrainGrid.Margin);
			float fy = (wy / this.cell) - 0.5f - ((cy * ChunkCells) - TerrainGrid.Margin);
			int ix = (int)MathF.Floor(fx), iy = (int)MathF.Floor(fy);
			Span<float> bx = stackalloc float[4], by = stackalloc float[4];
			Spline(fx - ix, bx);
			Spline(fy - iy, by);
			float sum = 0;

			for (int j = 0; j < 4; j++)
				for (int i = 0; i < 4; i++)
					sum += bx[i] * by[j] * distances[(Math.Clamp(iy - 1 + j, 0, width - 1) * width) + Math.Clamp(ix - 1 + i, 0, width - 1)];

			return sum;
		}

		/// <param name="parallel">Paint rows on all cores, for a chunk the game is waiting for</param>
		private uint[] Paint(int cx, int cy, bool parallel)
		{
			if (this.print)
				return this.PaintPrint(cx, cy, parallel);

			int size = ChunkCells * this.cell;
			int x0 = cx * size, y0 = cy * size;
			float k = this.cell / 64f;
			float[] r = new float[size * size], g = new float[size * size], b = new float[size * size];
			// which pixels are open water (1) or river (2): they move when the screen is shown (see GBitmap.SeaAlpha)
			byte[] water = new byte[size * size];

			float warpAmount = 14 * k * this.coastWarp, groundRadius = 13 * k * this.groundBlend, coastRadius = 5 * k * this.coastWarp, surfRadius = 24 * k, depthRadius = 70 * k * this.shallows;
			float hillRadius = 15 * k;
			// a wandering coast also gets a fine, craggy edge
			float crag = this.coastWarp > 1 ? 0.3f * warpAmount * this.cragginess : 0;
			float riverWidth = 3.6f * k, bankWidth = 6.8f * k;
			bool hasGemGlades = !this.print && this.layout && this.HasSpriteSet("jungle_gems") && this.FeatureAt != null;
			int reach = (Math.Max(groundRadius, surfRadius) > this.cell / 2f) ? 2 : 1;

			Texture ocean = this.TextureOr("ocean"), shallow = this.TextureOr("shallow"), sand = this.TextureOr("desert"), fields = this.TextureOr("plains");
			Texture grassTex = this.TextureOr("grass"), rockTex = this.TextureOr("mountains");
			(Texture Texture, float Weight)[][] ground = Array.ConvertAll(this.groundParts, parts => Array.ConvertAll(parts, part => (this.TextureOr(part.Name), part.Weight)));
			TerrainGrid grid = new TerrainGrid(this, cx, cy);
			float[]? seaDistances = this.sea > 0 ? SeaDistances(grid, cx, cy) : null;

			List<(int Ci, int Cj, int Step, int DiagDx, int DiagDy)>? chunkCities = null;
			if (this.FeatureAt != null && this.layout)
			{
				for (int fj = (cy * ChunkCells) - 2; fj < ((cy + 1) * ChunkCells) + 2; fj++)
				{
					if (fj < 0 || fj >= MapHeight)
						continue;

					for (int fi = (cx * ChunkCells) - 2; fi < ((cx + 1) * ChunkCells) + 2; fi++)
					{
						int feat = this.Feature(fi, fj);
						if ((feat & City) == 0)
							continue;

						chunkCities ??= new List<(int, int, int, int, int)>();
						int step = Math.Min((feat & CityStepMask) >> CityStepShift, 5);
						(int diagDx, int diagDy) = this.DiagonalHarborCorner(fi, fj);
						chunkCities.Add((fi, fj, step, diagDx, diagDy));
					}
				}
			}

			// rows are independent, so they can be painted in parallel
			Rows(size, parallel, py =>
			{
				int wy = y0 + py;

				for (int px = 0; px < size; px++)
				{
					int wx = x0 + px;
					int i = (py * size) + px;

					// warped position: straight cell borders become organic
					float ox = (this.Noise(wx, wy, 24 * k, 1) - 0.5f) * 2 * warpAmount + (this.Noise(wx, wy, 8 * k, 2) - 0.5f) * 0.6f * warpAmount;
					float oy = (this.Noise(wx, wy, 24 * k, 3) - 0.5f) * 2 * warpAmount + (this.Noise(wx, wy, 8 * k, 4) - 0.5f) * 0.6f * warpAmount;

					// the craggy edge is for coasts and borders only: rivers keep their smooth course
					float cragX = 0, cragY = 0;

					if (crag > 0)
					{
						cragX = (this.Noise(wx, wy, 3 * k, 11) - 0.5f) * crag;
						cragY = (this.Noise(wx, wy, 3 * k, 12) - 0.5f) * crag;
						ox += cragX;
						oy += cragY;
					}

					float padLand = 0, diagCove = 0;
					if (chunkCities != null)
					{
						float warpCalm = 0;
						for (int cIdx = 0; cIdx < chunkCities.Count; cIdx++)
						{
							var city = chunkCities[cIdx];
							this.EvaluateCityShore(wx, wy, k, city.Ci, city.Cj, city.Step, city.DiagDx, city.DiagDy, ref warpCalm, ref padLand, ref diagCove);
						}

						if (warpCalm > 0)
						{
							float calmMul = 1f - (0.78f * warpCalm);
							ox *= calmMul;
							oy *= calmMul;
							cragX *= calmMul;
							cragY *= calmMul;
						}
					}

					float qx = wx + ox, qy = wy + oy;
					int ci = (int)MathF.Floor(qx / this.cell), cj = (int)MathF.Floor(qy / this.cell);

					// ground and coast from the 3x3 cells around the warped position (5x5 when the ground blends over more than
					// half a cell)
					float gr = 0, gg = 0, gb = 0, land = 0, surfLand = 0, groundWeight = 0, hillW = 0;

					for (int dj = -reach; dj <= reach; dj++)
					{
						float wyG = this.Weight(qy, cj + dj, groundRadius);
						float wyC = this.Weight(qy, cj + dj, coastRadius);
						float wyS = this.Weight(qy, cj + dj, surfRadius);

						for (int di = -reach; di <= reach; di++)
						{
							int terrain = grid.At(ci + di, cj + dj);
							bool isLand = terrain != Water;
							float w = wyG * this.Weight(qx, ci + di, groundRadius);

							// terrains reach into each other in patches where they meet, as a painter mixes them
							if (w > 0 && this.mix > 0 && isLand)
								w *= MathF.Exp(this.mix * 3.2f * ((this.Noise(wx, wy, 34 * k, 50 + terrain) * 0.65f) + (this.Noise(wx, wy, 11 * k, 70 + terrain) * 0.35f) - 0.5f));

							if (w > 0)
							{
								groundWeight += w;

								if (terrain == Hills)
									hillW += w;

								foreach ((Texture t, float weight) in ground[terrain])
								{
									int ti = this.TextureIndex(t, wx, wy);
									gr += w * weight * t.R[ti];
									gg += w * weight * t.G[ti];
									gb += w * weight * t.B[ti];
								}
							}

							if (isLand)
							{
								land += wyC * this.Weight(qx, ci + di, coastRadius);
								surfLand += wyS * this.Weight(qx, ci + di, surfRadius);
							}
						}
					}

					if (this.mix > 0 && groundWeight > 0)
					{
						gr /= groundWeight;
						gg /= groundWeight;
						gb /= groundWeight;

						// the hill share as a part of the whole, like the ground
						hillW /= groundWeight;
					}

					if (padLand > 0)
					{
						land = Math.Max(land, padLand);
						surfLand = Math.Max(surfLand, padLand);
					}

					if (diagCove > 0 && padLand < 0.72f)
					{
						float cut = diagCove * Smooth(0.72f, 0.25f, padLand);
						land = Math.Min(land, 1f - cut);
					}

					if (land >= 0.5f)
					{
						if (hillW > 0.008f)
						{
							float HillElev(float pxW, float pyW)
							{
								int hi0 = (int)MathF.Floor(pxW / this.cell), hj0 = (int)MathF.Floor(pyW / this.cell);
								float hMask = 0f;
								for (int hdj = -1; hdj <= 1; hdj++)
								{
									float wyH = this.Weight(pyW, hj0 + hdj, hillRadius);
									if (wyH <= 0f)
										continue;
									for (int hdi = -1; hdi <= 1; hdi++)
									{
										if (grid.At(hi0 + hdi, hj0 + hdj) == Hills)
											hMask += wyH * this.Weight(pxW, hi0 + hdi, hillRadius);
									}
								}
								if (hMask <= 0.001f)
									return 0f;

								float rx = pxW + ((this.Noise(pxW, pyW, 24 * k, 41) - 0.5f) * 14 * k);
								float ry = pyW + ((this.Noise(pxW, pyW, 24 * k, 42) - 0.5f) * 14 * k);
								float n1 = this.Noise(rx + (ry * 0.45f), ry, 24 * k, 43);
								float ridge1 = 1f - MathF.Abs((n1 * 2f) - 1f);
								ridge1 = ridge1 * (0.55f + (0.45f * ridge1));
								float n2 = this.Noise(rx, ry, 12 * k, 44);
								float undul = (ridge1 * 0.65f) + (n2 * 0.35f);
								return hMask * (0.48f + (0.52f * undul));
							}

							float eps = 3.2f * k;
							float cityFlat = 1f - this.CityClearing(wx, wy, k);
							float dry = Smooth(0.50f, 0.72f, land);
							float hArea = Smooth(0.04f, 0.55f, hillW) * dry * cityFlat;
							float eC = HillElev(qx, qy);
							float eNW = HillElev(qx - eps, qy - eps);
							float eSE = HillElev(qx + eps, qy + eps);
							float brush = (this.Noise(wx, wy, 4.0f * k, 45) - 0.5f) * 0.22f;
							float slope = (((eSE - eNW) * 2.85f) + brush) * dry * cityFlat;

							float steep = Smooth(0.24f, 0.72f, MathF.Abs(slope)) * hArea * (0.55f + (0.45f * this.Noise(wx, wy, 7 * k, 46)));
							if (steep > 0.005f)
							{
								int ri = this.TextureIndex(rockTex, wx, wy);
								float screeW = steep * 0.42f;
								gr = Lerp(gr, rockTex.R[ri], screeW);
								gg = Lerp(gg, rockTex.G[ri], screeW);
								gb = Lerp(gb, rockTex.B[ri], screeW);
							}

							float baseShift = hArea * 0.35f;
							gr = Lerp(gr, (gr * 0.96f) + 6f, baseShift);
							gg = Lerp(gg, gg * 0.92f, baseShift);
							gb = Lerp(gb, (gb * 0.88f) + 4f, baseShift);

							float lit = Math.Clamp(slope, 0f, 1.15f);
							float shd = Math.Clamp(-slope, 0f, 1.15f);
							gr = Math.Clamp(gr + (lit * 34f) + (eC * hArea * 12f), 0f, 255f) * (1f - (shd * 0.40f));
							gg = Math.Clamp(gg + (lit * 28f) + (eC * hArea * 10f), 0f, 255f) * (1f - (shd * 0.36f));
							gb = Math.Clamp(gb + (lit * 11f) + (eC * hArea * 4f), 0f, 255f) * (1f - (shd * 0.26f));
						}

						if (hasGemGlades)
						{
							int uc = (int)MathF.Floor((float)wx / this.cell), vc = (int)MathF.Floor((float)wy / this.cell);
							if (grid.At(uc, vc) == Jungle && (this.Feature(uc, vc) & SpecialResource) != 0)
							{
								float gdx = wx - ((uc + 0.5f) * this.cell), gdy = wy - ((vc + 0.46f) * this.cell);
								float gDist = MathF.Sqrt((gdx * gdx) + (gdy * gdy)) / this.cell;
								float gladeW = Smooth(0.42f, 0.16f, gDist) * 0.68f;
								if (gladeW > 0)
								{
									int gi = this.TextureIndex(grassTex, wx, wy);
									gr = Lerp(gr, grassTex.R[gi] * 1.04f, gladeW);
									gg = Lerp(gg, grassTex.G[gi] * 1.06f, gladeW);
									gb = Lerp(gb, grassTex.B[gi] * 1.02f, gladeW);
								}
							}
						}

						if (this.printedCoast)
						{
							// a printed coast: a dark key line at the water's edge, then a cream beach fading into the land
							float key = Smooth(0.6f, 0.52f, land);
							float beach = Smooth(0.75f, 0.6f, land) * (1 - key);
							r[i] = (gr * (1 - beach)) + (232 * beach);
							g[i] = (gg * (1 - beach)) + (220 * beach);
							b[i] = (gb * (1 - beach)) + (188 * beach);
							r[i] = (r[i] * (1 - key)) + (30 * key);
							g[i] = (g[i] * (1 - key)) + (26 * key);
							b[i] = (b[i] * (1 - key)) + (22 * key);
						}
						else if (this.beach)
						{
							// a beach: pale dry sand fading into the land, a little darker where the sand is wet
							int bi = this.TextureIndex(sand, wx, wy);
							float sandy = Smooth(0.97f, 0.62f, land), wet = 1 - (0.2f * Smooth(0.62f, 0.5f, land));
							// the beach's colour: the desert sand lightened to cream (beachtone 1) or as it is (0)
							float lift = 1 - (0.3f * this.beachTone), cream = 68 * this.beachTone;
							r[i] = ((gr * (1 - sandy)) + (((sand.R[bi] * lift) + cream) * sandy)) * wet;
							g[i] = ((gg * (1 - sandy)) + (((sand.G[bi] * lift) + cream) * sandy)) * wet;
							b[i] = ((gb * (1 - sandy)) + (((sand.B[bi] * lift) + cream) * sandy)) * wet;
						}
						else
						{
							// dark, rocky rim along the shore
							float rim = 1 - (0.55f * (1 - Smooth(0.5f, 0.85f, land)));
							r[i] = gr * rim;
							g[i] = gg * rim;
							b[i] = gb * rim;
						}
					}
					else
					{
						// water: shallow near land, deep away from it
						float near = 0;
						int ui = (int)MathF.Floor((float)wx / this.cell), uj = (int)MathF.Floor((float)wy / this.cell);

						for (int dj = -2; dj <= 2; dj++)
						{
							float wyD = this.Weight(wy, uj + dj, depthRadius);

							if (wyD <= 0)
								continue;

							for (int di = -2; di <= 2; di++)
							{
								if (grid.At(ui + di, uj + dj) != Water)
									near += wyD * this.Weight(wx, ui + di, depthRadius);
							}
						}

						float shallowness = Smooth(0.02f, 0.45f, near);
						float far = seaDistances != null
							? this.SeaDistanceAt(seaDistances, cx, cy, wx + ((this.Noise(wx, wy, 60 * k, 94) - 0.5f) * 30 * k), wy + ((this.Noise(wx, wy, 60 * k, 95) - 0.5f) * 30 * k))
							: 0;

						if (seaDistances != null)
						{
							// water hemmed in by land (lakes, inlets) is pale only along its shores and clear teal in the middle,
							// so it reads as water and not as a milky blot
							shallowness *= 1 - (0.7f * Smooth(0.7f, 1.8f, far) * this.sea);
						}

						int oi = this.TextureIndex(ocean, wx, wy), si = this.TextureIndex(shallow, wx, wy);
						float wr = ocean.R[oi] + ((shallow.R[si] - ocean.R[oi]) * shallowness);
						float wg = ocean.G[oi] + ((shallow.G[si] - ocean.G[oi]) * shallowness);
						float wb = ocean.B[oi] + ((shallow.B[si] - ocean.B[oi]) * shallowness);

						if (seaDistances != null)
						{
							// the open sea deepens away from land, with swells running along the coast
							float deep = Smooth(1.1f, 4.2f, far) * 0.42f * this.sea;
							wr *= 1 - (deep * 1.05f);
							wg *= 1 - (deep * 0.85f);
							wb *= 1 - (deep * 0.55f);

							// swells: soft bands of lighter water following the coast, long and broken here and there
							float phase = (far * 1.6f) + ((this.Noise(wx, wy, 48 * k, 90) - 0.5f) * 0.7f);
							float wave = Smooth(0.2f, 0.04f, MathF.Abs(phase - MathF.Floor(phase) - 0.5f));
							float broken = Smooth(0.3f, 0.58f, this.Noise(wx, wy, 30 * k, 91));
							float swell = wave * broken * Smooth(0.45f, 0.85f, far) * Smooth(3.2f, 1.6f, far) * 0.17f * this.sea;
							wr += ((wr * 1.45f) + 26 - wr) * swell;
							wg += ((wg * 1.35f) + 30 - wg) * swell;
							wb += ((wb * 1.2f) + 30 - wb) * swell;
						}

						// surf: a broken white line just off the coast, and fainter foam further out
						float foamNoise = (this.Noise(wx, wy, 12 * k, 5) * 0.7f) + (this.Noise(wx, wy, 4 * k, 6) * 0.3f);
						float surf = Smooth(0.2f, 0.34f, surfLand) * Smooth(0.5f, 0.4f, land) * Smooth(0.4f, 0.7f, foamNoise);
						surf += Smooth(0.08f, 0.14f, surfLand) * Smooth(0.2f, 0.14f, surfLand) * Smooth(0.6f, 0.8f, foamNoise) * 0.35f;
						surf = Math.Min(surf, 1) * 0.7f * this.surf;
						// the foam is the water's own colour lightened (sea 1), so a shore reads as surf and not as a chalk line
						float foamR = Lerp(210, Math.Min(255, (wr * 1.25f) + 70), this.sea);
						float foamG = Lerp(216, Math.Min(255, (wg * 1.2f) + 66), this.sea);
						float foamB = Lerp(205, Math.Min(255, (wb * 1.15f) + 60), this.sea);
						surf *= 1 - (0.3f * this.sea);
						r[i] = (wr * (1 - surf)) + (foamR * surf);
						g[i] = (wg * (1 - surf)) + (foamG * surf);
						b[i] = (wb * (1 - surf)) + (foamB * surf);
						water[i] = 1;
					}

					// a road or railroad through mountains, hills or woods is cut into them: a lane of earth and scree along it,
					// its walls (or the canopy's edge) in shadow, so the road runs through the land rather than over it
					if (land >= 0.5f && this.topDown && this.FeatureAt != null)
					{
						int rci = (int)MathF.Floor((float)wx / this.cell), rcj = (int)MathF.Floor((float)wy / this.cell);
						int here = grid.At(rci, rcj);
						bool woods = here == Forest || here == Jungle, rocky = here == Mountains || here == Hills;

						if ((woods || rocky) && ((this.Feature(rci, rcj) | this.Feature(rci - 1, rcj) | this.Feature(rci + 1, rcj) | this.Feature(rci, rcj - 1) | this.Feature(rci, rcj + 1) |
							this.Feature(rci - 1, rcj - 1) | this.Feature(rci + 1, rcj + 1) | this.Feature(rci - 1, rcj + 1) | this.Feature(rci + 1, rcj - 1)) & RoadCorridor) != 0)
						{
							float half = (here == Mountains ? 10f : 8.5f) * k * (0.8f + (0.4f * this.Noise(wx, wy, 9 * k, 77)));
							float rd = this.RoadDistance(wx, wy, rci, rcj);

							if (rd < half * 1.8f)
							{
								// the lane's floor: packed earth and scree in rock, grass and earth under the trees
								float floor = Smooth(half, half * 0.6f, rd);
								float fr = woods ? 112 : 128, fg = woods ? 116 : 114, fb = woods ? 64 : 86;
								float grain = 0.9f + (0.2f * this.Noise(wx, wy, 2.5f * k, 78));
								// the terrain's own texture still shows through the floor, so the lane is part of the land
								r[i] += ((fr * grain) - r[i]) * floor * 0.5f;
								g[i] += ((fg * grain) - g[i]) * floor * 0.5f;
								b[i] += ((fb * grain) - b[i]) * floor * 0.5f;

								// the walls of the cut, or the canopy's edge: a band of shadow just outside the floor
								float wall = Smooth(half * 0.75f, half * 1.05f, rd) * Smooth(half * 1.8f, half * 1.15f, rd);
								float dark = wall * (woods ? 0.5f : 0.45f);
								r[i] *= 1 - dark;
								g[i] *= 1 - (dark * 0.9f);
								b[i] *= 1 - (dark * 0.75f);
							}
						}
					}

					// a mine is cut into a mountain or hill: a levelled terrace round a dark adit, and a fan of pale spoil tipped
					// down the slope below it; a fortress stands on a levelled earth platform inside a dark ditch
					if (land >= 0.5f && this.topDown && this.FeatureAt != null)
					{
						int sci = (int)MathF.Floor((float)wx / this.cell), scj = (int)MathF.Floor((float)wy / this.cell);
						int site = this.Feature(sci, scj), siteT = grid.At(sci, scj);
						float cx0 = sci * this.cell, cy0 = scj * this.cell;

						if ((site & MineSite) != 0 && (siteT == Mountains || siteT == Hills))
						{
							float mx = cx0 + (0.57f * this.cell), my = cy0 + (0.56f * this.cell);
							float dx = wx - mx, dy = wy - my;
							float wob = (this.Noise(wx, wy, 4 * k, 79) - 0.5f) * 3 * k;
							// spoil: downhill (to the lower right), widening as it goes
							float along = ((dx + dy) * 0.707f) + wob, across = MathF.Abs((dx - dy) * 0.707f);
							float spoil = Smooth(-2 * k, 3 * k, along) * Smooth(28 * k, 18 * k, along) * Smooth(4.5f * k + (along * 0.5f), 1.5f * k + (along * 0.35f), across);
							float speck = 0.85f + (0.3f * this.Noise(wx, wy, 1.6f * k, 80));
							r[i] += ((196 * speck) - r[i]) * spoil * 0.92f;
							g[i] += ((180 * speck) - g[i]) * spoil * 0.92f;
							b[i] += ((150 * speck) - b[i]) * spoil * 0.92f;
							// the terrace and the adit's dark mouth
							float dist = MathF.Sqrt((dx * dx) + (dy * dy)) + wob;
							float terrace = Smooth(12 * k, 8 * k, dist);
							r[i] += (156 - r[i]) * terrace * 0.75f;
							g[i] += (140 - g[i]) * terrace * 0.75f;
							b[i] += (108 - b[i]) * terrace * 0.75f;
							float mouth = Smooth(5 * k, 3.4f * k, MathF.Sqrt((dx * dx) + ((dy + (0.6f * k)) * (dy + (0.6f * k)) * 1.6f)));
							r[i] += (24 - r[i]) * mouth;
							g[i] += (20 - g[i]) * mouth;
							b[i] += (18 - b[i]) * mouth;
						}

						if ((site & FortressSite) != 0)
						{
							float fx2 = cx0 + (0.5f * this.cell), fy2 = cy0 + (0.56f * this.cell);
							float ex = (wx - fx2) / (0.37f * this.cell), ey = (wy - fy2) / (0.26f * this.cell);
							float e = MathF.Sqrt((ex * ex) + (ey * ey)) + ((this.Noise(wx, wy, 5 * k, 83) - 0.5f) * 0.12f);
							float platform = Smooth(1.05f, 0.9f, e);
							r[i] += (132 - r[i]) * platform * 0.75f;
							g[i] += (118 - g[i]) * platform * 0.75f;
							b[i] += (86 - b[i]) * platform * 0.75f;
							float ditch = Smooth(1.05f, 1.15f, e) * Smooth(1.45f, 1.2f, e) * 0.45f;
							r[i] *= 1 - ditch;
							g[i] *= 1 - (ditch * 0.9f);
							b[i] *= 1 - (ditch * 0.8f);
						}
					}

					// rivers wind through river cells and on into the sea
					if (land >= 0.5f)
					{
						float rqx = qx - cragX, rqy = qy - cragY, wobble = 1;

						if (this.softRivers)
						{
							// a soft river meanders on a slow curve and swells and narrows a little
							rqx += (this.Noise(wx, wy, 48 * k, 8) - 0.5f) * 24 * k;
							rqy += (this.Noise(wx, wy, 48 * k, 9) - 0.5f) * 24 * k;
							wobble = 1.1f + (0.45f * this.Noise(wx, wy, 30 * k, 10));
						}

						float d = this.RiverDistance(grid.At, rqx, rqy, ci, cj) / wobble;

						if (d < bankWidth)
						{
							if (d < riverWidth)
							{
								int si = this.TextureIndex(shallow, wx, wy);
								bool lit = this.RiverDistance(grid.At, rqx - (2 * k), rqy, ci, cj) / wobble >= riverWidth;
								float light = lit ? 1.35f : 1f;

								if (this.softRivers)
								{
									// bright cerulean-teal sunlit river stream, lifted in midstream and crisp on the sunlit bank
									int oi = this.TextureIndex(ocean, wx, wy);
									float midRatio = Math.Clamp(1f - (d / riverWidth), 0f, 1f);
									float wr = Lerp(ocean.R[oi], shallow.R[si], 0.58f);
									float wg = Lerp(ocean.G[oi], shallow.G[si], 0.58f);
									float wb = Lerp(ocean.B[oi], shallow.B[si], 0.58f);
									light = lit ? 1.24f : (0.96f + (0.08f * midRatio));
									r[i] = wr * light;
									g[i] = (wg * light) + (4f * midRatio);
									b[i] = (wb * light) + (10f * midRatio);
								}
								else
								{
									r[i] = ((shallow.R[si] * 0.7f) + 18) * light;
									g[i] = ((shallow.G[si] * 0.7f) + 21) * light;
									b[i] = ((shallow.B[si] * 0.7f) + 21) * light;
								}

								water[i] = 2;
							}
							else
							{
								// a dark bank, or with soft rivers a rich damp bank rim that settles the river cleanly into land
								float bank = this.softRivers ? 0.68f + (0.32f * Smooth(riverWidth, bankWidth, d)) : 0.5f;
								r[i] *= bank;
								g[i] *= bank;
								b[i] *= bank;
							}
						}
					}

					// a city stands on a paved stone apron inside an organic garden & orchard verge (replacing bare brown dirt);
					// painted after rivers so rivers keep flowing right past the outer city gardens
					if (land >= 0.5f && this.layout)
					{
						this.CityClearingDetail(wx + cragX, wy + cragY, k, out float innerApron, out float outerVerge, out int maxStep);

						if (innerApron > 0 || outerVerge > 0)
						{
							// the trees or rocks to the top left cast their shadow into it
							float clearCombo = Math.Max(innerApron, outerVerge * 0.65f);
							float reach = 10 * k, sx = wx + cragX - reach, sy = wy + cragY - reach;
							int beyond = grid.At((int)MathF.Floor(sx / this.cell), (int)MathF.Floor(sy / this.cell));
							float tall = (beyond == Forest || beyond == Jungle) ? 0.5f : (beyond == Hills || beyond == Mountains) ? 0.35f : 0.12f;
							float shadow = 1 - (tall * clearCombo * (1 - this.CityClearing(sx, sy, k)));

							int fi = this.TextureIndex(fields, wx, wy);
							int di = this.TextureIndex(sand, wx, wy);
							int gi = this.TextureIndex(grassTex, wx, wy);
							float mottle = this.Noise(wx, wy, 7 * k, 15);
							float lawnMottle = this.Noise(wx, wy, 4.5f * k, 23);

							// inner paved limestone/cobble courtyard apron (for size >= 2) or warm compact hamlet footing
							float apronR, apronG, apronB;
							if (maxStep >= 2)
							{
								float stoneTone = (sand.R[di] * 0.36f) + (fields.R[fi] * 0.24f) + 64f + (mottle * 16f);
								apronR = stoneTone * 1.02f;
								apronG = stoneTone * 0.96f;
								apronB = stoneTone * 0.80f;
							}
							else
							{
								apronR = ((fields.R[fi] * 0.50f) + (sand.R[di] * 0.50f)) * 0.86f + 16f + (mottle * 10f);
								apronG = ((fields.G[fi] * 0.50f) + (sand.G[di] * 0.50f)) * 0.82f + 12f + (mottle * 8f);
								apronB = ((fields.B[fi] * 0.50f) + (sand.B[di] * 0.50f)) * 0.72f + 6f + (mottle * 5f);
							}

							// outer cultivated garden lawn (blended gently with underlying turf so there is no bare dirt halo)
							float hedgeDark = lawnMottle < 0.26f ? 0.90f : lawnMottle > 0.76f ? 1.06f : 1.0f;
							float rawGardenR = ((grassTex.R[gi] * 0.76f) + (fields.R[fi] * 0.24f)) * hedgeDark;
							float rawGardenG = (((grassTex.G[gi] * 0.80f) + (fields.G[fi] * 0.20f)) + 6f) * hedgeDark;
							float rawGardenB = ((grassTex.B[gi] * 0.76f) + (fields.B[fi] * 0.24f)) * hedgeDark;
							float gardenR = Lerp(r[i], rawGardenR, 0.45f);
							float gardenG = Lerp(g[i], rawGardenG, 0.45f);
							float gardenB = Lerp(b[i], rawGardenB, 0.45f);

							float apronMix = Smooth(0.28f, 0.82f, innerApron);
							float fr = Lerp(gardenR, apronR, apronMix);
							float fg = Lerp(gardenG, apronG, apronMix);
							float fb = Lerp(gardenB, apronB, apronMix);

							if (this.topDown)
							{
								// seen from above, a town sits in a patchwork of small fields with earth paths between them
								float fieldSize = 12 * k;
								float ux = wx + ((this.Noise(wx, wy, 26 * k, 71) - 0.5f) * 5 * k), uy = wy + ((this.Noise(wx, wy, 26 * k, 72) - 0.5f) * 5 * k);
								int gi0 = (int)MathF.Floor(ux / fieldSize), gj0 = (int)MathF.Floor(uy / fieldSize);
								float f1 = float.MaxValue, f2 = float.MaxValue;
								int pu = 0, pv = 0;

								// irregular fields: each is the ground nearest one of a scatter of points; hedges where two meet
								for (int nj = -1; nj <= 1; nj++)
								{
									for (int ni = -1; ni <= 1; ni++)
									{
										int ci2 = gi0 + ni, cj2 = gj0 + nj;
										float seedX = (ci2 + 0.1f + (0.8f * Hash(ci2, cj2, 75))) * fieldSize, seedY = (cj2 + 0.1f + (0.8f * Hash(ci2, cj2, 76))) * fieldSize;
										float dd = ((ux - seedX) * (ux - seedX)) + ((uy - seedY) * (uy - seedY));

										if (dd < f1)
										{
											f2 = f1;
											f1 = dd;
											pu = ci2;
											pv = cj2;
										}
										else if (dd < f2)
										{
											f2 = dd;
										}
									}
								}

								float border = (MathF.Sqrt(f2) - MathF.Sqrt(f1)) / fieldSize;
								float pick = Hash(pu, pv, 73);
								(float pr, float pg, float pb) = pick < 0.25f ? (122f, 130f, 54f) : pick < 0.5f ? (170f, 150f, 70f) : pick < 0.72f ? (92f, 110f, 44f) : pick < 0.88f ? (184f, 160f, 88f) : (140f, 120f, 70f);
								float angle = Hash(pu, pv, 74) * MathF.PI;
								float along = (ux * MathF.Cos(angle)) + (uy * MathF.Sin(angle));
								float furrow = 1 + (0.06f * MathF.Sin(along / (1.1f * k)));
								// hedgerows: dark green lines between the fields
								float path = Smooth(0.06f, 0.015f, border);
								pr = Lerp(pr * furrow, 62f, path * 0.7f);
								pg = Lerp(pg * furrow, 74f, path * 0.7f);
								pb = Lerp(pb * furrow, 42f, path * 0.7f);
								fr = pr;
								fg = pg;
								fb = pb;
							}

							int localT = grid.At((int)MathF.Floor((wx + cragX) / this.cell), (int)MathF.Floor((wy + cragY) / this.cell));
							if (water[i] != 2)
							{
								if (localT == Arctic)
								{
									fr = Lerp(fr, Math.Max(r[i] * 0.90f, 178f), 0.62f);
									fg = Lerp(fg, Math.Max(g[i] * 0.90f, 182f), 0.62f);
									fb = Lerp(fb, Math.Max(b[i] * 0.92f, 190f), 0.62f);
								}
								else if (localT == Tundra || localT == Mountains)
								{
									fr = (fr * 0.58f) + (r[i] * 0.42f * 0.96f);
									fg = (fg * 0.58f) + (g[i] * 0.42f * 0.98f);
									fb = (fb * 0.58f) + (b[i] * 0.42f * 1.08f);
								}
								else if (localT == Desert)
								{
									float oasisGreen = (1f - apronMix) * (maxStep >= 3 ? 0.42f : 0.22f);
									float sandMix = 0.52f * (1f - oasisGreen);
									fr = Lerp(fr, (sand.R[di] * 0.85f) + 32f, sandMix);
									fg = Lerp(fg, (sand.G[di] * 0.85f) + 24f, sandMix);
									fb = Lerp(fb, (sand.B[di] * 0.85f) + 12f, sandMix);
								}
								else if (localT == Forest || localT == Jungle || localT == Swamp)
								{
									float outerMoss = 0.34f * (1f - apronMix);
									fr = Lerp(fr, r[i] * 0.96f, outerMoss);
									fg = Lerp(fg, g[i] * 1.02f, outerMoss);
									fb = Lerp(fb, b[i] * 0.95f, outerMoss);
								}
							}

							float vergeWeight = maxStep <= 1 ? 0.16f : maxStep == 2 ? 0.24f : maxStep == 3 ? 0.30f : 0.38f;

							// seen from above, the fields round a town are a part of the land in their own right
							if (this.topDown)
								vergeWeight = 0.85f;
							float rawMask = Math.Max(innerApron, outerVerge * vergeWeight);
							float shoreKeep = (this.beach && water[i] != 2) ? 0.25f + (0.75f * Smooth(0.54f, 0.88f, land)) : 1f;
							float blend = (water[i] == 2 ? Smooth(0.68f, 0.94f, innerApron) : rawMask) * shoreKeep;
							float edge = (1 - (0.03f * Smooth(0.18f, 0.5f, blend) * Smooth(0.88f, 0.55f, blend))) * shadow;
							r[i] = ((r[i] * (1 - blend)) + (fr * blend)) * edge;
							g[i] = ((g[i] * (1 - blend)) + (fg * blend)) * edge;
							b[i] = ((b[i] * (1 - blend)) + (fb * blend)) * edge;

							if (blend > 0.55f)
								water[i] = 0;
						}
					}
				}
			});

			// which pixels the scenery covers with a picture: the relief, snow and regional colour are for the bare ground
			byte[] covered = new byte[size * size];
			this.PaintScenery(cx, cy, r, g, b, water, size, this.layout ? grid : null, null, covered);

			// light: drifting cloud shadows and a slight warm grade on the highlights
			uint[] pixels = new uint[size * size];

			Rows(size, parallel, py =>
			{
				for (int px = 0; px < size; px++)
				{
					int i = (py * size) + px;
					int wx = x0 + px, wy = y0 + py;
					float cloud = Math.Clamp(((this.Noise(wx, wy, 160 * k, 7) * 0.7f) + (this.Noise(wx, wy, 60 * k, 8) * 0.3f) - 0.45f) * 2.2f, 0, 1);
					float shade = 1 - (cloud * 0.35f * this.clouds);

					// how much bare ground shows: pictures (a gold mountain, a gem glade, a resource) keep their own colours
					float bare = 1 - (covered[i] / 255f);

					if (water[i] == 0 && bare > 0 && (this.relief > 0 || this.variety > 0))
					{
						if (this.relief > 0)
						{
							// lit from the top left: slopes facing the light brighter, the far sides in shadow
							float e = 3 * k;
							float dx = this.HeightAt(grid, wx + e, wy, k) - this.HeightAt(grid, wx - e, wy, k);
							float dy = this.HeightAt(grid, wx, wy + e, k) - this.HeightAt(grid, wx, wy - e, k);
							float slope = 46 / k;
							float nx = -dx * slope, ny = -dy * slope;
							float lit = ((nx * -0.6f) + (ny * -0.6f) + 0.53f) / MathF.Sqrt((nx * nx) + (ny * ny) + 1);
							float strength = this.relief;

							if (this.topDown)
							{
								// mountains seen from above are their relief: ridges and valleys lit much more strongly, and
								// snow on the highest crests
								float height = this.HeightAt(grid, wx, wy, k);
								// mountains and hills carry their own painted light and snow: the ground's relief stays off them,
								// or it smears over their ridges
								strength *= 1 - (0.9f * Smooth(0.3f, 0.6f, height));
								float snow = 0;

								if (snow > 0)
								{
									r[i] += (236 - r[i]) * snow * bare;
									g[i] += (238 - g[i]) * snow * bare;
									b[i] += (240 - b[i]) * snow * bare;
								}
							}

							// soft, not clipped: steep slopes approach their darkest and brightest gradually
							shade *= 1 + (bare * strength * 0.38f * MathF.Tanh((lit - 0.53f) * 2.9f));
						}

						if (this.variety > 0)
						{
							// regions drift between lush and dry
							float moist = ((this.Noise(wx, wy, 520 * k, 41) * 0.7f) + (this.Noise(wx, wy, 190 * k, 42) * 0.3f) - 0.5f) * 2 * this.variety * bare;
							r[i] *= 1 - (0.09f * moist);
							g[i] *= 1 + (0.02f * moist);
							b[i] *= 1 - (0.05f * moist);
							shade *= 1 - (0.06f * moist);
						}
					}
					float l = (r[i] + g[i] + b[i]) / 765f;
					float scale = shade * (1 + (this.grade * (-0.08f + (0.16f * l))));
					float rr = (r[i] * scale) + (this.grade * ((6 * l) - (2 * (1 - l))));
					float gg2 = (g[i] * scale) + (this.grade * 2 * l);
					float bb = (b[i] * scale) + (this.grade * ((-6 * l) + (4 * (1 - l))));
					uint alpha = water[i] == 1 ? GBitmap.SeaAlpha : water[i] == 2 ? GBitmap.RiverAlpha : 0xff000000u;
					pixels[i] = alpha | ((uint)Math.Clamp(rr, 0, 255) << 16) | ((uint)Math.Clamp(gg2, 0, 255) << 8) | (uint)Math.Clamp(bb, 0, 255);
				}
			});

			return pixels;
		}

		/// <summary>
		/// Paints a chunk like a woodblock print: every pixel takes the terrain of the cell at its warped position, so each
		/// terrain is a crisp area with an organic edge, and a dark key line is drawn where two terrains meet. Grassland and
		/// plains meet with a faint line, as two colour blocks of one print. Coasts get a key line and a cream beach on the
		/// land side and a band of cream foam on the water side; rivers a light core inside dark banks. Textures are drawn
		/// `pattern` times larger, and colours lean towards the paper by `paper`.
		/// </summary>
		/// <summary>
		/// The terrain shown at a position in cells: each cell around it votes for its terrain with a weight that falls
		/// off with distance from its centre, and the cell the position is in counts a little more. Edges stay where the
		/// cells meet, but corners are rounded off, so the cell grid does not show in the shapes of forests, coasts and ice.
		/// </summary>
		private int RoundedTerrain(TerrainGrid grid, float fx, float fy)
		{
			const float Radius = 1.25f, OwnCell = 1.3f;
			int ci = (int)MathF.Floor(fx), cj = (int)MathF.Floor(fy);
			Span<float> votes = stackalloc float[16];
			int best = -1;
			float bestVote = 0;

			for (int j = cj - 1; j <= cj + 1; j++)
			{
				for (int i = ci - 1; i <= ci + 1; i++)
				{
					float dx = (fx - (i + 0.5f)) / Radius, dy = (fy - (j + 0.5f)) / Radius;
					float w = 1 - (dx * dx) - (dy * dy);

					if (w <= 0)
						continue;

					int terrain = grid.At(i, j);
					terrain = terrain == River ? Grassland : terrain;
					float vote = votes[terrain] += w * w * ((i == ci && j == cj) ? OwnCell : 1);

					if (vote > bestVote)
					{
						bestVote = vote;
						best = terrain;
					}
				}
			}

			return best;
		}

		/// <summary>
		/// The terrain a print shows at a pixel: the rounded terrain at a position moved by a slow, wide wobble and a
		/// finer one, so edges between terrains are organic
		/// </summary>
		private int PrintTerrain(TerrainGrid grid, float wx, float wy)
		{
			this.PrintWarp(wx, wy, out float ox, out float oy);

			return this.RoundedTerrain(grid, (wx + ox) / this.cell, (wy + oy) / this.cell);
		}

		// how far a print moves a pixel before looking up its terrain
		private void PrintWarp(float wx, float wy, out float ox, out float oy)
		{
			float k = this.cell / 64f, warpAmount = 22 * k;
			ox = ((this.Noise(wx, wy, 40 * k, 1) - 0.5f) * 2 * warpAmount) + ((this.Noise(wx, wy, 11 * k, 2) - 0.5f) * 0.5f * warpAmount);
			oy = ((this.Noise(wx, wy, 40 * k, 3) - 0.5f) * 2 * warpAmount) + ((this.Noise(wx, wy, 11 * k, 4) - 0.5f) * 0.5f * warpAmount);
		}

		/// <summary>
		/// For each water pixel of the region, the distance to the nearest land pixel (0 on land), by a chamfer transform
		/// </summary>
		private static float[] WaterDistance(int[] region, int ext)
		{
			bool[] land = new bool[ext * ext];

			for (int i = 0; i < land.Length; i++)
				land[i] = region[i] != Water;

			return ChamferDistance(land, ext);
		}

		/// <summary>
		/// For each pixel of the region, about how far it is from the nearest pixel next to another terrain
		/// </summary>
		private static float[] BorderDistance(int[] region, int ext)
		{
			bool[] border = new bool[ext * ext];

			for (int y = 0; y < ext; y++)
			{
				for (int x = 0; x < ext; x++)
				{
					int i = (y * ext) + x, own = region[i];
					border[i] = (x > 0 && region[i - 1] != own) || (x < ext - 1 && region[i + 1] != own) ||
						(y > 0 && region[i - ext] != own) || (y < ext - 1 && region[i + ext] != own);
				}
			}

			return ChamferDistance(border, ext);
		}

		// distance from each pixel to the nearest seed pixel, by a two pass chamfer transform
		private static float[] ChamferDistance(bool[] seeds, int ext)
		{
			const float Diagonal = 1.4142f;
			float[] d = new float[ext * ext];

			for (int i = 0; i < d.Length; i++)
				d[i] = seeds[i] ? 0 : float.MaxValue / 2;

			for (int y = 0; y < ext; y++)
			{
				for (int x = 0; x < ext; x++)
				{
					int i = (y * ext) + x;

					if (x > 0) d[i] = Math.Min(d[i], d[i - 1] + 1);
					if (y > 0) d[i] = Math.Min(d[i], d[i - ext] + 1);
					if (x > 0 && y > 0) d[i] = Math.Min(d[i], d[i - ext - 1] + Diagonal);
					if (x < ext - 1 && y > 0) d[i] = Math.Min(d[i], d[i - ext + 1] + Diagonal);
				}
			}

			for (int y = ext - 1; y >= 0; y--)
			{
				for (int x = ext - 1; x >= 0; x--)
				{
					int i = (y * ext) + x;

					if (x < ext - 1) d[i] = Math.Min(d[i], d[i + 1] + 1);
					if (y < ext - 1) d[i] = Math.Min(d[i], d[i + ext] + 1);
					if (x < ext - 1 && y < ext - 1) d[i] = Math.Min(d[i], d[i + ext + 1] + Diagonal);
					if (x > 0 && y < ext - 1) d[i] = Math.Min(d[i], d[i + ext - 1] + Diagonal);
				}
			}

			return d;
		}

		private uint[] PaintPrint(int cx, int cy, bool parallel)
		{
			int size = ChunkCells * this.cell;
			int x0 = cx * size, y0 = cy * size;
			float k = this.cell / 64f;
			// the shallows fade out over this distance from the shore, so the margin has to reach that far
			float shallowRange = 48 * k * this.shallows;
			int margin = (int)MathF.Ceiling(Math.Max(8 * k, shallowRange * 1.3f)) + 2;
			int ext = size + (2 * margin);
			float coastLine = 1.3f * k, beachWidth = 3 * k, softWidth = 7 * k;
			float riverWidth = 3.4f * k, riverLine = 1.0f * k, waveAt = 10 * k;

			Texture ocean = this.TextureOr("ocean"), shallow = this.TextureOr("shallow");
			Texture[] ground = Array.ConvertAll(this.groundParts, parts => this.TextureOr(parts[0].Name));
			TerrainGrid grid = new TerrainGrid(this, cx, cy);
			// the resources of the chunk's cells and their neighbours, painted into the ground
			const int FeatureMargin = 2;
			int featureLeft = (cx * ChunkCells) - FeatureMargin, featureTop = (cy * ChunkCells) - FeatureMargin, featureWidth = ChunkCells + (2 * FeatureMargin);
			bool[] resources = new bool[featureWidth * featureWidth];

			if (this.PaintsFeatures)
			{
				for (int fj = 0; fj < featureWidth; fj++)
					for (int fi = 0; fi < featureWidth; fi++)
						resources[(fj * featureWidth) + fi] = (this.Feature(featureLeft + fi, featureTop + fj) & SpecialResource) != 0;
			}

			// pass 1: the terrain of every pixel (with a margin, for the lines and the shallows). The warp is smooth, so it is
			// worked out every few pixels and blended in between.
			const int WarpStep = 4;
			int warpWidth = (ext / WarpStep) + 2;
			float[] warpX = new float[warpWidth * warpWidth], warpY = new float[warpWidth * warpWidth];

			Rows(warpWidth, parallel, gy =>
			{
				for (int gx = 0; gx < warpWidth; gx++)
					this.PrintWarp(x0 - margin + (gx * WarpStep), y0 - margin + (gy * WarpStep), out warpX[(gy * warpWidth) + gx], out warpY[(gy * warpWidth) + gx]);
			});

			int[] region = new int[ext * ext];

			Rows(ext, parallel, ey =>
			{
				int wy = y0 - margin + ey;
				int gy = ey / WarpStep;
				float fy = (ey % WarpStep) / (float)WarpStep;

				for (int ex = 0; ex < ext; ex++)
				{
					int wx = x0 - margin + ex;
					int gx = ex / WarpStep, g = (gy * warpWidth) + gx;
					float fx = (ex % WarpStep) / (float)WarpStep;
					float ox = Lerp(Lerp(warpX[g], warpX[g + 1], fx), Lerp(warpX[g + warpWidth], warpX[g + warpWidth + 1], fx), fy);
					float oy = Lerp(Lerp(warpY[g], warpY[g + 1], fx), Lerp(warpY[g + warpWidth], warpY[g + warpWidth + 1], fx), fy);
					region[(ey * ext) + ex] = this.RoundedTerrain(grid, (wx + ox) / this.cell, (wy + oy) / this.cell);
				}
			});

			float[] seaDepth = WaterDistance(region, ext);
			float[] border = BorderDistance(region, ext);
			// a painted river strip is laid along the courses at its own proportions, riverStripWidth across
			Sprite? strip = this.riverStrip;
			float riverStripWidth = 7 * k, riverInk = 1.1f * k, riverBank = 1.6f * k;
			float period = (strip != null) ? strip.Width * riverStripWidth / strip.Height : 64 * k;
			RiverCourses rivers = new RiverCourses(this, grid, cx, cy, period);
			// only pixels this close to a river are drawn differently
			rivers.Reach = Math.Max((riverStripWidth / 2) + riverInk + riverBank, riverWidth + riverLine) + 2 + (3 * k);
			float[] r = new float[size * size], g = new float[size * size], b = new float[size * size];
			byte[] water = new byte[size * size];
			int reach = (int)MathF.Ceiling(Math.Max(softWidth, beachWidth + coastLine)) + 1;

			// pass 2: colour, lines, coast and rivers
			Rows(size, parallel, py =>
			{
				int wy = y0 + py;

				for (int px = 0; px < size; px++)
				{
					int wx = x0 + px;
					int i = (py * size) + px;
					int e = ((py + margin) * ext) + px + margin;
					int own = region[e];
					bool isWater = own == Water;

					// distance to the nearest other terrain and which it is, and to the coast
					float edge = float.MaxValue, coast = float.MaxValue;
					int nearest = own;

					// far from any border there is nothing to look for
					for (int dy = (border[e] > (reach * 1.42f) + 1) ? reach + 1 : -reach; dy <= reach; dy++)
					{
						for (int dx = -reach; dx <= reach; dx++)
						{
							int other = region[e + (dy * ext) + dx];

							if (other == own)
								continue;

							float d = MathF.Sqrt((dx * dx) + (dy * dy));

							if ((other == Water) != isWater)
								coast = Math.Min(coast, d);

							if (d < edge && other != Water && !isWater)
							{
								edge = d;
								nearest = other;
							}
						}
					}

					float cr, cg, cb;
					// texture positions, drawn larger by the pattern sizes
					float tx = wx / this.pattern, ty = wy / this.pattern;
					float sx = wx / this.seaPattern, sy = wy / this.seaPattern;

					if (isWater)
					{
						// the sea lightens towards the shore like a bokashi gradient that follows the coast, reaching a little
						// further out in some places than others
						float depth = seaDepth[e];
						float range = shallowRange * (0.4f + (1.2f * this.Noise(wx, wy, 120 * k, 17)));
						// a defined band of shallow water along the shore, its outer edge drawn with a faint darker line
						float band = range * 0.6f;
						float shallowness = Smooth(band + (1.2f * k), band - (1.2f * k), depth);
						Fetch(ocean, sx, sy, out float or, out float og, out float ob);
						Fetch(shallow, sx, sy, out float sr, out float sg, out float sb);
						cr = or + ((sr - or) * shallowness);
						cg = og + ((sg - og) * shallowness);
						cb = ob + ((sb - ob) * shallowness);
						float dropOff = Smooth(1.6f * k, 0.4f * k, MathF.Abs(depth - band - (1.4f * k))) * 0.35f;
						cr += (26 - cr) * dropOff;
						cg += (44 - cg) * dropOff;
						cb += (70 - cb) * dropOff;

						// one broken pale wave line running along the shore
						float waveOffset = (this.Noise(wx, wy, 25 * k, 23) - 0.5f) * 3 * k;
						float wave = Smooth(0.9f * k, 0.3f * k, MathF.Abs(depth - waveAt - waveOffset))
							* Smooth(0.45f, 0.6f, this.Noise(wx, wy, 16 * k, 24)) * 0.55f;
						cr += (230 - cr) * wave;
						cg += (228 - cg) * wave;
						cb += (214 - cb) * wave;
						water[i] = 1;
					}
					else
					{
						// a lone peak rises from foothills, not from a patch of bare rock: rock ground is for ranges
						int groundOf = own;

						if (own == Mountains)
						{
							int mci = (int)MathF.Floor((float)wx / this.cell), mcj = (int)MathF.Floor((float)wy / this.cell), range = 0;

							for (int nj = -1; nj <= 1; nj++)
								for (int ni = -1; ni <= 1; ni++)
									if ((ni != 0 || nj != 0) && grid.At(mci + ni, mcj + nj) == Mountains)
										range++;

							if (range < 2)
								groundOf = Hills;
						}

						float ownPattern = this.terrainPattern[groundOf] > 0 ? this.terrainPattern[groundOf] : this.pattern;
						Fetch(ground[groundOf], wx / ownPattern, wy / ownPattern, out cr, out cg, out cb);

						// like the colour blocks of a woodblock print, terrains meet in a cut edge with a fine key line; under
						// forest and jungle, whose crowns make their edge, the ground fades instead
						if (nearest != own && IsCrisp(own, nearest))
						{
							float ink = Smooth(1.5f * k, 0.5f * k, edge) * ((own == Arctic || nearest == Arctic || own == Swamp || nearest == Swamp) ? 0.55f : 0.42f);
							cr += (58 - cr) * ink;
							cg += (52 - cg) * ink;
							cb += (44 - cb) * ink;
						}
						else if (nearest != own)
						{
							float nearestPattern = this.terrainPattern[nearest] > 0 ? this.terrainPattern[nearest] : this.pattern;
							Fetch(ground[nearest], wx / nearestPattern, wy / nearestPattern, out float nr, out float ng, out float nb);
							float fade = 0.5f * Smooth(softWidth, 0, edge);
							cr += (nr - cr) * fade;
							cg += (ng - cg) * fade;
							cb += (nb - cb) * fade;
						}

						// a resource is painted into the ground of its cell: a field, a pond, a seam, a glade
						int rci = (int)MathF.Floor((float)wx / this.cell), rcj = (int)MathF.Floor((float)wy / this.cell);
						int rfi = rci - featureLeft, rfj = rcj - featureTop;

						if (rfi >= 0 && rfj >= 0 && rfi < featureWidth && rfj < featureWidth && resources[(rfj * featureWidth) + rfi])
						{
							int kind = grid.At(rci, rcj);
							kind = kind == River ? Grassland : kind;

							if (this.PaintResourceGround(kind, wx, wy, rci, rcj, ground, shallow, ocean, sx, sy, ref cr, ref cg, ref cb))
								water[i] = (byte)(kind == Arctic ? 1 : 2);
						}

						// a narrow cream beach inside the coast line; below hills and mountains a shore of grey rock
						float beach = Smooth(coastLine + beachWidth, coastLine + (beachWidth * 0.3f), coast) * 0.6f;
						bool rocky = own == Mountains || own == Hills;
						float rock = rocky ? 0.8f + (0.4f * this.Noise(wx, wy, 2.5f * k, 41)) : 1;
						cr += ((rocky ? 150 * rock : 232) - cr) * beach;
						cg += ((rocky ? 146 * rock : 220) - cg) * beach;
						cb += ((rocky ? 136 * rock : 186) - cb) * beach;

						// rivers: the painted river strip, bent along each course; without one, a ribbon of blue inside a fine
						// ink line, lighter on the bank towards the light (top left) and deeper on the other, with a broken white
						// line of flowing water along the light side
						float d = rivers.Locate(wx, wy, out float along);

						if (strip != null)
						{
							// the river swells and narrows a little along its course
							float half = riverStripWidth / 2 * (0.82f + (0.36f * this.Noise(wx, wy, 70 * k, 43)));

							// a pale bank outside a fine ink line, as the mockup draws its rivers
							if (d < half + riverInk + riverBank + 1)
							{
								float bank = Smooth(half + riverInk + riverBank + 0.5f, half + riverInk + riverBank - 0.5f, d) * 0.75f;
								cr += (232 - cr) * bank;
								cg += (224 - cg) * bank;
								cb += (196 - cb) * bank;
								float ink = Smooth(half + riverInk + 0.5f, half + riverInk - 0.5f, d) * 0.92f;
								cr += (30 - cr) * ink;
								cg += (32 - cg) * ink;
								cb += (40 - cb) * ink;
							}

							if (d < half + 1)
							{
								// the strip from its middle out to one bank: both banks are alike, so either side of the course
								// takes the same half
								float v = ((strip.Height - 1) / 2f) + (d / half * (strip.Height / 2f));
								float u = (along - MathF.Floor(along)) * strip.Width;
								SampleRepeating(strip, u, v, out float pr, out float pg, out float pb, out float pa);
								// the water ends cleanly at the ink line
								float inside = Smooth(half + 0.5f, half - 0.5f, d);
								pr *= inside;
								pg *= inside;
								pb *= inside;
								pa *= inside;
								cr = pr + (cr * (1 - pa));
								cg = pg + (cg * (1 - pa));
								cb = pb + (cb * (1 - pa));

								if (pa > 0.5f)
									water[i] = 2;
							}
						}
						else if (d < riverWidth + riverLine + 1)
						{
							float inLine = Smooth(riverWidth + riverLine + 0.5f, riverWidth + riverLine - 0.5f, d);
							float inCore = Smooth(riverWidth + 0.5f, riverWidth - 0.5f, d);
							cr += (30 - cr) * inLine * 0.9f;
							cg += (34 - cg) * inLine * 0.9f;
							cb += (44 - cb) * inLine * 0.9f;

							if (inCore > 0)
							{
								// which bank: the distance grows towards the lower right on the lower right bank
								float side = Math.Clamp((rivers.Distance(wx + k, wy + k) - d) / (1.4f * k), -1, 1);
								float across = Math.Clamp(side * d / riverWidth, -1, 1);
								float t = (across + 1) / 2;
								float br = 120 + ((50 - 120) * t), bg = 170 + ((96 - 170) * t), bb = 214 + ((160 - 214) * t);
								float flow = Smooth(0.75f * k, 0.25f * k, MathF.Abs(across + 0.45f) * riverWidth)
									* Smooth(0.5f, 0.62f, this.Noise(wx, wy, 5 * k, 35));
								br += (236 - br) * flow;
								bg += (236 - bg) * flow;
								bb += (226 - bb) * flow;
								cr += (br - cr) * inCore;
								cg += (bg - cg) * inCore;
								cb += (bb - cb) * inCore;
							}

							if (inCore > 0.5f)
								water[i] = 2;
						}
					}

					// a crisp ink line on the land side of the coast
					if (!isWater)
					{
						float ink = Smooth(coastLine + 0.5f, coastLine - 0.5f, coast - 0.5f) * 0.9f;
						cr += (40 - cr) * ink;
						cg += (34 - cg) * ink;
						cb += (28 - cb) * ink;
					}

					r[i] = cr;
					g[i] = cg;
					b[i] = cb;
				}
			});

			this.PaintScenery(cx, cy, r, g, b, water, size, grid, rivers);

			// the paper: colours lean towards cream and lose a little saturation, with a faint fibrous grain
			uint[] pixels = new uint[size * size];

			Rows(size, parallel, py =>
			{
				for (int px = 0; px < size; px++)
				{
					int i = (py * size) + px;
					int wx = x0 + px, wy = y0 + py;
					float grain = 1 + ((this.Noise(wx, wy, 3 * k, 10) - 0.5f) * 0.05f);
					float l = ((r[i] * 0.3f) + (g[i] * 0.59f) + (b[i] * 0.11f)) * this.paper * 0.5f;
					float rr = ((r[i] * (1 - this.paper)) + (((236 * 0.5f) + l) * this.paper * 0.9f)) * grain;
					float gg = ((g[i] * (1 - this.paper)) + (((228 * 0.5f) + l) * this.paper * 0.9f)) * grain;
					float bb = ((b[i] * (1 - this.paper)) + (((204 * 0.5f) + l) * this.paper * 0.9f)) * grain;
					uint alpha = water[i] == 1 ? GBitmap.SeaAlpha : water[i] == 2 ? GBitmap.RiverAlpha : 0xff000000u;
					pixels[i] = alpha | ((uint)Math.Clamp(rr, 0, 255) << 16) | ((uint)Math.Clamp(gg, 0, 255) << 8) | (uint)Math.Clamp(bb, 0, 255);
				}
			});

			return pixels;
		}

		// Bilinear sample of a texture at a fractional position, repeating
		private static void Fetch(Texture t, float x, float y, out float r, out float g, out float b)
		{
			int s = t.Size;
			int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
			float fx = x - x0, fy = y - y0;
			int xa = ((x0 % s) + s) % s, ya = ((y0 % s) + s) % s;
			int xb = (xa + 1) % s, yb = (ya + 1) % s;
			int i00 = (ya * s) + xa, i10 = (ya * s) + xb, i01 = (yb * s) + xa, i11 = (yb * s) + xb;
			float w00 = (1 - fx) * (1 - fy), w10 = fx * (1 - fy), w01 = (1 - fx) * fy, w11 = fx * fy;
			r = (t.R[i00] * w00) + (t.R[i10] * w10) + (t.R[i01] * w01) + (t.R[i11] * w11);
			g = (t.G[i00] * w00) + (t.G[i10] * w10) + (t.G[i01] * w01) + (t.G[i11] * w11);
			b = (t.B[i00] * w00) + (t.B[i10] * w10) + (t.B[i01] * w01) + (t.B[i11] * w11);
		}

		/// <summary>
		/// Runs row(0 .. count - 1), in parallel on threads of its own. Not on the thread pool: the game's timer runs there,
		/// and its callbacks can be waiting for the graphics lock that the painting thread holds.
		/// </summary>
		private static void Rows(int count, bool parallel, Action<int> row)
		{
			int threads = parallel ? Math.Clamp(Environment.ProcessorCount, 1, 8) : 1;
			int next = -1;

			void Work()
			{
				int i;

				while ((i = Interlocked.Increment(ref next)) < count)
					row(i);
			}

			Thread[] helpers = new Thread[threads - 1];

			for (int t = 0; t < helpers.Length; t++)
			{
				helpers[t] = new Thread(Work) { IsBackground = true, Name = "Painted map rows" };
				helpers[t].Start();
			}

			Work();

			foreach (Thread helper in helpers)
				helper.Join();
		}

		private Texture TextureOr(string name)
		{
			if (this.textures.TryGetValue(name, out Texture? texture))
				return texture;

			// a missing texture shows as a dull grey-green, so the rest of the painting still works
			int size = this.cell * TextureCells;
			texture = new Texture { Size = size, R = new float[size * size], G = new float[size * size], B = new float[size * size] };
			Array.Fill(texture.R, 70f);
			Array.Fill(texture.G, 80f);
			Array.Fill(texture.B, 50f);
			this.textures[name] = texture;

			return texture;
		}

		private int TextureIndex(Texture texture, int x, int y)
		{
			int s = texture.Size;

			return ((((y % s) + s) % s) * s) + (((x % s) + s) % s);
		}

		/// <summary>
		/// The river courses a print chunk can show, worked out once per chunk. In each river cell the river curves
		/// smoothly from the middle of one side to the middle of another (a quadratic curve bent by the cell centre); a
		/// spring starts thin at the centre; into the sea the river runs on to the sea cell's centre, under the coast. Every
		/// point of a course is then moved by a slow wobble and a quicker one, so the river swings in S-bends, while its
		/// width stays even. Points on the sides of cells move the same way in both cells, so courses join without breaks.
		/// </summary>
		private sealed class RiverCourses
		{
			private readonly int left, top, width, cell;
			// per cell, its courses: x, y, a width adjustment (added to the distance) and the position along the course in
			// lengths of the river strip, per point
			private readonly List<float[]>?[] courses;
			// per cell, the box around all its courses: left, top, right, bottom
			private readonly float[] boxes;

			/// <param name="period">The length of the river strip on the map, in pixels; each course is a whole number of
			/// strip lengths long, so the strip runs on from one course into the next</param>
			public RiverCourses(PaintedMap map, TerrainGrid grid, int cx, int cy, float period)
			{
				// as far as the scenery of a chunk reaches, plus a cell
				const int Reach = 4;
				this.cell = map.cell;
				this.left = (cx * ChunkCells) - Reach;
				this.top = (cy * ChunkCells) - Reach;
				this.width = ChunkCells + (2 * Reach);
				this.courses = new List<float[]>?[this.width * this.width];
				this.boxes = new float[this.width * this.width * 4];
				float k = this.cell / 64f, half = this.cell / 2f;
				Span<int> ways = stackalloc int[4];

				for (int j = this.top; j < this.top + this.width; j++)
				{
					for (int i = this.left; i < this.left + this.width; i++)
					{
						if (grid.At(i, j) != River)
							continue;

						List<float[]> list = new List<float[]>();
						float ax = (i * this.cell) + half, ay = (j * this.cell) + half;
						int count = 0;

						for (int w = 0; w < 4; w++)
						{
							int terrain = grid.At(i + Sides[w].X, j + Sides[w].Y);

							if (terrain != River && terrain != Water)
								continue;

							ways[count++] = w;

							if (terrain == Water)
								list.Add(Curve(ax + (Sides[w].X * half), ay + (Sides[w].Y * half), ax + (Sides[w].X * this.cell * 0.75f), ay + (Sides[w].Y * this.cell * 0.75f), ax + (Sides[w].X * this.cell), ay + (Sides[w].Y * this.cell), 0, 0));
						}

						if (count == 0)
							list.Add(new[] { ax, ay, -3 * k, 0 });
						else if (count == 1)
							list.Add(Curve(ax, ay, ax + (Sides[ways[0]].X * half * 0.5f), ay + (Sides[ways[0]].Y * half * 0.5f), ax + (Sides[ways[0]].X * half), ay + (Sides[ways[0]].Y * half), 2.5f * k, 0));

						if (count == 2)
						{
							list.Add(Curve(ax + (Sides[ways[0]].X * half), ay + (Sides[ways[0]].Y * half), ax, ay,
								ax + (Sides[ways[1]].X * half), ay + (Sides[ways[1]].Y * half), 0, 0));
						}
						else if (count > 2)
						{
							// a fork: every branch runs into the centre, bending on its way towards the others so it joins them
							// in a curve, and the branches meet in one point, not a loop
							for (int a = 0; a < count; a++)
							{
								float ex = ax + (Sides[ways[a]].X * half), ey = ay + (Sides[ways[a]].Y * half);
								float ox = 0, oy = 0;

								for (int c = 0; c < count; c++)
								{
									if (c != a)
									{
										ox += Sides[ways[c]].X;
										oy += Sides[ways[c]].Y;
									}
								}

								list.Add(Curve(ex, ey, ((ex + ax) / 2) + (ox * half * 0.35f), ((ey + ay) / 2) + (oy * half * 0.35f), ax, ay, 0, 0));
							}
						}

						foreach (float[] course in list)
						{
							for (int n = 0; n < course.Length; n += 4)
							{
								float x = course[n], y = course[n + 1];
								course[n] = x + ((map.Noise(x, y, 44 * k, 31) - 0.5f) * 20 * k) + ((map.Noise(x, y, 20 * k, 33) - 0.5f) * 12 * k);
								course[n + 1] = y + ((map.Noise(x, y, 44 * k, 32) - 0.5f) * 20 * k) + ((map.Noise(x, y, 20 * k, 34) - 0.5f) * 12 * k);
							}

							// the position along the course, stretched a little to a whole number of strip lengths
							float length = 0;

							for (int n = 4; n < course.Length; n += 4)
							{
								length += PaintedMap.Distance(course[n - 4], course[n - 3], course[n], course[n + 1]);
								course[n + 3] = length;
							}

							float lengths = Math.Max(1, MathF.Round(length / period));

							for (int n = 4; n < course.Length; n += 4)
								course[n + 3] = (length > 0) ? course[n + 3] / length * lengths : 0;
						}

						int index = ((j - this.top) * this.width) + (i - this.left);
						this.courses[index] = list;
						float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;

						foreach (float[] course in list)
						{
							for (int n = 0; n < course.Length; n += 4)
							{
								minX = Math.Min(minX, course[n]);
								maxX = Math.Max(maxX, course[n]);
								minY = Math.Min(minY, course[n + 1]);
								maxY = Math.Max(maxY, course[n + 1]);
							}
						}

						this.boxes[index * 4] = minX;
						this.boxes[(index * 4) + 1] = minY;
						this.boxes[(index * 4) + 2] = maxX;
						this.boxes[(index * 4) + 3] = maxY;
					}
				}
			}

			// a quadratic curve from p to q bent towards c, as points with a width adjustment going from `from` to `to`
			private static float[] Curve(float px, float py, float cx, float cy, float qx, float qy, float from, float to)
			{
				const int Pieces = 12;
				float[] points = new float[(Pieces + 1) * 4];

				for (int n = 0; n <= Pieces; n++)
				{
					float t = n / (float)Pieces, u = 1 - t;
					points[n * 4] = (u * u * px) + (2 * u * t * cx) + (t * t * qx);
					points[(n * 4) + 1] = (u * u * py) + (2 * u * t * cy) + (t * t * qy);
					points[(n * 4) + 2] = from + ((to - from) * t);
				}

				return points;
			}

			/// <summary>
			/// Distance from a pixel to the nearest river course, less the course's width adjustment there
			/// </summary>
			public float Distance(float x, float y) => this.Locate(x, y, out _);

			/// <summary>
			/// Pixels further than this from every course are not measured: Locate returns MaxValue for them
			/// </summary>
			public float Reach { get; set; } = float.MaxValue;

			/// <summary>
			/// Distance from a pixel to the nearest river course, less the course's width adjustment there, and the
			/// position along that course, in strip lengths
			/// </summary>
			public float Locate(float x, float y, out float along)
			{
				int ci = (int)MathF.Floor(x / this.cell), cj = (int)MathF.Floor(y / this.cell);
				float best = float.MaxValue;
				along = 0;

				for (int j = cj - 1; j <= cj + 1; j++)
				{
					for (int i = ci - 1; i <= ci + 1; i++)
					{
						if (i < this.left || j < this.top || i >= this.left + this.width || j >= this.top + this.width)
							continue;

						int index = ((j - this.top) * this.width) + (i - this.left);
						List<float[]>? list = this.courses[index];

						// nothing of this cell's rivers comes near enough to matter
						if (list == null || x < this.boxes[index * 4] - this.Reach || y < this.boxes[(index * 4) + 1] - this.Reach ||
							x > this.boxes[(index * 4) + 2] + this.Reach || y > this.boxes[(index * 4) + 3] + this.Reach)
							continue;

						foreach (float[] c in list)
						{
							if (c.Length == 4)
							{
								float d = MathF.Sqrt(((x - c[0]) * (x - c[0])) + ((y - c[1]) * (y - c[1]))) + c[2];

								if (d < best)
								{
									best = d;
									along = 0;
								}

								continue;
							}

							for (int n = 4; n < c.Length; n += 4)
							{
								float ax = c[n - 4], ay = c[n - 3], bx = c[n], by = c[n + 1];
								float dx = bx - ax, dy = by - ay, length = (dx * dx) + (dy * dy);
								float t = length > 0 ? Math.Clamp((((x - ax) * dx) + ((y - ay) * dy)) / length, 0, 1) : 0;
								float ex = x - (ax + (t * dx)), ey = y - (ay + (t * dy));
								float d = MathF.Sqrt((ex * ex) + (ey * ey)) + c[n - 2] + ((c[n + 2] - c[n - 2]) * t);

								if (d < best)
								{
									best = d;
									along = c[n - 1] + ((c[n + 3] - c[n - 1]) * t);
								}
							}
						}
					}
				}

				return best;
			}
		}

		private static readonly (int X, int Y)[] Sides = { (0, -1), (1, 0), (0, 1), (-1, 0) };

		/// <summary>
		/// Distance from a point to the river lines of the cells around it: each river cell joins its centre to the
		/// centres of neighbouring river and ocean cells
		/// </summary>
		private float RiverDistance(Func<int, int, int> terrainFn, float x, float y, int ci, int cj)
		{
			float best = float.MaxValue;
			float half = this.cell / 2f;

			for (int dj = -1; dj <= 1; dj++)
			{
				for (int di = -1; di <= 1; di++)
				{
					int i = ci + di, j = cj + dj;

					if (terrainFn(i, j) != River)
						continue;

					(float ax, float ay) = this.RiverCentre(terrainFn, i, j);
					bool joined = false;

					foreach ((int nx, int ny) in new[] { (0, -1), (1, 0), (0, 1), (-1, 0) })
					{
						int terrain = terrainFn(i + nx, j + ny);

						if (terrain == River || terrain == Water)
						{
							(float bx, float by) = terrain == River ? this.RiverCentre(terrainFn, i + nx, j + ny) :
								(((i + nx) * this.cell) + half, ((j + ny) * this.cell) + half);
							best = Math.Min(best, SegmentDistance(x, y, ax, ay, bx, by));
							joined = true;
						}
					}

					if (!joined)
						best = Math.Min(best, MathF.Sqrt(((x - ax) * (x - ax)) + ((y - ay) * (y - ay))) - (3 * this.cell / 64f));
				}
			}

			return best;
		}

		private (float X, float Y) RiverCentre(Func<int, int, int> terrainFn, int i, int j)
		{
			float half = this.cell / 2f, x = (i * this.cell) + half, y = (j * this.cell) + half;
			int R(int di, int dj) => terrainFn(i + di, j + dj) == River ? 1 : 0;
			int down = R(0, -1) + R(0, 1) + R(0, -2) + R(0, 2), across = R(-1, 0) + R(1, 0) + R(-2, 0) + R(2, 0);

			if (down > across)
			{
				int right = R(1, 0) * Math.Max(R(1, -1), R(1, 1)), left = R(-1, 0) * Math.Max(R(-1, -1), R(-1, 1));
				x += (right - left) * half;
			}
			else if (across > down)
			{
				int below = R(0, 1) * Math.Max(R(-1, 1), R(1, 1)), above = R(0, -1) * Math.Max(R(-1, -1), R(1, -1));
				y += (below - above) * half;
			}

			return (x, y);
		}

		private static float SegmentDistance(float x, float y, float ax, float ay, float bx, float by)
		{
			float dx = bx - ax, dy = by - ay, length = (dx * dx) + (dy * dy);
			// two halves of a wide river share their point: no segment, and no division by zero
			float t = length > 0 ? Math.Clamp((((x - ax) * dx) + ((y - ay) * dy)) / length, 0, 1) : 0;
			float ex = x - (ax + (t * dx)), ey = y - (ay + (t * dy));

			return MathF.Sqrt((ex * ex) + (ey * ey));
		}

		/// <summary>
		/// Trees, hills and mountains of the cells around a chunk, back to front, each with a soft shadow to the lower right.
		/// A print (with the chunk's terrain grid) has no shadows, never draws over the sea, fades the feet of hills and
		/// mountains into the ground, and builds forest and jungle from tree crowns packed edge to edge, wherever its
		/// rounded terrain is forest or jungle, so the crowns make the scalloped edge of the wood.
		/// </summary>
		private void PaintScenery(int cx, int cy, float[] r, float[] g, float[] b, byte[] water, int size, TerrainGrid? grid = null, RiverCourses? rivers = null, byte[]? covered = null)
		{
			List<Placement> placements = new List<Placement>();
			float k = this.cell / 64f;

			// art reaches about two cells up and up to two cells sideways (clusters of peaks), so cells beyond the chunk are
			// placed too
			for (int j = (cy * ChunkCells) - 1; j < ((cy + 1) * ChunkCells) + 3; j++)
			{
				if (j < 0 || j >= MapHeight)
					continue;

				for (int i = (cx * ChunkCells) - 3; i < ((cx + 1) * ChunkCells) + 3; i++)
				{
					int terrain = this.Terrain(i, j);

					// the same cell always gets the same art, whichever chunk paints it
					Random random = new Random((WrapX(i) * 7919) + (j * 104729) + 17);
					float left = i * this.cell, top = j * this.cell;

					if (grid != null)
					{
						this.PlacePrintCell(placements, grid, rivers, random, i, j, terrain, left, top);
						continue;
					}

					switch (terrain)
					{
						case Forest:
							for (int n = 0; n < 26; n++)
								this.Place(placements, "trees", random, left + Range(random, 0, 64) * k, top + Range(random, 8, 66) * k, Range(random, 28, 40) * k);
							break;

						case Jungle:
							for (int n = 0; n < 12; n++)
								this.Place(placements, "jungle", random, left + Range(random, 0, 64) * k, top + Range(random, 10, 66) * k, Range(random, 28, 38) * k);
							break;

						case Hills:
							for (int n = 0; n < 2; n++)
								this.Place(placements, "hills", random, left + Range(random, 16, 48) * k, top + Range(random, 30, 60) * k, Range(random, 36, 46) * k, grid != null);
							break;

						case Mountains:
							this.Place(placements, "mountains", random, left + Range(random, 26, 38) * k, top + Range(random, 54, 62) * k, Range(random, 78, 96) * k, grid != null);
							break;

						case Grassland:
						case River:
							// a print scatters more, smaller trees over open land
							if (this.print)
							{
								for (int n = random.Next(4); n >= 0; n--)
								{
									if (random.NextDouble() < 0.6)
										this.Place(placements, "trees", random, left + Range(random, 6, 58) * k, top + Range(random, 12, 62) * k, Range(random, 17, 24) * k);
								}
							}
							else if (random.NextDouble() < 0.25)
							{
								this.Place(placements, "trees", random, left + Range(random, 8, 56) * k, top + Range(random, 14, 60) * k, Range(random, 22, 28) * k);
							}
							break;
					}
				}
			}

			int x0 = cx * size, y0 = cy * size;

			// a print, and scenery laid out like one, has no cast shadows
			if (!this.print && !this.layout)
			{
				foreach (Placement p in placements)
					DrawShadow(p, r, g, b, size, x0, y0);
			}

			placements.Sort((a, c) => a.Base.CompareTo(c.Base));

			// tree crowns seen from above: all their shadows first, then the crowns, back to front
			if (placements.Any(p => p.Crown))
			{
				foreach (Placement p in placements)
					if (p.Crown)
						this.DrawCrownShadow(p, r, g, b, water, size, x0, y0);

				foreach (Placement p in placements)
					if (p.Crown)
						this.DrawCrown(p, r, g, b, water, size, x0, y0);

				placements.RemoveAll(p => p.Crown);
			}

			foreach (Placement p in placements)
				DrawSprite(p, r, g, b, water, size, x0, y0, (grid == null || !this.print) ? null : (x, y) => this.PrintTerrain(grid, x, y) == Water, covered);
		}

		/// <summary>
		/// The scenery of one cell of a print. A resource or hut of the cell stands in the middle of it, a little forward,
		/// and is drawn with the trees and peaks, back to front: a wood opens a clearing around it, hills and mountains
		/// step back so it stands at their foot, and open land keeps its trees out of the way.
		/// </summary>
		private void PlacePrintCell(List<Placement> placements, TerrainGrid grid, RiverCourses? rivers, Random random, int i, int j, int terrain, float left, float top)
		{
			float k = this.cell / 64f;

			// a city's cell is cleared of trees, hills and peaks, so the town stands on its own ground
			if ((this.Feature(i, j) & City) != 0)
				return;

			int rawFeat = this.Feature(i, j);
			int feature = this.PaintsFeatures ? rawFeat : 0;
			bool hasSpecial = (rawFeat & SpecialResource) != 0;
			bool goldMt = !this.print && hasSpecial && terrain == Mountains && this.HasSpriteSet("mountains_gold");
			bool coalHill = !this.print && hasSpecial && terrain == Hills && this.HasSpriteSet("hills_coal");
			bool gemJungle = !this.print && hasSpecial && terrain == Jungle && this.HasSpriteSet("jungle_gems");

			float fx = left + (32 * k), fy = top + (gemJungle ? 46 * k : 50 * k);
			float clear = (feature != 0) ? 22 * k : gemJungle ? 19 * k : 0;
			// hills and peaks beside and in front of a resource or hut stay low enough not to hide it
			bool besideFeature = this.PaintsFeatures && (this.Feature(i - 1, j) != 0 || this.Feature(i + 1, j) != 0 ||
				this.Feature(i - 1, j - 1) != 0 || this.Feature(i, j - 1) != 0 || this.Feature(i + 1, j - 1) != 0);
			// nor do peaks below, beside, or above a city crowd into its walls or clearing
			bool cityLeft = (this.Feature(i - 1, j) & City) != 0, cityRight = (this.Feature(i + 1, j) & City) != 0;
			bool cityAbove = (this.Feature(i, j - 1) & City) != 0, cityBelow = (this.Feature(i, j + 1) & City) != 0;
			bool cityDiagSouth = ((this.Feature(i - 1, j - 1) | this.Feature(i + 1, j - 1)) & City) != 0;
			float lower = besideFeature ? 0.55f : (cityLeft || cityRight || cityAbove) ? 0.55f : cityDiagSouth ? 0.65f : cityBelow ? 0.78f : 1f;

			// hills and peaks by the water step back from it and stand a little lower, so they rise from a rocky shore
			// rather than hang over the water; likewise step back slightly from neighboring city boundaries
			bool waterLeft = this.Terrain(i - 1, j) == Water, waterRight = this.Terrain(i + 1, j) == Water;
			bool waterBelow = this.Terrain(i, j + 1) == Water;
			float awayX = ((waterLeft ? 9 : 0) - (waterRight ? 9 : 0)) * k, awayY = waterBelow ? -9 * k : 0;
			float cityAwayX = ((cityLeft ? 10 : 0) - (cityRight ? 10 : 0)) * k;
			float cityAwayY = ((cityAbove ? 12 : 0) - (cityBelow ? 8 : 0)) * k;
			float pushX = awayX + cityAwayX, pushY = awayY + cityAwayY;
			float shore = (waterLeft || waterRight || waterBelow) ? 0.82f : 1;

			if (this.topDown)
			{
				switch (terrain)
				{
					case Forest:
					case Jungle:
						// the canopy is the terrain's own painted ground; only a resource's picture stands on it
						// a resource painted into the terrain's own picture still has its picture, or it would not show
						if (gemJungle)
							this.Place(placements, "jungle_gems", random, fx + (pushX * 0.4f), top + (48 * k) + (pushY * 0.4f), Range(random, 42, 48) * k * shore, true, 0, 0, false);
						break;

					case Hills:
						if (coalHill)
							this.Place(placements, "hills_coal", random, left + (Range(random, 29, 35) * k) + (pushX * 0.6f), top + (Range(random, 52, 57) * k) + (pushY * 0.6f),
								Range(random, 48, 55) * k * Math.Max(lower, 0.85f) * shore, true, 0, 0, false);
						break;

					case Mountains:
						if (!goldMt && this.HasSpriteSet("peaks"))
							this.PlacePeaks(placements, random, i, j, left, top, lower * shore);

						if (goldMt)
							this.Place(placements, "mountains_gold", random, left + (Range(random, 29, 35) * k) + pushX, top + (Range(random, 58, 62) * k) + pushY,
								Range(random, 62, 68) * k * Math.Max(lower, 0.90f) * shore, true, Range(random, -0.02f, 0.03f), Range(random, -0.04f, 0.03f), false, maxWidth: 78 * k);
						break;

					case Grassland:
					case Plains:
					{
						// open land seen from high above: lone trees scattered over it and the odd copse
						bool grass = terrain == Grassland;
						// plains stay open: trees there only blur them with grassland
						int lone = 0;

						for (int n = 0; n < lone; n++)
						{
							float x = left + (Range(random, 2, 62) * k), y = top + (Range(random, 2, 62) * k);

							if (Distance(x, y, fx, fy) >= clear && (rivers == null || rivers.Distance(x, y) > 6 * k))
								this.PlaceCrown(placements, random, x, y, Range(random, 2.6f, 3.8f) * k, Grassland);
						}

						if (!grass && random.NextDouble() < 0 && Distance(left + (32 * k), top + (32 * k), fx, fy) >= clear * 1.2f)
						{
							float copseX = left + (Range(random, 18, 46) * k), copseY = top + (Range(random, 18, 46) * k);

							for (int n = 5 + random.Next(6); n >= 0; n--)
							{
								float x = copseX + (Range(random, -9, 9) * k), y = copseY + (Range(random, -9, 9) * k);

								if (rivers == null || rivers.Distance(x, y) > 7 * k)
									this.PlaceCrown(placements, random, x, y, Range(random, 3.5f, 5.5f) * k, Grassland);
							}
						}
						break;
					}
				}
			}
			else switch (terrain)
			{
				case Forest:
				case Jungle:
					this.PlaceCanopy(placements, grid, random, terrain, left, top, fx, fy, clear);
					if (gemJungle)
						this.Place(placements, "jungle_gems", random, fx + (pushX * 0.4f), top + (48 * k) + (pushY * 0.4f), Range(random, 42, 48) * k * shore, true, 0, 0, false);
					break;

				case Hills:
					if (coalHill)
					{
						this.Place(placements, "hills_coal", random, left + (Range(random, 29, 35) * k) + (pushX * 0.6f), top + (Range(random, 52, 57) * k) + (pushY * 0.6f),
							Range(random, 48, 55) * k * Math.Max(lower, 0.85f) * shore, true, 0, 0, false);
					}
					else
					{
						for (int n = 0; n < 2; n++)
						{
							float baseY = (feature != 0) ? top + Range(random, 24, 36) * k : top + Range(random, 30, 60) * k;
							this.Place(placements, "hills", random, left + (Range(random, 16, 48) * k) + pushX, baseY + pushY,
								Range(random, 36, 46) * k * Math.Max(lower, 0.75f) * shore, true);
						}
					}
					break;

				case Mountains:
					if (feature != 0)
						this.Place(placements, "mountains", random, left + (Range(random, 26, 38) * k) + pushX, top + (Range(random, 38, 44) * k) + pushY, Range(random, 64, 74) * k * shore, true);
					else if (!this.print && goldMt)
					{
						// Keep Gold Mountains strictly anchored to a single square so resource ownership is unmistakable
						float mainX = Range(random, 29, 35);
						float mainBaseY = top + (Range(random, 58, 62) * k) + pushY;
						float mainH = Range(random, 62, 68) * k * Math.Max(lower, 0.90f) * shore;
						this.Place(placements, "mountains_gold", random, left + (mainX * k) + pushX, mainBaseY,
							mainH, true, Range(random, -0.02f, 0.03f), Range(random, -0.04f, 0.03f), false, maxWidth: 78 * k);
					}
					else if (!this.print)
					{
						// Blend adjacent normal mountains into connected alpine ridges without spilling across non-mountain or resource tiles
						bool mtL = grid.At(i - 1, j) == Mountains, mtR = grid.At(i + 1, j) == Mountains;
						bool mtU = grid.At(i, j - 1) == Mountains, mtD = grid.At(i, j + 1) == Mountains;
						bool goldL = mtL && (this.Feature(i - 1, j) & SpecialResource) != 0;
						bool goldR = mtR && (this.Feature(i + 1, j) & SpecialResource) != 0;
						bool goldU = mtU && (this.Feature(i, j - 1) & SpecialResource) != 0;
						bool goldD = mtD && (this.Feature(i, j + 1) & SpecialResource) != 0;

						float mainX = Range(random, (mtL && !goldL) ? 24 : 28, (mtR && !goldR) ? 40 : 36);
						float mainBaseY = top + (Range(random, (mtU && !goldU) ? 54 : 57, (mtD && !goldD) ? 64 : 61) * k) + pushY;
						float mainH = Range(random, 58, 70) * k * lower * shore;
						float mainMaxW = (mtL && mtR && !goldL && !goldR) ? 94 * k : ((mtL || mtR) && !goldL && !goldR) ? 84 * k : 72 * k;
						float mainMaxAspect = (mtL && mtR && !goldL && !goldR) ? 1.85f : 1.45f;
						this.Place(placements, "mountains", random, left + (mainX * k) + pushX, mainBaseY,
							mainH, true, Range(random, -0.05f, 0.06f), Range(random, -0.10f, 0.06f), true, maxWidth: mainMaxW, maxAspect: mainMaxAspect);

						if (mtR && !cityRight)
						{
							this.Place(placements, "mountains", random, left + (Range(random, 60, 68) * k) + (awayX * 0.5f) + cityAwayX, top + (Range(random, 54, 60) * k) + pushY,
								(goldR ? Range(random, 30, 36) : Range(random, 42, 54)) * k * lower * shore, true, Range(random, -0.05f, 0.07f), Range(random, -0.13f, -0.02f),
								maxWidth: (goldR ? 50 : 78) * k, maxAspect: goldR ? 1.45f : 2.1f);
						}

						if (mtD && !goldD && !cityBelow)
						{
							this.Place(placements, "mountains", random, left + (Range(random, 24, 40) * k) + pushX, top + (Range(random, 76, 84) * k) + cityAwayY,
								Range(random, 40, 50) * k * lower * shore, true, Range(random, -0.05f, 0.06f), Range(random, -0.12f, -0.02f),
								maxWidth: 68 * k, maxAspect: 1.65f);
						}

						if (mtR && mtD && grid.At(i + 1, j + 1) == Mountains && !goldR && !goldD &&
							(this.Feature(i + 1, j + 1) & SpecialResource) == 0 && !cityRight && !cityBelow)
						{
							this.Place(placements, "mountains", random, left + (Range(random, 58, 70) * k) + (awayX * 0.5f) + cityAwayX, top + (Range(random, 74, 82) * k) + cityAwayY,
								Range(random, 36, 46) * k * lower * shore, true, Range(random, -0.05f, 0.06f), Range(random, -0.14f, -0.03f),
								maxWidth: 60 * k, maxAspect: 1.6f);
						}
					}
					else
						this.Place(placements, "mountains", random, left + (Range(random, 26, 38) * k) + pushX, top + (Range(random, 54, 62) * k) + pushY, Range(random, 78, 96) * k * lower * shore, true);
					break;

				case Grassland:
				case River when this.print:
					// a print scatters more, smaller trees over open land
					if (!this.print)
					{
						// painted scenery laid out like a print has small trees, so open land gets an occasional tight copse
						// instead of lone dark specks scattered across the grass
						if (random.NextDouble() < 0.16 && Distance(left + (32 * k), top + (40 * k), fx, fy) >= clear * 1.2f)
						{
							float copseX = left + (Range(random, 18, 46) * k), copseY = top + (Range(random, 26, 50) * k);

							for (int n = 3 + random.Next(3); n >= 0; n--)
							{
								float x = copseX + (Range(random, -8, 8) * k), baseY = copseY + (Range(random, -5, 5) * k);

								if ((rivers == null || rivers.Distance(x, baseY) > 7 * k) && this.CityClearing(x, baseY - (14 * k), k) <= 0.22f &&
									this.RoadDistance(x, baseY - (10 * k), i, j) > 9.5f * k)
									this.Place(placements, "trees", random, x, baseY, Range(random, 30, 38) * k);
							}
						}

						break;
					}

					for (int n = random.Next(4); n >= 0; n--)
					{
						float x = left + Range(random, 6, 58) * k, baseY = top + Range(random, 12, 62) * k, height = Range(random, 17, 24) * k;

						// nor do they stand in a river
						bool dry = rivers == null || rivers.Distance(x, baseY) > 7 * k;

						if (random.NextDouble() < 0.6 && Distance(x, baseY, fx, fy) >= clear * 1.2f && dry)
							this.Place(placements, "trees", random, x, baseY, height);
					}
					break;
			}

			bool both = (feature & (SpecialResource | MinorTribeHut)) == (SpecialResource | MinorTribeHut);

			if ((feature & SpecialResource) != 0 && terrain < ResourceNames.Length)
				this.PlaceResource(placements, random, terrain, fx - (both ? 12 * k : 0), fy);

			if ((feature & MinorTribeHut) != 0)
				this.PlaceFeature(placements, "hut", fx + (both ? 12 * k : 0), fy + (2 * k), 42 * k, false);
		}

		private static float Distance(float ax, float ay, float bx, float by)
		{
			return MathF.Sqrt(((ax - bx) * (ax - bx)) + ((ay - by) * (ay - by)));
		}

		// a resource or hut standing at (x, baseY), fitted into a box of the given size
		private void PlaceFeature(List<Placement> placements, string name, float x, float baseY, float box, bool onWater, bool flip = false, float fade = 0)
		{
			if (!this.features.TryGetValue(name, out Sprite? sprite))
				return;

			float height = box * Math.Min(1, (float)sprite.Height / sprite.Width), width = height * sprite.Width / sprite.Height;
			placements.Add(new Placement { Sprite = sprite, X = x - (width / 2), Base = baseY, Width = width, Height = height, OnWater = onWater, Flip = flip, Fade = fade });
		}

		/// <summary>
		/// Paints the ground of a resource into a pixel of its cell: an oasis pond, a wheat field, a greener pasture, a
		/// glade in a wood, a coal seam, gold flecks, a breathing hole in the ice, cut peat beds. Returns true where the
		/// pixel became water.
		/// </summary>
		private bool PaintResourceGround(int kind, int wx, int wy, int ci, int cj, Texture[] ground, Texture shallow, Texture ocean,
			float sx, float sy, ref float cr, ref float cg, ref float cb)
		{
			float k = this.cell / 64f;
			float dx = wx - ((ci * this.cell) + (32 * k)), dy = wy - ((cj * this.cell) + (46 * k));
			float wobble = (this.Noise(wx, wy, 9 * k, 45) - 0.5f) * 0.3f;
			// distance from the middle of the design, 1 at its edge, in an organic oval
			float Oval(float radius) => (MathF.Sqrt((dx * dx) + (dy * dy * 1.6f)) / (radius * k)) + wobble;
			void Mix(float r, float g, float b, float amount, ref float cr, ref float cg, ref float cb)
			{
				cr += (r - cr) * amount;
				cg += (g - cg) * amount;
				cb += (b - cb) * amount;
			}
			float ink;

			switch (kind)
			{
				case Desert:
				{
					// an oasis: a pond with an inked rim inside a ring of green
					float d = Oval(13);
					Fetch(ground[Grassland], wx / this.pattern, wy / this.pattern, out float gr, out float gg, out float gb);
					Mix(gr, gg, gb, Smooth(1.9f, 1.2f, d) * 0.85f, ref cr, ref cg, ref cb);
					ink = Smooth(1.12f, 1.0f, d) * Smooth(0.82f, 0.94f, d);

					if (d < 1)
					{
						Fetch(shallow, sx, sy, out float sr, out float sg, out float sb);
						cr = sr;
						cg = sg;
						cb = sb;
					}

					Mix(40, 40, 46, ink * 0.85f, ref cr, ref cg, ref cb);
					return d < 0.95f;
				}

				case Grassland:
				{
					// wheat: two strips of field side by side, one ripe gold and one still green-gold, ploughed in clear
					// furrows, with soft rounded corners and a fine ink edge
					bool second = dx > 0;
					float sdx = second ? dx - (12 * k) : dx + (12 * k);
					float edge = MathF.Pow(MathF.Pow(MathF.Abs(sdx) / (11 * k), 4) + MathF.Pow(MathF.Abs(dy) / (14 * k), 4), 0.25f) + (wobble * 0.3f);
					float furrow = MathF.Abs(MathF.Sin((second ? wy : wx) / (1.6f * k)));
					float field = Smooth(1.0f, 0.92f, edge);
					if (second)
						Mix(214 - (60 * furrow), 196 - (46 * furrow), 104 - (40 * furrow), field * 0.95f, ref cr, ref cg, ref cb);
					else
						Mix(232 - (66 * furrow), 188 - (58 * furrow), 78 - (36 * furrow), field * 0.95f, ref cr, ref cg, ref cb);
					Mix(70, 58, 36, Smooth(1.08f, 1.0f, edge) * Smooth(0.9f, 1.0f, edge) * 0.75f, ref cr, ref cg, ref cb);
					return false;
				}

				case Plains:
				{
					// horses: a greener pasture
					Fetch(ground[Grassland], wx / this.pattern, wy / this.pattern, out float gr, out float gg, out float gb);
					Mix(gr, gg, gb, Smooth(1.5f, 0.8f, Oval(22)) * 0.75f, ref cr, ref cg, ref cb);
					return false;
				}

				case Forest:
				case Jungle:
				{
					// a glade in the wood, open grass where the bird or the gems are
					Fetch(ground[Grassland], wx / this.pattern, wy / this.pattern, out float gr, out float gg, out float gb);
					Mix(gr, gg, gb, Smooth(1.3f, 0.8f, Oval(20)) * 0.9f, ref cr, ref cg, ref cb);
					return false;
				}

				case Hills:
				{
					// coal: a grey-brown outcrop of bare earth around the coal heap, a few dark stones in it
					float d = Oval(17);
					Mix(118, 104, 86, Smooth(1.2f, 0.75f, d) * 0.7f, ref cr, ref cg, ref cb);
					Mix(48, 44, 42, Smooth(0.74f, 0.8f, this.Noise(wx, wy, 1.6f * k, 46)) * Smooth(1.0f, 0.7f, d) * 0.8f, ref cr, ref cg, ref cb);
					return false;
				}

				case Mountains:
				{
					// gold: bright flecks in the rock at the foot
					float fleck = Smooth(0.66f, 0.74f, this.Noise(wx, wy, 1.8f * k, 47)) * Smooth(1.2f, 0.6f, Oval(18));
					Mix(232, 188, 70, fleck, ref cr, ref cg, ref cb);
					return false;
				}

				case Arctic:
				{
					// a seal's breathing hole in the ice
					float d = Oval(8);

					if (d < 1)
					{
						Fetch(ocean, sx, sy, out float or, out float og, out float ob);
						cr = or;
						cg = og;
						cb = ob;
					}

					Mix(40, 44, 56, Smooth(1.15f, 1.0f, d) * Smooth(0.85f, 0.97f, d) * 0.8f, ref cr, ref cg, ref cb);
					return d < 0.95f;
				}

				case Swamp:
				{
					// peat: dark cut beds in rows
					float bed = Smooth(1.0f, 0.9f, Math.Max(MathF.Abs(dx) / (20 * k), MathF.Abs(dy) / (14 * k)) + (wobble * 0.4f));
					float row = MathF.Abs(MathF.Sin(wy / (3.2f * k))) > 0.35f ? 1 : 0.55f;
					Mix(78 * row, 56 * row, 38 * row, bed * 0.85f, ref cr, ref cg, ref cb);
					return false;
				}
			}

			return false;
		}

		/// <summary>
		/// The objects of a resource for a print: drawn smaller and without an outline, alone or in a small group, so
		/// they belong to the painted ground of their design
		/// </summary>
		private void PlaceResource(List<Placement> placements, Random random, int terrain, float fx, float fy)
		{
			float k = this.cell / 64f;
			string name = "res_" + ResourceNames[terrain];

			switch (terrain)
			{
				case Desert:
					// palms around the pond
					this.Place(placements, "jungle", random, fx - (17 * k), fy - (2 * k), 30 * k);
					this.Place(placements, "jungle", random, fx + (15 * k), fy + (3 * k), 26 * k);
					break;

				case Grassland:
					// the field is the design; a sheaf stands at its edge
					this.PlaceFeature(placements, name, fx + (16 * k), fy - (6 * k), 18 * k, false);
					break;

				case Plains:
				case Tundra:
					// a small herd
					this.PlaceFeature(placements, name, fx - (9 * k), fy - (4 * k), 24 * k, false);
					this.PlaceFeature(placements, name, fx + (11 * k), fy + (2 * k), 21 * k, false, true);
					break;

				case Water:
					// a shoal seen through the water
					this.PlaceFeature(placements, name, fx - (8 * k), fy - (6 * k), 26 * k, true, false, 0.45f);
					this.PlaceFeature(placements, name, fx + (10 * k), fy + (4 * k), 22 * k, true, true, 0.45f);
					break;

				case Arctic:
					this.PlaceFeature(placements, name, fx + (12 * k), fy - (4 * k), 24 * k, false);
					break;

				default:
					this.PlaceFeature(placements, name, fx, fy - (4 * k), 24 * k, false);
					break;
			}
		}

		private static float RoadHash(int x, int y, int seed)
		{
			uint h = (uint)((x * 374761393) ^ (y * 668265263) ^ (seed * 1442695041));
			h = (h ^ (h >> 13)) * 1274126177u;
			return ((h ^ (h >> 16)) & 0xffff) / 65535f;
		}

		/// <summary>
		/// Where a road passes through a cell, as an offset in pixels from the cell's centre (cell `c` pixels across): a
		/// little off centre in open land, so roads meander; at a city's centre. The map window draws roads through these
		/// points and the painted map cuts their lanes along the same course.
		/// </summary>
		public static (float X, float Y) RoadWaypoint(int wx, int y, bool city, bool mountains, float c)
		{
			if (city)
				return (0, 0);

			float u = c / 64f;
			return ((RoadHash(wx, y, 1) - 0.5f) * 7f * u, ((RoadHash(wx, y, 2) - 0.5f) * 7f * u) + (mountains ? 3.5f * u : 0));
		}

		/// <summary>
		/// How far a road between two cells bows sideways at its middle, in pixels; (wx, y) is the cell the road is drawn
		/// from, the one whose neighbour (nwx, ny) lies east, south-west, south or south-east of it
		/// </summary>
		public static float RoadBend(int wx, int y, int nwx, int ny, float c) => (RoadHash((wx * 3) + nwx, (y * 5) + ny, 3) - 0.5f) * 16f * (c / 64f);

		private (float X, float Y) RoadPoint(int x, int y)
		{
			int wx = WrapX(x);
			(float ox, float oy) = RoadWaypoint(wx, y, (this.Feature(x, y) & City) != 0, this.Terrain(x, y) == Mountains, this.cell);
			return ((x * this.cell) + (this.cell * 0.5f) + ox, (y * this.cell) + (this.cell * 0.5f) + oy);
		}

		/// <summary>
		/// Distance in world pixels from point (x, y) to the road/rail segments crossing cell (ci, cj) and its neighbors
		/// </summary>
		private float RoadDistance(float x, float y, int ci, int cj)
		{
			if (this.FeatureAt == null || this.print)
				return float.MaxValue;

			float half = this.cell * 0.5f;
			float best = float.MaxValue;
			(int Dx, int Dy)[] allDirs = { (-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1) };

			for (int dj = -1; dj <= 1; dj++)
			{
				for (int di = -1; di <= 1; di++)
				{
					int rx = ci + di, ry = cj + dj;
					if ((this.Feature(rx, ry) & RoadCorridor) == 0)
						continue;

					float ax = (rx * this.cell) + half, ay = (ry * this.cell) + half;
					bool linked = false;

					foreach ((int dx, int dy) in allDirs)
					{
						int nx = rx + dx, ny = ry + dy;
						if ((this.Feature(nx, ny) & (RoadCorridor | City)) == 0)
							continue;

						if (dx != 0 && dy != 0)
						{
							if ((this.Feature(rx + dx, ry) & (RoadCorridor | City)) != 0 ||
								(this.Feature(rx, ry + dy) & (RoadCorridor | City)) != 0)
								continue;
						}

						if (this.topDown)
						{
							// the same bowed course the map window draws the road along, from the cell it is drawn from
							bool fwd = (dx == 1 && dy == 0) || (dx == -1 && dy == 1) || (dx == 0 && dy == 1) || (dx == 1 && dy == 1);
							int fx0 = fwd ? rx : nx, fy0 = fwd ? ry : ny, tx0 = fwd ? nx : rx, ty0 = fwd ? ny : ry;
							(float pax, float pay) = this.RoadPoint(fx0, fy0);
							(float pbx, float pby) = this.RoadPoint(tx0, ty0);
							float mx = (pax + pbx) / 2, my = (pay + pby) / 2;
							float perpX = -(pby - pay), perpY = pbx - pax, plen = MathF.Sqrt((perpX * perpX) + (perpY * perpY));

							if (plen > 1)
							{
								float bendBy = RoadBend(WrapX(fx0), fy0, WrapX(tx0), ty0, this.cell);
								mx += perpX / plen * bendBy;
								my += perpY / plen * bendBy;
							}

							float lx = pax, ly = pay;
							for (int st = 1; st <= 10; st++)
							{
								float t = st / 10f, it = 1 - t;
								float qx = (it * it * pax) + (2 * it * t * mx) + (t * t * pbx), qy = (it * it * pay) + (2 * it * t * my) + (t * t * pby);
								best = Math.Min(best, SegmentDistance(x, y, lx, ly, qx, qy));
								lx = qx;
								ly = qy;
							}
						}
						else
						{
							best = Math.Min(best, SegmentDistance(x, y, ax, ay, (nx * this.cell) + half, (ny * this.cell) + half));
						}

						linked = true;
					}

					if (!linked)
						best = Math.Min(best, Distance(x, y, ax, ay));
				}
			}

			return best;
		}

		/// <summary>
		/// The crowns of a forest or jungle cell for a print: a jittered grid of trees, each kept only where the print's
		/// rounded terrain under its crown is the same wood, and none within `clear` of (fx, fy), where a resource or hut
		/// stands in a clearing
		/// </summary>
		/// <summary>
		/// A wood seen from above: its cell filled with overlapping tree crowns, and where it meets open land, crowns that
		/// thin out over the border into the land beside it, so a wood ends in scattered trees instead of an edge
		/// </summary>
		private void PlaceCrowns(List<Placement> placements, Random random, int i, int j, int terrain, float left, float top, float fx, float fy, float clear)
		{
			float k = this.cell / 64f;
			bool forest = terrain == Forest;
			// a dense canopy of many small crowns, as a forest looks from high above
			int across = forest ? 10 : 9;
			float step = 64f / across;

			for (int v = 0; v < across; v++)
			{
				for (int u = 0; u < across; u++)
				{
					float x = left + (((u + 0.5f) * step) + (Range(random, -0.4f, 0.4f) * step)) * k;
					float y = top + (((v + 0.5f) * step) + (Range(random, -0.4f, 0.4f) * step)) * k;

					// seen from above, a forest's own ground is its canopy: only a few crowns stand out of it
					if (Distance(x, y, fx, fy) >= clear && !this.topDown)
						this.PlaceCrown(placements, random, x, y, Range(random, forest ? 3.4f : 3.8f, forest ? 5.2f : 5.8f) * k, terrain);
				}
			}

			// over each border with open land, trees thinning out
			(int Dx, int Dy)[] sides = { (1, 0), (-1, 0), (0, 1), (0, -1) };

			foreach ((int dx, int dy) in sides)
			{
				int beside = this.Terrain(i + dx, j + dy);

				if (beside == terrain || beside == Water || beside == Mountains || beside == Forest || beside == Jungle ||
					(this.Feature(i + dx, j + dy) & City) != 0)
					continue;

				for (int n = 0; n < 7; n++)
				{
					float along = Range(random, 4, 60), into = Range(random, 0, 1);
					into *= into;
					float depth = into * 26;

					if (random.NextDouble() > 1 - (into * 0.85f))
						continue;

					float x = left + ((dx != 0) ? (dx > 0 ? 64 + depth : -depth) : along) * k;
					float y = top + ((dy != 0) ? (dy > 0 ? 64 + depth : -depth) : along) * k;
					this.PlaceCrown(placements, random, x, y, Range(random, 2.2f, 3.2f) * (1 - (into * 0.3f)) * k, terrain);
				}
			}
		}

		// a crown's soft shadow on the ground, cast down and to the right by the light from the top left
		private void DrawCrownShadow(Placement p, float[] r, float[] g, float[] b, byte[] water, int size, int x0, int y0)
		{
			float radius = p.Width, sx = p.X + (radius * 0.45f), sy = p.Base + (radius * 0.55f), reach = radius * 1.15f;
			int left = Math.Max(0, (int)(sx - reach) - x0), right = Math.Min(size - 1, (int)(sx + reach) - x0);
			int top = Math.Max(0, (int)(sy - reach) - y0), bottom = Math.Min(size - 1, (int)(sy + reach) - y0);

			for (int py = top; py <= bottom; py++)
			{
				for (int px = left; px <= right; px++)
				{
					int i = (py * size) + px;

					if (water[i] == 1)
						continue;

					float d = Distance(px + x0, py + y0, sx, sy) / reach;
					float dark = (p.Width < 5 * this.cell / 64f ? 0.24f : 0.34f) * Smooth(1, 0.55f, d);
					r[i] *= 1 - dark;
					g[i] *= 1 - (dark * 0.9f);
					b[i] *= 1 - (dark * 0.75f);
				}
			}
		}

		// a tree crown seen from above: a leafy dome lit from the top left, its edge broken into clumps of leaves
		private void DrawCrown(Placement p, float[] r, float[] g, float[] b, byte[] water, int size, int x0, int y0)
		{
			float k = this.cell / 64f;
			float radius = p.Width, reach = radius * 1.25f;
			Texture leaves = this.TextureOr((int)p.Height == Jungle ? "jungle" : "forest");
			bool palm = this.topDown && (int)p.Height == Jungle;
			int left = Math.Max(0, (int)(p.X - reach) - x0), right = Math.Min(size - 1, (int)(p.X + reach) - x0);
			int top = Math.Max(0, (int)(p.Base - reach) - y0), bottom = Math.Min(size - 1, (int)(p.Base + reach) - y0);

			for (int py = top; py <= bottom; py++)
			{
				for (int px = left; px <= right; px++)
				{
					int i = (py * size) + px;

					if (water[i] == 1)
						continue;

					float wx = px + x0, wy = py + y0;
					float ux = (wx - p.X) / radius, uy = (wy - p.Base) / radius;
					float d = MathF.Sqrt((ux * ux) + (uy * uy));
					// clumps of leaves break the outline
					float clumps = this.Noise(wx, wy, Math.Min(3.2f * k, radius * 0.7f), 61) - 0.5f;
					float edge = 1 + (clumps * 0.38f);
					float frond = 0;

					if (palm)
					{
						// a palm from above: a star of fronds from its crown, each frond's spine lit
						float ang = MathF.Atan2(uy, ux);
						frond = MathF.Abs(MathF.Cos((ang * 3.5f) + (p.Warmth * 3)));
						edge = 0.45f + (0.65f * frond) + (clumps * 0.12f);
					}

					if (d >= edge)
						continue;

					float cover = Smooth(edge, edge - 0.12f, d);
					// a dome: lit where it faces the top left, darker round its far side, a little darker at its rim
					float z = MathF.Sqrt(Math.Max(0, 1 - Math.Min(1, d * d)));
					float lit = Math.Clamp((((-ux * 0.55f) - (uy * 0.6f)) + (z * 0.58f)) / 1.0f, 0, 1);
					float leafy = 0.82f + (0.36f * this.Noise(wx, wy, 2.2f * k, 62));
					// a lone small tree on open land is lit all through and reads lighter than a deep forest
					float lone = Smooth(4.2f * k, 3.4f * k, radius);
					float tone = (0.62f + (0.72f * lit)) * leafy * (1 + p.Shade) * (1 + (0.45f * lone));

					if (palm)
						tone *= (0.62f + (0.3f * frond)) * (0.75f + (0.25f * Smooth(0.05f, 0.3f, d)));
					int ti = this.TextureIndex(leaves, (int)wx, (int)wy);
					if (this.topDown)
						tone *= 0.82f;

					float lr = leaves.R[ti] * tone * (1 + (0.06f * p.Warmth)), lg = leaves.G[ti] * tone, lb = leaves.B[ti] * tone * (1 - (0.05f * p.Warmth));

					if ((int)p.Height == Grassland)
					{
						// a lone tree on open land: a warmer olive green than the deep forest
						lr = (lr * 1.25f) + (14 * tone);
						lg *= 1.02f;
						lb *= 0.62f;
					}
					r[i] += (lr - r[i]) * cover;
					g[i] += (lg - g[i]) * cover;
					b[i] += (lb - b[i]) * cover;
				}
			}
		}

		/// <summary>
		/// The peaks of a mountain cell seen from above, painted pictures from the theme's peaks set placed as one system with
		/// what is built on the cell: they stand clear of a road or railroad through it (which then runs along the valley
		/// between them), leave the flank where a mine goes in, and stand back round a fortress; and they reach a little
		/// over the cell's edges, so neighbouring mountain cells join into a range
		/// </summary>
		private void PlacePeaks(List<Placement> placements, Random random, int i, int j, float left, float top, float scale)
		{
			float k = this.cell / 64f;
			int feature = this.Feature(i, j);
			bool road = (feature & RoadCorridor) != 0, mine = (feature & MineSite) != 0, fort = (feature & FortressSite) != 0;
			// spots kept free: the mine's mouth and the fortress, where the map window draws them
			var keep = new List<(float X, float Y, float R)>();

			if (mine)
				keep.Add((left + (36 * k), top + (36 * k), 15 * k));

			if (fort)
				keep.Add((left + (32 * k), top + (36 * k), 27 * k));

			// candidate summits on a jittered grid across the cell, the biggest first
			var spots = new List<(float X, float Y)>();
			for (int v = 0; v < 3; v++)
				for (int u = 0; u < 3; u++)
					spots.Add((left + ((10 + (u * 22) + Range(random, -5, 5)) * k), top + ((10 + (v * 22) + Range(random, -5, 5)) * k)));

			spots = spots.OrderBy(sp => Distance(sp.X, sp.Y, left + (32 * k), top + (32 * k)) + Range(random, 0, 10 * k)).ToList();
			var placed = new List<(float X, float Y, float R)>();
			bool open = !road && !mine && !fort;

			foreach ((float x, float y) in spots)
			{
				// one big massif fills an open cell; with something built on it, smaller peaks gather round it
				// a big peak and smaller ones overlapping it, so the cell's mountains and its neighbours' join into a range
				float radius = (placed.Count == 0 ? (open ? Range(random, 28, 33) : Range(random, 19, 24)) : Range(random, 16, 22)) * k * scale;
				float roadRoom = road ? this.RoadDistance(x, y, i, j) : float.MaxValue;

				if (roadRoom < radius * 0.7f)
					radius = roadRoom / 0.7f;

				if (keep.Any(kp => Distance(x, y, kp.X, kp.Y) < kp.R + (radius * 0.55f)))
					continue;

				if (radius < 9 * k || placed.Any(pp => Distance(x, y, pp.X, pp.Y) < (pp.R + radius) * 0.4f))
					continue;

				this.Place(placements, "peaks", random, x, y + radius, radius * 2, allowFlip: false);
				placed.Add((x, y, radius));

				if (placed.Count >= (open ? 3 : 4))
					break;
			}
		}

		private void PlaceCrown(List<Placement> placements, Random random, float x, float y, float radius, int terrain)
		{
			placements.Add(new Placement { Crown = true, X = x, Base = y, Width = radius, Height = terrain, Shade = Range(random, -0.12f, 0.12f), Warmth = Range(random, -1, 1) });
		}

		private void PlaceCanopy(List<Placement> placements, TerrainGrid grid, Random random, int terrain, float left, float top, float fx, float fy, float clear)
		{
			float k = this.cell / 64f;
			bool forest = terrain == Forest;
			int across = forest ? 4 : 3;
			float step = 64f / across;
			int ci = (int)MathF.Floor((left + (32 * k)) / this.cell);
			int cj = (int)MathF.Floor((top + (32 * k)) / this.cell);

			for (int v = 0; v < across; v++)
			{
				for (int u = 0; u < across; u++)
				{
					float x = left + (((u + 0.5f) * step) + Range(random, -0.35f, 0.35f) * step) * k;
					float baseY = top + (((v + 0.5f) * step) + Range(random, -0.3f, 0.3f) * step + (forest ? 12 : 14)) * k;
					float height = (forest ? Range(random, 28, 36) : Range(random, 30, 40)) * k;

					// the crown, not the trunk, has to stand on the wood, and step back cleanly around city clearings and road lanes
					float cityClear = this.CityClearing(x, baseY - (height * 0.35f), k);
					if (cityClear > 0.30f)
						continue;
					if (cityClear > 0.04f)
						height *= Lerp(1f, 0.72f, (cityClear - 0.04f) / 0.26f);

					float roadDist = this.RoadDistance(x, baseY - (height * 0.35f), ci, cj);
					if (roadDist < 9.5f * k)
						continue;
					if (roadDist < 15f * k)
						height *= Lerp(0.74f, 1f, (roadDist - (9.5f * k)) / (5.5f * k));

					if (this.PrintTerrain(grid, x, baseY - (height * 0.55f)) == terrain && Distance(x, baseY, fx, fy) >= clear)
						this.Place(placements, forest ? "trees" : "jungle", random, x, baseY, height);
				}
			}
		}

		private static float Range(Random random, float from, float to)
		{
			return from + ((float)random.NextDouble() * (to - from));
		}

		private void Place(List<Placement> placements, string set, Random random, float x, float baseY, float height, bool fadeFoot = false, float warmth = 0, float shade = 0, bool allowFlip = true, float maxWidth = 0, float maxAspect = 0)
		{
			List<Sprite> list = this.sprites.TryGetValue(set, out List<Sprite>? s) ? s : new List<Sprite>();
			if (list.Count == 0)
				return;

			if (maxAspect > 0)
			{
				List<Sprite> compact = list.Where(sp => sp.Width <= sp.Height * maxAspect).ToList();
				if (compact.Count > 0)
					list = compact;
			}

			int choice = random.Next(list.Count);
			bool flip = allowFlip && random.NextDouble() < 0.5;
			Sprite sprite = list[choice];

			if (set == "trees")
				height *= this.treeScale;
			else if (set == "jungle")
				height *= this.jungleScale < 0 ? this.treeScale : this.jungleScale;

			float width = height * sprite.Width / sprite.Height;
			if (maxWidth > 0 && width > maxWidth)
			{
				float shrink = maxWidth / width;
				width = maxWidth;
				height *= shrink;
			}

			placements.Add(new Placement { Sprite = sprite, X = x - (width / 2), Base = baseY, Width = width, Height = height, Flip = flip, FadeFoot = fadeFoot, Warmth = warmth, Shade = shade });
		}

		/// <param name="waterAt">In a print, whether a print's terrain is water at a point: a column of the sprite whose foot
		/// stands in water is not drawn over water, so the sprite may rise against water behind it but not hang over water
		/// beside or in front of it</param>
		private static void DrawSprite(Placement p, float[] r, float[] g, float[] b, byte[] water, int size, int x0, int y0, Func<float, float, bool>? waterAt = null, byte[]? covered = null)
		{
			Sprite s = p.Sprite;
			float top = p.Base - p.Height;
			int fromX = Math.Max(0, (int)MathF.Floor(p.X) - x0), toX = Math.Min(size, (int)MathF.Ceiling(p.X + p.Width) - x0);
			int fromY = Math.Max(0, (int)MathF.Floor(top) - y0), toY = Math.Min(size, (int)MathF.Ceiling(p.Base) - y0);
			bool print = waterAt != null;
			// per column: does its foot (a little above the base, where the drawing is solid) stand in water
			bool[]? wetFoot = null;

			if (print && !p.OnWater && toX > fromX)
			{
				wetFoot = new bool[toX - fromX];

				for (int px = fromX; px < toX; px++)
					wetFoot[px - fromX] = waterAt!(px + x0 + 0.5f, p.Base - (p.Height * 0.12f));
			}

			for (int py = fromY; py < toY; py++)
			{
				float v = ((py + y0 + 0.5f - top) / p.Height * s.Height) - 0.5f;

				for (int px = fromX; px < toX; px++)
				{
					float u = ((px + x0 + 0.5f - p.X) / p.Width * s.Width) - 0.5f;

					if (p.Flip)
						u = s.Width - 1 - u;

					Sample(s, u, v, out float sr, out float sg, out float sb, out float sa);
					int i = (py * size) + px;

					// in a print, what stands on land does not reach into the water beside or in front of it, though its top may
					// rise against the water behind; the feet of hills and mountains fade into the ground
					if (wetFoot != null && water[i] != 0 && wetFoot[px - fromX])
						continue;

					if (p.FadeFoot)
					{
						float foot = Smooth(s.Height, s.Height * 0.72f, v);
						sr *= foot;
						sg *= foot;
						sb *= foot;
						sa *= foot;
					}

					if (p.Warmth != 0 || p.Shade != 0)
					{
						float sMul = 1 + p.Shade;
						sr = Math.Clamp(sr * sMul * (1 + p.Warmth), 0, 255);
						sg = Math.Clamp(sg * sMul, 0, 255);
						sb = Math.Clamp(sb * sMul * (1 - p.Warmth), 0, 255);
					}

					if (p.Fade > 0)
					{
						sr *= 1 - p.Fade;
						sg *= 1 - p.Fade;
						sb *= 1 - p.Fade;
						sa *= 1 - p.Fade;
					}

					if (sa <= 0.003f)
						continue;

					r[i] = sr + (r[i] * (1 - sa));
					g[i] = sg + (g[i] * (1 - sa));
					b[i] = sb + (b[i] * (1 - sa));

					// how much of the pixel is a picture now, which the light on the ground then leaves alone
					if (covered != null)
						covered[i] = (byte)Math.Max(covered[i], Math.Clamp(sa * 255, 0, 255));

					// water under a tree or peak stays still
					if (sa > 0.35f)
						water[i] = 0;
				}
			}
		}

		// The sprite's silhouette, flattened to half its height and shifted right, darkens the ground
		private static void DrawShadow(Placement p, float[] r, float[] g, float[] b, int size, int x0, int y0)
		{
			Sprite s = p.Sprite;
			float height = p.Height * 0.5f;
			float left = p.X + (p.Width * 0.2f), bottom = p.Base + (p.Height * 0.03f), top = bottom - height;
			int fromX = Math.Max(0, (int)MathF.Floor(left) - x0), toX = Math.Min(size, (int)MathF.Ceiling(left + p.Width) - x0);
			int fromY = Math.Max(0, (int)MathF.Floor(top) - y0), toY = Math.Min(size, (int)MathF.Ceiling(bottom) - y0);

			for (int py = fromY; py < toY; py++)
			{
				int sy = Math.Clamp((int)((py + y0 + 0.5f - top) / height * s.Height), 0, s.Height - 1);

				for (int px = fromX; px < toX; px++)
				{
					int sx = Math.Clamp((int)((px + x0 + 0.5f - left) / p.Width * s.Width), 0, s.Width - 1);

					if (p.Flip)
						sx = s.Width - 1 - sx;

					float k = s.Soft[(sy * s.Width) + sx] * 0.55f;

					if (k <= 0)
						continue;

					int i = (py * size) + px;
					r[i] *= 1 - k;
					g[i] *= 1 - k;
					b[i] *= 1 - k;
				}
			}
		}

		// Bilinear sample of premultiplied sprite colour, repeating across its width and transparent above and below it
		private static void SampleRepeating(Sprite s, float u, float v, out float r, out float g, out float b, out float a)
		{
			int iu = (int)MathF.Floor(u), iv = (int)MathF.Floor(v);
			float fu = u - iu, fv = v - iv;
			r = g = b = a = 0;

			for (int dy = 0; dy <= 1; dy++)
			{
				int y = iv + dy;

				if (y < 0 || y >= s.Height)
					continue;

				float wy = (dy == 0) ? 1 - fv : fv;

				for (int dx = 0; dx <= 1; dx++)
				{
					int x = (((iu + dx) % s.Width) + s.Width) % s.Width;
					float w = wy * ((dx == 0) ? 1 - fu : fu);
					int i = (y * s.Width) + x;
					r += s.R[i] * w;
					g += s.G[i] * w;
					b += s.B[i] * w;
					a += s.A[i] * w;
				}
			}
		}

		// Bilinear sample of premultiplied sprite colour, transparent outside the sprite
		private static void Sample(Sprite s, float u, float v, out float r, out float g, out float b, out float a)
		{
			int iu = (int)MathF.Floor(u), iv = (int)MathF.Floor(v);
			float fu = u - iu, fv = v - iv;
			r = g = b = a = 0;

			for (int dy = 0; dy <= 1; dy++)
			{
				int y = iv + dy;

				if (y < 0 || y >= s.Height)
					continue;

				float wy = (dy == 0) ? 1 - fv : fv;

				for (int dx = 0; dx <= 1; dx++)
				{
					int x = iu + dx;

					if (x < 0 || x >= s.Width)
						continue;

					float w = wy * ((dx == 0) ? 1 - fu : fu);
					int i = (y * s.Width) + x;
					r += s.R[i] * w;
					g += s.G[i] * w;
					b += s.B[i] * w;
					a += s.A[i] * w;
				}
			}
		}

		#endregion
	}
}
