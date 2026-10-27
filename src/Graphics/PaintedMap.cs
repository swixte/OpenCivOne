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

		public const int SpecialResource = 1, MinorTribeHut = 2;
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

			foreach (string set in new[] { "trees", "jungle", "hills", "mountains" })
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
						case "pattern": map.pattern = Math.Clamp(value, 0.25f, 8); break;
						case "seapattern": map.seaPattern = Math.Clamp(value, 0.25f, 8); break;
						case "paper": map.paper = Math.Clamp(value, 0, 1); break;
					}
				}
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

			return map;
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

					if (this.PaintsFeatures)
						hash = (hash * 7) + this.Feature(x, y);
				}
			}

			return hash;
		}

		private int WrapX(int x)
		{
			return ((x % MapWidth) + MapWidth) % MapWidth;
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
		/// Smooth value noise in [0, 1] with features about `size` pixels across, repeating around the world horizontally
		/// </summary>
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

			float warpAmount = 14 * k, groundRadius = 13 * k, coastRadius = 5 * k, surfRadius = 24 * k, depthRadius = 70 * k * this.shallows;
			float riverWidth = 3.6f * k, bankWidth = 6.5f * k;

			Texture ocean = this.TextureOr("ocean"), shallow = this.TextureOr("shallow");
			(Texture Texture, float Weight)[][] ground = Array.ConvertAll(this.groundParts, parts => Array.ConvertAll(parts, part => (this.TextureOr(part.Name), part.Weight)));
			TerrainGrid grid = new TerrainGrid(this, cx, cy);

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
					float qx = wx + ox, qy = wy + oy;
					int ci = (int)MathF.Floor(qx / this.cell), cj = (int)MathF.Floor(qy / this.cell);

					// ground and coast from the 3x3 cells around the warped position
					float gr = 0, gg = 0, gb = 0, land = 0, surfLand = 0;

					for (int dj = -1; dj <= 1; dj++)
					{
						float wyG = this.Weight(qy, cj + dj, groundRadius);
						float wyC = this.Weight(qy, cj + dj, coastRadius);
						float wyS = this.Weight(qy, cj + dj, surfRadius);

						for (int di = -1; di <= 1; di++)
						{
							int terrain = grid.At(ci + di, cj + dj);
							bool isLand = terrain != Water;
							float w = wyG * this.Weight(qx, ci + di, groundRadius);

							if (w > 0)
							{
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

					if (land >= 0.5f)
					{
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
						int oi = this.TextureIndex(ocean, wx, wy), si = this.TextureIndex(shallow, wx, wy);
						float wr = ocean.R[oi] + ((shallow.R[si] - ocean.R[oi]) * shallowness);
						float wg = ocean.G[oi] + ((shallow.G[si] - ocean.G[oi]) * shallowness);
						float wb = ocean.B[oi] + ((shallow.B[si] - ocean.B[oi]) * shallowness);

						// surf: a broken white line just off the coast, and fainter foam further out
						float foamNoise = (this.Noise(wx, wy, 12 * k, 5) * 0.7f) + (this.Noise(wx, wy, 4 * k, 6) * 0.3f);
						float surf = Smooth(0.2f, 0.34f, surfLand) * Smooth(0.5f, 0.4f, land) * Smooth(0.4f, 0.7f, foamNoise);
						surf += Smooth(0.08f, 0.14f, surfLand) * Smooth(0.2f, 0.14f, surfLand) * Smooth(0.6f, 0.8f, foamNoise) * 0.35f;
						surf = Math.Min(surf, 1) * 0.7f;
						r[i] = (wr * (1 - surf)) + (210 * surf);
						g[i] = (wg * (1 - surf)) + (216 * surf);
						b[i] = (wb * (1 - surf)) + (205 * surf);
						water[i] = 1;
					}

					// rivers wind through river cells and on into the sea
					if (land >= 0.5f)
					{
						float d = this.RiverDistance(grid, qx, qy, ci, cj);

						if (d < bankWidth)
						{
							if (d < riverWidth)
							{
								int si = this.TextureIndex(shallow, wx, wy);
								bool lit = this.RiverDistance(grid, qx - (2 * k), qy, ci, cj) >= riverWidth;
								float light = lit ? 1.35f : 1f;
								r[i] = ((shallow.R[si] * 0.7f) + 18) * light;
								g[i] = ((shallow.G[si] * 0.7f) + 21) * light;
								b[i] = ((shallow.B[si] * 0.7f) + 21) * light;
								water[i] = 2;
							}
							else
							{
								r[i] *= 0.5f;
								g[i] *= 0.5f;
								b[i] *= 0.5f;
							}
						}
					}
				}
			});

			this.PaintScenery(cx, cy, r, g, b, water, size);

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
						float range = shallowRange * (0.7f + (0.6f * this.Noise(wx, wy, 90 * k, 17)));
						float shallowness = Smooth(range, 0, depth);
						Fetch(ocean, sx, sy, out float or, out float og, out float ob);
						Fetch(shallow, sx, sy, out float sr, out float sg, out float sb);
						cr = or + ((sr - or) * shallowness);
						cg = og + ((sg - og) * shallowness);
						cb = ob + ((sb - ob) * shallowness);

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
						float ownPattern = this.terrainPattern[own] > 0 ? this.terrainPattern[own] : this.pattern;
						Fetch(ground[own], wx / ownPattern, wy / ownPattern, out cr, out cg, out cb);

						// terrains fade into each other like bokashi, half way each side of the border; forest and jungle are
						// edged by their tree crowns, drawn over the ground later
						if (nearest != own)
						{
							float nearestPattern = this.terrainPattern[nearest] > 0 ? this.terrainPattern[nearest] : this.pattern;
							Fetch(ground[nearest], wx / nearestPattern, wy / nearestPattern, out float nr, out float ng, out float nb);
							float fade = 0.5f * Smooth(softWidth, 0, edge);
							cr += (nr - cr) * fade;
							cg += (ng - cg) * fade;
							cb += (nb - cb) * fade;
						}

						// a narrow cream beach inside the coast line
						float beach = Smooth(coastLine + beachWidth, coastLine + (beachWidth * 0.3f), coast) * 0.6f;
						cr += (232 - cr) * beach;
						cg += (220 - cg) * beach;
						cb += (186 - cb) * beach;

						// rivers: the painted river strip, bent along each course; without one, a ribbon of blue inside a fine
						// ink line, lighter on the bank towards the light (top left) and deeper on the other, with a broken white
						// line of flowing water along the light side
						float d = rivers.Locate(wx, wy, out float along);

						if (strip != null)
						{
							float half = riverStripWidth / 2;

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
							// a fork: every branch runs into the centre, so the branches meet in one point, not a loop
							for (int a = 0; a < count; a++)
							{
								float ex = ax + (Sides[ways[a]].X * half), ey = ay + (Sides[ways[a]].Y * half);
								list.Add(Curve(ex, ey, (ex + ax) / 2, (ey + ay) / 2, ax, ay, 0, 0));
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
		private float RiverDistance(TerrainGrid grid, float x, float y, int ci, int cj)
		{
			float best = float.MaxValue;
			float half = this.cell / 2f;

			for (int dj = -1; dj <= 1; dj++)
			{
				for (int di = -1; di <= 1; di++)
				{
					int i = ci + di, j = cj + dj;

					if (grid.At(i, j) != River)
						continue;

					float ax = (i * this.cell) + half, ay = (j * this.cell) + half;
					bool joined = false;

					foreach ((int nx, int ny) in new[] { (0, -1), (1, 0), (0, 1), (-1, 0) })
					{
						int terrain = grid.At(i + nx, j + ny);

						if (terrain == River || terrain == Water)
						{
							best = Math.Min(best, SegmentDistance(x, y, ax, ay, ax + (nx * this.cell), ay + (ny * this.cell)));
							joined = true;
						}
					}

					if (!joined)
						best = Math.Min(best, MathF.Sqrt(((x - ax) * (x - ax)) + ((y - ay) * (y - ay))) - (3 * this.cell / 64f));
				}
			}

			return best;
		}

		private static float SegmentDistance(float x, float y, float ax, float ay, float bx, float by)
		{
			float dx = bx - ax, dy = by - ay;
			float t = Math.Clamp((((x - ax) * dx) + ((y - ay) * dy)) / ((dx * dx) + (dy * dy)), 0, 1);
			float ex = x - (ax + (t * dx)), ey = y - (ay + (t * dy));

			return MathF.Sqrt((ex * ex) + (ey * ey));
		}

		/// <summary>
		/// Trees, hills and mountains of the cells around a chunk, back to front, each with a soft shadow to the lower right.
		/// A print (with the chunk's terrain grid) has no shadows, never draws over the sea, fades the feet of hills and
		/// mountains into the ground, and builds forest and jungle from tree crowns packed edge to edge, wherever its
		/// rounded terrain is forest or jungle, so the crowns make the scalloped edge of the wood.
		/// </summary>
		private void PaintScenery(int cx, int cy, float[] r, float[] g, float[] b, byte[] water, int size, TerrainGrid? grid = null, RiverCourses? rivers = null)
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

			// a print has no cast shadows
			if (!this.print)
			{
				foreach (Placement p in placements)
					DrawShadow(p, r, g, b, size, x0, y0);
			}

			placements.Sort((a, c) => a.Base.CompareTo(c.Base));

			foreach (Placement p in placements)
				DrawSprite(p, r, g, b, water, size, x0, y0, grid != null);
		}

		/// <summary>
		/// The scenery of one cell of a print. A resource or hut of the cell stands in the middle of it, a little forward,
		/// and is drawn with the trees and peaks, back to front: a wood opens a clearing around it, hills and mountains
		/// step back so it stands at their foot, and open land keeps its trees out of the way.
		/// </summary>
		private void PlacePrintCell(List<Placement> placements, TerrainGrid grid, RiverCourses? rivers, Random random, int i, int j, int terrain, float left, float top)
		{
			float k = this.cell / 64f;
			int feature = this.PaintsFeatures ? this.Feature(i, j) : 0;
			float fx = left + (32 * k), fy = top + (50 * k), clear = (feature != 0) ? 22 * k : 0;
			// hills and peaks beside and in front of a resource or hut stay low enough not to hide it
			bool besideFeature = this.PaintsFeatures && (this.Feature(i - 1, j) != 0 || this.Feature(i + 1, j) != 0 ||
				this.Feature(i - 1, j - 1) != 0 || this.Feature(i, j - 1) != 0 || this.Feature(i + 1, j - 1) != 0);
			float lower = besideFeature ? 0.55f : 1;

			switch (terrain)
			{
				case Forest:
				case Jungle:
					this.PlaceCanopy(placements, grid, random, terrain, left, top, fx, fy, clear);
					break;

				case Hills:
					for (int n = 0; n < 2; n++)
					{
						float baseY = (feature != 0) ? top + Range(random, 24, 36) * k : top + Range(random, 30, 60) * k;
						this.Place(placements, "hills", random, left + Range(random, 16, 48) * k, baseY, Range(random, 36, 46) * k * Math.Max(lower, 0.75f), true);
					}
					break;

				case Mountains:
					if (feature != 0)
						this.Place(placements, "mountains", random, left + Range(random, 26, 38) * k, top + Range(random, 38, 44) * k, Range(random, 64, 74) * k, true);
					else
						this.Place(placements, "mountains", random, left + Range(random, 26, 38) * k, top + Range(random, 54, 62) * k, Range(random, 78, 96) * k * lower, true);
					break;

				case Grassland:
				case River:
					// a print scatters more, smaller trees over open land
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
				this.PlaceFeature(placements, "res_" + ResourceNames[terrain], fx - (both ? 12 * k : 0), fy, 38 * k, terrain == Water);

			if ((feature & MinorTribeHut) != 0)
				this.PlaceFeature(placements, "hut", fx + (both ? 12 * k : 0), fy + (2 * k), 42 * k, false);
		}

		private static float Distance(float ax, float ay, float bx, float by)
		{
			return MathF.Sqrt(((ax - bx) * (ax - bx)) + ((ay - by) * (ay - by)));
		}

		// a resource or hut standing at (x, baseY), fitted into a box of the given size
		private void PlaceFeature(List<Placement> placements, string name, float x, float baseY, float box, bool onWater)
		{
			if (!this.features.TryGetValue(name, out Sprite? sprite))
				return;

			float height = box * Math.Min(1, (float)sprite.Height / sprite.Width), width = height * sprite.Width / sprite.Height;
			placements.Add(new Placement { Sprite = sprite, X = x - (width / 2), Base = baseY, Width = width, Height = height, OnWater = onWater });
		}

		/// <summary>
		/// The crowns of a forest or jungle cell for a print: a jittered grid of trees, each kept only where the print's
		/// rounded terrain under its crown is the same wood, and none within `clear` of (fx, fy), where a resource or hut
		/// stands in a clearing
		/// </summary>
		private void PlaceCanopy(List<Placement> placements, TerrainGrid grid, Random random, int terrain, float left, float top, float fx, float fy, float clear)
		{
			float k = this.cell / 64f;
			bool forest = terrain == Forest;
			int across = forest ? 4 : 3;
			float step = 64f / across;

			for (int v = 0; v < across; v++)
			{
				for (int u = 0; u < across; u++)
				{
					float x = left + (((u + 0.5f) * step) + Range(random, -0.35f, 0.35f) * step) * k;
					float baseY = top + (((v + 0.5f) * step) + Range(random, -0.3f, 0.3f) * step + (forest ? 12 : 14)) * k;
					float height = (forest ? Range(random, 28, 36) : Range(random, 30, 40)) * k;

					// the crown, not the trunk, has to stand on the wood
					if (this.PrintTerrain(grid, x, baseY - (height * 0.55f)) == terrain && Distance(x, baseY, fx, fy) >= clear)
						this.Place(placements, forest ? "trees" : "jungle", random, x, baseY, height);
				}
			}
		}

		private static float Range(Random random, float from, float to)
		{
			return from + ((float)random.NextDouble() * (to - from));
		}

		private void Place(List<Placement> placements, string set, Random random, float x, float baseY, float height, bool fadeFoot = false)
		{
			List<Sprite> list = this.sprites.TryGetValue(set, out List<Sprite>? s) ? s : new List<Sprite>();
			int choice = random.Next(Math.Max(1, list.Count));
			bool flip = random.NextDouble() < 0.5;

			if (list.Count == 0)
				return;

			Sprite sprite = list[choice];
			float width = height * sprite.Width / sprite.Height;
			placements.Add(new Placement { Sprite = sprite, X = x - (width / 2), Base = baseY, Width = width, Height = height, Flip = flip, FadeFoot = fadeFoot });
		}

		private static void DrawSprite(Placement p, float[] r, float[] g, float[] b, byte[] water, int size, int x0, int y0, bool print = false)
		{
			Sprite s = p.Sprite;
			float top = p.Base - p.Height;
			int fromX = Math.Max(0, (int)MathF.Floor(p.X) - x0), toX = Math.Min(size, (int)MathF.Ceiling(p.X + p.Width) - x0);
			int fromY = Math.Max(0, (int)MathF.Floor(top) - y0), toY = Math.Min(size, (int)MathF.Ceiling(p.Base) - y0);

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

					// in a print, what stands on land does not reach into the sea in front of it, though its top may rise
					// against the sea behind; the feet of hills and mountains fade into the ground
					if (print && water[i] == 1 && !p.OnWater && v > s.Height * 0.7f)
						continue;

					if (p.FadeFoot)
					{
						float foot = Smooth(s.Height, s.Height * 0.8f, v);
						sr *= foot;
						sg *= foot;
						sb *= foot;
						sa *= foot;
					}

					if (sa <= 0.003f)
						continue;

					r[i] = sr + (r[i] * (1 - sa));
					g[i] = sg + (g[i] * (1 - sa));
					b[i] = sb + (b[i] * (1 - sa));

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
