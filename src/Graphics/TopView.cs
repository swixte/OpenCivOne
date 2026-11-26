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
		private static readonly HashSet<string> Foot = new HashSet<string> { "phalanx", "legion", "militia", "musketeers", "riflemen", "diplomat" };
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
			["musketeers"] = 0.2f, ["riflemen"] = 0.2f, ["diplomat"] = 0.2f, ["cavalry"] = 0.33f, ["caravan"] = 0.42f,
			["cannon"] = 0.32f, ["artillery"] = 0.32f, ["armor"] = 0.34f, ["mech_infantry"] = 0.34f, ["fighter"] = 0.34f,
			["bomber"] = 0.38f, ["nuclear"] = 0.24f, ["frigate"] = 0.42f, ["ironclad"] = 0.42f, ["cruiser"] = 0.44f,
			["battleship"] = 0.46f, ["submarine"] = 0.42f, ["carrier"] = 0.46f, ["transport"] = 0.44f,
		};
		// how round a base looks from the camera's height; ships are painted from lower down, so theirs are flatter
		private const float BaseSquash = 0.62f;
		private static readonly Dictionary<string, float> UnitSquash = new Dictionary<string, float> { ["trireme"] = 0.4f, ["sail"] = 0.48f, ["frigate"] = 0.46f,
			["ironclad"] = 0.44f, ["cruiser"] = 0.42f, ["battleship"] = 0.42f, ["submarine"] = 0.4f, ["carrier"] = 0.42f, ["transport"] = 0.44f };

		// the share of a cell each step of the town ladder covers: a hamlet, three steps of towns, a city, a capital
		// a hamlet a little larger than the renders have it, so it reads at game size next to the units' bases
		private static readonly float[] StepSize = { 0.5f, 0.58f, 0.7f, 0.84f, 0.98f, 0.98f };
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
		private readonly Dictionary<string, SKBitmap> townVariants = new Dictionary<string, SKBitmap>();
		// coastal harbors: 4 size tiers (0=hamlet landing, 1=town wharf, 2=stone port, 3=classical harbor) x 8 shores (0=E, 1=W, 2=S, 3=N, 4=SE, 5=SW, 6=NE, 7=NW)
		private readonly SKBitmap?[,] ports = new SKBitmap?[4, 8];
		private readonly SKTypeface face;
		private readonly SKTypeface badgeFace;
		// huts and special resources (by terrain id, ResourceNames), painted into the view in place of the classic sprites
		private SKBitmap? hut;
		private readonly SKBitmap?[] resources = new SKBitmap?[ResourceNames.Length];
		private readonly bool[] integratedResources = new bool[ResourceNames.Length];
		private SKBitmap? mineSlope, mineDesert;
		private GBitmap.SmokeSource[] smokeSources = Array.Empty<GBitmap.SmokeSource>();
		private readonly List<SKBitmap> roadTrees = new List<SKBitmap>();
		private readonly List<SKBitmap> roadJungle = new List<SKBitmap>();
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
			this.badgeFace = SansFace();
		}

		public GBitmap.SmokeSource[] SmokeSources => this.smokeSources;

		public GBitmap.SmokeSource[] LastSmallSmokeSources { get; private set; } = Array.Empty<GBitmap.SmokeSource>();

		public int Width => this.width;

		public int Height => this.height;

		public uint[] Pixels => this.pixels;

		public bool Blends => true;

		public bool DrawsCities => true;

		public bool DrawsUnit(int type) => type >= 0 && type < this.units.Length && this.units[type] != null;

		public bool DrawsFeatures => this.hut != null && this.resources.All(r => r != null) && this.shield != null;

		public bool DrawsImprovements => this.DrawsFeatures;

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
			// the look of the theme's own units, measured, so units from another set can be brought to it
			(float Mean, float Spread, float Saturation) look = PaintLook(view.units.Where(u => u != null).Select(u => u!.Picture));

			foreach ((int type, string path) in borrowed)
			{
				string name = UnitNames[type];
				string like = Ships.Contains(name) ? "sail" : Foot.Contains(name) || name == "diplomat" ? "phalanx" :
					name == "cavalry" ? "knights" : name == "caravan" ? "settlers" : "catapult";
				int likeType = Array.IndexOf(UnitNames, like);
				UnitArt? reference = view.units[likeType] ?? view.units.FirstOrDefault(u => u != null);
				bool byHeight = Foot.Contains(name) || name == "diplomat" || name == "cavalry";
				(float, bool)? size = reference == null ? null : ((byHeight ? reference.SolidHeight : reference.SolidWidth), byHeight);
				view.units[type] = LoadUnit(path, name, c, true, size, look);
			}


			// a hut a little over half a cell across, a resource a little under, as a print paints them
			(string? hutPath, bool hutOwn) = Find("hut");

			if (hutPath != null && Decode(hutPath) is SKBitmap hutRaw)
			{
				using SKBitmap toned = hutOwn ? hutRaw : Toned(hutRaw);
				using SKBitmap sized = Resize(toned, c * (hutOwn ? 0.52f : 0.44f) / Math.Max(toned.Width, toned.Height));
				view.hut = CrispTown(sized, 0.34f);

				if (!hutOwn)
					hutRaw.Dispose();
			}

			for (int i = 0; i < ResourceNames.Length; i++)
			{
				(string? path, bool own) = Find($"res_{ResourceNames[i]}");

				if (path != null && Decode(path) is SKBitmap raw)
				{
					using (raw)
					{
						using SKBitmap toned0 = own ? raw.Copy() : Toned(raw);
						// borrowed fish sit cold and silver on the warm sea, and coal and peat are near-black lumps on the
						// yellow ground: warm and lift them when borrowed, and draw fish smaller
						string res = ResourceNames[i];
						using SKBitmap toned = !own && res == "fish" ? Graded(toned0, 0.25f, 1.12f, 0.98f, 0.8f) :
							!own && res is "coal" or "peat" ? Graded(toned0, 0.38f, 1.14f, 1.0f, 0.82f) : toned0.Copy();
						using SKBitmap sized = Resize(toned, c * (res == "fish" ? (own ? 0.42f : 0.36f) : 0.46f) / Math.Max(toned.Width, toned.Height));
						view.resources[i] = (own && res == "fish") ? CrispTown(sized, 0.25f) : sized.Copy();
					}
				}
			}

			bool HasPaintedSet(string folderName)
			{
				string dir = Path.Combine(painted, folderName);
				return Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*.png").Any();
			}

			// Terrain-integrated mineral formations painted directly into PaintedMap skip floating resource icons
			view.integratedResources[4] = HasPaintedSet("hills_coal");
			view.integratedResources[5] = HasPaintedSet("mountains_gold");
			view.integratedResources[9] = HasPaintedSet("jungle_gems");

			SKBitmap? LoadMineSprite(string name, float cellScale)
			{
				(string? path, bool own) = Find(name);
				if (path == null || Decode(path) is not SKBitmap raw)
					return null;
				using (raw)
				{
					using SKBitmap toned = own ? raw.Copy() : Toned(raw);
					using SKBitmap sized = Resize(toned, c * cellScale / Math.Max(toned.Width, toned.Height));
					return CrispTown(sized, 0.34f);
				}
			}

			view.mineSlope = LoadMineSprite("mine_slope", 0.48f);
			view.mineDesert = LoadMineSprite("mine_desert", 0.50f);

			void LoadCanopySet(string folderName, List<SKBitmap> list, float scale)
			{
				string dir = Path.Combine(painted, folderName);
				if (!Directory.Exists(dir))
					return;
				foreach (string p in Directory.GetFiles(dir, "*.png").OrderBy(x => x).Take(6))
				{
					if (Decode(p) is SKBitmap raw)
						using (raw)
							list.Add(Resize(raw, c * scale / Math.Max(raw.Width, raw.Height)));
				}
			}

			LoadCanopySet("trees", view.roadTrees, 0.27f);
			LoadCanopySet("jungle", view.roadJungle, 0.29f);

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

					using SKBitmap soft = SoftTown(raw, w == 1 && step > 0);
					float f = c * StepSize[step] / Math.Max(soft.Width, soft.Height);
					using SKBitmap sized = Resize(soft, f);
					view.towns[step, w] = CrispTown(sized);
				}
			}

			if (Directory.Exists(ladder))
			{
				foreach (string dir in Directory.GetDirectories(ladder))
				{
					string folder = Path.GetFileName(dir).ToLowerInvariant();

					for (int step = 0; step < 6; step++)
					{
						for (int w = 0; w < 2; w++)
						{
							string suffix = w == 1 ? "w" : "o";
							string path = Path.Combine(dir, $"t{step}_{suffix}.png");

							if (!File.Exists(path))
								continue;

							using SKBitmap? raw = Decode(path);

							if (raw == null)
								continue;

							using SKBitmap soft = SoftTown(raw, w == 1 && step > 0);
							float f = c * StepSize[step] / Math.Max(soft.Width, soft.Height);
							using SKBitmap sized = Resize(soft, f);
							view.townVariants[$"{folder}/t{step}_{suffix}"] = CrispTown(sized);
						}
					}
				}
			}

			// coastal harbors across 4 size tiers (port_{0,1,2,3}_{e,w,s,n,se,sw,ne,nw}.png): sized relative to painted coastal villages & towns so harbors are unmistakable without dwarfing cities
			string[] dirSuffix = { "e", "w", "s", "n", "se", "sw", "ne", "nw" };
			float[] portScale = { 0.42f, 0.50f, 0.54f, 0.64f };

			for (int tier = 0; tier < 4; tier++)
			{
				for (int d = 0; d < 8; d++)
				{
					string path = Path.Combine(objects, $"port_{tier}_{dirSuffix[d]}.png");

					if (!File.Exists(path) || Decode(path) is not SKBitmap raw)
						continue;

					// keep raw width/height scaling so multi-pier harbors preserve proper house roof & vessel proportion,
					// and scale North/South to match the cross-axis span cleanly
					using (raw)
						view.ports[tier, d] = Resize(raw, c * portScale[tier] / Math.Max(raw.Width, raw.Height));
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

		private SKBitmap? TownArt((string Style, int Age) look, int step, bool walled)
		{
			if (this.townVariants.Count > 0)
			{
				int age = Math.Clamp(look.Age, 0, 3);
				string style = string.IsNullOrEmpty(look.Style) ? "cl" : look.Style.ToLowerInvariant();
				string[] wallOrder = age >= 2 ? new[] { "o", "w" } : (walled ? new[] { "w", "o" } : new[] { "o", "w" });

				SKBitmap? TryFolderStep(string folder, int st)
				{
					foreach (string w in wallOrder)
						if (this.townVariants.TryGetValue($"{folder}/t{st}_{w}", out SKBitmap? art))
							return art;
					return null;
				}

				SKBitmap? TryFolder(string folder) => TryFolderStep(folder, step) ?? (step == 5 ? TryFolderStep(folder, 4) : null);

				if (step == 0 && age <= 1)
				{
					SKBitmap? hut = TryFolder($"{style}_a{age}") ?? TryFolder(style);
					if (hut != null)
						return hut;
					return this.towns[0, walled ? 1 : 0] ?? this.towns[0, 0];
				}

				if (age >= 2)
				{
					SKBitmap? modernOrInd = TryFolder($"{style}_a{age}") ?? TryFolder($"a{age}");
					if (modernOrInd != null)
						return modernOrInd;

					if (age == 3)
					{
						SKBitmap? prev = TryFolder($"{style}_a2") ?? TryFolder("a2");
						if (prev != null)
							return prev;
					}
				}
				else
				{
					SKBitmap? early = TryFolder($"{style}_a{age}");
					if (early != null)
						return early;
				}

				SKBitmap? styled = TryFolder(style);
				if (styled != null)
					return styled;
			}

			return this.towns[step, walled ? 1 : 0] ?? this.towns[step, 0];
		}

		public bool Render(float cx, float cy, IsoView.Scene scene)
		{
			long sig = 17;

			unchecked
			{
				sig = (sig * 31) + (long)(cx * 16) + ((long)(cy * 16) << 20);

				foreach (var c in scene.Cities)
					sig = (sig * 37) + (c.X * 1000) + (c.Y * 10) + c.Size + c.Name.GetHashCode() + c.Colour + (scene.Walls(Wrap(c.X), c.Y) ? 7 : 0) + scene.CityLook(Wrap(c.X), c.Y).GetHashCode();

				if (this.DrawsFeatures)
				{
					for (int y = (int)MathF.Floor(cy - (this.rows / 2)) - 1; y <= cy + (this.rows / 2) + 1; y++)
					{
						for (int x = (int)MathF.Floor(cx - (this.columns / 2)) - 1; x <= cx + (this.columns / 2) + 1; x++)
						{
							if (y < 0 || y >= MapHeight)
								continue;

							sig = (sig * 7) + (scene.Explored(Wrap(x), y) ? scene.Feature(Wrap(x), y) + (scene.Terrain(Wrap(x), y) * 4) +
								(scene.ShieldBonus(Wrap(x), y) ? 64 : 0) + ((scene.Improvements(Wrap(x), y) & 0x7f) * 128) : 99);
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
			this.pixels = this.Paint(cx, cy, scene, null, null, out this.smokeSources);

			return true;
		}

		/// <summary>
		/// The towns, units and features of a small map, such as the city screen's 5 x 5 cells, centred on (cx, cy) in map
		/// cells and the given number of cells across and down; without town names
		/// </summary>
		public uint[] RenderSmall(float cx, float cy, int columns, int rows, IsoView.Scene scene)
		{
			uint[] result = this.Paint(cx, cy, scene, null, null, columns * this.cell, rows * this.cell, false, out GBitmap.SmokeSource[] smallSmoke);
			this.LastSmallSmokeSources = smallSmoke;
			return result;
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
				(x, y) => (x, y) == from ? ((to.X - from.X) * t, (to.Y - from.Y) * t, 1, false) : null, null, out _);
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

					// sparks flying out from the clash: small soft dabs of warm light, not lines
					for (int i = 0; i < 10; i++)
					{
						double angle = rng.NextDouble() * Math.PI * 2;
						float len = c * (0.12f + (0.25f * (float)rng.NextDouble())) * (0.4f + (0.6f * k));
						float ax = (float)Math.Cos(angle), ay = (float)Math.Sin(angle) * 0.7f;
						PaintDab(canvas, mx + (ax * len), my + (ay * len), c * (0.022f + (0.012f * (float)rng.NextDouble())),
							new SKColor(255, (byte)(206 + rng.Next(40)), (byte)(120 + rng.Next(60)), a), c * 0.012f);
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
						float r = c * (0.08f + (0.15f * kk));
						float alpha = 0.8f * MathF.Sin(kk * MathF.PI);
						float px = lx + ox + (ox * kk), py = ly - rise;
						// a billow of grey smoke built of several small, uneven dabs, as gouache is laid on: dark underneath,
						// a mid grey body, pale where the light catches the top left
						for (int dab = 0; dab < 5; dab++)
						{
							float dx2 = ((float)rng.NextDouble() - 0.5f) * r * 1.1f, dy2 = ((float)rng.NextDouble() - 0.5f) * r * 0.8f;
							float rr = r * (0.45f + (0.3f * (float)rng.NextDouble()));
							PaintDab(canvas, px + dx2 + (rr * 0.18f), py + dy2 + (rr * 0.25f), rr, new SKColor(66, 64, 66, (byte)(110 * alpha)), rr * 0.6f);
							PaintDab(canvas, px + dx2, py + dy2, rr * 0.8f, new SKColor(122, 120, 120, (byte)(120 * alpha)), rr * 0.55f);
							PaintDab(canvas, px + dx2 - (rr * 0.25f), py + dy2 - (rr * 0.3f), rr * 0.42f, new SKColor(206, 202, 192, (byte)(110 * alpha)), rr * 0.45f);
						}
					}
				}
			}

			return this.Paint(this.lastCentre.X, this.lastCentre.Y, this.lastScene, (x, y) => Pose(x, y), Effects, out _);
		}

		/// <summary>
		/// The grassland production-bonus marker (+1 shield in Civ1): a quiet, grounded weathered-stone &amp; muted antique-bronze
		/// shield emblem nestled into the grass so it stays readable for gameplay without clashing like a bright red sign
		/// </summary>
		private static void PaintShield(SKCanvas canvas, float x, float y, int c)
		{
			float u = c / 64f, w = 10.2f * u, h = 11.4f * u;

			SKPath Outline(float dx, float dy, float k)
			{
				var path = new SKPath();
				float l = x - (w * k / 2) + dx, r = x + (w * k / 2) + dx, t = y - (h * k / 2) + dy, b = y + (h * k / 2) + dy;
				path.MoveTo(l, t);
				path.LineTo(r, t);
				path.LineTo(r, t + (h * k * 0.45f));
				path.QuadTo(r, t + (h * k * 0.85f), (l + r) / 2, b);
				path.QuadTo(l, t + (h * k * 0.85f), l, t + (h * k * 0.45f));
				path.Close();
				return path;
			}

			using SKPath shadow = Outline(0.8f * u, 1.1f * u, 1.08f), rim = Outline(0, 0, 1.0f), body = Outline(0, 0, 0.72f), lit = Outline(-0.35f * u, -0.45f * u, 0.36f);
			using SKPaint shadowPaint = new SKPaint { IsAntialias = true, Color = new SKColor(16, 20, 10, 155), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 1.1f * u) };
			using SKPaint rimPaint = new SKPaint { IsAntialias = true, Color = new SKColor(238, 214, 144, 248), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 0.15f * u) };
			using SKPaint bodyPaint = new SKPaint { IsAntialias = true, Color = new SKColor(48, 58, 64, 240), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 0.15f * u) };
			using SKPaint litPaint = new SKPaint { IsAntialias = true, Color = new SKColor(246, 226, 162, 225), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 0.35f * u) };
			canvas.DrawPath(shadow, shadowPaint);
			canvas.DrawPath(rim, rimPaint);
			canvas.DrawPath(body, bodyPaint);
			canvas.DrawPath(lit, litPaint);
		}

		private static float Hash01(int x, int y, int seed)
		{
			uint h = (uint)((x * 374761393) ^ (y * 668265263) ^ (seed * 1442695041));
			h = (h ^ (h >> 13)) * 1274126177u;
			return ((h ^ (h >> 16)) & 0xffff) / 65535f;
		}

		/// <summary>
		/// Painted gouache farmland patchwork for irrigated tiles: seamless cross-cell warped meadows, brushed furrow waves,
		/// soft hedgerows, arid desert oasis agriculture with narrow irrigation ditches, hill terraces, and exact water clipping
		/// </summary>
		private static void PaintIrrigation(SKCanvas canvas, float left, float top, float cols, float rows, int c, IsoView.Scene scene)
		{
			const int CityFlag = 0x1, IrrigFlag = 0x2, ForestTerrain = 3, MountainsTerrain = 5, JungleTerrain = 9, RiverTerrain = 11, DesertTerrain = 0, HillsTerrain = 4;
			float u = c / 64f;
			int yMin = Math.Max(0, (int)MathF.Floor(top) - 1), yMax = Math.Min(MapHeight - 1, (int)MathF.Ceiling(top + rows) + 1);
			int xMin = (int)MathF.Floor(left) - 1, xMax = (int)MathF.Ceiling(left + cols) + 1;

			bool IsIrrig(int gx, int gy)
			{
				if (gy < 0 || gy >= MapHeight)
					return false;
				int wgx = Wrap(gx);
				if (!scene.Explored(wgx, gy))
					return false;
				int imp = scene.Improvements(wgx, gy);
				return (imp & IrrigFlag) != 0 && (imp & CityFlag) == 0 && scene.Terrain(wgx, gy) != Water;
			}

			bool NearWater(int gx, int gy)
			{
				for (int dy = -1; dy <= 1; dy++)
				{
					int ny = gy + dy;
					if (ny < 0 || ny >= MapHeight)
						return true;
					for (int dx = -1; dx <= 1; dx++)
					{
						int t = scene.Terrain(Wrap(gx + dx), ny);
						if (t == Water || t == RiverTerrain)
							return true;
					}
				}
				return false;
			}

			float WarpWeight(float sx, float sy, int cxCell, int cyCell, out bool desertSource)
			{
				float best = 0f;
				desertSource = false;
				float rx = sx - (cxCell + 0.5f), ry = sy - (cyCell + 0.5f);
				// Continuous superellipse across irrigated union so warped edges flow smoothly across cell lines without square chops
				for (int dj = -1; dj <= 1; dj++)
				{
					for (int di = -1; di <= 1; di++)
					{
						if (!IsIrrig(cxCell + di, cyCell + dj))
							continue;
						float lx = MathF.Abs(rx - di) / 0.57f;
						float ly = MathF.Abs(ry - dj) / 0.57f;
						float d = MathF.Pow(MathF.Pow(lx, 3.2f) + MathF.Pow(ly, 3.2f), 1f / 3.2f);
						float w = Math.Clamp((1.12f - d) / 0.24f, 0f, 1f);
						w = w * w * (3f - (2f * w));
						if (w > best)
						{
							best = w;
							int srcY = Math.Clamp(cyCell + dj, 0, MapHeight - 1);
							desertSource = scene.Terrain(Wrap(cxCell + di), srcY) == DesertTerrain;
						}
					}
				}
				return best;
			}

			using SKBitmap cellBmp = new SKBitmap(new SKImageInfo(c, c, SKColorType.Bgra8888, SKAlphaType.Unpremul));
			using SKPaint imgPaint = new SKPaint { IsAntialias = true };
			using SKPaint shrubPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

			const int MaskN = 16;
			float[] waterGrid = new float[(MaskN + 1) * (MaskN + 1)];

			for (int y = yMin; y <= yMax; y++)
			{
				for (int x = xMin; x <= xMax; x++)
				{
					int wx = Wrap(x);
					if (!scene.Explored(wx, y))
						continue;

					int imp = scene.Improvements(wx, y), terrain = scene.Terrain(wx, y);
					if ((imp & CityFlag) != 0 || terrain == Water || terrain == MountainsTerrain)
						continue;

					bool ownIrrig = (imp & IrrigFlag) != 0;
					if (!ownIrrig)
					{
						// Spillover across cell boundary flows ONLY into compatible open dry land (not Forests, Jungle, Mountains, Water)
						if (terrain == ForestTerrain || terrain == JungleTerrain)
							continue;
						bool adjIrrig = false;
						for (int dj = -1; dj <= 1 && !adjIrrig; dj++)
							for (int di = -1; di <= 1 && !adjIrrig; di++)
								adjIrrig = (di != 0 || dj != 0) && IsIrrig(x + di, y + dj);
						if (!adjIrrig)
							continue;
					}

					bool clipWater = NearWater(x, y);
					if (clipWater)
					{
						for (int gj = 0; gj <= MaskN; gj++)
						{
							float my = y + (gj / (float)MaskN);
							for (int gi = 0; gi <= MaskN; gi++)
							{
								float mx = x + (gi / (float)MaskN);
								waterGrid[(gj * (MaskN + 1)) + gi] = scene.WaterKindAt(mx, my) != 0 ? 0f : 1f;
							}
						}
					}

					float hillAlphaMul = terrain == HillsTerrain ? 0.74f : 0.84f;

					for (int py = 0; py < c; py++)
					{
						float fy = (py + 0.5f) / c;
						float gy = y + fy;
						float wyGrid = fy * MaskN;
						int y0 = Math.Clamp((int)wyGrid, 0, MaskN - 1);
						float ty = wyGrid - y0;

						for (int px = 0; px < c; px++)
						{
							float fx = (px + 0.5f) / c;
							float gx = x + fx;

							// Smooth world-space organic coordinate warp continuous across neighboring tiles
							float ox = (0.075f * MathF.Sin((gy * 6.8f) + (gx * 1.9f))) + (0.032f * MathF.Cos((gy * 15.3f) - (gx * 6.1f)));
							float oy = (0.075f * MathF.Cos((gx * 6.6f) - (gy * 2.1f))) + (0.032f * MathF.Sin((gx * 14.9f) + (gy * 6.4f)));
							float sx = gx + ox, sy = gy + oy;

							float fieldMask = WarpWeight(sx, sy, x, y, out bool desertSource);
							if (fieldMask <= 0.02f)
							{
								cellBmp.SetPixel(px, py, SKColors.Transparent);
								continue;
							}

							bool isDesert = terrain == DesertTerrain || desertSource;

							// Staggered brick-bond crop plots in warped coordinates (~2.15 plots across a cell)
							const float Freq = 2.15f;
							float pyScaled = sy * Freq;
							int pv = (int)MathF.Floor(pyScaled);
							float pxScaled = (sx * Freq) + ((pv & 1) != 0 ? 0.38f : 0f);
							int pu = (int)MathF.Floor(pxScaled);

							float fu = pxScaled - pu, fv = pyScaled - pv;
							float distBorder = MathF.Min(MathF.Min(fu, 1f - fu), MathF.Min(fv, 1f - fv));

							int wpu = ((pu % 172) + 172) % 172;
							float pick = Hash01(wpu, pv, 51);
							float rCol, gCol, bCol;

							if (isDesert)
							{
								// Sun-baked arid alluvial barley, sage & dry ochre plots blended naturally into warm sand
								(rCol, gCol, bCol) = pick < 0.30f ? (124f, 146f, 66f) :
									pick < 0.56f ? (148f, 160f, 78f) :
									pick < 0.78f ? (202f, 178f, 92f) :
									pick < 0.90f ? (212f, 190f, 114f) : (182f, 148f, 86f);
							}
							else
							{
								(rCol, gCol, bCol) = pick < 0.28f ? (212f, 184f, 82f) :
									pick < 0.52f ? (188f, 170f, 78f) :
									pick < 0.76f ? (126f, 154f, 58f) :
									pick < 0.90f ? (106f, 144f, 52f) : (168f, 134f, 68f);
							}

							// Brushed directional crop furrows inside each plot
							bool horiz = Hash01(wpu, pv, 53) < 0.5f;
							float fCoord = horiz ? fv : fu;
							float wave = MathF.Sin((fCoord * 28f) + (terrain == HillsTerrain ? MathF.Sin(fu * MathF.PI) * 3.5f : 0f));
							rCol += wave * (isDesert ? 13f : 16f);
							gCol += wave * (isDesert ? 11f : 14f);
							bCol += wave * 6f;

							// Soft hedgerow & earthen lane along plot dividers
							float hedge = Math.Clamp((0.11f - distBorder) / 0.11f, 0f, 1f);
							if (hedge > 0f)
							{
								float wob = 0.72f + (0.28f * MathF.Sin((sx * 31f) + (sy * 27f)));
								float hw = hedge * (isDesert ? 0.52f : 0.68f) * wob;
								float lr = isDesert ? 156f : (distBorder < 0.032f ? 174f : 62f);
								float lg = isDesert ? 128f : (distBorder < 0.032f ? 156f : 82f);
								float lb = isDesert ? 76f : (distBorder < 0.032f ? 104f : 34f);
								rCol += (lr - rCol) * hw;
								gCol += (lg - gCol) * hw;
								bCol += (lb - bCol) * hw;
							}

							// Soft outer verge rim where fields meet un-irrigated ground (warm sandy earth in desert, olive hedge on grass)
							if (fieldMask < 0.85f && fieldMask > 0.12f)
							{
								float rim = MathF.Sin(Math.Clamp((fieldMask - 0.12f) / 0.73f, 0f, 1f) * MathF.PI) * (isDesert ? 0.42f : 0.54f);
								float orR = isDesert ? 156f : 58f, orG = isDesert ? 128f : 76f, orB = isDesert ? 76f : 32f;
								rCol += (orR - rCol) * rim;
								gCol += (orG - gCol) * rim;
								bCol += (orB - bCol) * rim;
							}

							float alpha = (isDesert ? 0.84f : hillAlphaMul) * fieldMask;
							if (clipWater)
							{
								float wxG = fx * MaskN;
								int x0 = Math.Clamp((int)wxG, 0, MaskN - 1);
								float tx = wxG - x0;
								int idx = (y0 * (MaskN + 1)) + x0;
								float dry = ((waterGrid[idx] * (1f - tx)) + (waterGrid[idx + 1] * tx)) * (1f - ty) +
									((waterGrid[idx + MaskN + 1] * (1f - tx)) + (waterGrid[idx + MaskN + 2] * tx)) * ty;

								float wMask = Math.Clamp((dry - 0.12f) / 0.42f, 0f, 1f);
								wMask = wMask * wMask * (3f - (2f * wMask));
								if (wMask <= 0.01f)
								{
									cellBmp.SetPixel(px, py, SKColors.Transparent);
									continue;
								}
								alpha *= wMask;
							}

							byte br = (byte)Math.Clamp(rCol, 0, 255);
							byte bg = (byte)Math.Clamp(gCol, 0, 255);
							byte bb = (byte)Math.Clamp(bCol, 0, 255);
							byte ba = (byte)Math.Clamp(255f * alpha, 0, 255);
							cellBmp.SetPixel(px, py, new SKColor(br, bg, bb, ba));
						}
					}

					float scrX = MathF.Round((x - left) * c), scrY = MathF.Round((y - top) * c);
					canvas.DrawBitmap(cellBmp, scrX, scrY, imgPaint);

					// Subtle hedgerow & date-palm shrub dabs on owned irrigated cells (never in water)
					if (ownIrrig)
					{
						int dabCount = terrain == DesertTerrain ? ((wx + y) % 2 == 0 ? 2 : 1) : 2;
						for (int db = 0; db < dabCount; db++)
						{
							float dfx = 0.26f + (Hash01(wx, y, 210 + db) * 0.48f);
							float dfy = 0.26f + (Hash01(wx, y, 220 + db) * 0.48f);
							if (scene.WaterKindAt(x + dfx, y + dfy) != 0)
								continue;
							float dpx = scrX + (dfx * c), dpy = scrY + (dfy * c);
							float sr = (terrain == DesertTerrain ? 1.55f : 1.6f) * u;
							shrubPaint.Color = terrain == DesertTerrain ? new SKColor(86, 108, 52, 165) : new SKColor(68, 92, 36, 170);
							canvas.DrawCircle(dpx, dpy, sr, shrubPaint);
							shrubPaint.Color = terrain == DesertTerrain ? new SKColor(136, 148, 84, 130) : new SKColor(126, 158, 66, 150);
							canvas.DrawCircle(dpx - (0.35f * sr), dpy - (0.35f * sr), sr * 0.52f, shrubPaint);
						}
					}
				}
			}
		}

		/// <summary>
		/// Painted gouache country roads (warm ochre dirt tracks with earthy umber verges), railroads (gravel ballast,
		/// timber sleepers and twin dark-iron rails), arched bridges over rivers, and overhanging woodland canopy crowns
		/// </summary>
		private void PaintRoads(SKCanvas canvas, float left, float top, float cols, float rows, int c, IsoView.Scene scene)
		{
			const int CityFlag = 0x1, ForestTerrain = 3, MountainsTerrain = 5, JungleTerrain = 9, RoadFlag = 0x8, RailFlag = 0x10;
			float u = c / 64f;

			(float X, float Y) Point(int x, int y, int imp)
			{
				float px = ((x - left) + 0.5f) * c, py = ((y - top) + 0.5f) * c;

				// roads meet a city cleanly at its centre; open-land waypoints meander slightly through mountain passes
				// the same course as the painted map's lane for the road (PaintedMap.RoadWaypoint)
				int wxp = Wrap(x);
				(float ox, float oy) = PaintedMap.RoadWaypoint(wxp, y, (imp & CityFlag) != 0, y >= 0 && y < MapHeight && scene.Terrain(wxp, y) == MountainsTerrain, c);
				px += ox;
				py += oy;

				return (px, py);
			}

			var roadSegs = new List<(float X0, float Y0, float Mx, float My, float X1, float Y1)>();
			var railSegs = new List<(float X0, float Y0, float Mx, float My, float X1, float Y1)>();
			var soloRoad = new List<(float X, float Y)>();
			var soloRail = new List<(float X, float Y)>();

			int yMin = Math.Max(0, (int)MathF.Floor(top) - 1), yMax = Math.Min(MapHeight - 1, (int)MathF.Ceiling(top + rows) + 1);
			int xMin = (int)MathF.Floor(left) - 1, xMax = (int)MathF.Ceiling(left + cols) + 1;
			(int Dx, int Dy)[] forward = { (1, 0), (-1, 1), (0, 1), (1, 1) };
			(int Dx, int Dy)[] allDirs = { (-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1) };

			for (int y = yMin; y <= yMax; y++)
			{
				for (int x = xMin; x <= xMax; x++)
				{
					int wx = Wrap(x);

					if (!scene.Explored(wx, y))
						continue;

					int imp = scene.Improvements(wx, y);

					if ((imp & (RoadFlag | RailFlag)) == 0)
						continue;

					(float ax, float ay) = Point(x, y, imp);
					bool hasNeighbour = false;

					foreach ((int dx, int dy) in allDirs)
					{
						int ny = y + dy;

						if (ny < 0 || ny >= MapHeight)
							continue;

						int nwx = Wrap(x + dx);

						if (scene.Explored(nwx, ny) && (scene.Improvements(nwx, ny) & (RoadFlag | RailFlag | CityFlag)) != 0)
						{
							hasNeighbour = true;
							break;
						}
					}

					if (!hasNeighbour)
					{
						if ((imp & RailFlag) != 0)
							soloRail.Add((ax, ay));
						else
							soloRoad.Add((ax, ay));
					}

					foreach ((int dx, int dy) in forward)
					{
						int nx = x + dx, ny = y + dy;

						if (ny < 0 || ny >= MapHeight)
							continue;

						int nwx = Wrap(nx);

						if (!scene.Explored(nwx, ny))
							continue;

						int nImp = scene.Improvements(nwx, ny);

						if ((nImp & (RoadFlag | RailFlag | CityFlag)) == 0)
							continue;

						// skip diagonal shortcut when either orthogonal corner already bridges the two cells
						if (dx != 0 && dy != 0)
						{
							int impA = scene.Improvements(Wrap(x + dx), y), impB = scene.Improvements(wx, y + dy);

							if ((impA & (RoadFlag | RailFlag | CityFlag)) != 0 || (impB & (RoadFlag | RailFlag | CityFlag)) != 0)
								continue;
						}

						(float bx, float by) = Point(nx, ny, nImp);
						float mx = (ax + bx) / 2, my = (ay + by) / 2;
						float px = -(by - ay), py = bx - ax;
						float len = MathF.Sqrt((px * px) + (py * py));

						if (len > 1)
						{
							float bend = PaintedMap.RoadBend(wx, y, nwx, ny, c);
							mx += px / len * bend;
							my += py / len * bend;
						}

						bool isRail = ((imp & (RailFlag | CityFlag)) != 0) && ((nImp & (RailFlag | CityFlag)) != 0) &&
							(((imp & RailFlag) != 0) || ((nImp & RailFlag) != 0));

						if (isRail)
							railSegs.Add((ax, ay, mx, my, bx, by));
						else
							roadSegs.Add((ax, ay, mx, my, bx, by));
					}
				}
			}

			// Dirt roads: hand-painted warm ochre country lane made of overlapping soft brushwork dabs along the curve
			if (roadSegs.Count > 0 || soloRoad.Count > 0)
			{
				using SKPath roadPath = new SKPath();

				foreach (var s in roadSegs)
				{
					roadPath.MoveTo(s.X0, s.Y0);
					roadPath.QuadTo(s.Mx, s.My, s.X1, s.Y1);
				}

				using SKPaint verge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round, StrokeWidth = 6.0f * u, Color = new SKColor(58, 40, 20, 120), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 1.2f * u) };
				using SKPaint bed = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round, StrokeWidth = 3.4f * u, Color = new SKColor(176, 146, 98, 225), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 0.6f * u) };

				if (roadSegs.Count > 0)
				{
					canvas.DrawPath(roadPath, verge);
					canvas.DrawPath(roadPath, bed);

					// painterly texture along each curve: slightly irregular warm ochre & sunlit cream dabs
					foreach (var s in roadSegs)
					{
						float dist = MathF.Sqrt(((s.X1 - s.X0) * (s.X1 - s.X0)) + ((s.Y1 - s.Y0) * (s.Y1 - s.Y0)));
						int steps = Math.Max(6, (int)MathF.Round(dist / (3.4f * u)));

						for (int i = 0; i <= steps; i++)
						{
							float t = i / (float)steps, it = 1 - t;
							float qx = (it * it * s.X0) + (2 * it * t * s.Mx) + (t * t * s.X1);
							float qy = (it * it * s.Y0) + (2 * it * t * s.My) + (t * t * s.Y1);
							float j1 = Hash01((int)qx, (int)qy, 7) - 0.5f, j2 = Hash01((int)qx, (int)qy, 11) - 0.5f;
							float rDab = (1.6f + (0.9f * Hash01((int)qx, (int)qy, 13))) * u;
							SKColor dabCol = Mix(new SKColor(160, 126, 78, 150), new SKColor(206, 180, 132, 165), Hash01((int)qx, (int)qy, 17));
							PaintDab(canvas, qx + (j1 * 1.4f * u), qy + (j2 * 1.4f * u), rDab, dabCol, 0.5f * u);
						}
					}
				}

				foreach ((float sx, float sy) in soloRoad)
				{
					PaintDab(canvas, sx, sy, 4.8f * u, new SKColor(58, 40, 20, 165), 1.0f * u);
					PaintDab(canvas, sx, sy, 3.1f * u, new SKColor(202, 170, 112, 238), 0.5f * u);
					PaintDab(canvas, sx - (0.4f * u), sy - (0.4f * u), 1.5f * u, new SKColor(236, 212, 158, 200), 0.35f * u);
				}
			}

			// Railroads: gravel ballast, timber sleepers, twin dark-iron rails
			if (railSegs.Count > 0 || soloRail.Count > 0)
			{
				using SKPath railPath = new SKPath();

				foreach (var s in railSegs)
				{
					railPath.MoveTo(s.X0, s.Y0);
					railPath.QuadTo(s.Mx, s.My, s.X1, s.Y1);
				}

				using SKPaint ballast = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round, StrokeWidth = 6.8f * u, Color = new SKColor(108, 96, 78, 195), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 0.7f * u) };
				using SKPaint tiePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeWidth = 1.25f * u, Color = new SKColor(66, 46, 28, 225) };
				using SKPaint railDark = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeWidth = 1.2f * u, Color = new SKColor(46, 48, 52, 240) };
				using SKPaint railGlint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeWidth = 0.55f * u, Color = new SKColor(204, 206, 200, 165) };

				if (railSegs.Count > 0)
				{
					canvas.DrawPath(railPath, ballast);

					foreach (var s in railSegs)
					{
						float dist = MathF.Sqrt(((s.X1 - s.X0) * (s.X1 - s.X0)) + ((s.Y1 - s.Y0) * (s.Y1 - s.Y0)));
						int steps = Math.Max(4, (int)MathF.Round(dist / (4.4f * u)));
						using SKPath leftRail = new SKPath(), rightRail = new SKPath();

						for (int i = 0; i <= steps; i++)
						{
							float t = i / (float)steps, it = 1 - t;
							float qx = (it * it * s.X0) + (2 * it * t * s.Mx) + (t * t * s.X1);
							float qy = (it * it * s.Y0) + (2 * it * t * s.My) + (t * t * s.Y1);
							float tx = (2 * it * (s.Mx - s.X0)) + (2 * t * (s.X1 - s.Mx));
							float ty = (2 * it * (s.My - s.Y0)) + (2 * t * (s.Y1 - s.My));
							float tl = MathF.Sqrt((tx * tx) + (ty * ty));

							if (tl < 0.01f)
								continue;

							float nx = -ty / tl, ny = tx / tl;

							if (i > 0 && i < steps)
								canvas.DrawLine(qx - (nx * 2.5f * u), qy - (ny * 2.5f * u), qx + (nx * 2.5f * u), qy + (ny * 2.5f * u), tiePaint);

							float lx = qx - (nx * 1.4f * u), ly = qy - (ny * 1.4f * u);
							float rx = qx + (nx * 1.4f * u), ry = qy + (ny * 1.4f * u);

							if (i == 0)
							{
								leftRail.MoveTo(lx, ly);
								rightRail.MoveTo(rx, ry);
							}
							else
							{
								leftRail.LineTo(lx, ly);
								rightRail.LineTo(rx, ry);
							}
						}

						canvas.DrawPath(leftRail, railDark);
						canvas.DrawPath(rightRail, railDark);
						canvas.DrawPath(leftRail, railGlint);
						canvas.DrawPath(rightRail, railGlint);
					}
				}

				foreach ((float sx, float sy) in soloRail)
				{
					PaintDab(canvas, sx, sy, 4.8f * u, new SKColor(108, 96, 78, 195), 0.7f * u);
					PaintDab(canvas, sx, sy, 2.2f * u, new SKColor(52, 54, 58, 225), 0.25f * u);
				}
			}

			// Bridges where roads or railroads cross painted rivers
			PaintSegmentBridges(canvas, roadSegs, false, left, top, c, scene);
			PaintSegmentBridges(canvas, railSegs, true, left, top, c, scene);

			// Real overhanging tree/jungle crowns framing roads and railroads through woodlands
			if (this.roadTrees.Count > 0 || this.roadJungle.Count > 0)
			{
				using SKPaint overhangPaint = new SKPaint { IsAntialias = true };
				using SKPaint overhangShadow = new SKPaint { IsAntialias = true, Color = new SKColor(18, 28, 12, 70), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 1.4f * u) };
				int segIdx = 0;

				foreach (var s in roadSegs.Concat(railSegs))
				{
					segIdx++;
					float[] ts = { 0.24f, 0.50f, 0.76f };
					for (int k = 0; k < ts.Length; k++)
					{
						float t = ts[k], it = 1 - t;
						(float qx, float qy) = EvalQuad(s, t);
						float mxW = left + (qx / c), myW = top + (qy / c);
						int fy = (int)MathF.Floor(myW);
						if (fy < 0 || fy >= MapHeight)
							continue;
						int fx = Wrap((int)MathF.Floor(mxW));
						if (!scene.Explored(fx, fy) || (scene.Improvements(fx, fy) & CityFlag) != 0)
							continue;

						int terr = scene.Terrain(fx, fy);
						List<SKBitmap>? pool = terr == ForestTerrain ? this.roadTrees : terr == JungleTerrain ? this.roadJungle : null;
						if (pool == null || pool.Count == 0)
							continue;

						if (scene.WaterKindAt(mxW, myW) != 0)
							continue;

						float tx = (2 * it * (s.Mx - s.X0)) + (2 * t * (s.X1 - s.Mx));
						float ty = (2 * it * (s.My - s.Y0)) + (2 * t * (s.Y1 - s.My));
						float tl = MathF.Sqrt((tx * tx) + (ty * ty));
						if (tl < 0.01f)
							continue;

						float nx = -ty / tl, ny = tx / tl;
						for (int side = -1; side <= 1; side += 2)
						{
							if (Hash01(fx * 7 + segIdx, fy * 13 + k, 800 + side) < 0.25f)
								continue;

							float offset = (6.6f + (Hash01(segIdx, k, 810 + side) * 3.0f)) * u * side;
							float ox = qx + (nx * offset), oy = qy + (ny * offset);
							float omx = left + (ox / c), omy = top + (oy / c);
							if (scene.WaterKindAt(omx, omy) != 0)
								continue;

							SKBitmap crown = pool[(int)(Hash01(segIdx * 5 + fx, fy + k, 820 + side) * pool.Count) % pool.Count];
							float sc = 0.72f + (Hash01(segIdx, k, 830 + side) * 0.26f);
							float cw = crown.Width * sc, ch = crown.Height * sc;
							float cxDraw = ox - (cw * 0.5f), cyDraw = oy - (ch * 0.65f);
							canvas.DrawOval(new SKRect(cxDraw + (cw * 0.15f), cyDraw + (ch * 0.65f), cxDraw + (cw * 0.92f), cyDraw + ch + (1.5f * u)), overhangShadow);
							canvas.DrawBitmap(crown, new SKRect(MathF.Round(cxDraw), MathF.Round(cyDraw), MathF.Round(cxDraw + cw), MathF.Round(cyDraw + ch)), overhangPaint);
						}
					}
				}
			}
		}

		private static void PaintSegmentBridges(SKCanvas canvas, IReadOnlyList<(float X0, float Y0, float Mx, float My, float X1, float Y1)> segs,
			bool isRail, float left, float top, int c, IsoView.Scene scene)
		{
			if (segs.Count == 0)
				return;

			float u = c / 64f;
			const int Samples = 22;
			Span<bool> inRiver = stackalloc bool[Samples + 1];

			using SKPaint fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
			using SKPaint stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };

			foreach (var s in segs)
			{
				bool any = false;
				for (int i = 1; i < Samples; i++)
				{
					float t = i / (float)Samples, it = 1 - t;
					float qx = (it * it * s.X0) + (2 * it * t * s.Mx) + (t * t * s.X1);
					float qy = (it * it * s.Y0) + (2 * it * t * s.My) + (t * t * s.Y1);
					bool wet = scene.WaterKindAt(left + (qx / c), top + (qy / c)) == 2;
					inRiver[i] = wet;
					if (wet)
						any = true;
				}

				if (!any)
					continue;

				inRiver[0] = false;
				inRiver[Samples] = false;

				int idx = 1;
				while (idx < Samples)
				{
					if (!inRiver[idx])
					{
						idx++;
						continue;
					}

					int start = idx;
					while (idx < Samples && inRiver[idx])
						idx++;
					int end = idx - 1;

					float t0 = Math.Max(0.04f, (start / (float)Samples) - 0.06f);
					float t1 = Math.Min(0.96f, (end / (float)Samples) + 0.06f);
					float tm = (t0 + t1) * 0.5f;

					(float ax, float ay) = EvalQuad(s, t0);
					(float bx, float by) = EvalQuad(s, t1);
					(float cx, float cy) = EvalQuad(s, tm);

					float dx = bx - ax, dy = by - ay;
					float len = MathF.Sqrt((dx * dx) + (dy * dy));
					if (len < 6f * u)
						len = 13f * u;
					else
						len += 4.5f * u;

					float angle = MathF.Atan2(dy, dx) * 180f / MathF.PI;
					float halfL = len * 0.5f;
					float halfW = (isRail ? 3.1f : 2.8f) * u;

					// Cast shadow onto water beneath bridge
					using (SKPaint sh = new SKPaint { IsAntialias = true, Color = new SKColor(12, 24, 34, 95), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 1.3f * u) })
					{
						canvas.Save();
						canvas.Translate(cx + (1.2f * u), cy + (1.8f * u));
						canvas.RotateDegrees(angle);
						canvas.DrawRoundRect(-halfL, -halfW, len, halfW * 2f, 1.0f * u, 1.0f * u, sh);
						canvas.Restore();
					}

					canvas.Save();
					canvas.Translate(cx, cy);
					canvas.RotateDegrees(angle);

					if (!isRail)
					{
						// Stone/timber bridge deck with warm weathered planks and stone parapets
						fill.Color = new SKColor(178, 156, 118, 245);
						canvas.DrawRoundRect(-halfL, -halfW, len, halfW * 2f, 0.9f * u, 0.9f * u, fill);

						stroke.Color = new SKColor(120, 98, 66, 185);
						stroke.StrokeWidth = 0.85f * u;
						for (float px = -halfL + (2.2f * u); px <= halfL - (1.8f * u); px += 2.6f * u)
							canvas.DrawLine(px, -halfW + u, px, halfW - u, stroke);

						fill.Color = new SKColor(82, 70, 54, 240);
						canvas.DrawRect(-halfL, -halfW - (0.6f * u), len, 1.3f * u, fill);
						canvas.DrawRect(-halfL, halfW - (0.7f * u), len, 1.3f * u, fill);

						fill.Color = new SKColor(216, 200, 166, 235);
						canvas.DrawRect(-halfL + u, -halfW - (0.4f * u), len - (2f * u), 0.55f * u, fill);
						canvas.DrawRect(-halfL + u, halfW - (0.5f * u), len - (2f * u), 0.55f * u, fill);
					}
					else
					{
						// Iron & timber railway trestle bridge
						fill.Color = new SKColor(82, 68, 54, 245);
						canvas.DrawRoundRect(-halfL, -halfW, len, halfW * 2f, 0.8f * u, 0.8f * u, fill);

						stroke.Color = new SKColor(58, 42, 26, 235);
						stroke.StrokeWidth = 1.25f * u;
						for (float px = -halfL + (1.6f * u); px <= halfL - (1.2f * u); px += 2.6f * u)
							canvas.DrawLine(px, -halfW + (0.6f * u), px, halfW - (0.6f * u), stroke);

						// Twin steel rails across bridge
						stroke.Color = new SKColor(40, 42, 46, 245);
						stroke.StrokeWidth = 1.2f * u;
						canvas.DrawLine(-halfL, -1.4f * u, halfL, -1.4f * u, stroke);
						canvas.DrawLine(-halfL, 1.4f * u, halfL, 1.4f * u, stroke);
						stroke.Color = new SKColor(212, 214, 208, 205);
						stroke.StrokeWidth = 0.55f * u;
						canvas.DrawLine(-halfL, -1.5f * u, halfL, -1.5f * u, stroke);
						canvas.DrawLine(-halfL, 1.3f * u, halfL, 1.3f * u, stroke);

						// Iron side girders
						fill.Color = new SKColor(52, 54, 58, 245);
						canvas.DrawRect(-halfL, -halfW - (0.65f * u), len, 1.35f * u, fill);
						canvas.DrawRect(-halfL, halfW - (0.7f * u), len, 1.35f * u, fill);
						fill.Color = new SKColor(154, 158, 162, 210);
						canvas.DrawRect(-halfL + u, -halfW - (0.5f * u), len - (2f * u), 0.5f * u, fill);
					}

					canvas.Restore();
				}
			}
		}

		private static (float X, float Y) EvalQuad((float X0, float Y0, float Mx, float My, float X1, float Y1) s, float t)
		{
			float it = 1 - t;
			return ((it * it * s.X0) + (2 * it * t * s.Mx) + (t * t * s.X1), (it * it * s.Y0) + (2 * it * t * s.My) + (t * t * s.Y1));
		}

		/// <summary>
		/// Terrain-adaptive painted gouache mine: compact pad-free gouache adit &amp; ore cart on mountains/hills or timbered
		/// shaft hoist on desert/flats tucked cleanly in the lower-right shoulder beside peaks and units (procedural fallback if missing)
		/// </summary>
		private void PaintMine(SKCanvas canvas, float cellX, float cellY, int c, int wx, int y, int terrain, bool hasSpecial, IsoView.Scene scene, SKPaint image)
		{
			const int DesertTerrain = 0, HillsTerrain = 4, MountainsTerrain = 5;
			float u = c / 64f;
			bool isSlope = terrain == MountainsTerrain || terrain == HillsTerrain;

			void SafeDab(float px, float py, float rad, SKColor col, float soft)
			{
				if (scene.WaterKindAt(wx + ((px - cellX) / c), y + ((py - cellY) / c)) == 0)
					PaintDab(canvas, px, py, rad, col, soft);
			}

			SKBitmap? sprite = isSlope ? this.mineSlope : this.mineDesert;
			if (sprite != null)
			{
				bool isMt = terrain == MountainsTerrain;
				float mx = cellX + (isMt ? 0.57f : isSlope ? 0.56f : 0.62f) * c;
				float my = cellY + (isMt ? 0.56f : isSlope ? 0.58f : 0.60f) * c;
				float sw = sprite.Width, sh = sprite.Height;
				SKColor shadowCol = !isSlope ? new SKColor(42, 30, 14, 78) : isMt ? new SKColor(24, 22, 20, 88) : new SKColor(30, 24, 16, 82);
				SafeDab(mx + (sw * 0.04f), my + (sh * 0.16f), sw * 0.36f, shadowCol, 1.6f * u);
				if (isSlope)
				{
					SKColor cutCol = isMt ? new SKColor(34, 30, 26, 135) : new SKColor(58, 48, 30, 125);
					SafeDab(mx - (sw * 0.24f), my - (sh * 0.20f), sw * 0.24f, cutCol, 1.8f * u);
				}

				float drawX = MathF.Round(mx - (sw * 0.5f)), drawY = MathF.Round(my - (sh * 0.5f));
				canvas.DrawBitmap(sprite, drawX, drawY, image);

				if (isSlope)
				{
					SKColor browCol = isMt
						? (hasSpecial ? new SKColor(122, 94, 48, 155) : new SKColor(78, 72, 66, 155))
						: (hasSpecial ? new SKColor(54, 52, 48, 155) : new SKColor(118, 102, 62, 145));
					SafeDab(mx - (sw * 0.34f), my - (sh * 0.30f), 2.4f * u, browCol, 1.1f * u);
					SafeDab(mx - (sw * 0.20f), my - (sh * 0.36f), 1.9f * u, browCol, 1.1f * u);
				}

				if (isSlope && hasSpecial)
				{
					float rx = mx + (sw * 0.16f), ry = my + (sh * 0.16f);
					if (isMt)
					{
						SafeDab(rx, ry, 1.4f * u, new SKColor(242, 198, 70, 225), 0.35f * u);
						SafeDab(rx - (0.3f * u), ry - (0.2f * u), 0.75f * u, new SKColor(255, 234, 142, 235), 0.25f * u);
					}
					else
					{
						SafeDab(rx, ry, 1.5f * u, new SKColor(24, 26, 32, 225), 0.35f * u);
						SafeDab(rx - (0.3f * u), ry - (0.2f * u), 0.75f * u, new SKColor(80, 86, 96, 200), 0.25f * u);
					}
				}

				return;
			}

			float pMx = cellX + (0.71f * c), pMy = cellY + (0.64f * c);

			using SKPaint fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
			using SKPaint stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };

			if (isSlope)
			{
				// Fallback rock/earth excavated spoil apron blended into the mountain or hill slope
				bool isMt = terrain == MountainsTerrain;
				SKColor darkRock = isMt ? new SKColor(48, 44, 40, 195) : new SKColor(68, 60, 42, 190);
				SKColor midRock = isMt ? new SKColor(78, 74, 68, 180) : new SKColor(108, 94, 66, 175);
				SKColor hiRock = isMt ? new SKColor(112, 104, 92, 150) : new SKColor(142, 124, 86, 145);

				SafeDab(pMx, pMy - (4.2f * u), 7.4f * u, midRock, 2.2f * u);
				SafeDab(pMx - (2.4f * u), pMy + (1.8f * u), 6.2f * u, darkRock, 2.0f * u);
				SafeDab(pMx + (2.6f * u), pMy - (2.0f * u), 5.0f * u, hiRock, 1.6f * u);
				SafeDab(pMx - (6.8f * u), pMy + (4.2f * u), 4.8f * u, midRock, 1.8f * u);

				float px = pMx + (0.5f * u), py = pMy - (1.4f * u);
				float aw = 3.8f * u, ah = 3.5f * u;
				using (SKPath mouth = new SKPath())
				{
					mouth.MoveTo(px - aw, py + ah);
					mouth.LineTo(px - aw, py - (0.3f * u));
					mouth.QuadTo(px, py - (ah * 1.45f), px + aw, py - (0.3f * u));
					mouth.LineTo(px + aw, py + ah);
					mouth.Close();
					fill.Color = new SKColor(16, 14, 13, 245);
					canvas.DrawPath(mouth, fill);
				}

				// Oak side posts + thick lintel beam + top-left sunlight highlight
				stroke.Color = new SKColor(112, 74, 38, 245);
				stroke.StrokeWidth = 1.6f * u;
				canvas.DrawLine(px - aw, py + ah, px - aw + (0.4f * u), py - (2.0f * u), stroke);
				canvas.DrawLine(px + aw, py + ah, px + aw - (0.4f * u), py - (2.0f * u), stroke);
				stroke.StrokeWidth = 1.9f * u;
				canvas.DrawLine(px - aw - (0.8f * u), py - (2.2f * u), px + aw + (0.8f * u), py - (2.2f * u), stroke);
				stroke.Color = new SKColor(176, 132, 76, 220);
				stroke.StrokeWidth = 0.75f * u;
				canvas.DrawLine(px - aw - (0.6f * u), py - (2.7f * u), px + aw + (0.3f * u), py - (2.7f * u), stroke);

				// 3. Short curving ore rail spur coming out of the adit forward-left
				float tx0 = px - (0.3f * u), ty0 = py + (3.2f * u);
				float tmx = px - (4.0f * u), tmy = py + (5.2f * u);
				float tx1 = px - (8.2f * u), ty1 = py + (6.6f * u);
				stroke.Color = new SKColor(82, 56, 30, 235);
				stroke.StrokeWidth = 1.0f * u;
				for (int s = 1; s <= 4; s++)
				{
					float st = s / 5f, sit = 1f - st;
					float sx = (sit * sit * tx0) + (2 * sit * st * tmx) + (st * st * tx1);
					float sy = (sit * sit * ty0) + (2 * sit * st * tmy) + (st * st * ty1);
					canvas.DrawLine(sx - (0.7f * u), sy - (1.1f * u), sx + (0.7f * u), sy + (1.1f * u), stroke);
				}

				using (SKPath r1 = new SKPath(), r2 = new SKPath())
				{
					r1.MoveTo(tx0, ty0 - (0.9f * u));
					r1.QuadTo(tmx, tmy - (0.9f * u), tx1, ty1 - (0.9f * u));
					r2.MoveTo(tx0, ty0 + (0.9f * u));
					r2.QuadTo(tmx, tmy + (0.9f * u), tx1, ty1 + (0.9f * u));
					stroke.Color = new SKColor(50, 52, 56, 240);
					stroke.StrokeWidth = 0.85f * u;
					canvas.DrawPath(r1, stroke);
					canvas.DrawPath(r2, stroke);
					stroke.Color = new SKColor(186, 190, 194, 155);
					stroke.StrokeWidth = 0.35f * u;
					canvas.DrawPath(r1, stroke);
				}

				// 4. Resource-tinted ore mound beside the track end (+ tiny wooden ore cart on normal veins)
				float hx = tx1 - (0.5f * u), hy = ty1 - (1.8f * u);
				if (isMt && hasSpecial)
				{
					SafeDab(hx, hy, 2.7f * u, new SKColor(72, 64, 52, 230), 0.7f * u);
					SafeDab(hx - (0.8f * u), hy - (0.4f * u), 1.4f * u, new SKColor(238, 194, 68, 245), 0.35f * u);
					SafeDab(hx + (1.0f * u), hy + (0.3f * u), 1.1f * u, new SKColor(250, 224, 118, 240), 0.3f * u);
				}
				else if (!isMt && hasSpecial)
				{
					SafeDab(hx, hy, 2.9f * u, new SKColor(30, 32, 36, 240), 0.7f * u);
					SafeDab(hx - (0.7f * u), hy - (0.5f * u), 1.3f * u, new SKColor(78, 82, 92, 220), 0.35f * u);
				}
				else
				{
					SafeDab(hx - (1.4f * u), hy + (0.4f * u), 2.3f * u, new SKColor(118, 112, 104, 220), 0.65f * u);
					// Tiny wooden ore cart perched on the stub rails
					float cxC = (tx0 + tx1) * 0.5f, cyC = (ty0 + ty1) * 0.5f;
					fill.Color = new SKColor(122, 82, 42, 245);
					canvas.DrawRect(cxC - (2.1f * u), cyC - (2.4f * u), 4.2f * u, 2.6f * u, fill);
					fill.Color = new SKColor(72, 68, 66, 245);
					canvas.DrawOval(new SKRect(cxC - (1.7f * u), cyC - (2.8f * u), cxC + (1.7f * u), cyC - (1.5f * u)), fill);
					stroke.Color = new SKColor(178, 134, 78, 220);
					stroke.StrokeWidth = 0.5f * u;
					canvas.DrawLine(cxC - (2.0f * u), cyC - (2.4f * u), cxC + (2.0f * u), cyC - (2.4f * u), stroke);
				}
			}
			else
			{
				// Desert & open flats: compact timber-cribbed shaft pit + spoil rim + windlass
				SKColor rimCol = terrain == DesertTerrain ? new SKColor(174, 142, 88, 180) : new SKColor(116, 98, 66, 175);
				SKColor shadeCol = terrain == DesertTerrain ? new SKColor(134, 104, 60, 195) : new SKColor(78, 64, 42, 190);
				SafeDab(pMx, pMy + (0.8f * u), 7.2f * u, rimCol, 2.1f * u);
				SafeDab(pMx - (2.4f * u), pMy + (2.4f * u), 5.0f * u, shadeCol, 1.8f * u);

				float sx = pMx, sy = pMy + (0.8f * u);
				fill.Color = new SKColor(18, 15, 13, 245);
				canvas.DrawRect(sx - (3.4f * u), sy - (2.3f * u), 6.8f * u, 4.6f * u, fill);

				// Timber collar logs
				stroke.Color = new SKColor(118, 80, 42, 245);
				stroke.StrokeWidth = 1.35f * u;
				canvas.DrawRect(sx - (3.6f * u), sy - (2.5f * u), 7.2f * u, 5.0f * u, stroke);
				stroke.Color = new SKColor(178, 136, 78, 220);
				stroke.StrokeWidth = 0.65f * u;
				canvas.DrawLine(sx - (3.7f * u), sy - (2.8f * u), sx + (3.5f * u), sy - (2.8f * u), stroke);

				// Compact crossbar windlass & hanging rope
				stroke.Color = new SKColor(96, 62, 30, 245);
				stroke.StrokeWidth = 1.15f * u;
				canvas.DrawLine(sx - (2.8f * u), sy - (1.8f * u), sx - (2.4f * u), sy - (5.6f * u), stroke);
				canvas.DrawLine(sx + (2.8f * u), sy - (1.8f * u), sx + (2.4f * u), sy - (5.6f * u), stroke);
				canvas.DrawLine(sx - (3.1f * u), sy - (5.3f * u), sx + (3.1f * u), sy - (5.3f * u), stroke);
				stroke.Color = new SKColor(204, 186, 138, 225);
				stroke.StrokeWidth = 0.6f * u;
				canvas.DrawLine(sx, sy - (5.2f * u), sx, sy - (0.6f * u), stroke);

				SafeDab(sx - (5.6f * u), sy + (3.2f * u), 2.5f * u, new SKColor(118, 108, 92, 225), 0.7f * u);
			}
		}

		/// <summary>
		/// Depth-split gouache stone octagonal fortress with transparent terrain courtyard, weathered ashlar walls,
		/// crenellations, 4 corner bastions, and recessed south gate (backHalf behind garrisoned units, frontHalf over bases)
		/// </summary>
		private static void PaintFortress(SKCanvas canvas, float cellX, float cellY, int c, int wx, int y, bool backHalf)
		{
			float u = c / 64f;
			float fcx = cellX + (0.50f * c), fcy = cellY + (0.56f * c);
			float rx = 0.37f * c, ry = 0.26f * c;
			float wh = 5.4f * u, wt = 3.0f * u;

			float[] vx = new float[8];
			float[] vy = new float[8];
			for (int k = 0; k < 8; k++)
			{
				float a = (k + 0.5f) * MathF.PI / 4f;
				vx[k] = fcx + (MathF.Cos(a) * rx);
				vy[k] = fcy + (MathF.Sin(a) * ry);
			}

			using SKPaint fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
			using SKPaint stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };

			if (backHalf)
			{
				// Subtle translucent trodden ground + glacis shadow keeping native terrain visible inside the courtyard
				using SKPaint courtShade = new SKPaint { IsAntialias = true, Color = new SKColor(44, 36, 24, 36), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2.2f * u) };
				canvas.DrawOval(new SKRect(fcx - (rx * 0.88f), fcy - (ry * 0.85f), fcx + (rx * 0.88f), fcy + (ry * 0.85f)), courtShade);

				using SKPaint outerShade = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 5.2f * u, Color = new SKColor(22, 18, 12, 72), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 1.5f * u) };
				using SKPath ring = new SKPath();
				ring.MoveTo(vx[0] + (0.8f * u), vy[0] + (1.4f * u));
				for (int k = 1; k < 8; k++)
					ring.LineTo(vx[k] + (0.8f * u), vy[k] + (1.4f * u));
				ring.Close();
				canvas.DrawPath(ring, outerShade);
			}

			void DrawWallSegment(int i0, int i1, bool litFace)
			{
				float x0 = vx[i0], y0 = vy[i0], x1 = vx[i1], y1 = vy[i1];
				using (SKPath face = new SKPath())
				{
					face.MoveTo(x0, y0);
					face.LineTo(x1, y1);
					face.LineTo(x1, y1 - wh);
					face.LineTo(x0, y0 - wh);
					face.Close();
					fill.Color = litFace ? new SKColor(156, 138, 104, 248) : new SKColor(102, 88, 66, 248);
					canvas.DrawPath(face, fill);
				}

				// Mortar base line + parapet top + tiny stone crenellations
				stroke.Color = new SKColor(72, 60, 44, 225);
				stroke.StrokeWidth = 0.8f * u;
				canvas.DrawLine(x0, y0, x1, y1, stroke);

				stroke.Color = new SKColor(208, 190, 150, 248);
				stroke.StrokeWidth = wt;
				canvas.DrawLine(x0, y0 - wh, x1, y1 - wh, stroke);

				stroke.Color = new SKColor(124, 108, 82, 235);
				stroke.StrokeWidth = 0.65f * u;
				canvas.DrawLine(x0, y0 - wh + (wt * 0.45f), x1, y1 - wh + (wt * 0.45f), stroke);

				float len = MathF.Sqrt(((x1 - x0) * (x1 - x0)) + ((y1 - y0) * (y1 - y0)));
				int merlons = Math.Max(2, (int)MathF.Floor(len / (4.4f * u)));
				for (int m = 1; m <= merlons; m++)
				{
					float mt = m / (float)(merlons + 1);
					float mxP = x0 + ((x1 - x0) * mt), myP = y0 + ((y1 - y0) * mt) - wh;
					fill.Color = new SKColor(222, 206, 168, 248);
					canvas.DrawRect(mxP - (1.1f * u), myP - (1.8f * u), 2.2f * u, 1.5f * u, fill);
					fill.Color = new SKColor(92, 78, 56, 220);
					canvas.DrawRect(mxP - (1.1f * u), myP - (0.5f * u), 2.2f * u, 0.45f * u, fill);
				}
			}

			void DrawBastion(int vi)
			{
				float bx = vx[vi], by = vy[vi];
				float bw = 5.8f * u, th = wh + (1.3f * u);
				// Stone turret body
				fill.Color = new SKColor(148, 130, 96, 250);
				canvas.DrawRect(bx - (bw * 0.5f), by - th, bw, th + (0.6f * u), fill);
				fill.Color = new SKColor(102, 88, 66, 245);
				canvas.DrawRect(bx + (bw * 0.08f), by - th, bw * 0.42f, th + (0.6f * u), fill);
				// Sunlit top parapet deck & corner merlons
				fill.Color = new SKColor(214, 198, 160, 250);
				canvas.DrawRect(bx - (bw * 0.56f), by - th - (1.5f * u), bw * 1.12f, 2.2f * u, fill);
				fill.Color = new SKColor(130, 114, 88, 220);
				canvas.DrawRect(bx - (bw * 0.28f), by - th - (0.9f * u), bw * 0.56f, 1.0f * u, fill);
				stroke.Color = new SKColor(68, 56, 40, 220);
				stroke.StrokeWidth = 0.7f * u;
				canvas.DrawRect(bx - (bw * 0.5f), by - th, bw, th + (0.6f * u), stroke);
			}

			if (backHalf)
			{
				DrawWallSegment(4, 5, true);
				DrawWallSegment(5, 6, true);
				DrawWallSegment(6, 7, false);
				DrawWallSegment(3, 4, true);
				DrawWallSegment(7, 0, false);
				DrawBastion(5);
				DrawBastion(6);
			}
			else
			{
				DrawWallSegment(2, 3, true);
				DrawWallSegment(0, 1, false);
				DrawWallSegment(1, 2, true);

				// Recessed arched oak south gatehouse doorway
				float gx = (vx[1] + vx[2]) * 0.5f, gy = (vy[1] + vy[2]) * 0.5f;
				fill.Color = new SKColor(132, 114, 84, 250);
				canvas.DrawRect(gx - (3.4f * u), gy - wh - (0.6f * u), 6.8f * u, wh + (0.8f * u), fill);
				fill.Color = new SKColor(210, 194, 154, 248);
				canvas.DrawRect(gx - (3.6f * u), gy - wh - (1.4f * u), 7.2f * u, 1.4f * u, fill);
				using (SKPath arch = new SKPath())
				{
					arch.MoveTo(gx - (1.9f * u), gy + (0.1f * u));
					arch.LineTo(gx - (1.9f * u), gy - (2.4f * u));
					arch.QuadTo(gx, gy - (4.6f * u), gx + (1.9f * u), gy - (2.4f * u));
					arch.LineTo(gx + (1.9f * u), gy + (0.1f * u));
					arch.Close();
					fill.Color = new SKColor(58, 38, 22, 250);
					canvas.DrawPath(arch, fill);
				}

				DrawBastion(2);
				DrawBastion(1);
			}
		}

		/// <summary>
		/// Simple blackened charcoal & ash ground scorch for polluted tiles (paired with live rising smoke wisps in GBitmap)
		/// </summary>
		private static void PaintPollutionScorch(SKCanvas canvas, float cellX, float cellY, int c, int wx, int y, IsoView.Scene scene)
		{
			float u = c / 64f;
			float cx = cellX + (c * 0.50f), cy = cellY + (c * 0.50f);
			using SKPaint soot = new SKPaint { IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2.4f * u) };

			for (int k = 0; k < 5; k++)
			{
				float a = Hash01(wx, y, 710 + k) * MathF.Tau;
				float d = (0.04f + (Hash01(wx, y, 730 + k) * 0.14f)) * c;
				float px = cx + (MathF.Cos(a) * d), py = cy + (MathF.Sin(a) * d * 0.78f);
				float wmx = (px - cellX) / c, wmy = (py - cellY) / c;
				if (scene.WaterKindAt(wx + wmx, y + wmy) != 0)
					continue;

				float rx = (0.14f + (Hash01(wx, y, 750 + k) * 0.11f)) * c;
				float ry = rx * (0.68f + (Hash01(wx, y, 770 + k) * 0.18f));
				byte alpha = (byte)(155 + (Hash01(wx, y, 790 + k) * 45));
				soot.Color = new SKColor(28, 25, 22, alpha);
				canvas.DrawOval(new SKRect(px - rx, py - ry, px + rx, py + ry), soot);
			}

			// Slightly darker ember-ash core
			soot.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 1.5f * u);
			soot.Color = new SKColor(18, 16, 15, 195);
			canvas.DrawOval(new SKRect(cx - (0.16f * c), cy - (0.11f * c), cx + (0.16f * c), cy + (0.12f * c)), soot);
		}

		/// <summary>
		/// Harbor tier for a coastal city of a given population size (0 hamlet landing .. 3 grand classical harbor)
		/// </summary>
		public static int PortTier(int size) => size <= 2 ? 0 : size <= 5 ? 1 : size <= 9 ? 2 : 3;

		/// <summary>
		/// Picks the best cardinal coastal side (0=E, 1=W, 3=N, 2=S) or diagonal corner (4=SE, 5=SW, 6=NE, 7=NW)
		/// for a city's harbor by preferring more open sea over an enclosed pond, or -1 if inland
		/// </summary>
		private static int CoastalPortDirection(IsoView.Scene scene, int wx, int y)
		{
			bool IsSea(int cx, int cy) => cy >= 0 && cy < MapHeight && scene.Explored(Wrap(cx), cy) && scene.Terrain(Wrap(cx), cy) == Water;

			(int Dir, int Dx, int Dy)[] dirs = { (0, 1, 0), (1, -1, 0), (3, 0, -1), (2, 0, 1) };
			int bestDir = -1, bestScore = 0;

			foreach (var (dir, dx, dy) in dirs)
			{
				if (!IsSea(wx + dx, y + dy))
					continue;

				int score = 1;
				if (IsSea(wx + (2 * dx), y + (2 * dy))) score++;
				if (IsSea(wx + dx - dy, y + dy + dx)) score++;
				if (IsSea(wx + dx + dy, y + dy - dx)) score++;

				if (score > bestScore)
				{
					bestScore = score;
					bestDir = dir;
				}
			}

			if (bestDir < 0)
			{
				(int Dir, int Dx, int Dy)[] diags = { (4, 1, 1), (5, -1, 1), (6, 1, -1), (7, -1, -1) };
				foreach (var (dir, dx, dy) in diags)
				{
					if (!IsSea(wx + dx, y + dy))
						continue;

					int score = 1 +
						(IsSea(wx + (2 * dx), y + (2 * dy)) ? 1 : 0) +
						(IsSea(wx + (2 * dx), y + dy) ? 1 : 0) +
						(IsSea(wx + dx, y + (2 * dy)) ? 1 : 0);

					if (score > bestScore)
					{
						bestScore = score;
						bestDir = dir;
					}
				}
			}

			return bestDir;
		}

		/// <summary>
		/// Subtle slightly-curved trodden earth lane connecting an inland town clearing to its shoreline harbor
		/// </summary>
		private static void PaintHarborPath(SKCanvas canvas, float x0, float y0, float x1, float y1, int tier, float u)
		{
			float dx = x1 - x0, dy = y1 - y0;
			float len = MathF.Sqrt((dx * dx) + (dy * dy));

			if (len < 3f * u)
				return;

			float nx = -dy / len, ny = dx / len;
			float bend = ((tier & 1) == 0 ? 1f : -1f) * 2.2f * u;
			float mx = ((x0 + x1) * 0.5f) + (nx * bend), my = ((y0 + y1) * 0.5f) + (ny * bend);
			float w = (2.2f + (0.35f * tier)) * u;

			using SKPath lane = new SKPath();
			lane.MoveTo(x0, y0);
			lane.QuadTo(mx, my, x1, y1);

			using SKPaint verge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeWidth = w + (2.0f * u), Color = new SKColor(68, 48, 24, 90), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 1.1f * u) };
			using SKPaint bed = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeWidth = w, Color = new SKColor(186, 150, 88, 200), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 0.5f * u) };
			canvas.DrawPath(lane, verge);
			canvas.DrawPath(lane, bed);

			int dabs = Math.Max(4, (int)MathF.Round(len / (3.2f * u)));
			for (int i = 1; i < dabs; i++)
			{
				float t = i / (float)dabs, it = 1 - t;
				float qx = (it * it * x0) + (2 * it * t * mx) + (t * t * x1);
				float qy = (it * it * y0) + (2 * it * t * my) + (t * t * y1);
				PaintDab(canvas, qx, qy, (1.1f + (0.15f * tier)) * u, new SKColor(214, 184, 122, 165), 0.45f * u);
			}
		}

		private static void DrawDock(SKCanvas canvas, SKBitmap dock, float px, float py, int c, SKPaint image)
		{
			float u = c / 64f;
			// soft water-shadow shaped directly to the piers, breakwaters and moored boats (light from top-left)
			using SKPaint shadowPaint = new SKPaint
			{
				IsAntialias = true,
				ColorFilter = SKColorFilter.CreateBlendMode(new SKColor(8, 28, 42, 95), SKBlendMode.SrcIn),
				MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 1.6f * u)
			};
			canvas.DrawBitmap(dock, px + (1.5f * u), py + (2.0f * u), shadowPaint);
			canvas.DrawBitmap(dock, px, py, image);
		}

		private static void DrawDockTransformed(SKCanvas canvas, SKBitmap dock, float sx, float sy, float pivotX, float pivotY, float angleDeg, float scale, int c, SKPaint image)
		{
			float u = c / 64f;
			using SKPaint shadowPaint = new SKPaint
			{
				IsAntialias = true,
				ColorFilter = SKColorFilter.CreateBlendMode(new SKColor(8, 28, 42, 95), SKBlendMode.SrcIn),
				MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 1.6f * u)
			};

			canvas.Save();
			canvas.Translate(sx + (1.5f * u), sy + (2.0f * u));
			canvas.RotateDegrees(angleDeg);
			canvas.Scale(scale, scale);
			canvas.DrawBitmap(dock, -pivotX, -pivotY, shadowPaint);
			canvas.Restore();

			canvas.Save();
			canvas.Translate(sx, sy);
			canvas.RotateDegrees(angleDeg);
			canvas.Scale(scale, scale);
			canvas.DrawBitmap(dock, -pivotX, -pivotY, image);
			canvas.Restore();
		}

		private static void DrawTownShadow(SKCanvas canvas, SKBitmap art, float tx, float ty, float tw, float th, bool walled, int c)
		{
			float u = c / 64f;
			// subtle ambient-occlusion under the city footprint, lighter on unwalled towns so edges dissolve softly into outskirts
			using SKPaint contactPaint = new SKPaint
			{
				IsAntialias = true,
				ColorFilter = SKColorFilter.CreateBlendMode(new SKColor(16, 12, 8, (byte)(walled ? 40 : 22)), SKBlendMode.SrcIn),
				MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (walled ? 1.2f : 1.8f) * u)
			};
			canvas.DrawBitmap(art, new SKRect(tx, ty + (0.5f * u), tx + tw, ty + th + (0.5f * u)), contactPaint);

			using SKPaint shadowPaint = new SKPaint
			{
				IsAntialias = true,
				ColorFilter = SKColorFilter.CreateBlendMode(new SKColor(18, 14, 8, (byte)(walled ? 106 : 34)), SKBlendMode.SrcIn),
				MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (walled ? 1.9f : 2.2f) * u)
			};
			float ox = (walled ? 1.8f : 1.0f) * u, oy = (walled ? 2.4f : 1.4f) * u;
			canvas.DrawBitmap(art, new SKRect(tx + ox, ty + oy, tx + tw + ox, ty + th + oy), shadowPaint);
		}

		private static float Ease(float x) => x * x * (3 - (2 * x));

		/// <summary>
		/// A dab of paint: a soft-edged blob, a little irregular, as a brush leaves it
		/// </summary>
		private static void PaintDab(SKCanvas canvas, float x, float y, float r, SKColor colour, float soft)
		{
			using SKPaint paint = new SKPaint { IsAntialias = true, Color = colour, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(0.3f, soft)) };
			canvas.DrawOval(new SKRect(x - r, y - (r * 0.82f), x + r, y + (r * 0.82f)), paint);
		}

		private static SKColor Mix(SKColor a, SKColor b, float t)
		{
			t = Math.Clamp(t, 0, 1);
			return new SKColor((byte)(a.Red + ((b.Red - a.Red) * t)), (byte)(a.Green + ((b.Green - a.Green) * t)), (byte)(a.Blue + ((b.Blue - a.Blue) * t)), (byte)(a.Alpha + ((b.Alpha - a.Alpha) * t)));
		}

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
			Action<SKCanvas, float, float>? effects, out GBitmap.SmokeSource[] smokeOut) => this.Paint(cx, cy, scene, pose, effects, this.width, this.height, true, out smokeOut);

		/// <summary>
		/// The scene in a view of w x h HD pixels; names, if shown, under the towns
		/// </summary>
		private uint[] Paint(float cx, float cy, IsoView.Scene scene, Func<int, int, (float Dx, float Dy, float Alpha, bool Grey)?>? pose,
			Action<SKCanvas, float, float>? effects, int w, int h, bool names, out GBitmap.SmokeSource[] smokeOut)
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

			using SKFont badgeFont = new SKFont(this.badgeFace, 11.5f * c / 64f) { Edging = SKFontEdging.Antialias, Subpixel = false };
			using SKFont nameFont = new SKFont(this.face, 12 * c / 64f);
			using SKPaint fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
			using SKPaint stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };
			using SKPaint image = new SKPaint { IsAntialias = true };

			// painted farmland, roads, and bridges directly over the painted ground
			if (this.DrawsImprovements)
				PaintIrrigation(canvas, left, top, cols, rows, c, scene);

			if (this.DrawsFeatures)
				this.PaintRoads(canvas, left, top, cols, rows, c, scene);

			var smokeList = new List<GBitmap.SmokeSource>();
			var frontForts = new List<(int X, int Y)>();

			// huts, non-integrated resources, blackened pollution scorch, terrain mines, and back half of fortresses under units
			if (this.DrawsFeatures)
			{
				const int CityFlag = 0x1, MineFlag = 0x4, FortFlag = 0x20, PollFlag = 0x40;
				using SKPaint shade = new SKPaint { IsAntialias = true, Color = new SKColor(20, 16, 10, 90), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2.2f * c / 64f) };

				for (int y = (int)MathF.Floor(top) - 1; y <= top + rows; y++)
				{
					for (int x = (int)MathF.Floor(left) - 1; x <= left + cols; x++)
					{
						int wx = Wrap(x);

						if (y < 0 || y >= MapHeight || !scene.Explored(wx, y))
							continue;

						int feature = scene.Feature(wx, y), terrain = scene.Terrain(wx, y);
						int imp = scene.Improvements(wx, y);
						bool isCity = (imp & CityFlag) != 0 || scene.Cities.Any(t => Wrap(t.X) == wx && t.Y == y);
						float cellX = (x - left) * c, cellY = (y - top) * c;

						if (isCity)
							continue;

						if (this.shield != null && scene.ShieldBonus(wx, y))
							PaintShield(canvas, cellX + (c / 2f), cellY + (c / 2f), c);

						if ((imp & PollFlag) != 0)
						{
							PaintPollutionScorch(canvas, cellX, cellY, c, wx, y, scene);
							smokeList.Add(new GBitmap.SmokeSource(cellX + (c * 0.50f), cellY + (c * 0.46f), c, (wx * 73) ^ (y * 199)));
						}

						bool skipRes = terrain >= 0 && terrain < this.integratedResources.Length && this.integratedResources[terrain];

						// gold in the mountains and gems in the jungle catch the light: glints twinkle over them
						if ((feature & 1) != 0 && (terrain == 5 || terrain == 9))
							smokeList.Add(new GBitmap.SmokeSource(cellX + (c * 0.5f), cellY + (c * (terrain == 5 ? 0.4f : 0.58f)), c, (wx * 131) ^ (y * 977), terrain == 5 ? 1 : 2));
						SKBitmap? art = (feature & 2) != 0 ? this.hut : (feature & 1) != 0 && !skipRes && terrain >= 0 && terrain < this.resources.Length ? this.resources[terrain] : null;

						if (art != null)
						{
							float px = cellX + ((c - art.Width) / 2f), py = cellY + ((c - art.Height) / 2f) + (c * 0.06f);
							if (terrain != Water)
								canvas.DrawOval(new SKRect(px + (art.Width * 0.12f), py + (art.Height * 0.78f), px + (art.Width * 0.92f), py + (art.Height * 1.02f)), shade);
							canvas.DrawBitmap(art, MathF.Round(px), MathF.Round(py), image);
						}

						if ((imp & MineFlag) != 0)
							this.PaintMine(canvas, cellX, cellY, c, wx, y, terrain, (feature & 1) != 0, scene, image);

						if ((imp & FortFlag) != 0)
						{
							PaintFortress(canvas, cellX, cellY, c, wx, y, true);
							frontForts.Add((x, y));
						}
					}
				}
			}

			smokeOut = smokeList.Count == 0 ? Array.Empty<GBitmap.SmokeSource>() : smokeList.ToArray();

			var labels = new List<(string Name, float X, float Y)>();

			// towns, top to bottom: the town, its size badge on its top-left corner, its name under it
			bool IsSeaCell(int cx2, int cy2) => cy2 >= 0 && cy2 < MapHeight && scene.Explored(Wrap(cx2), cy2) && scene.Terrain(Wrap(cx2), cy2) == Water;

			foreach (var town in scene.Cities.OrderBy(t => t.Y).ThenBy(t => Near(t.X, cx)))
			{
				(float x0, float y0) = Corner(town.X, town.Y);

				if (!InView(x0, y0))
					continue;

				int wx = Wrap(town.X);
				int step = Step(town.Size);
				bool walled = scene.Walls(wx, town.Y);
				SKBitmap? art = this.TownArt(scene.CityLook(wx, town.Y), step, walled);
				float u = c / 64f;
				float tx = x0, ty = y0, tw = c * 0.6f, th = c * 0.6f;

				bool wE = IsSeaCell(wx + 1, town.Y), wW = IsSeaCell(wx - 1, town.Y);
				bool wN = IsSeaCell(wx, town.Y - 1), wS = IsSeaCell(wx, town.Y + 1);
				int waterSides = (wE ? 1 : 0) + (wW ? 1 : 0) + (wN ? 1 : 0) + (wS ? 1 : 0);
				bool squeezed = (wE && wW) || (wN && wS) || waterSides >= 3;

				if (art != null)
				{
					float cityScale = (squeezed && step >= 4) ? 0.86f : (squeezed && step == 3) ? 0.90f : (waterSides >= 2 && step >= 4) ? 0.92f : 1.0f;
					tw = art.Width * cityScale;
					th = art.Height * cityScale;
					float nudgeX = (wE != wW) ? ((wW ? 2.5f : -2.5f) * u) : 0f;
					float nudgeY = (wN != wS) ? ((wN ? 2.5f : -2.5f) * u) : 0f;
					tx = MathF.Round(x0 + ((c - tw) / 2f) + nudgeX);
					ty = MathF.Round(y0 + ((c - th) / 2f) + nudgeY);
				}
				else
				{
					tx = x0 + ((c - tw) / 2);
					ty = y0 + ((c - th) / 2);
				}

				float cellCx = x0 + (c * 0.5f), cellCy = y0 + (c * 0.5f);
				float tcx = tx + (tw * 0.5f), tcy = ty + (th * 0.5f);

				// coastal harbor anchored directly on the painted shoreline edge (8-way: cardinal preferred, diagonal fallback)
				int portDir = CoastalPortDirection(scene, wx, town.Y);
				int portTier = PortTier(town.Size);
				bool cardinalPort = portDir >= 0 && portDir < 4;
				bool diagPort = portDir >= 4;
				SKBitmap? exactDiag = diagPort ? this.ports[portTier, portDir] : null;
				int fallbackDir = (portDir == 4 || portDir == 6) ? 0 : 1;
				SKBitmap? dock = cardinalPort
					? (this.ports[portTier, portDir] ?? this.ports[0, portDir])
					: diagPort
						? (exactDiag ?? this.ports[portTier, fallbackDir] ?? this.ports[0, fallbackDir])
						: null;
				bool tuckWalled = walled && step >= 3;
				bool drawDockBehind = portDir == 3 || portDir == 6 || portDir == 7 || tuckWalled;
				float dockX = 0, dockY = 0;
				float diagSx = 0, diagSy = 0, diagPivotX = 0, diagPivotY = 0, diagAngle = 0;
				const float diagScale = 0.82f;
				const float landFrac = 0.44f;

				if (dock != null && cardinalPort)
				{
					(int dx, int dy) = portDir == 0 ? (1, 0) : portDir == 1 ? (-1, 0) : portDir == 2 ? (0, 1) : (0, -1);
					float shoreDist = scene.ShoreDistance(wx, town.Y, dx, dy);
					float sx = cellCx + (dx * shoreDist * c), sy = cellCy + (dy * shoreDist * c);

					// on large walled cities, tuck the dock's landward house row cleanly behind the sea wall; smaller towns sit back from the beach
					float tuck = tuckWalled ? ((portDir <= 1 ? dock.Width : dock.Height) * 0.24f) : (2f * u);
					float fMax = portDir switch { 0 => tx + tw - tuck, 1 => tx - dock.Width + tuck, 2 => ty + th - tuck, _ => ty - dock.Height + tuck };
					switch (portDir)
					{
						case 0: // East
							dockX = MathF.Round(MathF.Max(sx - (dock.Width * landFrac), fMax));
							dockY = MathF.Round(tcy - (dock.Height * 0.5f));
							break;
						case 1: // West (nudge down slightly if close to the top-left size badge)
							dockX = MathF.Round(MathF.Min(sx - (dock.Width * (1f - landFrac)), fMax));
							dockY = MathF.Round(tcy - (dock.Height * 0.5f) + (dockX + dock.Width > tx - (2 * u) ? 3.0f * u : 0f));
							break;
						case 2: // South
							dockX = MathF.Round(tcx - (dock.Width * 0.5f));
							dockY = MathF.Round(MathF.Max(sy - (dock.Height * landFrac), fMax));
							break;
						case 3: // North
							dockX = MathF.Round(tcx - (dock.Width * 0.5f));
							dockY = MathF.Round(MathF.Min(sy - (dock.Height * (1f - landFrac)), fMax));
							break;
					}

					float gap = portDir == 0 ? (dockX - (tx + tw)) : portDir == 1 ? (tx - (dockX + dock.Width)) :
						portDir == 2 ? (dockY - (ty + th)) : (ty - (dockY + dock.Height));
					if (gap > -4 * u)
					{
						float laneEndX = portDir == 0 ? dockX + (dock.Width * landFrac) : portDir == 1 ? dockX + (dock.Width * (1f - landFrac)) : tcx;
						float laneEndY = portDir == 2 ? dockY + (dock.Height * landFrac) : portDir == 3 ? dockY + (dock.Height * (1f - landFrac)) : tcy;
						PaintHarborPath(canvas, tcx, tcy, laneEndX, laneEndY, portTier, u);
					}

					if (drawDockBehind)
						DrawDock(canvas, dock, dockX, dockY, c, image);
				}
				else if (dock != null && diagPort)
				{
					(int dx, int dy) = portDir switch { 4 => (1, 1), 5 => (-1, 1), 6 => (1, -1), _ => (-1, -1) };
					float ux = dx * 0.70710678f, uy = dy * 0.70710678f;
					float shoreDist = scene.ShoreDistance(wx, town.Y, dx, dy);
					diagSx = cellCx + (ux * shoreDist * c);
					diagSy = cellCy + (uy * shoreDist * c);
					PaintHarborPath(canvas, tcx, tcy, diagSx, diagSy, portTier, u);

					if (exactDiag != null)
					{
						dockX = MathF.Round(diagSx - (dock.Width * 0.5f));
						dockY = MathF.Round(diagSy - (dock.Height * 0.5f));
						if (drawDockBehind)
							DrawDock(canvas, dock, dockX, dockY, c, image);
					}
					else
					{
						diagPivotX = (fallbackDir == 0) ? (dock.Width * landFrac) : (dock.Width * (1f - landFrac));
						diagPivotY = dock.Height * 0.5f;
						diagAngle = portDir switch { 4 => 45f, 5 => -45f, 6 => -45f, _ => 45f };
						if (drawDockBehind)
							DrawDockTransformed(canvas, dock, diagSx, diagSy, diagPivotX, diagPivotY, diagAngle, diagScale, c, image);
					}
				}

				if (art != null)
				{
					DrawTownShadow(canvas, art, tx, ty, tw, th, walled, c);
					canvas.DrawBitmap(art, new SKRect(tx, ty, tx + tw, ty + th), image);
				}

				if (dock != null && !drawDockBehind)
				{
					if (cardinalPort || exactDiag != null)
						DrawDock(canvas, dock, dockX, dockY, c, image);
					else
						DrawDockTransformed(canvas, dock, diagSx, diagSy, diagPivotX, diagPivotY, diagAngle, diagScale, c, image);
				}

				// on a small map (the city screen) the size is in the title, and its worked-cell marks would cover a badge
				if (!names)
					continue;

				float s = 15 * u;
				float bx = MathF.Round(MathF.Max(x0 + u, tx - s + (3 * u))), by = MathF.Round(MathF.Max(y0 + u, ty - (4 * u)));
				float bw = MathF.Round(s + u), bh = MathF.Round(s + u);
				// soft drop shadow behind the painted badge plaque
				fill.Color = new SKColor(18, 14, 10, 115);
				canvas.DrawRoundRect(bx + u, by + (1.5f * u), bw, bh, 2 * u, 2 * u, fill);
				fill.Color = ToColor(town.Colour);
				canvas.DrawRoundRect(bx, by, bw, bh, 2 * u, 2 * u, fill);
				// subtle warm top-left highlight & bottom-right bevel so the plaque sits with the gouache art
				stroke.Color = new SKColor(255, 250, 235, 90);
				stroke.StrokeWidth = u;
				canvas.DrawLine(bx + u, by + u, bx + bw - u, by + u, stroke);
				canvas.DrawLine(bx + u, by + u, bx + u, by + bh - u, stroke);
				stroke.Color = Ink;
				stroke.StrokeWidth = u;
				canvas.DrawRoundRect(bx + (u / 2), by + (u / 2), bw - u, bh - u, 1.5f * u, 1.5f * u, stroke);
				string size = town.Size.ToString();
				bool darkText = Lightness(town.Colour) > 0.72f;
				float bmx = bx + (bw / 2f), bmy = by + (bh / 2f);

				if (!darkText)
				{
					fill.Color = new SKColor(16, 14, 12, 175);
					DrawBadgeCentred(canvas, size, bmx + (0.7f * u), bmy + (0.8f * u), badgeFont, fill);
				}

				fill.Color = darkText ? Ink : SKColors.White;
				DrawBadgeCentred(canvas, size, bmx, bmy, badgeFont, fill);

				// the name is written after the units, so a unit in or below the town never covers it
				float labelY = MathF.Min(y0 + c, ty + th + (2 * u));

				if (dock != null && portDir == 2)
					labelY = MathF.Max(labelY, dockY + dock.Height + (2 * u));
				else if (dock != null && (portDir == 4 || portDir == 5))
				{
					float diagBottom = exactDiag != null ? dockY + dock.Height : diagSy + (dock.Height * 0.42f * diagScale);
					labelY = MathF.Max(labelY, diagBottom + (2 * u));
				}

				// strip classic fixed-width padding and trailing NULs from GameData.CityNames so SkiaSharp never draws a tofu box or offsets centring
				string cleanName = town.Name.TrimEnd('\0', ' ', '\t', '\r', '\n');
				labels.Add((cleanName, x0 + (c / 2f), labelY));
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

			const int FortBit = 0x20, CityBit = 0x1;
			var depthItems = new List<(float SortY, int UnitIndex, int FortIndex)>(shown.Count + frontForts.Count);
			for (int i = 0; i < shown.Count; i++)
				depthItems.Add((shown[i].Y, i, -1));
			for (int i = 0; i < frontForts.Count; i++)
				depthItems.Add((frontForts[i].Y + 0.05f, -1, i));

			foreach (var item in depthItems.OrderBy(d => d.SortY).ThenBy(d => d.UnitIndex >= 0 ? shown[d.UnitIndex].X : frontForts[d.FortIndex].X))
			{
				if (item.FortIndex >= 0)
				{
					(int fx, int fy) = frontForts[item.FortIndex];
					PaintFortress(canvas, (fx - left) * c, (fy - top) * c, c, Wrap(fx), fy, false);
					continue;
				}

				int index = item.UnitIndex;
				var unit = shown[index];
				UnitArt? art = unit.Type >= 0 && unit.Type < this.units.Length ? this.units[unit.Type] : null;

				if (art == null)
					continue;

				float x0 = (unit.X - left) * c, y0 = (unit.Y - top) * c;

				if (!InView(x0, y0))
					continue;

				int uwx = Wrap((int)MathF.Round(unit.X)), uwy = Math.Clamp((int)MathF.Round(unit.Y), 0, MapHeight - 1);
				int uImp = scene.Improvements(uwx, uwy);
				bool inFortress = this.DrawsFeatures && !art.Ship && (uImp & FortBit) != 0 && (uImp & CityBit) == 0;
				bool ring = unit.Active && scene.ActiveShown;

				if (alphas[index] <= 0.01f)
					continue;

				if (alphas[index] < 1)
				{
					using SKPaint layer = new SKPaint { Color = SKColors.White.WithAlpha((byte)(255 * alphas[index])) };
					canvas.SaveLayer(layer);
				}

				this.DrawUnit(canvas, art, x0, y0 - (inFortress ? 0.075f * c : 0f), unit.Colour, unit.Marks, ring, fill, stroke, image, badgeFont, inFortress);

				if (alphas[index] < 1)
					canvas.Restore();
			}

			foreach ((string name, float nx, float ny) in labels)
			{
				float u = c / 64f;
				fill.Color = Ink;
				DrawCentred(canvas, name, nx + (2 * u), ny + (2 * u), nameFont, fill, false);
				DrawCentred(canvas, name, nx + u, ny + u, nameFont, fill, false);
				fill.Color = NameColour;
				DrawCentred(canvas, name, nx, ny, nameFont, fill, false);
			}

			effects?.Invoke(canvas, left, top);
			canvas.Flush();
			uint[] result = new uint[w * h];
			System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(target.GetPixelSpan()).CopyTo(result);

			return result;
		}

		private void DrawUnit(SKCanvas canvas, UnitArt art, float x0, float y0, uint colour, int marks, bool ring,
			SKPaint fill, SKPaint stroke, SKPaint image, SKFont font, bool inFortress = false)
		{
			int c = this.cell;
			float cx = x0 + (c / 2f), cy = y0 + (c * (art.Ship ? 0.6f : 0.7f));
			float rx = c * art.BaseR, ry = rx * art.Squash, side = c * 0.02f, u = c / 64f;
			SKColor col = ToColor(colour);
			bool grey = (marks & 1) != 0;

			// more units on the cell: a second base behind this one
			if ((marks & 4) != 0)
				this.DrawBase(canvas, cx + (rx * 0.32f) + (2 * u), cy - (ry * 0.55f) - (2 * u), rx, ry, side, col, false, fill, stroke);

			// fortified: a low bank of earth round the base, painted in dabs of earth and grass with a stake or two,
			// lit from the top left like the rest of the painting (suppressed inside stone fortresses so stone walls and dirt stakes don't clash)
			if ((marks & 2) != 0 && !inFortress)
			{
				Random dabs = new Random(7);
				float bx = rx + (4 * u), by = ry + (3 * u);

				for (int pass = 0; pass < 2; pass++)
				{
					for (int i = 0; i < 34; i++)
					{
						float a = i / 34f * MathF.PI * 2;
						float px = cx + (MathF.Cos(a) * bx), py = cy + (side / 2) + (MathF.Sin(a) * by);
						float lit = 0.5f - (0.5f * MathF.Sin(a + 0.8f));
						SKColor earth = Mix(new SKColor(96, 72, 42), new SKColor(176, 146, 96), lit * 0.8f + (0.2f * (float)dabs.NextDouble()));

						if (pass == 0)
							PaintDab(canvas, px + (0.8f * u), py + (1.2f * u), 2.6f * u, new SKColor(30, 22, 12, 90), 1.2f * u);
						else
						{
							PaintDab(canvas, px, py, (1.9f + (0.7f * (float)dabs.NextDouble())) * u, earth, 0.5f * u);

							if (dabs.NextDouble() < 0.25)
								PaintDab(canvas, px, py - (0.9f * u), 0.9f * u, Mix(new SKColor(96, 120, 52), new SKColor(150, 160, 80), lit), 0.4f * u);
						}
					}
				}

				// a few sharpened stakes along the front of the bank
				using SKPaint stake = new SKPaint { IsAntialias = true, StrokeWidth = 1.1f * u, StrokeCap = SKStrokeCap.Round, Color = new SKColor(78, 54, 30, 230) };

				for (int i = -2; i <= 2; i++)
				{
					float a = (MathF.PI / 2) + (i * 0.38f);
					float sx = cx + (MathF.Cos(a) * bx), sy = cy + (side / 2) + (MathF.Sin(a) * by);
					canvas.DrawLine(sx, sy, sx + (i * 0.4f * u), sy - (3.2f * u), stake);
				}
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

		/// <summary>
		/// Tight glyph-bounds centring for city badge numerals so lining figures sit crisp and dead-centre on every OS
		/// </summary>
		private static void DrawBadgeCentred(SKCanvas canvas, string text, float cx, float cy, SKFont font, SKPaint paint)
		{
			font.MeasureText(text, out SKRect bounds, paint);
			canvas.DrawText(text, MathF.Round(cx - bounds.MidX), MathF.Round(cy - bounds.MidY), font, paint);
		}

		private static SKTypeface SansFace()
		{
			foreach (string family in new[] { "Segoe UI", "DejaVu Sans", "Liberation Sans", "Arial" })
			{
				SKTypeface? face = SKTypeface.FromFamilyName(family, SKFontStyle.Bold);

				if (face != null && string.Equals(face.FamilyName, family, StringComparison.OrdinalIgnoreCase))
					return face;
			}

			return SKTypeface.FromFamilyName("sans-serif", SKFontStyle.Bold) ?? SKTypeface.Default;
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

		/// <summary>
		/// Gentle post-downscale unsharp mask weighted by neighbour alpha so tiny city details stay crisp at game size
		/// without dark edge halos
		/// </summary>
		private static SKBitmap CrispTown(SKBitmap b, float amount = 0.36f)
		{
			int w = b.Width, h = b.Height, n = w * h;
			float[] r = new float[n], g = new float[n], bl = new float[n], a = new float[n];
			byte[] origA = new byte[n];

			for (int y = 0; y < h; y++)
			{
				for (int x = 0; x < w; x++)
				{
					int i = (y * w) + x;
					SKColor p = b.GetPixel(x, y);
					r[i] = p.Red;
					g[i] = p.Green;
					bl[i] = p.Blue;
					origA[i] = p.Alpha;
					a[i] = p.Alpha / 255f;
				}
			}

			SKBitmap crisp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));

			for (int y = 0; y < h; y++)
			{
				for (int x = 0; x < w; x++)
				{
					int i = (y * w) + x;

					if (origA[i] == 0)
					{
						crisp.SetPixel(x, y, SKColors.Transparent);
						continue;
					}

					float sr = 0, sg = 0, sb = 0, wsum = 0;

					for (int dy = -1; dy <= 1; dy++)
					{
						int ny = y + dy;

						if (ny < 0 || ny >= h)
							continue;

						int ky = dy == 0 ? 2 : 1;

						for (int dx = -1; dx <= 1; dx++)
						{
							int nx = x + dx;

							if (nx < 0 || nx >= w)
								continue;

							int ni = (ny * w) + nx;
							float kw = ky * (dx == 0 ? 2 : 1) * a[ni];

							if (kw <= 0)
								continue;

							sr += r[ni] * kw;
							sg += g[ni] * kw;
							sb += bl[ni] * kw;
							wsum += kw;
						}
					}

					float blurR = wsum > 0 ? sr / wsum : r[i];
					float blurG = wsum > 0 ? sg / wsum : g[i];
					float blurB = wsum > 0 ? sb / wsum : bl[i];
					float edgeW = Math.Clamp((a[i] - 0.15f) / 0.55f, 0f, 1f);
					float factor = amount * edgeW;
					byte nr = (byte)Math.Clamp(r[i] + (factor * (r[i] - blurR)), 0f, 255f);
					byte ng = (byte)Math.Clamp(g[i] + (factor * (g[i] - blurG)), 0f, 255f);
					byte nb = (byte)Math.Clamp(bl[i] + (factor * (bl[i] - blurB)), 0f, 255f);
					crisp.SetPixel(x, y, new SKColor(nr, ng, nb, origA[i]));
				}
			}

			return crisp;
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
		private static UnitArt? LoadUnit(string path, string name, int c, bool tone, (float Pixels, bool Height)? like,
			(float Mean, float Spread, float Saturation)? look = null)
		{
			using SKBitmap? decoded = Decode(path);

			if (decoded == null)
				return null;

			using SKBitmap raw = !tone ? decoded.Copy() : look.HasValue ? Matched(decoded, look.Value) : Toned(decoded);

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

				// a caravan's driver walks beside his camel, too thin for the solid body: the base is set by his feet too,
				// from the plain outline, so both stand on it
				if (name == "caravan")
				{
					var outlineX = new List<float>();
					var outlineY = new List<float>();

					for (int y = 0; y < h; y++)
						for (int x = 0; x < w; x++)
							if (alpha[(y * w) + x] > 128)
							{
								outlineX.Add(x);
								outlineY.Add(y);
							}

					bottom = Percentile(outlineY, 99);
					fl = Percentile(outlineX, 1);
					fr = Percentile(outlineX, 99);
					lift = 0.12f;
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
		/// A painting graded towards the theme's light: shadows lifted by lift (0 to 1), then each channel scaled
		/// </summary>
		private static SKBitmap Graded(SKBitmap t, float lift, float kr, float kg, float kb)
		{
			SKBitmap result = new SKBitmap(new SKImageInfo(t.Width, t.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));

			for (int y = 0; y < t.Height; y++)
			{
				for (int x = 0; x < t.Width; x++)
				{
					SKColor p = t.GetPixel(x, y);
					float Lift(float v) => v + ((255 - v) * lift * (1 - (v / 255f)));
					result.SetPixel(x, y, new SKColor((byte)Math.Clamp(Lift(p.Red) * kr, 0, 255), (byte)Math.Clamp(Lift(p.Green) * kg, 0, 255),
						(byte)Math.Clamp(Lift(p.Blue) * kb, 0, 255), p.Alpha));
				}
			}

			return result;
		}

		/// <summary>
		/// The look of a set of paintings, over their solid pixels: mean and spread of lightness (0-255), and mean
		/// saturation (max - min channel)
		/// </summary>
		private static (float Mean, float Spread, float Saturation) PaintLook(IEnumerable<SKBitmap> pictures)
		{
			double sum = 0, sq = 0, sat = 0;
			long n = 0;

			foreach (SKBitmap b in pictures)
			{
				for (int y = 0; y < b.Height; y++)
				{
					for (int x = 0; x < b.Width; x++)
					{
						SKColor p = b.GetPixel(x, y);

						if (p.Alpha < 200)
							continue;

						float l = (0.3f * p.Red) + (0.59f * p.Green) + (0.11f * p.Blue);
						sum += l;
						sq += l * l;
						sat += Math.Max(p.Red, Math.Max(p.Green, p.Blue)) - Math.Min(p.Red, Math.Min(p.Green, p.Blue));
						n++;
					}
				}
			}

			if (n == 0)
				return (128, 50, 60);

			double mean = sum / n;

			return ((float)mean, (float)Math.Sqrt(Math.Max(1, (sq / n) - (mean * mean))), (float)(sat / n));
		}

		/// <summary>
		/// A painting from another set brought to the theme's look: its lightness moved and stretched to the theme's mean
		/// and spread, and its colour to the theme's saturation, keeping its hues
		/// </summary>
		private static SKBitmap Matched(SKBitmap t, (float Mean, float Spread, float Saturation) target)
		{
			(float mean, float spread, float saturation) = PaintLook(new[] { t });
			float k = Math.Clamp(target.Spread / spread, 0.6f, 1.4f), ks = Math.Clamp(target.Saturation / Math.Max(1, saturation), 0.7f, 1.8f);
			SKBitmap result = new SKBitmap(new SKImageInfo(t.Width, t.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));

			for (int y = 0; y < t.Height; y++)
			{
				for (int x = 0; x < t.Width; x++)
				{
					SKColor p = t.GetPixel(x, y);
					float l = (0.3f * p.Red) + (0.59f * p.Green) + (0.11f * p.Blue);
					float nl = ((l - mean) * k) + target.Mean;
					float r = nl + ((p.Red - l) * ks), g = nl + ((p.Green - l) * ks), b = nl + ((p.Blue - l) * ks);
					result.SetPixel(x, y, new SKColor((byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(b, 0, 255), p.Alpha));
				}
			}

			return result;
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
		private static SKBitmap SoftTown(SKBitmap t, bool walls = false)
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

			// a walled town's walls run round its very edge: its outline is only lightly softened, so they stay; unwalled towns feather gently
			float[] soft = walls ? Blur(Erode(a, w, h, 1), w, h, 1.5f) : Blur(Erode(a, w, h, 4), w, h, 9);
			float[] built = new float[n];

			for (int i = 0; i < n; i++)
			{
				a[i] = MathF.Min(a[i], soft[i]);
				float bright = (r[i] + g[i] + b[i]) / 3, sat = MathF.Max(r[i], MathF.Max(g[i], b[i])) - MathF.Min(r[i], MathF.Min(g[i], b[i]));
				bool terracotta = (r[i] > g[i] * 1.3f) && (r[i] > 110);
				bool paleStone = (bright > 165) && (sat < 70);
				bool coolSlateOrGlass = (b[i] > r[i] * 1.08f) && (b[i] > 75);
				bool jadeTile = (b[i] > r[i] * 1.15f) && (g[i] > r[i] * 1.10f) && (bright > 60);
				bool neutralRoof = (sat < 22) && (bright > 115);
				built[i] = (terracotta || paleStone || coolSlateOrGlass || jadeTile || neutralRoof || (bright < 72)) ? 255 : 0;
			}

			// and the band along its edge, where the walls stand, stays solid whatever its colour
			if (walls)
			{
				float[] inner = Erode(a, w, h, Math.Max(4, w / 28));

				for (int i = 0; i < n; i++)
					if (a[i] > 128 && inner[i] < 128)
						built[i] = 255;
			}

			built = Blur(Dilate(built, w, h, 2), w, h, 2);
			SKBitmap result = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));
			float hwInv = 2f / Math.Max(1, w), hhInv = 2f / Math.Max(1, h);

			for (int y = 0; y < h; y++)
			{
				float ny = (y - (h * 0.5f)) * hhInv;
				for (int x = 0; x < w; x++)
				{
					int i = (y * w) + x;
					float alpha = a[i] * (0.3f + (0.7f * built[i] / 255f));
					if (!walls)
					{
						float nx = (x - (w * 0.5f)) * hwInv;
						float rad = MathF.Sqrt((nx * nx) + (ny * ny));
						if (rad > 0.74f)
						{
							float ang = MathF.Atan2(ny, nx);
							float wob = 0.86f + (0.05f * MathF.Sin(3f * ang)) + (0.03f * MathF.Cos(5f * ang));
							float edgeT = Math.Clamp((wob + 0.14f - rad) / 0.18f, 0f, 1f);
							float taper = edgeT * edgeT * (3f - (2f * edgeT));
							if (built[i] > 180f && rad < 0.94f)
								taper = MathF.Max(taper, 0.88f);
							alpha *= taper;
						}
					}
					float[] c = { ((r[i] - 106f) * 1.18f) + 106f, ((g[i] - 106f) * 1.18f) + 106f, ((b[i] - 106f) * 1.18f) + 106f };
					float grey = (c[0] + c[1] + c[2]) / 3f;

					for (int k = 0; k < 3; k++)
					{
						c[k] = grey + ((c[k] - grey) * 1.26f);
						float t01 = MathF.Pow(Math.Clamp(c[k], 0f, 255f) / 255f, 0.93f);
						float sCurve = t01 * t01 * (3f - (2f * t01));
						c[k] = 255f * (t01 + (0.10f * (sCurve - t01)));
					}

					float mean = (c[0] + c[1] + c[2]) / 3f, spread = MathF.Max(c[0], MathF.Max(c[1], c[2])) - MathF.Min(c[0], MathF.Min(c[1], c[2]));
					float pale = Math.Clamp((mean - 188f) / 48f, 0f, 1f) * Math.Clamp(1f - (spread / 80f), 0f, 1f);

					for (int k = 0; k < 3; k++)
						c[k] += (255f - c[k]) * 0.36f * pale;

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
