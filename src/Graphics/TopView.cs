using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkiaSharp;

namespace OpenCivOne.Graphics
{
	/// <summary>
	/// The towns and units of a painted map, drawn top-down over the classic map window as the theme's map renders show
	/// them (themes/gouache/preview/compose.py): each town from the theme's ladder of painted towns by its size, with a
	/// size badge in its player's colour on its top-left corner and its name in a serif face under it, and each unit
	/// painted at one scale for all units, standing on a round base in its player's colour, like a miniature's. The
	/// ground, the water and everything else on the map stay the classic map's (the painted map), which shows wherever
	/// the view is clear. The art is the theme's hd/painted/objects/unit_NAME.png and hd/painted/towns/ladder.
	/// Unit types without painted art are left to the classic map.
	/// </summary>
	public sealed class TopView : IMapView
	{
		private const int MapWidth = 80, MapHeight = 50;

		// the special resource of each terrain, by terrain id, as the painted objects are named
		private static readonly string[] ResourceNames = { "oasis", "horses", "wheat", "pheasant", "coal", "gold", "caribou", "seal", "peat", "gems", "fish" };
		private const int Water = 10;

		private static readonly string[] UnitNames = { "settlers", "militia", "phalanx", "legion", "musketeers", "riflemen", "cavalry", "knights", "catapult",
			"cannon", "chariot", "armor", "mech_infantry", "artillery", "fighter", "bomber", "trireme", "sail", "frigate", "ironclad", "cruiser", "battleship",
			"submarine", "carrier", "transport", "nuclear", "diplomat", "caravan" };

		// as compose.py: men on foot stand on their feet, ships sit in the water by their hulls
		private static readonly HashSet<string> Foot = new HashSet<string> { "phalanx", "legion", "militia", "musketeers", "riflemen", "mech_infantry" };
		private static readonly HashSet<string> Ships = new HashSet<string> { "trireme", "sail", "frigate", "ironclad", "cruiser", "battleship", "submarine", "carrier", "transport" };
		// the sheet's pixels per cell: its largest unit, a ship, is about 300 pixels long and a cell across
		private const float UnitScale = 0.92f / 306;
		private static readonly Dictionary<string, float> UnitSize = new Dictionary<string, float> { ["phalanx"] = 0.95f, ["legion"] = 0.95f, ["militia"] = 0.95f };
		// bases as on a tabletop: a small round one for a man on foot, larger for horses and carts, largest for ships
		private static readonly Dictionary<string, float> BaseRadius = new Dictionary<string, float>
		{
			["phalanx"] = 0.2f, ["legion"] = 0.2f, ["militia"] = 0.2f, ["settlers"] = 0.38f, ["knights"] = 0.3f, ["chariot"] = 0.32f,
			["catapult"] = 0.32f, ["trireme"] = 0.44f, ["sail"] = 0.42f,
			// units the theme has not painted, by kind
			["musketeers"] = 0.2f, ["riflemen"] = 0.2f, ["diplomat"] = 0.2f, ["cavalry"] = 0.3f, ["caravan"] = 0.36f,
			["cannon"] = 0.32f, ["artillery"] = 0.32f, ["armor"] = 0.34f, ["mech_infantry"] = 0.34f, ["fighter"] = 0.34f,
			["bomber"] = 0.38f, ["nuclear"] = 0.24f, ["frigate"] = 0.42f, ["ironclad"] = 0.42f, ["cruiser"] = 0.44f,
			["battleship"] = 0.46f, ["submarine"] = 0.42f, ["carrier"] = 0.46f, ["transport"] = 0.44f,
		};
		// how round a base looks from the camera's height; ships are painted from lower down, so theirs are flatter
		private const float BaseSquash = 0.62f;
		private static readonly Dictionary<string, float> UnitSquash = new Dictionary<string, float> { ["trireme"] = 0.4f, ["sail"] = 0.48f, ["frigate"] = 0.46f,
			["ironclad"] = 0.44f, ["cruiser"] = 0.42f, ["battleship"] = 0.42f, ["submarine"] = 0.4f, ["carrier"] = 0.42f, ["transport"] = 0.44f };

		// the share of a cell each step of the town ladder covers: a hamlet, three steps of towns, a city, a capital
		private static readonly float[] StepSize = { 0.42f, 0.56f, 0.7f, 0.84f, 0.98f, 0.98f };
		private static readonly SKColor Ink = new SKColor(20, 18, 16);
		private static readonly SKColor NameColour = new SKColor(255, 250, 235);
		private static readonly SKColor Rim = new SKColor(250, 244, 226);

		private sealed class UnitArt
		{
			public string Name = "";
			public SKBitmap Picture = null!;
			// how much each pixel is the painted blue player colour, 0 to 1, and its brightness there
			public float[] Team = Array.Empty<float>();
			public float TeamValue = 0.7f;
			// the point of the picture on the middle of the base's top
			public float AnchorX, AnchorY;
			public float BaseR, Squash;
			public bool Ship;
			// the size of its solid body (thin spears and poles left out) as drawn, to size units from other sets by
			public float SolidWidth, SolidHeight;
		}

		private readonly int width, height, cell;
		// the view's size in cells (15 x 12 for the map window)
		private readonly float columns, rows;
		private uint[] pixels;
		private long signature = long.MinValue;
		private readonly UnitArt?[] units = new UnitArt?[UnitNames.Length];
		private readonly SKBitmap?[,] towns = new SKBitmap?[6, 2];
		private readonly SKTypeface face;
		// huts and special resources (by terrain id, ResourceNames), painted into the view in place of the classic sprites
		private SKBitmap? hut;
		private readonly SKBitmap?[] resources = new SKBitmap?[ResourceNames.Length];
		private SKBitmap? shield;
		private readonly Dictionary<(int Type, uint Colour, bool Grey), SKBitmap> coloured = new Dictionary<(int, uint, bool), SKBitmap>();

		private IsoView.Scene? lastScene;
		private (float X, float Y) lastCentre;

		private TopView(int width, int height, int cell)
		{
			this.width = width;
			this.height = height;
			this.cell = cell;
			this.columns = width / (float)cell;
			this.rows = height / (float)cell;
			this.pixels = new uint[width * height];
			this.face = SerifFace();
		}

		public int Width => this.width;

		public int Height => this.height;

		public uint[] Pixels => this.pixels;

		public bool Blends => true;

		public bool DrawsCities => true;

		public bool DrawsUnit(int type) => type >= 0 && type < this.units.Length && this.units[type] != null;

		public bool DrawsFeatures => this.hut != null && this.resources.All(r => r != null) && this.shield != null;

		// battles are painted with drawn dust and smoke, so any theme with this view shows them
		public bool HasBattleArt => true;

