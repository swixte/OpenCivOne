using System;
using System.Collections.Generic;
using System.IO;
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

		private readonly Func<int, int, int> terrainAt;
		private readonly int cell;
		private readonly int worldWidth;
		private readonly int worldHeight;
		private readonly Dictionary<string, Texture> textures = new Dictionary<string, Texture>();
		private readonly Dictionary<string, List<Sprite>> sprites = new Dictionary<string, List<Sprite>>();
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
			public const int Margin = 4;
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

			// every texture is in place before painting starts, so painting threads only read them
			foreach ((string Name, float Weight)[] parts in Ground)
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

			for (int y = (cy * ChunkCells) - 2; y < ((cy + 1) * ChunkCells) + 3; y++)
			{
				for (int x = (cx * ChunkCells) - 2; x < ((cx + 1) * ChunkCells) + 2; x++)
				{
					hash = (hash * 31) + this.Terrain(x, y);
				}
			}

			return hash;
		}

		private int WrapX(int x)
		{
			return ((x % MapWidth) + MapWidth) % MapWidth;
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
			int size = ChunkCells * this.cell;
			int x0 = cx * size, y0 = cy * size;
			float k = this.cell / 64f;
			float[] r = new float[size * size], g = new float[size * size], b = new float[size * size];

			float warpAmount = 14 * k, groundRadius = 13 * k, coastRadius = 5 * k, surfRadius = 24 * k, depthRadius = 70 * k;
			float riverWidth = 3.6f * k, bankWidth = 6.5f * k;

			Texture ocean = this.TextureOr("ocean"), shallow = this.TextureOr("shallow");
			(Texture Texture, float Weight)[][] ground = Array.ConvertAll(Ground, parts => Array.ConvertAll(parts, part => (this.TextureOr(part.Name), part.Weight)));
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
						// dark, rocky rim along the shore
						float rim = 1 - (0.55f * (1 - Smooth(0.5f, 0.85f, land)));
						r[i] = gr * rim;
						g[i] = gg * rim;
						b[i] = gb * rim;
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

			this.PaintScenery(cx, cy, r, g, b, size);

			// light: drifting cloud shadows and a slight warm grade on the highlights
			uint[] pixels = new uint[size * size];

			Rows(size, parallel, py =>
			{
				for (int px = 0; px < size; px++)
				{
					int i = (py * size) + px;
					int wx = x0 + px, wy = y0 + py;
					float cloud = Math.Clamp(((this.Noise(wx, wy, 160 * k, 7) * 0.7f) + (this.Noise(wx, wy, 60 * k, 8) * 0.3f) - 0.45f) * 2.2f, 0, 1);
					float shade = 1 - (cloud * 0.35f);
					float l = (r[i] + g[i] + b[i]) / 765f;
					float scale = shade * (0.92f + (0.16f * l));
					float rr = (r[i] * scale) + (6 * l) - (2 * (1 - l));
					float gg2 = (g[i] * scale) + (2 * l);
					float bb = (b[i] * scale) - (6 * l) + (4 * (1 - l));
					pixels[i] = 0xff000000u | ((uint)Math.Clamp(rr, 0, 255) << 16) | ((uint)Math.Clamp(gg2, 0, 255) << 8) | (uint)Math.Clamp(bb, 0, 255);
				}
			});

			return pixels;
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
		/// Trees, hills and mountains of the cells around a chunk, back to front, each with a soft shadow to the lower right
		/// </summary>
		private void PaintScenery(int cx, int cy, float[] r, float[] g, float[] b, int size)
		{
			List<Placement> placements = new List<Placement>();
			float k = this.cell / 64f;

			// art reaches about two cells up and one cell sideways, so cells beyond the chunk are placed too
			for (int j = (cy * ChunkCells) - 1; j < ((cy + 1) * ChunkCells) + 3; j++)
			{
				if (j < 0 || j >= MapHeight)
					continue;

				for (int i = (cx * ChunkCells) - 2; i < ((cx + 1) * ChunkCells) + 2; i++)
				{
					int terrain = this.Terrain(i, j);
					// the same cell always gets the same art, whichever chunk paints it
					Random random = new Random((WrapX(i) * 7919) + (j * 104729) + 17);
					float left = i * this.cell, top = j * this.cell;

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
								this.Place(placements, "hills", random, left + Range(random, 16, 48) * k, top + Range(random, 30, 60) * k, Range(random, 36, 46) * k);
							break;

						case Mountains:
							this.Place(placements, "mountains", random, left + Range(random, 26, 38) * k, top + Range(random, 54, 62) * k, Range(random, 78, 96) * k);
							break;

						case Grassland:
						case River:
							if (random.NextDouble() < 0.25)
								this.Place(placements, "trees", random, left + Range(random, 8, 56) * k, top + Range(random, 14, 60) * k, Range(random, 22, 28) * k);
							break;
					}
				}
			}

			int x0 = cx * size, y0 = cy * size;

			foreach (Placement p in placements)
				DrawShadow(p, r, g, b, size, x0, y0);

			placements.Sort((a, c) => a.Base.CompareTo(c.Base));

			foreach (Placement p in placements)
				DrawSprite(p, r, g, b, size, x0, y0);
		}

		private static float Range(Random random, float from, float to)
		{
			return from + ((float)random.NextDouble() * (to - from));
		}

		private void Place(List<Placement> placements, string set, Random random, float x, float baseY, float height)
		{
			List<Sprite> list = this.sprites.TryGetValue(set, out List<Sprite>? s) ? s : new List<Sprite>();
			int choice = random.Next(Math.Max(1, list.Count));
			bool flip = random.NextDouble() < 0.5;

			if (list.Count == 0)
				return;

			Sprite sprite = list[choice];
			float width = height * sprite.Width / sprite.Height;
			placements.Add(new Placement { Sprite = sprite, X = x - (width / 2), Base = baseY, Width = width, Height = height, Flip = flip });
		}

		private static void DrawSprite(Placement p, float[] r, float[] g, float[] b, int size, int x0, int y0)
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

					if (sa <= 0.003f)
						continue;

					int i = (py * size) + px;
					r[i] = sr + (r[i] * (1 - sa));
					g[i] = sg + (g[i] * (1 - sa));
					b[i] = sb + (b[i] * (1 - sa));
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
