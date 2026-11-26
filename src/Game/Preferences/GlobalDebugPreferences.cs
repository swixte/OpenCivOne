
namespace OpenCivOne
{
	public class GlobalDebugPreferences
	{
		private bool _debugMode = false;
		private bool _fastMode=false;
		private bool _saveEveryTurn = false;
		private bool _exportHDTemplates = false;

		public GlobalDebugPreferences() { }

		public bool DebugMode { get => this._debugMode; set => this._debugMode = value; }

		public bool FastMode { get => this._fastMode; set => this._fastMode = value; }

		public bool SaveEveryTurn { get => this._saveEveryTurn; set => this._saveEveryTurn = value; }

		/// <summary>
		/// Writes every loaded PIC file to the 'hd-templates' folder as a 2x PNG image, a starting point for HD art
		/// </summary>
		public bool ExportHDTemplates { get => this._exportHDTemplates; set => this._exportHDTemplates = value; }
	}
}