		/// <summary>
		/// The view for a theme whose painted map has painted units (painted/objects/unit_NAME.png) and a ladder of painted
		/// towns (painted/towns/ladder/t0_o.png ... t5_w.png); null for any other theme. Units, huts and resources the
		/// theme has not painted come from the painted objects of the fallback folder (the game's own hd folder), toned
		/// to the theme's brighter, bolder colours, so every unit stands on a base and no classic sprite is left on the map.
		/// </summary>
		public static TopView? Load(string hdFolder, int width, int height, int cell, string? fallbackFolder = null)
		{
			string painted = Path.Combine(hdFolder, "painted");
			string ladder = Path.Combine(painted, "towns", "ladder");
			string objects = Path.Combine(painted, "objects");
			string? fallback = fallbackFolder == null ? null : Path.Combine(fallbackFolder, "painted", "objects");

			if (!File.Exists(Path.Combine(ladder, "t0_o.png")) || !Directory.Exists(objects))
				return null;

			TopView view = new TopView(width, height, cell);
			int c = view.cell;

			// the theme's own picture of an object, or the fallback's toned to the theme (false for the second)
			(string? Path, bool Own) Find(string name)
			{
				string own = Path.Combine(objects, name + ".png");

				if (File.Exists(own))
					return (own, true);

				if (fallback != null && File.Exists(Path.Combine(fallback, name + ".png")))
					return (Path.Combine(fallback, name + ".png"), false);

				return (null, false);
			}

			// the theme's own units first, at the scale they were painted at together
			var borrowed = new List<(int Type, string Path)>();

			for (int type = 0; type < UnitNames.Length; type++)
			{
				(string? path, bool own) = Find($"unit_{UnitNames[type]}");

				if (path != null && own)
					view.units[type] = LoadUnit(path, UnitNames[type], c, false, null);
				else if (path != null)
					borrowed.Add((type, path));
			}

			if (view.units.All(u => u == null))
				return null;

			// then the others, each the size of the theme's own unit of its kind (another set is painted at another
			// scale): men on foot as tall as a phalanx, horsemen as a knight, carts as the settlers, guns, tanks and
			// aircraft as wide as a catapult, ships as a sailing ship
			foreach ((int type, string path) in borrowed)
			{
				string name = UnitNames[type];
				string like = Ships.Contains(name) ? "sail" : Foot.Contains(name) || name == "diplomat" ? "phalanx" :
					name == "cavalry" ? "knights" : name == "caravan" ? "settlers" : "catapult";
				int likeType = Array.IndexOf(UnitNames, like);
				UnitArt? reference = view.units[likeType] ?? view.units.FirstOrDefault(u => u != null);
				bool byHeight = Foot.Contains(name) || name == "diplomat" || name == "cavalry";
				(float, bool)? size = reference == null ? null : ((byHeight ? reference.SolidHeight : reference.SolidWidth), byHeight);
				view.units[type] = LoadUnit(path, name, c, true, size);
			}


			// a hut a little over half a cell across, a resource a little under, as a print paints them
			(string? hutPath, bool hutOwn) = Find("hut");

			if (hutPath != null && Decode(hutPath) is SKBitmap hutRaw)
			{
				using SKBitmap toned = hutOwn ? hutRaw : Toned(hutRaw);
				view.hut = Resize(toned, c * 0.56f / Math.Max(toned.Width, toned.Height));

				if (!hutOwn)
					hutRaw.Dispose();
			}

			for (int i = 0; i < ResourceNames.Length; i++)
			{
				(string? path, bool own) = Find($"res_{ResourceNames[i]}");

				if (path != null && Decode(path) is SKBitmap raw)
				{
					using SKBitmap toned = own ? raw : Toned(raw);
					view.resources[i] = Resize(toned, c * 0.46f / Math.Max(toned.Width, toned.Height));

					if (!own)
						raw.Dispose();
				}
			}

			for (int step = 0; step < 6; step++)
			{
				for (int w = 0; w < 2; w++)
				{
					string path = Path.Combine(ladder, $"t{step}_{(w == 1 ? "w" : "o")}.png");

					if (!File.Exists(path))
						path = Path.Combine(ladder, $"t{step}_o.png");

					if (!File.Exists(path))
						continue;

					using SKBitmap? raw = Decode(path);

					if (raw == null)
						continue;

					using SKBitmap soft = SoftTown(raw);
					float f = c * StepSize[step] / Math.Max(soft.Width, soft.Height);
					view.towns[step, w] = Resize(soft, f);
				}
			}

			// the grassland shield: the theme's own sprite (SP257 at 153, 41), drawn smaller and quieter than the classic
			// map's, as a mark on the ground rather than a sign over it
			string shieldPath = Path.Combine(hdFolder, "SP257", "153_41.png");

			if (File.Exists(shieldPath) && Decode(shieldPath) is SKBitmap shieldRaw)
			{
				view.shield = Resize(shieldRaw, c * 0.27f / Math.Max(shieldRaw.Width, shieldRaw.Height));
				shieldRaw.Dispose();
			}

			return view;
		}

		/// <summary>
		/// The town ladder's step for a city size, as the theme's map renders pair them: a hamlet at size 1, three
		/// towns, a city with a domed palace from size 8 and the marble capital from 12
		/// </summary>
		private static int Step(int size) => size >= 12 ? 5 : size >= 8 ? 4 : size >= 6 ? 3 : size >= 4 ? 2 : size >= 2 ? 1 : 0;

		/// <summary>
		/// The step of the city clearing the painted map opens round a city of a size (PaintedMap.CityStepShift)
		/// </summary>
		public static int ClearingStep(int size) => Step(size);

		public bool Render(float cx, float cy, IsoView.Scene scene)
		{
			long sig = 17;

			unchecked
			{
				sig = (sig * 31) + (long)(cx * 16) + ((long)(cy * 16) << 20);

				foreach (var c in scene.Cities)
					sig = (sig * 37) + (c.X * 1000) + (c.Y * 10) + c.Size + c.Name.GetHashCode() + c.Colour + (scene.Walls(Wrap(c.X), c.Y) ? 7 : 0);

				if (this.DrawsFeatures)
				{
					for (int y = (int)MathF.Floor(cy - (this.rows / 2)) - 1; y <= cy + (this.rows / 2) + 1; y++)
					{
						for (int x = (int)MathF.Floor(cx - (this.columns / 2)) - 1; x <= cx + (this.columns / 2) + 1; x++)
						{
							if (y < 0 || y >= MapHeight)
								continue;

							sig = (sig * 7) + (scene.Explored(Wrap(x), y) ? scene.Feature(Wrap(x), y) + (scene.Terrain(Wrap(x), y) * 4) + (scene.ShieldBonus(Wrap(x), y) ? 64 : 0) : 99);
						}
					}
				}

				foreach (var u in scene.Units)
					sig = (sig * 41) + (u.X * 1000) + (u.Y * 10) + u.Type + u.Colour + (scene.UnitMarks(Wrap(u.X), u.Y) * 7919);

				if (scene.Active is (int X, int Y, int Type, uint Colour) a)
					sig = (sig * 43) + (a.X * 1000) + (a.Y * 10) + a.Type + a.Colour + (scene.ActiveShown ? 1 : 2);
			}

			this.lastScene = scene;
			this.lastCentre = (cx, cy);

			if (sig == this.signature)
				return false;

			this.signature = sig;
			this.pixels = this.Paint(cx, cy, scene, null, null);

			return true;
		}

