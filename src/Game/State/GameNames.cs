using System.Text.Json;

namespace OpenCivOne
{
	/// <summary>
	/// New names for the game's units, advances, improvements, wonders, terrains, governments and nations, read from a
	/// names.json file. Each entry maps an original name to its new one; anything not named keeps its original name.
	/// </summary>
	public class GameNames
	{
		public Dictionary<string, string> Advances { get; set; } = new();

		public Dictionary<string, string> Units { get; set; } = new();

		public Dictionary<string, string> Improvements { get; set; } = new();

		public Dictionary<string, string> Wonders { get; set; } = new();

		public Dictionary<string, string> Terrains { get; set; } = new();

		public Dictionary<string, string> Governments { get; set; } = new();

		/// <summary>
		/// Ruler titles (Emperor, King, Comrade, President) and realm titles (Empire, Kingdom, Republic)
		/// </summary>
		public Dictionary<string, string> Titles { get; set; } = new();

		public Dictionary<string, string> Difficulties { get; set; } = new();

		/// <summary>
		/// Nations by their original plural name (Romans, Babylonians, ...)
		/// </summary>
		public Dictionary<string, NationNames> Nations { get; set; } = new();

		public class NationNames
		{
			public string? Leader { get; set; }

			public string? Nation { get; set; }

			public string? Nationality { get; set; }

			public string[]? Cities { get; set; }
		}

		/// <summary>
		/// Reads a names file; null when there is none or it can't be read
		/// </summary>
		public static GameNames? Load(string path)
		{
			if (!File.Exists(path))
				return null;

			try
			{
				return JsonSerializer.Deserialize<GameNames>(File.ReadAllText(path),
					new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
			}
			catch (Exception e)
			{
				Console.WriteLine($"Names file '{path}' is ignored: {e.Message}");
				return null;
			}
		}

		/// <summary>
		/// Renames everything named in this file
		/// </summary>
		public void Apply(GameData gameData, string[] governmentNames, string[] gameConcepts, string[] rulerTitles, string[] realmTitles,
			string[] difficultyNames)
		{
			foreach (TechnologyAdvanceDefinition advance in gameData.TechnologyAdvances)
				advance.Name = Rename(this.Advances, advance.Name);

			foreach (UnitDefinition unit in gameData.Units)
				unit.Name = Rename(this.Units, unit.Name);

			for (int i = 1; i < 25; i++)
			{
				ImprovementDefinition improvement = gameData.GetImprovementType(i);
				improvement.Name = Rename(this.Improvements, improvement.Name);
			}

			for (int i = 1; i < gameData.Wonders.Length; i++)
				gameData.Wonders[i].Name = Rename(this.Wonders, gameData.Wonders[i].Name);

			foreach (TerrainDefinition terrain in gameData.Terrains)
				terrain.Name = Rename(this.Terrains, terrain.Name);

			// governments are named in two forms: as types (Communist, Democratic) and as Encyclopedia topics (Communism, Democracy)
			for (int i = 0; i < governmentNames.Length; i++)
				governmentNames[i] = Rename(this.Governments, governmentNames[i]);

			for (int i = 0; i < gameConcepts.Length; i++)
				gameConcepts[i] = Rename(this.Governments, gameConcepts[i]);

			RenameAll(this.Titles, rulerTitles);
			RenameAll(this.Titles, realmTitles);
			RenameAll(this.Difficulties, difficultyNames);

			foreach (NationDefinition nation in gameData.Nations)
			{
				if (!this.Nations.TryGetValue(nation.Nation, out NationNames? names))
					continue;

				nation.Leader = names.Leader ?? nation.Leader;
				nation.Nationality = names.Nationality ?? nation.Nationality;
				nation.Nation = names.Nation ?? nation.Nation;

				for (int i = 0; names.Cities != null && i < names.Cities.Length && i < nation.Cities.Length; i++)
					nation.Cities[i] = names.Cities[i];
			}
		}

		private static void RenameAll(Dictionary<string, string> names, string[] list)
		{
			for (int i = 0; i < list.Length; i++)
				list[i] = Rename(names, list[i]);
		}

		private static string Rename(Dictionary<string, string> names, string name)
			=> names.TryGetValue(name, out string? newName) && !string.IsNullOrEmpty(newName) ? newName : name;
	}
}