		/// <summary>
		/// The towns, units and features of a small map, such as the city screen's 5 x 5 cells, centred on (cx, cy) in map
		/// cells and the given number of cells across and down; without town names
		/// </summary>
		public uint[] RenderSmall(float cx, float cy, int columns, int rows, IsoView.Scene scene)
		{
			return this.Paint(cx, cy, scene, null, null, columns * this.cell, rows * this.cell, false);
		}

		/// <summary>
		/// One frame of a unit moving from one cell to the next: the last scene with that unit drawn part of the way along
		/// </summary>
		public uint[]? MoveFrame(float t, (int X, int Y) from, (int X, int Y) to)
		{
			if (this.lastScene == null)
				return null;

			bool found = this.lastScene.Units.Any(u => Near(u.X, this.lastCentre.X) == from.X && u.Y == from.Y && this.DrawsUnit(u.Type)) ||
				(this.lastScene.Active is (int X, int Y, int Type, uint Colour) a && Near(a.X, this.lastCentre.X) == from.X && a.Y == from.Y && this.DrawsUnit(a.Type));

			if (!found)
				return null;

			return this.Paint(this.lastCentre.X, this.lastCentre.Y, this.lastScene,
				(x, y) => (x, y) == from ? ((to.X - from.X) * t, (to.Y - from.Y) * t, 1, false) : null, null);
		}

		public void EndMove()
		{
			// the next render repaints the scene as it is after the move
			this.signature = long.MinValue;
		}

		/// <summary>
		/// One frame of a battle (t from 0 to 1), painted like the rest of the view: the attacker lunges at the defender
		/// and falls back, a burst of dust and sparks where they meet, then the loser greys, sinks and fades under rising
		/// smoke while the winner stands
		/// </summary>
		public uint[] BattleFrame(float t, (int X, int Y) attacker, (int X, int Y) defender, bool attackerWins, int seed)
		{
			if (this.lastScene == null)
				return this.pixels;

			float dx = defender.X - attacker.X, dy = defender.Y - attacker.Y;
			// the lunge: out over the first 30%, back by 55%
			float lunge = t < 0.3f ? Ease(t / 0.3f) : t < 0.55f ? 1 - Ease((t - 0.3f) / 0.25f) : 0;
			// the loser: still until the clash is over, then gone by the end
			float fall = Math.Clamp((t - 0.5f) / 0.45f, 0, 1);
			(int X, int Y) loser = attackerWins ? defender : attacker;

			(float, float, float, bool)? Pose(int x, int y)
			{
				if ((x, y) == attacker && (x, y) != loser)
					return (dx * 0.36f * lunge, dy * 0.36f * lunge, 1, false);

				if ((x, y) == attacker)
					return (dx * 0.36f * lunge, (dy * 0.36f * lunge) + (0.1f * fall), 1 - fall, fall > 0);

				if ((x, y) == loser)
					return (0, 0.1f * fall, 1 - fall, fall > 0);

				// the defender rocks back when struck
				float hit = t > 0.22f && t < 0.42f ? MathF.Sin((t - 0.22f) / 0.2f * MathF.PI) : 0;
				return (dx * 0.06f * hit, dy * 0.06f * hit, 1, false);
			}

			void Effects(SKCanvas canvas, float left, float top)
			{
				int c = this.cell;
				Random rng = new Random(seed);
				// where they meet: a little short of the defender, at the height of the figures
				float mx = ((attacker.X + (dx * 0.68f) - left) + 0.5f) * c, my = ((attacker.Y + (dy * 0.68f) - top) + 0.5f) * c;

				if (t > 0.2f && t < 0.62f)
				{
					float k = (t - 0.2f) / 0.42f, r = c * (0.12f + (0.38f * k));
					byte a = (byte)(240 * MathF.Min(1, 2.5f * (1 - k)));
					using SKPaint glow = new SKPaint { IsAntialias = true, Color = new SKColor(255, 240, 200, a), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, r * 0.3f) };
					canvas.DrawCircle(mx, my, r * 0.7f, glow);
					using SKPaint dust = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = c * 0.06f * (1 - k), Color = new SKColor(196, 168, 120, a), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, c * 0.03f) };
					canvas.DrawOval(new SKRect(mx - r, my - (r * 0.6f), mx + r, my + (r * 0.6f)), dust);

					// sparks flying out from the clash
					using SKPaint spark = new SKPaint { IsAntialias = true, StrokeWidth = c * 0.026f, StrokeCap = SKStrokeCap.Round, Color = new SKColor(255, 214, 120, a) };

					for (int i = 0; i < 9; i++)
					{
						double angle = rng.NextDouble() * Math.PI * 2;
						float len = c * (0.15f + (0.25f * (float)rng.NextDouble())), from = len * k, to = from + (c * 0.06f);
						float ax = (float)Math.Cos(angle), ay = (float)Math.Sin(angle) * 0.7f;
						canvas.DrawLine(mx + (ax * from), my + (ay * from), mx + (ax * to), my + (ay * to), spark);
					}
				}

				// smoke from the loser, rising and spreading as it fades
				if (t > 0.45f)
				{
					float k = (t - 0.45f) / 0.55f;
					float lx = ((loser.X - left) + 0.5f) * c, ly = ((loser.Y - top) + 0.6f) * c;

					for (int i = 0; i < 6; i++)
					{
						float delay = i * 0.07f, kk = Math.Clamp((k - delay) / (1 - delay), 0, 1);

						if (kk <= 0)
							continue;

						float ox = ((float)rng.NextDouble() - 0.5f) * c * 0.5f, rise = c * (0.15f + (0.55f * kk)) * (0.7f + (0.6f * (float)rng.NextDouble()));
						float r = c * (0.1f + (0.18f * kk));
						byte shade = (byte)(70 + rng.Next(50));
						byte a = (byte)(170 * MathF.Sin(kk * MathF.PI));
						using SKPaint puff = new SKPaint { IsAntialias = true, Color = new SKColor(shade, (byte)(shade - 6), (byte)(shade - 14), a), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, r * 0.45f) };
						canvas.DrawCircle(lx + ox + (ox * kk), ly - rise, r, puff);
					}
				}
			}

			return this.Paint(this.lastCentre.X, this.lastCentre.Y, this.lastScene, (x, y) => Pose(x, y), Effects);
		}

		private static float Ease(float x) => x * x * (3 - (2 * x));

		public void EndBattle()
		{
			this.signature = long.MinValue;
		}

		public (int X, int Y) CellAt(float px, float py, float cx, float cy)
		{
			return (Wrap((int)MathF.Floor(cx - (this.columns / 2) + (px / this.cell))), (int)MathF.Floor(cy - (this.rows / 2) + (py / this.cell)));
		}

		private static int Wrap(int x) => ((x % MapWidth) + MapWidth) % MapWidth;

		// a cell's x unwrapped to lie nearest the view's centre
		private static int Near(int x, float cx) => x + (MapWidth * (int)MathF.Round((cx - x) / MapWidth));

		/// <summary>
		/// The scene: pose, if given, moves a cell's unit (in cells), fades it (alpha) and greys it; effects are drawn last,
		/// given the view's top-left corner in map cells
		/// </summary>
		private uint[] Paint(float cx, float cy, IsoView.Scene scene, Func<int, int, (float Dx, float Dy, float Alpha, bool Grey)?>? pose,
			Action<SKCanvas, float, float>? effects) => this.Paint(cx, cy, scene, pose, effects, this.width, this.height, true);

		/// <summary>
		/// The scene in a view of w x h HD pixels; names, if shown, under the towns
		/// </summary>
		private uint[] Paint(float cx, float cy, IsoView.Scene scene, Func<int, int, (float Dx, float Dy, float Alpha, bool Grey)?>? pose,
			Action<SKCanvas, float, float>? effects, int w, int h, bool names)
		{
			float cols = w / (float)this.cell, rows = h / (float)this.cell;
			int c = this.cell;
			float left = cx - (cols / 2), top = cy - (rows / 2);
			using SKBitmap target = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
			using SKCanvas canvas = new SKCanvas(target);
			canvas.Clear(SKColors.Transparent);

			// a cell's top-left corner in the view
			(float X, float Y) Corner(int x, int y) => ((Near(x, cx) - left) * c, (y - top) * c);
			bool InView(float px, float py) => px > -2 * c && px < w + c && py > -c && py < h + c;

			using SKFont badgeFont = new SKFont(this.face, 10 * c / 64f);
			using SKFont nameFont = new SKFont(this.face, 12 * c / 64f);
			using SKPaint fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
			using SKPaint stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };
			using SKPaint image = new SKPaint { IsAntialias = true };

			// huts and special resources on the explored cells in view, under the towns and units
			if (this.DrawsFeatures)
			{
				using SKPaint shade = new SKPaint { IsAntialias = true, Color = new SKColor(20, 16, 10, 90), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2.2f * c / 64f) };

				for (int y = (int)MathF.Floor(top) - 1; y <= top + rows; y++)
				{
					for (int x = (int)MathF.Floor(left) - 1; x <= left + cols; x++)
					{
						int wx = Wrap(x);

						if (y < 0 || y >= MapHeight || !scene.Explored(wx, y))
							continue;

						int feature = scene.Feature(wx, y), terrain = scene.Terrain(wx, y);

						if (this.shield != null && scene.ShieldBonus(wx, y))
						{
							float sx = ((x - left) * c) + ((c - this.shield.Width) / 2f), sy = ((y - top) * c) + ((c - this.shield.Height) / 2f);
							canvas.DrawOval(new SKRect(sx, sy + (this.shield.Height * 0.7f), sx + this.shield.Width, sy + (this.shield.Height * 1.05f)), shade);
							using SKPaint quiet = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha(215) };
							canvas.DrawBitmap(this.shield, MathF.Round(sx), MathF.Round(sy), quiet);
						}

						SKBitmap? art = (feature & 2) != 0 ? this.hut : (feature & 1) != 0 && terrain >= 0 && terrain < this.resources.Length ? this.resources[terrain] : null;

						if (art == null || scene.Cities.Any(t => Wrap(t.X) == wx && t.Y == y))
							continue;

						float px = ((x - left) * c) + ((c - art.Width) / 2f), py = ((y - top) * c) + ((c - art.Height) / 2f) + (c * 0.06f);

						// a soft shadow where it stands on land; at sea a resource floats in the water
						if (terrain != Water)
							canvas.DrawOval(new SKRect(px + (art.Width * 0.12f), py + (art.Height * 0.78f), px + (art.Width * 0.92f), py + (art.Height * 1.02f)), shade);

						canvas.DrawBitmap(art, MathF.Round(px), MathF.Round(py), image);
					}
				}
			}

			// towns, top to bottom: the town, its size badge on its top-left corner, its name under it
			foreach (var town in scene.Cities.OrderBy(t => t.Y).ThenBy(t => Near(t.X, cx)))
			{
				(float x0, float y0) = Corner(town.X, town.Y);

				if (!InView(x0, y0))
					continue;

				SKBitmap? art = this.towns[Step(town.Size), scene.Walls(Wrap(town.X), town.Y) ? 1 : 0] ?? this.towns[Step(town.Size), 0];
				float tx = x0, ty = y0, tw = c * 0.6f, th = c * 0.6f;

				if (art != null)
				{
					tw = art.Width;
					th = art.Height;
					tx = MathF.Round(x0 + ((c - tw) / 2));
					ty = MathF.Round(y0 + ((c - th) / 2));
					canvas.DrawBitmap(art, tx, ty, image);
				}
				else
				{
					tx = x0 + ((c - tw) / 2);
					ty = y0 + ((c - th) / 2);
				}

				float s = 13 * c / 64f, u = c / 64f;
				float bx = MathF.Max(x0 + u, tx - s + (2 * u)), by = MathF.Max(y0 + u, ty - (4 * u));
				fill.Color = ToColor(town.Colour);
				canvas.DrawRect(bx, by, s + u, s + u, fill);
				stroke.Color = Ink;
				stroke.StrokeWidth = u;
				canvas.DrawRect(bx + (u / 2), by + (u / 2), s, s, stroke);
				string size = town.Size.ToString();
				fill.Color = Lightness(town.Colour) > 0.72f ? Ink : SKColors.White;
				DrawCentred(canvas, size, bx + ((s + u) / 2), by + ((s + u) / 2), badgeFont, fill, true);

				if (!names)
					continue;

				float ny = MathF.Min(y0 + c, ty + th + (2 * u));
				float nx = x0 + (c / 2f);
				fill.Color = Ink;
				DrawCentred(canvas, town.Name, nx + (2 * u), ny + (2 * u), nameFont, fill, false);
				DrawCentred(canvas, town.Name, nx + u, ny + u, nameFont, fill, false);
				fill.Color = NameColour;
				DrawCentred(canvas, town.Name, nx, ny, nameFont, fill, false);
			}

			// units, back to front, each on its base; the unit waiting for orders also in a city, ringed while its marker
			// blinks on
			var shown = new List<(float X, float Y, int Type, uint Colour, int Marks, bool Active)>();
			(int X, int Y, int Type, uint Colour)? active = scene.Active;

			foreach (var unit in scene.Units)
			{
				bool isActive = active.HasValue && active.Value.X == unit.X && active.Value.Y == unit.Y;
				int type = isActive ? active!.Value.Type : unit.Type;
				shown.Add((Near(unit.X, cx), unit.Y, type, unit.Colour, scene.UnitMarks(Wrap(unit.X), unit.Y), isActive));
			}

			if (active.HasValue && !scene.Units.Any(u => u.X == active.Value.X && u.Y == active.Value.Y))
				shown.Add((Near(active.Value.X, cx), active.Value.Y, active.Value.Type, active.Value.Colour, 0, true));

			var alphas = new float[shown.Count];

			for (int i = 0; i < shown.Count; i++)
			{
				alphas[i] = 1;

				if (pose?.Invoke((int)shown[i].X, (int)shown[i].Y) is (float pdx, float pdy, float alpha, bool grey))
				{
					shown[i] = (shown[i].X + pdx, shown[i].Y + pdy, shown[i].Type, shown[i].Colour, shown[i].Marks | (grey ? 1 : 0), false);
					alphas[i] = alpha;
				}
			}

			foreach (int index in Enumerable.Range(0, shown.Count).OrderBy(i => shown[i].Y).ThenBy(i => shown[i].X))
			{
				var unit = shown[index];
				UnitArt? art = unit.Type >= 0 && unit.Type < this.units.Length ? this.units[unit.Type] : null;

				if (art == null)
					continue;

				float x0 = (unit.X - left) * c, y0 = (unit.Y - top) * c;

				if (!InView(x0, y0))
					continue;

				bool ring = unit.Active && scene.ActiveShown;

				if (alphas[index] <= 0.01f)
					continue;

				if (alphas[index] < 1)
				{
					using SKPaint layer = new SKPaint { Color = SKColors.White.WithAlpha((byte)(255 * alphas[index])) };
					canvas.SaveLayer(layer);
				}

				this.DrawUnit(canvas, art, x0, y0, unit.Colour, unit.Marks, ring, fill, stroke, image, badgeFont);

				if (alphas[index] < 1)
					canvas.Restore();
			}

			effects?.Invoke(canvas, left, top);
			canvas.Flush();
			uint[] result = new uint[w * h];
			System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(target.GetPixelSpan()).CopyTo(result);

			return result;
		}

		private void DrawUnit(SKCanvas canvas, UnitArt art, float x0, float y0, uint colour, int marks, bool ring,
			SKPaint fill, SKPaint stroke, SKPaint image, SKFont font)
		{
			int c = this.cell;
			float cx = x0 + (c / 2f), cy = y0 + (c * (art.Ship ? 0.6f : 0.7f));
			float rx = c * art.BaseR, ry = rx * art.Squash, side = c * 0.02f, u = c / 64f;
			SKColor col = ToColor(colour);
			bool grey = (marks & 1) != 0;

			// more units on the cell: a second base behind this one
			if ((marks & 4) != 0)
				this.DrawBase(canvas, cx + (3 * u), cy - (3 * u), rx, ry, side, col, false, fill, stroke);

			// fortified: a low ring of earth and stakes round the base
			if ((marks & 2) != 0)
			{
				SKRect bank = new SKRect(cx - rx - (4 * u), cy - ry - (2 * u), cx + rx + (4 * u), cy + ry + side + (4 * u));
				stroke.StrokeWidth = 3.2f * u;
				stroke.Color = new SKColor(92, 70, 44);
				canvas.DrawOval(bank, stroke);
				stroke.StrokeWidth = 1.2f * u;
				stroke.Color = new SKColor(150, 122, 82);
				canvas.DrawOval(new SKRect(bank.Left, bank.Top - u, bank.Right, bank.Bottom - u), stroke);
			}

			this.DrawBase(canvas, cx, cy, rx, ry, side, col, ring, fill, stroke);

			SKBitmap picture = this.Coloured(art, colour, grey);
			canvas.DrawBitmap(picture, MathF.Round(cx - art.AnchorX), MathF.Round(cy - art.AnchorY), image);

			// an order in progress: its letter on a small paper tag at the unit's top right
			char letter = (char)((marks >> 8) & 0xff);

			if (letter != '\0')
			{
				SKRect tag = new SKRect(cx + (8 * u), cy - (34 * u), cx + (19 * u), cy - (22 * u));
				fill.Color = new SKColor(240, 230, 204);
				canvas.DrawRect(tag, fill);
				stroke.Color = Ink;
				stroke.StrokeWidth = u;
				canvas.DrawRect(tag, stroke);
				fill.Color = Ink;
				DrawCentred(canvas, letter.ToString(), tag.MidX, tag.MidY, font, fill, true);
			}
		}

		/// <summary>
		/// A unit's round base, as compose.unit_base: a squashed disc in the player's colour with a thin darker side, a soft
		/// shadow, a pale rim and a highlight on its far half; the active unit's has a white ring round it
		/// </summary>
		private void DrawBase(SKCanvas canvas, float cx, float cy, float rx, float ry, float side, SKColor col, bool ring, SKPaint fill, SKPaint stroke)
		{
			float u = this.cell / 64f;
			SKColor dark = new SKColor((byte)(col.Red * 0.55f), (byte)(col.Green * 0.55f), (byte)(col.Blue * 0.55f));
			SKColor light = new SKColor((byte)Math.Min(255, (col.Red * 1.3f) + 40), (byte)Math.Min(255, (col.Green * 1.3f) + 40), (byte)Math.Min(255, (col.Blue * 1.3f) + 40), 120);

			using (SKPaint shadow = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 100), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(0.5f, side * 0.7f)) })
				canvas.DrawOval(new SKRect(cx - rx, cy - ry + (side * 1.5f), cx + rx + side, cy + ry + (side * 2)), shadow);

			if (ring)
			{
				stroke.Color = SKColors.White;
				stroke.StrokeWidth = 1.5f * u;
				canvas.DrawOval(new SKRect(cx - rx - (2.5f * u), cy - ry - (2.5f * u), cx + rx + (2.5f * u), cy + ry + side + (2.5f * u)), stroke);
			}

			fill.Color = dark;
			canvas.DrawOval(new SKRect(cx - rx, cy - ry + side, cx + rx, cy + ry + side), fill);
			canvas.DrawRect(new SKRect(cx - rx, cy, cx + rx, cy + side), fill);
			fill.Color = col;
			canvas.DrawOval(new SKRect(cx - rx, cy - ry, cx + rx, cy + ry), fill);

			using (SKPaint highlight = new SKPaint { IsAntialias = true, Color = light, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(0.5f, ry * 0.25f)) })
			{
				canvas.Save();
				canvas.ClipRect(new SKRect(cx - rx, cy - ry, cx + rx, cy + ry));
				canvas.DrawOval(new SKRect(cx - (rx * 0.8f), cy - (ry * 0.85f), cx + (rx * 0.5f), cy - (ry * 0.1f)), highlight);
				canvas.Restore();
			}

			stroke.Color = Rim;
			stroke.StrokeWidth = 0.9f * u;
			canvas.DrawOval(new SKRect(cx - rx, cy - ry, cx + rx, cy + ry), stroke);
		}

		/// <summary>
		/// A unit in a player's colour: the parts painted in the blue player colour (shields, cloaks, banners, sails) take the
		/// player's colour with their light and shade; a sentried unit is grey
		/// </summary>
		private SKBitmap Coloured(UnitArt art, uint colour, bool grey)
		{
			if (this.coloured.TryGetValue((Array.IndexOf(this.units, art), colour, grey), out SKBitmap? done))
				return done;

			SKBitmap src = art.Picture;
			SKBitmap result = new SKBitmap(new SKImageInfo(src.Width, src.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
			SKColor col = ToColor(colour);

			for (int y = 0; y < src.Height; y++)
			{
				for (int x = 0; x < src.Width; x++)
				{
					SKColor p = src.GetPixel(x, y);
					float r = p.Red, g = p.Green, b = p.Blue;
					float w = art.Team[(y * src.Width) + x];

					if (w > 0)
					{
						float k = (MathF.Max(r, MathF.Max(g, b)) / 255f) / art.TeamValue;
						(float tr, float tg, float tb) = Shade(col, k);
						r += (tr - r) * w;
						g += (tg - g) * w;
						b += (tb - b) * w;
					}

					if (grey)
					{
						float l = (0.3f * r) + (0.59f * g) + (0.11f * b);
						l = l + ((235 - l) * 0.25f);
						r = g = b = l;
					}

					result.SetPixel(x, y, new SKColor((byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(b, 0, 255), p.Alpha));
				}
			}

			this.coloured[(Array.IndexOf(this.units, art), colour, grey)] = result;

			return result;
		}

		// a colour darkened (k below 1) or lightened towards white (k above 1)
		private static (float, float, float) Shade(SKColor col, float k)
		{
			if (k <= 1)
				return (col.Red * k, col.Green * k, col.Blue * k);

			float t = Math.Min(1, (k - 1) * 0.8f);
			return (col.Red + ((255 - col.Red) * t), col.Green + ((255 - col.Green) * t), col.Blue + ((255 - col.Blue) * t));
		}

		private static float Lightness(uint colour) => ((((colour >> 16) & 0xff) * 0.3f) + (((colour >> 8) & 0xff) * 0.59f) + ((colour & 0xff) * 0.11f)) / 255f;

		private static SKColor ToColor(uint argb) => new SKColor((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, 255);

		/// <summary>
		/// Text centred across on x; on y centred (middle) or hanging from y (top)
		/// </summary>
		private static void DrawCentred(SKCanvas canvas, string text, float x, float y, SKFont font, SKPaint paint, bool middle)
		{
			float w = font.MeasureText(text);
			SKFontMetrics m = font.Metrics;
			float baseline = middle ? y - ((m.Ascent + m.Descent) / 2) - (m.Descent * 0.15f) : y - m.Ascent;
			canvas.DrawText(text, x - (w / 2), baseline, font, paint);
		}

		private static SKTypeface SerifFace()
		{
			foreach (string family in new[] { "Georgia", "DejaVu Serif", "Liberation Serif", "FreeSerif", "Times New Roman" })
			{
				SKTypeface? face = SKTypeface.FromFamilyName(family, SKFontStyle.Bold);

				if (face != null && string.Equals(face.FamilyName, family, StringComparison.OrdinalIgnoreCase))
					return face;
			}

			return SKTypeface.FromFamilyName("serif", SKFontStyle.Bold) ?? SKTypeface.Default;
		}

		private static SKBitmap? Decode(string path)
		{
			using SKBitmap? b = SKBitmap.Decode(path);

			return b?.Copy(SKColorType.Rgba8888);
		}

		/// <summary>
		/// A picture scaled by f, as smoothly as PIL's Lanczos: halved with a box filter while more than twice the size
		/// wanted (a cubic filter alone skips most of the pixels of a big reduction and turns a town to noise), then the
		/// rest of the way with a cubic filter. Premultiplied, so transparent edges do not darken.
		/// </summary>
		private static SKBitmap Resize(SKBitmap b, float f)
		{
			int w = Math.Max(1, (int)MathF.Round(b.Width * f)), h = Math.Max(1, (int)MathF.Round(b.Height * f));
			SKBitmap current = b.Copy(SKColorType.Rgba8888);

			while (current.Width >= 2 * w * 2 && current.Height >= 2 * h * 2)
			{
				SKBitmap half = Halve(current);
				current.Dispose();
				current = half;
			}

			SKBitmap sized = current.Resize(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul), new SKSamplingOptions(SKCubicResampler.CatmullRom));
			current.Dispose();

			return sized;
		}

		// half the size, each pixel the mean of four, in premultiplied colour
		private static SKBitmap Halve(SKBitmap b)
		{
			int w = b.Width / 2, h = b.Height / 2;
			SKBitmap half = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));

			for (int y = 0; y < h; y++)
			{
				for (int x = 0; x < w; x++)
				{
					float r = 0, g = 0, bl = 0, a = 0;

					for (int k = 0; k < 4; k++)
					{
						SKColor p = b.GetPixel((2 * x) + (k & 1), (2 * y) + (k >> 1));
						float pa = p.Alpha / 255f;
						r += p.Red * pa;
						g += p.Green * pa;
						bl += p.Blue * pa;
						a += pa;
					}

					half.SetPixel(x, y, a <= 0 ? SKColors.Transparent :
						new SKColor((byte)Math.Clamp(r / a, 0, 255), (byte)Math.Clamp(g / a, 0, 255), (byte)Math.Clamp(bl / a, 0, 255), (byte)Math.Clamp(a / 4 * 255, 0, 255)));
				}
			}

			return half;
		}

		#region Loading the art, as compose.py prepares it

		/// <summary>
		/// A painted unit at the one scale all units share, with where its base goes (compose.body): a unit on land
		/// stands on its base under the lowest part of its solid body, a man on foot on his feet; a ship sits in its base
		/// by its hull
		/// </summary>
		private static UnitArt? LoadUnit(string path, string name, int c, bool tone, (float Pixels, bool Height)? like)
		{
			using SKBitmap? decoded = Decode(path);

			if (decoded == null)
				return null;

			using SKBitmap raw = tone ? Toned(decoded) : decoded.Copy();

			int w = raw.Width, h = raw.Height;
			float[] alpha = new float[w * h];

			for (int y = 0; y < h; y++)
				for (int x = 0; x < w; x++)
					alpha[(y * w) + x] = raw.GetPixel(x, y).Alpha;

			// the solid body: alpha eroded by an 11 x 11 square, so thin spears and poles drop out
			float[] solid = Erode(alpha, w, h, 5);
			var xs = new List<float>();
			var ys = new List<float>();

			for (int y = 0; y < h; y++)
				for (int x = 0; x < w; x++)
					if (solid[(y * w) + x] > 128)
					{
						xs.Add(x);
						ys.Add(y);
					}

			if (xs.Count == 0)
				return null;

			bool ship = Ships.Contains(name);
			float ax, ay, half;

			if (ship)
			{
				float top = Percentile(ys, 2), bottom = Percentile(ys, 99);
				var hullX = new List<float>();
				var hullY = new List<float>();

				for (int i = 0; i < ys.Count; i++)
					if (ys[i] > bottom - (0.35f * (bottom - top)))
					{
						hullX.Add(xs[i]);
						hullY.Add(ys[i]);
					}

				float hl = Percentile(hullX, 2), hr = Percentile(hullX, 98);
				ax = (hl + hr) / 2;
				ay = Percentile(hullY, 50);
				half = (hr - hl) / 2;
			}
			else
			{
				float top = Percentile(ys, 3), bottom = Percentile(ys, 98);
				float fl = Percentile(xs, 2), fr = Percentile(xs, 98);
				float lift = name == "settlers" ? 0.22f : 0.1f;

				if (Foot.Contains(name))
				{
					var feet = new List<float>();

					for (int i = 0; i < ys.Count; i++)
						if (ys[i] > bottom - (0.3f * (bottom - top)))
							feet.Add(xs[i]);

					fl = Percentile(feet, 2);
					fr = Percentile(feet, 98);
					lift = 0.06f;
				}

				ax = (fl + fr) / 2;
				ay = bottom - (lift * (bottom - top));
				half = (fr - fl) / 2;
			}

			// a unit borrowed from another set takes the size of the theme's unit of its kind, by its solid body
			float solidWidth = Percentile(xs, 98) - Percentile(xs, 2) + 10, solidHeight = Percentile(ys, 98) - Percentile(ys, 2) + 10;
			float scale = like is (float pixels, bool byHeight) ? pixels / (byHeight ? solidHeight : solidWidth) :
				c * UnitScale * (UnitSize.TryGetValue(name, out float us) ? us : 1);
			float f = MathF.Min(scale, c * 0.95f / Math.Max(w, h));
			f = MathF.Min(f, c * 0.56f / MathF.Max(ax, w - ax));

			SKBitmap picture = Resize(raw, f);
			SKBitmap pic = new SKBitmap(new SKImageInfo(picture.Width, picture.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));

			for (int y = 0; y < picture.Height; y++)
				for (int x = 0; x < picture.Width; x++)
					pic.SetPixel(x, y, picture.GetPixel(x, y));

			picture.Dispose();

			// the painted blue player colour (compose.team_colour): saturated pixels of a blue hue
			float[] team = new float[pic.Width * pic.Height];
			float valueSum = 0, weightSum = 0;

			for (int y = 0; y < pic.Height; y++)
			{
				for (int x = 0; x < pic.Width; x++)
				{
					SKColor p = pic.GetPixel(x, y);
					p.ToHsv(out float hue, out float sat, out float val);
					// PIL's HSV: hue, saturation and value 0-255
					float hh = hue / 360f * 255f, ss = sat / 100f * 255f;
					float wgt = Math.Clamp((ss - 50) / 40, 0, 1) * Math.Clamp(1 - ((MathF.Abs(hh - 148) - 16) / 10), 0, 1);
					wgt *= p.Alpha / 255f;
					team[(y * pic.Width) + x] = wgt;
					valueSum += wgt * val / 100f;
					weightSum += wgt;
				}
			}

			float r = MathF.Min(MathF.Max(BaseRadius.TryGetValue(name, out float br) ? br : 0.3f, (half * f / c) + 0.05f), 0.47f);

			return new UnitArt
			{
				Name = name,
				Picture = pic,
				Team = team,
				TeamValue = weightSum > 0 ? Math.Max(0.2f, valueSum / weightSum) : 0.7f,
				AnchorX = ax * f,
				AnchorY = ay * f,
				BaseR = r,
				Squash = UnitSquash.TryGetValue(name, out float sq) ? sq : BaseSquash,
				Ship = ship,
				SolidWidth = solidWidth * f,
				SolidHeight = solidHeight * f,
			};
		}

		/// <summary>
		/// Art from another painted set brought nearer the theme's look: more contrast and colour, and lighter, as
		/// compose.soft_town brightens the towns (a little less, as it is already drawn to be read at game size)
		/// </summary>
		private static SKBitmap Toned(SKBitmap t)
		{
			SKBitmap result = new SKBitmap(new SKImageInfo(t.Width, t.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));

			for (int y = 0; y < t.Height; y++)
			{
				for (int x = 0; x < t.Width; x++)
				{
					SKColor p = t.GetPixel(x, y);
					float[] c = { ((p.Red - 110) * 1.18f) + 110, ((p.Green - 110) * 1.18f) + 110, ((p.Blue - 110) * 1.18f) + 110 };
					float grey = (c[0] + c[1] + c[2]) / 3;

					for (int k = 0; k < 3; k++)
					{
						c[k] = grey + ((c[k] - grey) * 1.3f);
						c[k] = 255 * MathF.Pow(Math.Clamp(c[k], 0, 255) / 255f, 0.82f);
					}

					result.SetPixel(x, y, new SKColor((byte)Math.Clamp(c[0], 0, 255), (byte)Math.Clamp(c[1], 0, 255), (byte)Math.Clamp(c[2], 0, 255), p.Alpha));
				}
			}

			return result;
		}

		/// <summary>
		/// A town with its outskirts faded out (compose.soft_town): the keyed edge pulled in and feathered and cleared of
		/// magenta spill, its own ground (gardens, earth) letting the terrain show through, and more contrast and colour
		/// than the generated painting has, so it reads at game size
		/// </summary>
		private static SKBitmap SoftTown(SKBitmap t)
		{
			int w = t.Width, h = t.Height, n = w * h;
			float[] r = new float[n], g = new float[n], b = new float[n], a = new float[n];

			for (int y = 0; y < h; y++)
			{
				for (int x = 0; x < w; x++)
				{
					SKColor p = t.GetPixel(x, y);
					int i = (y * w) + x;
					r[i] = p.Red;
					g[i] = p.Green;
					b[i] = p.Blue;
					a[i] = p.Alpha;
				}
			}

			bool[] pink = new bool[n];
			var er = new List<float>();
			var eg = new List<float>();
			var eb = new List<float>();

			for (int i = 0; i < n; i++)
			{
				pink[i] = (r[i] - g[i] > 25) && (b[i] - g[i] > 25);

				if (a[i] > 250 && !pink[i])
				{
					er.Add(r[i]);
					eg.Add(g[i]);
					eb.Add(b[i]);
				}
			}

			float mr = er.Count > 0 ? Percentile(er, 50) : 120, mg = eg.Count > 0 ? Percentile(eg, 50) : 100, mb = eb.Count > 0 ? Percentile(eb, 50) : 70;

			for (int i = 0; i < n; i++)
			{
				if (pink[i])
				{
					r[i] = mr;
					g[i] = mg;
					b[i] = mb;
				}
			}

			float[] soft = Blur(Erode(a, w, h, 3), w, h, 6);
			float[] built = new float[n];

			for (int i = 0; i < n; i++)
			{
				a[i] = MathF.Min(a[i], soft[i]);
				float bright = (r[i] + g[i] + b[i]) / 3, sat = MathF.Max(r[i], MathF.Max(g[i], b[i])) - MathF.Min(r[i], MathF.Min(g[i], b[i]));
				built[i] = (((r[i] > g[i] * 1.3f) && (r[i] > 110)) || ((bright > 165) && (sat < 70)) || (bright < 60)) ? 255 : 0;
			}

			built = Blur(Dilate(built, w, h, 2), w, h, 2);
			SKBitmap result = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));

			for (int y = 0; y < h; y++)
			{
				for (int x = 0; x < w; x++)
				{
					int i = (y * w) + x;
					float alpha = a[i] * (0.3f + (0.7f * built[i] / 255f));
					float[] c = { ((r[i] - 110) * 1.25f) + 110, ((g[i] - 110) * 1.25f) + 110, ((b[i] - 110) * 1.25f) + 110 };
					float grey = (c[0] + c[1] + c[2]) / 3;

					for (int k = 0; k < 3; k++)
					{
						c[k] = grey + ((c[k] - grey) * 1.3f);
						c[k] = 255 * MathF.Pow(Math.Clamp(c[k], 0, 255) / 255f, 0.8f);
					}

					float mean = (c[0] + c[1] + c[2]) / 3, spread = MathF.Max(c[0], MathF.Max(c[1], c[2])) - MathF.Min(c[0], MathF.Min(c[1], c[2]));
					float pale = Math.Clamp((mean - 170) / 50, 0, 1) * Math.Clamp(1 - (spread / 90), 0, 1);

					for (int k = 0; k < 3; k++)
						c[k] += (255 - c[k]) * 0.6f * pale;

					result.SetPixel(x, y, new SKColor((byte)Math.Clamp(c[0], 0, 255), (byte)Math.Clamp(c[1], 0, 255), (byte)Math.Clamp(c[2], 0, 255), (byte)Math.Clamp(alpha, 0, 255)));
				}
			}

			return result;
		}

		private static float Percentile(List<float> values, float p)
		{
			var sorted = values.OrderBy(v => v).ToList();
			float pos = (sorted.Count - 1) * p / 100f;
			int lo = (int)MathF.Floor(pos), hi = Math.Min(sorted.Count - 1, lo + 1);

			return sorted[lo] + ((sorted[hi] - sorted[lo]) * (pos - lo));
		}

		// minimum (Erode) or maximum (Dilate) over a (2r+1) square, as PIL's MinFilter and MaxFilter
		private static float[] Erode(float[] v, int w, int h, int r) => SquareFilter(v, w, h, r, true);

		private static float[] Dilate(float[] v, int w, int h, int r) => SquareFilter(v, w, h, r, false);

		private static float[] SquareFilter(float[] v, int w, int h, int r, bool min)
		{
			float[] rows = new float[v.Length], result = new float[v.Length];

			for (int y = 0; y < h; y++)
			{
				for (int x = 0; x < w; x++)
				{
					float m = min ? float.MaxValue : float.MinValue;

					for (int k = Math.Max(0, x - r); k <= Math.Min(w - 1, x + r); k++)
						m = min ? MathF.Min(m, v[(y * w) + k]) : MathF.Max(m, v[(y * w) + k]);

					rows[(y * w) + x] = m;
				}
			}

			for (int y = 0; y < h; y++)
			{
				for (int x = 0; x < w; x++)
				{
					float m = min ? float.MaxValue : float.MinValue;

					for (int k = Math.Max(0, y - r); k <= Math.Min(h - 1, y + r); k++)
						m = min ? MathF.Min(m, rows[(k * w) + x]) : MathF.Max(m, rows[(k * w) + x]);

					result[(y * w) + x] = m;
				}
			}

			return result;
		}

		// a Gaussian blur of about the given radius (PIL's GaussianBlur takes the standard deviation), edges clamped
		private static float[] Blur(float[] v, int w, int h, float sigma)
		{
			int r = (int)MathF.Ceiling(sigma * 3);
			float[] kernel = new float[(2 * r) + 1];
			float sum = 0;

			for (int i = -r; i <= r; i++)
			{
				kernel[i + r] = MathF.Exp(-(i * i) / (2 * sigma * sigma));
				sum += kernel[i + r];
			}

			for (int i = 0; i < kernel.Length; i++)
				kernel[i] /= sum;

			float[] tmp = new float[v.Length], result = new float[v.Length];

			for (int y = 0; y < h; y++)
				for (int x = 0; x < w; x++)
				{
					float s = 0;

					for (int k = -r; k <= r; k++)
						s += kernel[k + r] * v[(y * w) + Math.Clamp(x + k, 0, w - 1)];

					tmp[(y * w) + x] = s;
				}

			for (int y = 0; y < h; y++)
				for (int x = 0; x < w; x++)
				{
					float s = 0;

					for (int k = -r; k <= r; k++)
						s += kernel[k + r] * tmp[(Math.Clamp(y + k, 0, h - 1) * w) + x];

					result[(y * w) + x] = s;
				}

			return result;
		}

		#endregion
	}
}
