using System.Runtime.InteropServices;
using OpenCivOne.Graphics;
using SkiaSharp;

// Exercises the GBitmap HD layer the way the game uses it: load a sprite sheet PIC onto a screen,
// cut 16x16 sprites from it, blit them transparently onto screen 0, overdraw with UI primitives,
// save and restore screen 0, and render the 2x frame the window shows.

int failures = 0;
void Check(bool ok, string what) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {what}"); if (!ok) failures++; }

string dir = Path.Combine(Path.GetTempPath(), "hdtest-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
int S = GBitmap.HDScale;

uint[] Render(GBitmap b)
{
	int w = b.Width * S, h = b.Height * S;
	IntPtr mem = Marshal.AllocHGlobal(w * h * 4);
	b.WriteScaledPixels(mem, w * 4);
	int[] tmp = new int[w * h];
	Marshal.Copy(mem, tmp, 0, tmp.Length);
	Marshal.FreeHGlobal(mem);
	return tmp.Select(v => (uint)v).ToArray();
}
uint PaletteColor(GBitmap b, byte index) => b.Palette[index].ToUInt32();
uint Out(uint[] frame, GBitmap b, int hx, int hy) => frame[hy * b.Width * S + hx];
bool HasHD(GBitmap b) => typeof(GBitmap).GetProperty("HDMask", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(b) != null;

// A 32x16 sheet with two 16x16 sprites: a filled square with a transparent border (index 0 outside)
GBitmap MakeSheet()
{
	var sheet = new GBitmap(32, 16);
	for (int y = 0; y < 16; y++)
		for (int x = 0; x < 32; x++)
			sheet.SetPixel(x, y, (byte)((x % 16 >= 4 && x % 16 < 12 && y >= 4 && y < 12) ? (x < 16 ? 9 : 12) : 0));
	return sheet;
}
string picPath = Path.Combine(dir, "SHEET.PIC");
MakeSheet().SaveToPIC(picPath, false);

// ---------- 1. No HD files: behaviour must be exactly the original ----------
{
	var loaded = GBitmap.FromPICFile(picPath, false)!;
	Check(!HasHD(loaded), "no HD file: no HD layer allocated");

	var screen1 = new GBitmap();
	screen1.DrawBitmap(0, 0, loaded, false);
	var sprite = new GBitmap(16, 16);
	sprite.DrawBitmap(0, 0, screen1, new GRectangle(0, 0, 16, 16), false);
	var screen0 = new GBitmap();
	screen0.FillRectangle(new GRectangle(0, 0, 320, 200), 5, PixelWriteModeEnum.Normal);
	screen0.DrawBitmap(100, 50, sprite, true);
	Check(!HasHD(screen0), "no HD file: screen 0 has no HD layer after sprite blits");

	uint[] frame = Render(screen0);
	bool nn = true;
	for (int y = 0; y < 200 * S && nn; y++)
		for (int x = 0; x < 320 * S; x++)
			if (frame[y * 320 * S + x] != PaletteColor(screen0, screen0.GetPixel(x / S, y / S))) { nn = false; break; }
	Check(nn, "no HD file: 2x frame is an exact nearest-neighbour upscale of the indexed screen");
}

// ---------- 2. Template export, then the template used as HD art: frame must not change ----------
{
	GBitmap.HDTemplatePath = Path.Combine(dir, "templates");
	var loaded = GBitmap.FromPICFile(picPath, false)!;
	GBitmap.HDTemplatePath = null;
	string template = Path.Combine(dir, "templates", "SHEET.png");
	Check(File.Exists(template), "template export writes SHEET.png");

	using var t = SKBitmap.Decode(template);
	Check(t.Width == 64 && t.Height == 32, $"template is 2x the PIC size ({t.Width}x{t.Height})");
	Check(t.GetPixel(0, 0).Alpha == 0 && t.GetPixel(12, 12).Alpha == 255, "template: index 0 is transparent, other indices opaque");
}

string hdDir = Path.Combine(dir, "hd");
Directory.CreateDirectory(hdDir);
// copy template as the HD art: must be visually identical to no HD at all
File.Copy(Path.Combine(dir, "templates", "SHEET.png"), Path.Combine(hdDir, "SHEET.png"));
string picPath2 = Path.Combine(dir, "SHEET2.PIC");
File.Copy(picPath, picPath2);
File.Copy(Path.Combine(dir, "templates", "SHEET.png"), Path.Combine(hdDir, "SHEET2.png"));

uint[] Pipeline(string pic, out GBitmap screen0)
{
	var loaded = GBitmap.FromPICFile(pic, false)!;
	var screen1 = new GBitmap();
	screen1.DrawBitmap(0, 0, loaded, false);                       // LoadPIC onto screen 1
	var spriteA = new GBitmap(16, 16);
	spriteA.DrawBitmap(0, 0, screen1, new GRectangle(0, 0, 16, 16), false);   // ScreenToBitmap
	var spriteB = new GBitmap(16, 16);
	spriteB.DrawBitmap(0, 0, screen1, new GRectangle(16, 0, 16, 16), false);
	screen0 = new GBitmap();
	screen0.FillRectangle(new GRectangle(0, 0, 320, 200), 5, PixelWriteModeEnum.Normal);
	screen0.DrawBitmap(100, 50, spriteA, true);                    // DrawBitmapToScreen (transparent)
	screen0.DrawBitmap(108, 50, spriteB, true);                    // overlapping sprite
	return Render(screen0);
}
{
	uint[] reference = Pipeline(picPath, out var refScreen);     // SHEET.png also exists now, so use plain copy below
}
{
	// reference without HD: a PIC whose name has no HD twin
	string plain = Path.Combine(dir, "PLAIN.PIC");
	File.Copy(picPath, plain);
	uint[] reference = Pipeline(plain, out var s0);
	uint[] withTemplate = Pipeline(picPath2, out var s1);
	Check(HasHD(s1), "template as HD art: HD layer travels from PIC to screen 0");
	Check(reference.SequenceEqual(withTemplate), "template as HD art: 2x frame identical to the no-HD frame");
}

// ---------- 3. Real HD art: different colours, alpha, art outside the original silhouette ----------
{
	using var art = new SKBitmap(64, 32, SKColorType.Bgra8888, SKAlphaType.Unpremul);
	art.Erase(SKColors.Transparent);
	for (int y = 8; y < 24; y++) for (int x = 8; x < 24; x++) art.SetPixel(x, y, new SKColor(0xff, 0x80, 0x00));          // sprite A body, orange
	art.SetPixel(2, 2, new SKColor(0x00, 0xff, 0x00));                                                                 // A: art where original is transparent
	art.SetPixel(40, 8, new SKColor(0x00, 0x00, 0xff, 0x80));                                                          // B: half-transparent blue, original transparent
	for (int y = 8; y < 24; y++) for (int x = 40; x < 56; x++) if (!(x == 40 && y == 8)) art.SetPixel(x, y, new SKColor(0x20, 0x20, 0x20));
	string pic3 = Path.Combine(dir, "ART.PIC");
	File.Copy(picPath, pic3);
	using (var fs = File.Create(Path.Combine(hdDir, "ART.png"))) art.Encode(fs, SKEncodedImageFormat.Png, 100);

	uint[] frame = Pipeline(pic3, out var s0);
	uint bg = PaletteColor(s0, 5);
	uint A(uint c) => c & 0xffffff;
	Check(A(Out(frame, s0, (100 + 6) * S, (50 + 6) * S)) == 0xff8000, "HD art: sprite A body shows HD colour");
	Check(A(Out(frame, s0, (100 + 1) * S, (50 + 1) * S)) == 0x00ff00, "HD art: HD pixel outside the original silhouette is drawn");
	Check(s0.GetPixel(101, 51) == 5, "HD art: indexed pixel under that HD-only pixel keeps the background index");
	Check(A(Out(frame, s0, (100 + 1) * S + 1, (50 + 1) * S)) == A(bg), "HD art: transparent HD subpixel shows the background");
	uint half = Out(frame, s0, (108 + 4) * S, (50 + 4) * S);
	uint Mix(uint c, int sh) => ((((c >> sh) & 0xff) * 0x80) + (((bg >> sh) & 0xff) * 0x7f)) / 0xff;
	uint expectHalf = (Mix(0x0000ff, 16) << 16) | (Mix(0x0000ff, 8) << 8) | Mix(0x0000ff, 0);
	Check(A(half) == expectHalf, $"HD art: half-transparent pixel is blended over the background ({A(half):x6} vs {expectHalf:x6})");
	Check(A(Out(frame, s0, (108 + 6) * S, (50 + 6) * S)) == 0x202020, "HD art: overlapping sprite B covers sprite A");
	Check((Out(frame, s0, 0, 0) & 0xff000000) == (bg & 0xff000000) && (Out(frame, s0, 212, 112) & 0xff000000) == (bg & 0xff000000),
		"HD art: output alpha byte matches the palette convention");

	// ---------- 4. UI drawing over HD pixels falls back to the original ----------
	s0.FillRectangle(new GRectangle(104, 54, 2, 2), 15, PixelWriteModeEnum.Normal);
	s0.SetPixel(110, 56, 14);
	s0.ReplaceColor(new GRectangle(100, 50, 4, 4), 5, 7);
	frame = Render(s0);
	Check(A(Out(frame, s0, 104 * S, 54 * S)) == A(PaletteColor(s0, 15)), "UI: FillRectangle over HD shows the filled colour");
	Check(A(Out(frame, s0, 110 * S, 56 * S)) == A(PaletteColor(s0, 14)), "UI: SetPixel over HD shows the pixel colour");
	Check(A(Out(frame, s0, 100 * S, 50 * S)) == A(PaletteColor(s0, 7)), "UI: ReplaceColor recolours the background");
	Check(A(Out(frame, s0, 106 * S, 56 * S)) == 0xff8000, "UI: untouched HD pixels stay HD");

	// ---------- 5. Dialog save/restore (screen 0 -> screen 1 -> screen 0) keeps HD ----------
	uint[] before = Render(s0);
	var backup = new GBitmap();
	backup.DrawBitmap(0, 0, s0, new GRectangle(0, 0, 320, 200), false);
	s0.FillRectangle(new GRectangle(90, 40, 60, 40), 3, PixelWriteModeEnum.Normal);   // a dialog box
	s0.DrawBitmap(0, 0, backup, new GRectangle(0, 0, 320, 200), false);
	Check(before.SequenceEqual(Render(s0)), "save/restore: frame identical after a dialog is drawn and removed");

	// ---------- 6. Dissolve effect pixel copy ----------
	var target = new GBitmap();
	for (int y = 0; y < 200; y++) for (int x = 0; x < 320; x++) target.CopyPixel(x, y, s0);
	Check(before.SequenceEqual(Render(target)), "CopyPixel: copying every pixel reproduces the frame, HD included");
}

// ---------- 7. Wrong-size HD image is ignored ----------
{
	using var bad = new SKBitmap(10, 10);
	string pic4 = Path.Combine(dir, "BAD.PIC");
	File.Copy(picPath, pic4);
	using (var fs = File.Create(Path.Combine(hdDir, "BAD.png"))) bad.Encode(fs, SKEncodedImageFormat.Png, 100);
	Check(!HasHD(GBitmap.FromPICFile(pic4, false)!), "wrong-size HD image is ignored");
}

// ---------- 8. Per-sprite region files, key colors and player recoloring ----------
{
	string pic = Path.Combine(dir, "UNITS.PIC");
	File.Copy(picPath, pic);
	string regionDir = Path.Combine(hdDir, "UNITS");
	Directory.CreateDirectory(regionDir);

	// sprite A (0,0) replaced: orange body, primary key (magenta 0x80 = exact colour) band at rows 4-5, secondary key (cyan) at rows 6-7
	using (var art = new SKBitmap(32, 32, SKColorType.Bgra8888, SKAlphaType.Unpremul))
	{
		art.Erase(SKColors.Transparent);
		for (int y = 4; y < 28; y++) for (int x = 4; x < 28; x++) art.SetPixel(x, y, new SKColor(0xff, 0x80, 0x00));
		for (int y = 8; y < 12; y++) for (int x = 4; x < 28; x++) art.SetPixel(x, y, new SKColor(0x80, 0x00, 0x80));
		for (int y = 12; y < 16; y++) for (int x = 4; x < 28; x++) art.SetPixel(x, y, new SKColor(0x00, 0x40, 0x40));   // half brightness
		using var fs = File.Create(Path.Combine(regionDir, "0_0.png")); art.Encode(fs, SKEncodedImageFormat.Png, 100);
	}
	// a city-style sprite with keys meaning index 0 (transparent, takes the colour below) and 5
	using (var art = new SKBitmap(8, 8, SKColorType.Bgra8888, SKAlphaType.Unpremul))
	{
		art.Erase(new SKColor(0x80, 0x00, 0x80));
		for (int y = 0; y < 2; y++) for (int x = 0; x < 2; x++) art.SetPixel(x, y, new SKColor(0x00, 0x80, 0x80));
		using var fs = File.Create(Path.Combine(regionDir, "28_0_k0-5.png")); art.Encode(fs, SKEncodedImageFormat.Png, 100);
	}
	File.WriteAllText(Path.Combine(regionDir, "notes.txt"), "ignored");

	var loaded = GBitmap.FromPICFile(pic, false)!;
	Check(HasHD(loaded), "regions: HD layer created from region files alone");
	Check(loaded.GetPixel(6, 4) == 10 && loaded.GetPixel(6, 6) == 2, "regions: key pixels set the original pixel to the key index (10, 2)");
	Check(loaded.GetPixel(0, 0) == 0, "regions: transparent region pixels set the original pixel to transparent");
	Check(loaded.GetPixel(28, 0) == 5 && loaded.GetPixel(29, 1) == 0, "regions: _k0-5 suffix sets key indexes");
	Check(loaded.GetPixel(20, 6) == 12, "regions: pixels outside regions keep the original");

	// player recoloring, as LoadBitmaps does on screen 1
	var screen1 = new GBitmap();
	screen1.SetPaletteColor(10, Avalonia.Media.Color.FromRgb(0x00, 0xff, 0x00));
	screen1.SetPaletteColor(2, Avalonia.Media.Color.FromRgb(0x00, 0x80, 0x00));
	screen1.SetPaletteColor(12, Avalonia.Media.Color.FromRgb(0xff, 0x00, 0x00));
	screen1.SetPaletteColor(9, Avalonia.Media.Color.FromRgb(0x00, 0x00, 0xff));
	screen1.DrawBitmap(0, 0, loaded, false);
	screen1.ReplaceColor(new GRectangle(0, 0, 16, 16), 10, 12);   // primary -> player colour (red)
	screen1.ReplaceColor(new GRectangle(0, 0, 16, 16), 2, 9);     // secondary -> blue
	var unit = new GBitmap(16, 16);
	unit.CopyPalette(screen1);
	unit.DrawBitmap(0, 0, screen1, new GRectangle(0, 0, 16, 16), false);

	var screen0 = new GBitmap();
	screen0.CopyPalette(screen1);
	screen0.FillRectangle(new GRectangle(0, 0, 320, 200), 5, PixelWriteModeEnum.Normal);
	screen0.DrawBitmap(40, 40, unit, true);
	uint[] frame = Render(screen0);
	uint C(int hx, int hy) => Out(frame, screen0, hx, hy) & 0xffffff;
	Check(C(40 * S + 6, 40 * S + 4) == 0xff8000, "keys: plain HD pixels unchanged by recoloring");
	Check(C(40 * S + 6, 40 * S + 8) == 0xff0000, $"keys: primary key shows the player colour ({C(40 * S + 6, 40 * S + 8):x6})");
	Check(C(40 * S + 6, 40 * S + 12) == 0x00007f, $"keys: secondary key shows the secondary colour at half brightness ({C(40 * S + 6, 40 * S + 12):x6})");

	// sentry: grey HD drawing
	var screenG = new GBitmap();
	screenG.CopyPalette(screen1);
	screenG.FillRectangle(new GRectangle(0, 0, 320, 200), 5, PixelWriteModeEnum.Normal);
	screenG.DrawBitmap(new GPoint(40, 40), unit, unit.Rectangle, true, true);
	uint[] g = Render(screenG);
	uint body = Out(g, screenG, 40 * S + 6, 40 * S + 4) & 0xffffff, band = Out(g, screenG, 40 * S + 6, 40 * S + 8) & 0xffffff;
	bool isGrey(uint c) => ((c >> 16) & 0xff) == ((c >> 8) & 0xff) && ((c >> 8) & 0xff) == (c & 0xff);
	Check(isGrey(body) && isGrey(band) && body != 0, $"sentry: HD unit drawn in grey ({body:x6}, {band:x6})");
	Check((Out(g, screenG, 0, 0) & 0xffffff) == (Out(frame, screen0, 0, 0) & 0xffffff), "sentry: background not greyed");

	// city: keys meaning index 0 take the colour of the filled square under them; index 5 key follows a 5 -> 9 swap
	var city = new GBitmap(8, 8);
	city.CopyPalette(screen1);
	city.DrawBitmap(0, 0, screen1, new GRectangle(28, 0, 4, 4), false);
	var screenC = new GBitmap();
	screenC.CopyPalette(screen1);
	screenC.FillRectangle(new GRectangle(60, 60, 4, 4), 12, PixelWriteModeEnum.Normal);
	screenC.DrawBitmap(60, 60, city, true);
	screenC.ReplaceColor(new GRectangle(60, 60, 4, 4), 5, 9);
	uint[] cf = Render(screenC);
	Check((Out(cf, screenC, 61 * S, 61 * S) & 0xffffff) == 0xff0000, "city: index-0 key shows the player colour filled under it");
	Check((Out(cf, screenC, 60 * S, 60 * S) & 0xffffff) == 0x0000ff, "city: index-5 key follows the 5 -> secondary colour swap");
}

// ---------- 8b. Tall art reaching above a sprite ----------
{
	string pic = Path.Combine(dir, "TALL.PIC");
	File.Copy(picPath, pic);
	string regionDir = Path.Combine(hdDir, "TALL");
	Directory.CreateDirectory(regionDir);
	// sprite at (16,0), 16x16, with 4 original rows (8 HD rows) above it: red above, blue in the sprite
	using (var art = new SKBitmap(32, 40, SKColorType.Bgra8888, SKAlphaType.Unpremul))
	{
		art.Erase(SKColors.Transparent);
		for (int y = 0; y < 8; y++) for (int x = 10; x < 22; x++) art.SetPixel(x, y, new SKColor(0xff, 0x00, 0x00));
		for (int y = 8; y < 40; y++) for (int x = 4; x < 28; x++) art.SetPixel(x, y, new SKColor(0x00, 0x00, 0xff));
		using var fs = File.Create(Path.Combine(regionDir, "16_0_t4.png")); art.Encode(fs, SKEncodedImageFormat.Png, 100);
	}
	var loaded = GBitmap.FromPICFile(pic, false)!;
	Check(GBitmap.HasHDOverflow, "tall: loading tall art turns on HasHDOverflow");
	Check(loaded.GetPixel(20, 2) != 0, "tall: the region below the tall part loads as usual");

	var sheet = new GBitmap();
	sheet.DrawBitmap(0, 0, loaded, false);                                // LoadPIC onto screen 1
	var sprite = new GBitmap(16, 16);
	sprite.DrawBitmap(0, 0, sheet, new GRectangle(16, 0, 16, 16), false); // ScreenToBitmap
	var other = new GBitmap(16, 16);
	other.DrawBitmap(0, 0, sheet, new GRectangle(0, 0, 16, 16), false);

	var screen = new GBitmap();
	screen.FillRectangle(new GRectangle(0, 0, 320, 200), 5, PixelWriteModeEnum.Normal);
	screen.HDOverflowClip = new GRectangle(80, 8, 240, 192);
	screen.DrawBitmap(100, 40, sprite, true);
	screen.DrawBitmap(140, 40, other, true);
	screen.DrawBitmap(200, 10, sprite, true);                             // its tall part would cross the clip edge at y = 8
	uint[] f = Render(screen);
	uint P(int x, int y) => Out(f, screen, x, y) & 0xffffff;
	uint bg = PaletteColor(screen, 5) & 0xffffff;
	Check(P(100 * S + 12, 40 * S - 4) == 0xff0000, "tall: art above the sprite is drawn in the cell above");
	Check(P(100 * S + 12, 40 * S + 4) == 0x0000ff, "tall: the sprite itself is drawn");
	Check(P(100 * S + 2, 40 * S - 4) == bg, "tall: transparent tall pixels leave the background");
	Check(screen.GetPixel(106, 38) == 5, "tall: original pixels above the sprite are not changed");
	Check(P(140 * S + 12, 40 * S - 4) == bg, "tall: other sprites from the sheet have no tall part");
	Check(P(200 * S + 12, 9 * S) == 0xff0000 && P(200 * S + 12, 7 * S) == bg, "tall: drawing stops at the clip rectangle");

	var onlyTall = new GBitmap();
	onlyTall.FillRectangle(new GRectangle(0, 0, 320, 200), 5, PixelWriteModeEnum.Normal);
	onlyTall.DrawHDOverflow(new GPoint(100, 40), sprite);
	uint[] ft = Render(onlyTall);
	Check((Out(ft, onlyTall, 100 * S + 12, 40 * S - 4) & 0xffffff) == 0xff0000 && (Out(ft, onlyTall, 100 * S + 12, 40 * S + 4) & 0xffffff) == bg,
		"tall: DrawHDOverflow draws the part above the sprite only");

	// a different sheet loaded over the same screen drops the old tall art
	var plain = GBitmap.FromPICFile(Path.Combine(dir, "PLAIN.PIC"), false)!;
	sheet.DrawBitmap(0, 0, plain, false);
	var sprite2 = new GBitmap(16, 16);
	sprite2.DrawBitmap(0, 0, sheet, new GRectangle(16, 0, 16, 16), false);
	var screen2 = new GBitmap();
	screen2.FillRectangle(new GRectangle(0, 0, 320, 200), 5, PixelWriteModeEnum.Normal);
	screen2.DrawBitmap(100, 40, sprite2, true);
	Check((Out(Render(screen2), screen2, 100 * S + 12, 40 * S - 4) & 0xffffff) == bg, "tall: loading another sheet over it removes the tall art");
}

// ---------- 9. The generated art set (assets/hd) loads completely onto 320x200 sheets ----------
{
	string assets = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../assets/hd"));
	if (Directory.Exists(assets))
	{
		string gameDir = Path.Combine(dir, "game");
		CopyDirectory(assets, Path.Combine(gameDir, "hd"));
		foreach (string sheetDir in Directory.GetDirectories(assets))
		{
			string sheet = Path.GetFileName(sheetDir);
			string sheetPic = Path.Combine(gameDir, sheet + ".PIC");
			new GBitmap(320, 200).SaveToPIC(sheetPic, false);
			var b = GBitmap.FromPICFile(sheetPic, false)!;
			var mask = (bool[])typeof(GBitmap).GetProperty("HDMask", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(b)!;
			int files = 0, missing = 0;
			foreach (string f in Directory.GetFiles(sheetDir, "*.png"))
			{
				var m = System.Text.RegularExpressions.Regex.Match(Path.GetFileName(f), @"^(\d+)_(\d+)(?:_t(\d+))?");
				int x = int.Parse(m.Groups[1].Value), y = int.Parse(m.Groups[2].Value);
				int above = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;
				using var img = SKBitmap.Decode(f);
				for (int yy = 0; yy < img.Height / S - above; yy++) for (int xx = 0; xx < img.Width / S; xx++) if (!mask[(y + yy) * 320 + x + xx]) missing++;
				files++;
			}
			Check(missing == 0, $"art set: all {files} {sheet} sprites load into the sheet ({missing} pixels missing)");
		}
	}
	else
	{
		Console.WriteLine($"SKIP  art set not found at {assets}");
	}
}

void CopyDirectory(string from, string to)
{
	Directory.CreateDirectory(to);
	foreach (string f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
	foreach (string d in Directory.GetDirectories(from)) CopyDirectory(d, Path.Combine(to, Path.GetFileName(d)));
}

// ---------- 10. 4x art (64x64 tiles) ----------
{
	GBitmap.HDScale = 4;
	string pic = Path.Combine(dir, "FOUR.PIC");
	File.Copy(picPath, pic);
	string regionDir = Path.Combine(hdDir, "FOUR");
	Directory.CreateDirectory(regionDir);
	using (var art = new SKBitmap(64, 80, SKColorType.Bgra8888, SKAlphaType.Unpremul))
	{
		art.Erase(SKColors.Transparent);
		for (int y = 0; y < 16; y++) for (int x = 24; x < 40; x++) art.SetPixel(x, y, new SKColor(0xff, 0x00, 0x00));      // tall part, 4 rows
		for (int y = 16; y < 80; y++) for (int x = 8; x < 56; x++) art.SetPixel(x, y, new SKColor(0x00, (byte)(y * 3), 0xff));
		using var fs = File.Create(Path.Combine(regionDir, "0_0_t4.png")); art.Encode(fs, SKEncodedImageFormat.Png, 100);
	}
	var loaded = GBitmap.FromPICFile(pic, false)!;
	var sheet = new GBitmap();
	sheet.DrawBitmap(0, 0, loaded, false);
	var sprite = new GBitmap(16, 16);
	sprite.DrawBitmap(0, 0, sheet, new GRectangle(0, 0, 16, 16), false);
	var screen = new GBitmap();
	screen.FillRectangle(new GRectangle(0, 0, 320, 200), 5, PixelWriteModeEnum.Normal);
	screen.DrawBitmap(100, 40, sprite, true);
	screen.DrawBitmap(20, 20, other4(), true);

	int w = 320 * 4, h = 200 * 4;
	IntPtr mem = Marshal.AllocHGlobal(w * h * 4);
	screen.WriteScaledPixels(mem, w * 4);
	int[] px = new int[w * h];
	Marshal.Copy(mem, px, 0, px.Length);
	Marshal.FreeHGlobal(mem);
	uint At(int x, int y) => (uint)px[y * w + x] & 0xffffff;
	Check(At(100 * 4 + 20, 40 * 4 + 20) == ((0u << 16) | ((uint)(36 * 3) << 8) | 0xff), $"4x: HD pixel drawn at full 4x detail ({At(100 * 4 + 20, 40 * 4 + 20):x6})");
	Check(At(100 * 4 + 21, 40 * 4 + 21) != At(100 * 4 + 21, 40 * 4 + 22), "4x: neighbouring rows within one original pixel differ (4x4 blocks are used)");
	Check(At(100 * 4 + 30, 40 * 4 - 8) == 0xff0000, "4x: tall art is drawn above the sprite");
	uint bg = PaletteColor(screen, 5) & 0xffffff;
	Check(At(0, 0) == bg && At(3, 3) == bg, "4x: pixels without HD art are a 4x4 nearest-neighbour block");
	Check(At(20 * 4, 20 * 4) == (PaletteColor(screen, 12) & 0xffffff), "4x: sprites without HD art fall back to the original");
	GBitmap.HDScale = 2;
}

GBitmap other4()
{
	var b = new GBitmap(4, 4);
	b.FillRectangle(new GRectangle(0, 0, 4, 4), 12, PixelWriteModeEnum.Normal);
	return b;
}

// ---------- 11. painted map ----------
{
	string repo = AppContext.BaseDirectory;
	while (!Directory.Exists(Path.Combine(repo, "assets", "painted")))
		repo = Path.GetDirectoryName(repo.TrimEnd(Path.DirectorySeparatorChar))!;
	// PAINTED_ART previews another painted art folder, for example a theme's
	string art = Environment.GetEnvironmentVariable("PAINTED_ART") ?? Path.Combine(repo, "assets", "painted");

	string[] scene = {
		"oooooooooooooooooooooo", "oooooaaaaooooooooooooo", "ooootttttaoooooggooooo", "ooogttffhmmooogppgoooo",
		"oogggffhhmmmooggpffooo", "oogpgrrrhhmooggprfffoo", "ooppgrggggmooogprgfjoo", "oodppgrfffjoooogrgjjjo",
		"oodddprssjjjooooorjjoo", "ooodddrsssjooooooooooo", "oooodddrsooooooooooooo", "oooooooooooooooooooooo" };
	const string codes = "dpgfhmtasjor";
	int[,] map = new int[80, 50];
	for (int y = 0; y < 50; y++)
		for (int x = 0; x < 80; x++)
			map[x, y] = 10;
	for (int y = 0; y < scene.Length; y++)
		for (int x = 0; x < scene[y].Length; x++)
			map[x + 4, y + 2] = codes.IndexOf(scene[y][x]);
	// land across the world's wrap-around edge
	for (int y = 20; y < 26; y++)
		for (int x = 76; x < 84; x++)
			map[x % 80, y] = (x % 3 == 0) ? 3 : 2;

	Check(PaintedMap.Load(dir, 64, (x, y) => 10) == null, "painted: no hd/painted folder, no painted map");
	PaintedMap painted = PaintedMap.Load(art, 64, (x, y) => map[x, y])!;
	Check(painted != null, "painted: art folder loads");

	uint[] Cell(PaintedMap m, int x, int y) { uint[] c = new uint[64 * 64]; m.CopyCell(x, y, c, 0, 64); return c; }
	double Diff(uint a, uint b) => Math.Abs((int)((a >> 16) & 255) - (int)((b >> 16) & 255)) + Math.Abs((int)((a >> 8) & 255) - (int)((b >> 8) & 255)) + Math.Abs((int)(a & 255) - (int)(b & 255));
	// mean colour step across the border between two horizontally adjacent cells, and inside the left one
	(double Across, double Inside) Steps(PaintedMap m, int x, int y)
	{
		// inside: the largest of the last three steps, as art drawn larger than 1:1 alternates small and large steps
		uint[] l = Cell(m, x, y), r = Cell(m, x + 1, y);
		double across = 0;
		double[] inside = new double[3];
		for (int i = 0; i < 64; i++)
		{
			across += Diff(l[i * 64 + 63], r[i * 64]);
			for (int s = 0; s < 3; s++)
				inside[s] += Diff(l[i * 64 + 60 + s], l[i * 64 + 61 + s]);
		}
		return (across / 64, inside.Max() / 64);
	}

	PaintedMap small = PaintedMap.Load(art, 32, (x, y) => map[x, y])!;
	uint[] smallCell = new uint[32 * 32];
	small.CopyCell(10, 6, smallCell, 0, 32);
	Check(smallCell.Distinct().Count() > 100, "painted: paints at 2x too (32 pixels per cell)");

	// other art (a print with crisp areas) is checked for seams on plain grassland, where content edges cannot fall on the seam
	PaintedMap seamMap = Environment.GetEnvironmentVariable("PAINTED_ART") == null ? painted : PaintedMap.Load(art, 64, (x, y) => 2)!;
	string? seamOut = Environment.GetEnvironmentVariable("PAINTED_SEAM");
	if (seamOut != null)
	{
		using var seamImage = new SKBitmap(4 * 64, 2 * 64, SKColorType.Bgra8888, SKAlphaType.Premul);
		for (int cy2 = 0; cy2 < 2; cy2++)
			for (int cx2 = 0; cx2 < 4; cx2++)
			{
				uint[] c = Cell(seamMap, 6 + cx2, 7 + cy2);
				for (int y = 0; y < 64; y++)
					for (int x = 0; x < 64; x++)
						seamImage.SetPixel(cx2 * 64 + x, cy2 * 64 + y, new SKColor(c[y * 64 + x] | 0xff000000));
			}
		using var seamFile = File.OpenWrite(seamOut);
		seamImage.Encode(seamFile, SKEncodedImageFormat.Png, 100);
	}
	var chunkBorder = Steps(seamMap, 7, 7);
	Check(chunkBorder.Across < chunkBorder.Inside * 2 + 4, $"painted: no seam between chunks ({chunkBorder.Across:0.0} across vs {chunkBorder.Inside:0.0} inside)");
	var wrap = Steps(seamMap, 79, 22);
	if (Environment.GetEnvironmentVariable("PAINTED_SEAM") is string wrapOut)
	{
		using var wrapImage = new SKBitmap(4 * 64, 64, SKColorType.Bgra8888, SKAlphaType.Premul);
		foreach ((int cx3, int i3) in new[] { (77, 0), (78, 1), (79, 2), (0, 3) })
		{
			uint[] c = Cell(seamMap, cx3, 22);
			for (int y = 0; y < 64; y++)
				for (int x = 0; x < 64; x++)
					wrapImage.SetPixel(i3 * 64 + x, y, new SKColor(c[y * 64 + x] | 0xff000000));
		}
		using var wrapFile = File.OpenWrite(wrapOut + ".wrap.png");
		wrapImage.Encode(wrapFile, SKEncodedImageFormat.Png, 100);
	}
	Check(wrap.Across < wrap.Inside * 2 + 4, $"painted: no seam where the world wraps around ({wrap.Across:0.0} vs {wrap.Inside:0.0})");
	Check(Cell(painted, 10, 6).SequenceEqual(Cell(painted, 10, 6)), "painted: a cell paints the same every time");
	if (Environment.GetEnvironmentVariable("PAINTED_ART") == null)
		Check(Cell(painted, 50, 40).All(c => ((c >> 16) & 255) < 120 && (c & 255) >= ((c >> 16) & 255)), "painted: open ocean is dark water");

	uint[] before = Cell(painted, 9, 8);
	int old = map[9, 8];
	map[9, 8] = 0;
	Check(!before.SequenceEqual(Cell(painted, 9, 8)), "painted: a terrain change paints the cell again");
	map[9, 8] = old;
	Check(before.SequenceEqual(Cell(painted, 9, 8)), "painted: changing it back restores the painting");

	GBitmap.HDScale = 4;
	var screen = new GBitmap(320, 200);
	screen.FillRectangle(new GRectangle(0, 0, 320, 200), 5, PixelWriteModeEnum.Normal);
	screen.DrawPaintedCell(96, 24, painted, 10, 6);
	int w = 320 * 4, h = 200 * 4;
	IntPtr mem = Marshal.AllocHGlobal(w * h * 4);
	screen.WriteScaledPixels(mem, w * 4);
	int[] px = new int[w * h];
	Marshal.Copy(mem, px, 0, px.Length);
	Marshal.FreeHGlobal(mem);
	uint[] expect = Cell(painted, 10, 6);
	bool same = true;
	for (int y = 0; y < 64; y++)
		for (int x = 0; x < 64; x++)
			same &= ((uint)px[(24 * 4 + y) * w + 96 * 4 + x] & 0xffffff) == (expect[y * 64 + x] & 0xffffff);
	Check(same, "painted: DrawPaintedCell shows the cell's painting at 4x");
	Check(((uint)px[0] & 0xffffff) == (PaletteColor(screen, 5) & 0xffffff), "painted: the rest of the screen is untouched");
	var other = new GBitmap(320, 200);
	other.DrawPaintedCell(0, 0, new PaintedMap(32, (x, y) => 10), 0, 0);
	Check(!HasHD(other), "painted: a painted map of the wrong cell size is ignored");

	// moving water: the painting marks open water and rivers, the screen moves them when shown
	int Marked(uint[] c, uint kind) => c.Count(p => (p & 0xff000000) == kind);
	Check(Marked(Cell(painted, 40, 40), GBitmap.SeaAlpha) == 64 * 64, "water: open sea is marked as sea");
	Check(Marked(Cell(painted, 13, 6), GBitmap.SeaAlpha) + Marked(Cell(painted, 13, 6), GBitmap.RiverAlpha) == 0, "water: a mountain cell has no water");
	Check(Marked(Cell(painted, 9, 7), GBitmap.RiverAlpha) > 0, "water: a river cell has river water");
	var sea = new GBitmap(320, 200);
	sea.DrawPaintedCell(96, 24, painted, 40, 40);
	IntPtr seaMem = Marshal.AllocHGlobal(w * h * 4);
	GBitmap.AnimateWater = false;
	sea.WriteScaledPixels(seaMem, w * 4);
	int[] still = new int[w * h];
	Marshal.Copy(seaMem, still, 0, still.Length);
	uint[] seaCell = Cell(painted, 40, 40);
	bool stillSame = true;
	for (int y = 0; y < 64; y++)
		for (int x = 0; x < 64; x++)
			stillSame &= ((uint)still[(24 * 4 + y) * w + 96 * 4 + x] & 0xffffff) == (seaCell[y * 64 + x] & 0xffffff);
	Check(sea.ShowsWater && stillSame, "water: a screen with sea shows it, still when moving water is off");
	GBitmap.AnimateWater = true;
	sea.WriteScaledPixels(seaMem, w * 4);
	Marshal.FreeHGlobal(seaMem);
	Check(sea.ShowsWater, "water: a screen with sea shows it when moving water is on");
	IntPtr otherMem = Marshal.AllocHGlobal(w * h * 4);
	other.WriteScaledPixels(otherMem, w * 4);
	Marshal.FreeHGlobal(otherMem);
	Check(!other.ShowsWater, "water: a screen without painted water does not");
	GBitmap.HDScale = 2;

	// resources and huts: a print paints them into its scenery
	var featureCells = new Dictionary<(int, int), int> {
		[(10, 5)] = PaintedMap.MinorTribeHut, [(13, 5)] = PaintedMap.MinorTribeHut, [(7, 5)] = PaintedMap.MinorTribeHut,
		[(12, 5)] = PaintedMap.SpecialResource, [(23, 8)] = PaintedMap.SpecialResource, [(16, 3)] = PaintedMap.SpecialResource,
		[(19, 5)] = PaintedMap.SpecialResource | PaintedMap.MinorTribeHut, [(6, 9)] = PaintedMap.SpecialResource };
	// and in the preview, one resource on each kind of terrain
	if (Environment.GetEnvironmentVariable("PAINTED_PREVIEW") != null)
	{
		var seen = new HashSet<int>();
		for (int y = 3; y < 13; y++)
			for (int x = 6; x < 26; x++)
				if ((x, y) != (10, 9) && !featureCells.ContainsKey((x, y)) && seen.Add(map[x, y]) &&
					!featureCells.ContainsKey((x - 1, y)) && !featureCells.ContainsKey((x, y - 1)))
					featureCells[(x, y)] = PaintedMap.SpecialResource;
	}
	PaintedMap featured = PaintedMap.Load(art, 64, (x, y) => map[x, y])!;
	featured.FeatureAt = (x, y) => featureCells.TryGetValue((x, y), out int f) ? f : 0;
	if (featured.Print)
	{
		Check(featured.PaintsFeatures, "features: a print with objects paints resources and huts");
		Check(!Cell(painted, 10, 5).SequenceEqual(Cell(featured, 10, 5)), "features: a hut is painted into a forest cell");
		Check(Cell(painted, 10, 9).SequenceEqual(Cell(featured, 10, 9)), "features: cells without one are unchanged");
	}
	else
		Check(!featured.PaintsFeatures, "features: a painterly map leaves resources and huts to the game");

	// ISO_LEGEND renders a terrain legend from the isometric renderer: one row per terrain, plain and with its special
	// resource, a road, irrigation, a mine and a hut
	if (Environment.GetEnvironmentVariable("ISO_LEGEND") is string legendOut && Environment.GetEnvironmentVariable("ISO_THEME") is string legendTheme)
	{
		string[] names = { "Desert", "Plains", "Grassland", "Forest", "Hills", "Mountains", "Tundra", "Arctic", "Swamp", "Jungle", "Ocean", "River" };
		string[] columns = { "plain", "resource", "road", "irrigation", "mine", "fortress", "pollution", "hut" };
		const int cw = 220, ch = 150, lw = 110;
		using var sheet = new SKBitmap(lw + (columns.Length * cw), 30 + (names.Length * ch));
		using var sc = new SKCanvas(sheet);
		sc.Clear(new SKColor(30, 26, 22));
		using var font = new SKFont(SKTypeface.Default, 16);
		using var ink = new SKPaint { Color = new SKColor(244, 236, 216), IsAntialias = true };
		for (int c = 0; c < columns.Length; c++)
			sc.DrawText(columns[c], lw + (c * cw) + 10, 20, font, ink);
		for (int t = 0; t < names.Length; t++)
		{
			sc.DrawText(names[t], 8, 30 + (t * ch) + (ch / 2), font, ink);
			for (int c = 0; c < columns.Length; c++)
			{
				var view = OpenCivOne.Graphics.IsoView.Load(legendTheme, cw, ch)!;
				int terrain = t, column = c;
				// a 5 x 5 patch of the terrain (ocean around ocean, grassland around the rest) with the sample in the middle
				var sceneL = new OpenCivOne.Graphics.IsoView.Scene
				{
					Terrain = (x, y) => (Math.Abs(x - 40) <= 1 && Math.Abs(y - 25) <= 1 && terrain != 11) || terrain == 10 ? terrain : (terrain == 11 && x == 40 && Math.Abs(y - 25) <= 2 ? 11 : (terrain == 11 && x == 40 && y == 28 ? 10 : 2)),
					// road, irrigation and pollution cover the block (so roads join and fields meet); mine and fortress the middle
					Improvements = (x, y) => (Math.Abs(x - 40) > 1 || Math.Abs(y - 25) > 1) ? 0 :
						column switch { 2 => 0x8, 3 => 0x2, 6 => 0x40, 4 when (x, y) == (40, 25) => 0x4, 5 when (x, y) == (40, 25) => 0x20, _ => 0 },
					Feature = (x, y) => (x, y) != (40, 25) ? 0 : column == 1 ? 1 : column == 7 ? 2 : 0,
				};
				view.Render(40.5f, 25.5f, sceneL);
				using var cell = new SKBitmap(cw, ch, SKColorType.Bgra8888, SKAlphaType.Premul);
				Marshal.Copy(Array.ConvertAll(view.Pixels, v => unchecked((int)v)), 0, cell.GetPixels(), view.Pixels.Length);
				sc.DrawBitmap(cell, lw + (c * cw), 30 + (t * ch));
			}
		}
		using var legendFile = File.OpenWrite(legendOut);
		sheet.Encode(legendFile, SKEncodedImageFormat.Png, 100);
		Console.WriteLine("legend written");
	}

	// ISO_TRANSITIONS renders each land terrain bordering grassland, ocean and its neighbour in the list, and an
	// unexplored edge
	if (Environment.GetEnvironmentVariable("ISO_TRANSITIONS") is string trOut && Environment.GetEnvironmentVariable("ISO_THEME") is string trTheme)
	{
		string[] names = { "Desert", "Plains", "Grassland", "Forest", "Hills", "Mountains", "Tundra", "Arctic", "Swamp", "Jungle" };
		string[] columns = { "| grassland", "| ocean", "| next terrain", "| unexplored" };
		const int cw = 300, ch = 170, lw = 110;
		using var sheet = new SKBitmap(lw + (columns.Length * cw), 30 + (names.Length * ch));
		using var sc = new SKCanvas(sheet);
		sc.Clear(new SKColor(30, 26, 22));
		using var font = new SKFont(SKTypeface.Default, 16);
		using var ink = new SKPaint { Color = new SKColor(244, 236, 216), IsAntialias = true };
		for (int c = 0; c < columns.Length; c++)
			sc.DrawText(columns[c], lw + (c * cw) + 10, 20, font, ink);
		for (int t = 0; t < names.Length; t++)
		{
			sc.DrawText(names[t], 8, 30 + (t * ch) + (ch / 2), font, ink);
			for (int c = 0; c < columns.Length; c++)
			{
				var view = OpenCivOne.Graphics.IsoView.Load(trTheme, cw, ch)!;
				int terrain = t, column = c, neighbour = c switch { 0 => 2, 1 => 10, 2 => (t + 1) % 10, _ => t };
				// the terrain on the left half (x < 40), the other on the right; a ragged diagonal border
				var sceneT = new OpenCivOne.Graphics.IsoView.Scene
				{
					Terrain = (x, y) => (x - y + ((x * 7 + y * 3) % 2)) < 15 ? terrain : neighbour,
					Explored = (x, y) => column != 3 || (x - y + ((x * 5 + y) % 2)) < 15,
				};
				view.Render(40.5f, 25.5f, sceneT);
				using var cell = new SKBitmap(cw, ch, SKColorType.Bgra8888, SKAlphaType.Premul);
				Marshal.Copy(Array.ConvertAll(view.Pixels, v => unchecked((int)v)), 0, cell.GetPixels(), view.Pixels.Length);
				sc.DrawBitmap(cell, lw + (c * cw), 30 + (t * ch));
			}
		}
		using var trFile = File.OpenWrite(trOut);
		sheet.Encode(trFile, SKEncodedImageFormat.Png, 100);
		Console.WriteLine("transitions written");
	}

	// ISO_SCENE renders a judging scene at the default zoom: a city with eight irrigated tiles and roads leaving it on
	// three sides, a road to a second city that branches towards the hills and crosses a river, a junction in the
	// woods, and a railroad from the second city along the coast
	if (Environment.GetEnvironmentVariable("ISO_SCENE") is string sceneOut && Environment.GetEnvironmentVariable("ISO_THEME") is string sceneTheme)
	{
		int SceneTerrain(int x, int y)
		{
			if (x + y > 74 || x - y > 22) return 10;                   // sea to the south-east and east
			if (Math.Abs((x - 34) - (y - 20)) <= 1 && x + y >= 54 && x < 40) return 3;  // a wood
			if (x >= 44 && y <= 22) return 4;                           // hills
			if (x >= 46 && y <= 19) return 5;                           // mountains
			if (y >= 18 && x == (y < 21 ? 39 : y < 24 ? 40 : y < 28 ? 41 : 42) || (x, y) == (41, 28) || (x, y) == (40, 24)) return 11; // a river from the hills to the sea
			if ((x * 7 + y * 13) % 11 == 0) return 1;                   // patches of plains
			return 2;
		}
		(int X, int Y) rome = (38, 26), nara = (44, 29);
		var roadCells = new HashSet<(int, int)> { (39, 26), (40, 27), (41, 27), (42, 28), (43, 28), (43, 27), (44, 26), (45, 25), (45, 24),
			(37, 26), (36, 26), (35, 27), (34, 27), (38, 25), (38, 24), (37, 23), (39, 23) };
		// a railroad from Nara along the coast and up into the hills
		var railCells = new HashSet<(int, int)> { (45, 28), (46, 27), (47, 26), (47, 25), (47, 24), (46, 24) };
		var view = OpenCivOne.Graphics.IsoView.Load(sceneTheme, 960, 768)!;
		var sceneS = new OpenCivOne.Graphics.IsoView.Scene
		{
			Terrain = SceneTerrain,
			Improvements = (x, y) => (x, y) == rome || (x, y) == nara ? 0x9 : railCells.Contains((x, y)) ? 0x18 : roadCells.Contains((x, y)) ? 0x8 | (x == 36 && y == 26 ? 0x2 : 0) :
				(Math.Abs(x - rome.X) <= 1 && Math.Abs(y - rome.Y) <= 2 && SceneTerrain(x, y) != 10) || (x, y) == (36, 25) || (x, y) == (36, 26) ? 0x2 : 0,
			Feature = (x, y) => (x, y) == (35, 29) ? 1 : 0,
		};
		sceneS.Cities.Add((rome.X, rome.Y, "Rome", 6, 0xffc43a2cu));
		sceneS.Cities.Add((nara.X, nara.Y, "Nara", 3, 0xffc43a2cu));
		sceneS.Units.Add((40, 24, 7, 0xffc43a2cu));
		sceneS.Units.Add((37, 29, 2, 0xff345caau));
		view.Render(41f, 27.5f, sceneS);
		using var img = new SKBitmap(960, 768, SKColorType.Bgra8888, SKAlphaType.Premul);
		Marshal.Copy(Array.ConvertAll(view.Pixels, v => unchecked((int)v)), 0, img.GetPixels(), view.Pixels.Length);
		using var sceneFile = File.OpenWrite(sceneOut);
		img.Encode(sceneFile, SKEncodedImageFormat.Png, 100);
		Console.WriteLine("scene written");
	}

	// ISO_TRANSECT renders a strip of land from the pole to the equator: arctic, tundra, conifer forest, grassland,
	// plains, desert and jungle in that order, each zone with a mountain group, a hill group, the river that runs
	// through them all and a stretch of coast, so every terrain variant and every border can be judged in one picture
	if (Environment.GetEnvironmentVariable("ISO_TRANSECT") is string transectOut && Environment.GetEnvironmentVariable("ISO_THEME") is string transectTheme)
	{
		const int Desert = 0, Plains = 1, Grassland = 2, Forest = 3, Hills = 4, Mountains = 5, Tundra = 6, Arctic = 7, Jungle = 9, Water = 10, River = 11;
		// the zones, by row: (first row, terrain)
		(int From, int Terrain)[] zones = { (0, Arctic), (2, Tundra), (6, Forest), (11, Grassland), (15, Plains), (19, Desert), (24, Jungle) };

		int Zone(int y)
		{
			int t = zones[0].Terrain;
			foreach (var z in zones) if (y >= z.From) t = z.Terrain;
			return t;
		}

		int StripTerrain(int x, int y)
		{
			if (y > 29 || x < 30 || x > 46) return Water;
			int zone = Zone(y);
			int zoneTop = 0;
			foreach (var z in zones) if (y >= z.From) zoneTop = z.From;
			int mid = zoneTop + 1;

			// the coast: ragged, a cell in or out by row
			if (x >= 45 + ((y * 7) % 3 == 0 ? 1 : 0)) return Water;
			// the river down the middle of every zone, then east to the sea
			// the river winds down through every zone and runs straight out into the sea at the bottom
			int riverX = 41 + (int)MathF.Round(MathF.Sin(y * 0.45f) * 1.6f);
			if (y >= 2 && y <= 29 && x == riverX) return River;
			if (zone == Arctic) return Arctic;
			// a mountain group and a hill group in each zone
			if (y >= mid && y <= mid + 1 && (x == 32 || x == 33) && !(x == 33 && y == mid + 1)) return Mountains;
			if (y >= mid && y <= mid + 1 && (x == 36 || x == 37)) return Hills;
			// conifer forest with clearings, grassland with a wood, plains with a little grass
			if (zone == Forest && (x * 5 + y * 3) % 7 == 0) return Grassland;
			if (zone == Grassland && x == 39 && y == 13) return Forest;
			return zone;
		}

		var tView = OpenCivOne.Graphics.IsoView.Load(transectTheme, 2400, 1300)!;
		var tScene = new OpenCivOne.Graphics.IsoView.Scene { Terrain = StripTerrain };
		tView.Render(40, 15, tScene);
		using var tImg = new SKBitmap(2400, 1300, SKColorType.Bgra8888, SKAlphaType.Premul);
		Marshal.Copy(Array.ConvertAll(tView.Pixels, v => unchecked((int)v)), 0, tImg.GetPixels(), tView.Pixels.Length);
		using var tFile = File.Create(transectOut);
		tImg.Encode(tFile, SKEncodedImageFormat.Png, 100);
		Console.WriteLine("transect written");
	}

	// ISO_GENWORLDS generates the terrain QA fixtures (tools/isoproof/terrain-qa/fixtures.json) with the game's own map
	// generator, without a screen, and writes each world as a dump (terrain:features per cell) into the folder given:
	// NAME-classic.txt with its settings, NAME-young.txt with the youngest age and NAME-smoothed.txt with terrain smoothing
	if (Environment.GetEnvironmentVariable("ISO_GENWORLDS") is string genOut)
	{
		string fixturesPath = Path.Combine(AppContext.BaseDirectory, "../../../../../tools/isoproof/terrain-qa/fixtures.json");
		using var fixtures = System.Text.Json.JsonDocument.Parse(File.ReadAllText(fixturesPath));
		OpenCivOne.MapInitAndIntro.SkipAnimation = true;
		Directory.CreateDirectory(genOut);

		foreach (var world in fixtures.RootElement.GetProperty("worlds").EnumerateArray())
		{
			string name = world.GetProperty("name").GetString()!;

			foreach ((string mode, bool young) in new[] { ("classic", false), ("young", true), ("smoothed", false) })
			{
				OpenCivOne.MapInitAndIntro.ForceTerrainSmoothing = mode == "smoothed";
				// a fresh game for every world, as a new game starts with empty tables of continents and oceans
				var game = new OpenCivOne.OpenCivOneGame(null!);
				game.GameData.RandomSeed = (ushort)world.GetProperty("seed").GetInt32();
				game.Var_d76a_EarthMap = false;
				game.Var_7ef6_PlanetLandMass = world.GetProperty("land").GetInt32();
				game.Var_7ef8_PlanetTemperature = world.GetProperty("temperature").GetInt32();
				game.Var_7efa_PlanetClimate = world.GetProperty("climate").GetInt32();
				game.Var_7efc_PlanetAge = young ? 0 : world.GetProperty("age").GetInt32();
				game.MapInitAndIntro.GenerateNewMap();
				var lines = new List<string>();

				for (int y = 0; y < 50; y++)
				{
					var row = new List<string>();

					for (int x = 0; x < 80; x++)
					{
						var t = game.MapManagement.GetTerrainType(x, y);
						int f = (game.MapManagement.F0_2aea_1836_CellHasSpecialResource(x, y) ? 1 : 0) | (game.MapManagement.F0_2aea_1894_CellHasMinorTribeHut(x, y, t) ? 2 : 0);
						row.Add($"{(int)t}:{f}");
					}

					lines.Add(string.Join(" ", row));
				}

				File.WriteAllLines(Path.Combine(genOut, $"{name}-{mode}.txt"), lines);
			}

			Console.WriteLine($"generated {name}");
		}
	}

	// ISO_RENDERCHECKS renders each world dump in the folder given, at the game's zoom over the whole map, and reports
	// the terrain QA render checks per world: neighbouring cells of one mountain range or wood drawn in different art
	// variants, the share of neighbouring same-terrain cells drawing the identical sprite, and sprites larger than a city
	if (Environment.GetEnvironmentVariable("ISO_RENDERCHECKS") is string rcDir && Environment.GetEnvironmentVariable("ISO_THEME") is string rcTheme)
	{
		var report = new List<string> { "world\tvariant mismatches\tidentical neighbours (%)\tlarger than a city" };

		foreach (string dump in Directory.GetFiles(rcDir, "*.txt").OrderBy(f => f))
		{
			string[] rows = File.ReadAllLines(dump);
			int[,] t = new int[80, 50];

			for (int y = 0; y < 50; y++)
			{
				string[] cs = rows[y].Split(' ');

				for (int x = 0; x < 80; x++)
					t[x, y] = int.Parse(cs[x].Split(':')[0]);
			}

			var view = OpenCivOne.Graphics.IsoView.Load(rcTheme, 960, 768)!;
			var rcScene = new OpenCivOne.Graphics.IsoView.Scene { Terrain = (x, y) => t[x, y] };
			var placed = new List<(int X, int Y, string Set, int Index, int Width, int Height)>();

			// views every 8 cells across and down cover the whole world
			for (int cy = 4; cy < 50; cy += 8)
			{
				for (int cx = 4; cx < 80; cx += 8)
				{
					view.Render(cx + 0.5f, cy + 0.5f, rcScene);
					placed.AddRange(view.Placements);
				}
			}

			// the art family of a set: the variants of peaks, hills and woods
			string Family(string set) => set.StartsWith("peaks") ? "peaks" : set.StartsWith("hills") ? "hills" : set is "trees" or "broadleaf" ? "wood" : set;
			var setAt = new Dictionary<(int, int, string), string>();

			// the art of a range, a group of hills or a wood: what its own cells draw (not copses or rises on open land)
			foreach (var p in placed)
				if ((t[p.X, p.Y] == 5 && Family(p.Set) == "peaks") || (t[p.X, p.Y] == 4 && Family(p.Set) == "hills") || (t[p.X, p.Y] == 3 && Family(p.Set) == "wood"))
					setAt.TryAdd((p.X, p.Y, Family(p.Set)), p.Set);

			int mismatches = 0;

			foreach (var kv in setAt)
			{
				(int x, int y, string fam) = kv.Key;
				int terrain = t[x, y];

				// only neighbours of the same terrain (one range, one wood) are compared
				foreach ((int dx, int dy) in new[] { (1, 0), (0, 1) })
				{
					int nx = (x + dx) % 80, ny = y + dy;

					if (ny < 50 && t[nx, ny] == terrain && setAt.TryGetValue((nx, ny, fam), out string? otherSet) && otherSet != kv.Value)
						mismatches++;
				}
			}

			// the sprite each cell shows most, and how often a neighbour of the same terrain shows the very same one
			var main = placed.GroupBy(p => (p.X, p.Y)).ToDictionary(g => g.Key, g => g.GroupBy(p => (p.Set, p.Index)).OrderByDescending(h => h.Count()).First().Key);
			int pairs = 0, identical = 0;

			foreach (var kv in main)
			{
				(int x, int y) = kv.Key;

				foreach ((int dx, int dy) in new[] { (1, 0), (0, 1) })
				{
					int nx = (x + dx) % 80, ny = y + dy;

					if (ny < 50 && t[nx, ny] == t[x, y] && main.TryGetValue((nx, ny), out var otherMain))
					{
						pairs++;
						identical += otherMain == kv.Value ? 1 : 0;
					}
				}
			}

			(int cw, int ch) = view.CitySize;
			int larger = placed.Where(p => !p.Set.StartsWith("peaks") && p.Set != "town" && p.Width > cw && p.Height > ch).Select(p => p.Set).Distinct().Count();
			report.Add($"{Path.GetFileNameWithoutExtension(dump)}\t{mismatches}\t{(pairs == 0 ? 0 : identical * 100.0 / pairs):F1}\t{larger}");
		}

		File.WriteAllLines(Path.Combine(rcDir, "render-checks.tsv"), report);
		Console.WriteLine(string.Join("\n", report));
	}

	// ISO_WORLD renders a world the game generated (written by the game with ISO_DUMP) as an overview of the whole
	// map in four bands, west to east, into ISO_WORLD_OUT
	if (Environment.GetEnvironmentVariable("ISO_WORLD") is string worldIn && Environment.GetEnvironmentVariable("ISO_WORLD_OUT") is string worldOut &&
		Environment.GetEnvironmentVariable("ISO_THEME") is string worldTheme)
	{
		string[] rows = File.ReadAllLines(worldIn);
		int[,] wt = new int[80, 50], wf = new int[80, 50];

		for (int y = 0; y < 50; y++)
		{
			string[] cells = rows[y].Split(' ');

			for (int x = 0; x < 80; x++)
			{
				string[] parts = cells[x].Split(':');
				wt[x, y] = int.Parse(parts[0]);
				wf[x, y] = int.Parse(parts[1]);
			}
		}

		var worldScene = new OpenCivOne.Graphics.IsoView.Scene { Terrain = (x, y) => wt[x, y], Feature = (x, y) => wf[x, y] };
		Directory.CreateDirectory(worldOut);

		// ISO_WORLD_VIEWS: close views at the game's own zoom instead, "x,y;x,y;..." centred on those cells
		if (Environment.GetEnvironmentVariable("ISO_WORLD_VIEWS") is string centres)
		{
			int n = 0;

			foreach (string c in centres.Split(';', StringSplitOptions.RemoveEmptyEntries))
			{
				string[] xy = c.Split(',');
				var view = OpenCivOne.Graphics.IsoView.Load(worldTheme, 960, 768)!;
				view.Render(float.Parse(xy[0]) + 0.5f, float.Parse(xy[1]) + 0.5f, worldScene);
				using var img = new SKBitmap(960, 768, SKColorType.Bgra8888, SKAlphaType.Premul);
				Marshal.Copy(Array.ConvertAll(view.Pixels, v => unchecked((int)v)), 0, img.GetPixels(), view.Pixels.Length);
				using var f = File.Create(Path.Combine(worldOut, $"view{++n}.png"));
				img.Encode(f, SKEncodedImageFormat.Png, 100);
			}

			Console.WriteLine("views written");
			return 0;
		}

		// the world seen from above in four overlapping views along the equator, each about 20 columns of the map
		for (int i = 0; i < 4; i++)
		{
			var view = OpenCivOne.Graphics.IsoView.Load(worldTheme, 2600, 2100)!;
			view.Render((i * 20) + 10, 25, worldScene);
			using var img = new SKBitmap(2600, 2100, SKColorType.Bgra8888, SKAlphaType.Premul);
			Marshal.Copy(Array.ConvertAll(view.Pixels, v => unchecked((int)v)), 0, img.GetPixels(), view.Pixels.Length);
			using var f = File.Create(Path.Combine(worldOut, $"world{i + 1}.png"));
			img.Encode(f, SKEncodedImageFormat.Png, 100);
		}

		Console.WriteLine("world written");
	}

	// ISO_SHOWCASE renders a developed world into a folder: a large continent with every terrain, rivers from the
	// mountains to the sea, cities of many sizes joined by roads and a railroad, irrigated fields, mines, a fortress,
	// pollution, huts, resources and units; an overview and close views of three cities, so the art can be judged as
	// it would look in a game well under way. ISO_SEED picks another world.
	if (Environment.GetEnvironmentVariable("ISO_SHOWCASE") is string showDir && Environment.GetEnvironmentVariable("ISO_THEME") is string showTheme)
	{
		int seed = int.TryParse(Environment.GetEnvironmentVariable("ISO_SEED"), out int sd) ? sd : 7;
		const int MW = 80, MH = 50;
		const int Desert = 0, Plains = 1, Grassland = 2, Forest = 3, Hills = 4, Mountains = 5, Tundra = 6, Arctic = 7, Swamp = 8, Jungle = 9, Water = 10, River = 11;

		float H(int x, int y, int s)
		{
			unchecked
			{
				uint h = ((uint)x * 374761393u) + ((uint)y * 668265263u) + ((uint)(s + (seed * 7919)) * 1013904223u);
				h = (h ^ (h >> 15)) * 2246822519u;
				h = (h ^ (h >> 13)) * 3266489917u;
				h ^= h >> 16;
				return (h & 0xffffff) / (float)0x1000000;
			}
		}

		float Noise(float x, float y, int s)
		{
			int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
			float fx = x - x0, fy = y - y0;
			fx = fx * fx * (3 - (2 * fx));
			fy = fy * fy * (3 - (2 * fy));
			float a = H(x0, y0, s), b = H(x0 + 1, y0, s), c = H(x0, y0 + 1, s), d = H(x0 + 1, y0 + 1, s);
			return (a + ((b - a) * fx)) + (((c + ((d - c) * fx)) - (a + ((b - a) * fx))) * fy);
		}

		float Fbm(float x, float y, int s) => (Noise(x, y, s) * 0.55f) + (Noise(x * 2.1f, y * 2.1f, s + 1) * 0.3f) + (Noise(x * 4.3f, y * 4.3f, s + 2) * 0.15f);

		var terr = new int[MW, MH];
		var elev = new float[MW, MH];

		for (int y = 0; y < MH; y++)
		{
			for (int x = 0; x < MW; x++)
			{
				float dx = (x - 40) / 27f, dy = (y - 25) / 17f;
				float e = (Fbm(x / 6f, y / 6f, 1) * 0.9f) + ((1 - ((dx * dx) + (dy * dy))) * 0.5f) - 0.5f;
				// ridges: mountains follow narrow bands where a second noise crosses its middle
				float ridge = MathF.Abs(Fbm(x / 9f, y / 9f, 5) - 0.5f);
				elev[x, y] = e;
				float m = Fbm(x / 8f, y / 8f, 9);
				int t;

				if (y <= 1 || y >= 48)
					t = Arctic; // the polar ice
				else if (e <= 0)
					t = Water;
				else if (y <= 2 || y >= 47)
					t = Arctic;
				else if (e > 0.1f && ridge < 0.022f)
					t = Mountains;
				else if (e > 0.04f && ridge < 0.055f)
					t = Hills;
				else if (y <= 6 || y >= 43)
					t = Tundra;
				else if (m < 0.4f)
					t = Math.Abs(y - 25) < 12 ? Desert : Plains;
				else if (m < 0.47f)
					t = Plains;
				else if (m < 0.57f)
					t = Grassland;
				else if (m < 0.63f)
					t = Forest;
				else if (e < 0.08f)
					t = Swamp;
				else
					t = Math.Abs(y - 25) < 9 ? Jungle : Forest;

				terr[x, y] = t;
			}
		}

		// rivers: from springs in the hills and mountains, downhill to the sea
		bool Land(int x, int y) => y >= 0 && y < MH && terr[(x + MW) % MW, y] != Water;
		(int, int)[] four = { (1, 0), (0, 1), (-1, 0), (0, -1) };
		var springs = new List<(int, int)>();

		for (int y = 4; y < MH - 4; y++)
			for (int x = 0; x < MW; x++)
				if ((terr[x, y] == Hills || terr[x, y] == Mountains) && H(x, y, 31) < 0.06f)
					springs.Add((x, y));

		foreach ((int sx, int sy) in springs.Take(7))
		{
			int x = sx, y = sy;
			var seen = new HashSet<(int, int)>();
			var course = new List<(int, int)>();

			for (int step = 0; step < 40; step++)
			{
				seen.Add((x, y));
				course.Add((x, y));
				(int, int) next = (-1, -1);
				float best = float.MaxValue;
				bool reached = false;

				foreach ((int dx, int dy) in four)
				{
					int nx = (x + dx + MW) % MW, ny = y + dy;

					if (ny < 0 || ny >= MH || seen.Contains((nx, ny)))
						continue;

					if (terr[nx, ny] == Water)
					{
						reached = true;
						break;
					}

					float e = elev[nx, ny] + (H(nx, ny, 33) * 0.08f);

					if (e < best)
					{
						best = e;
						next = (nx, ny);
					}
				}

				if (reached || next.Item1 < 0)
					break;

				(x, y) = next;
			}

			// a river shows below the mountains: the spring's own peaks stay peaks
			foreach ((int cx, int cy) in course)
				if (terr[cx, cy] != Mountains)
					terr[cx, cy] = River;
		}

		// cities on good land, spread out
		string[] names = { "Kyoto", "Osaka", "Nara", "Edo", "Sakai", "Nagoya", "Kobe", "Sendai", "Hakata", "Kamakura", "Nikko", "Hiroshima" };
		int[] sizes = { 12, 8, 6, 3, 10, 5, 2, 7, 4, 1, 9, 6 };
		var cities = new List<(int X, int Y)>();
		var candidates = new List<(float Score, int X, int Y)>();

		for (int y = 5; y < MH - 5; y++)
		{
			for (int x = 0; x < MW; x++)
			{
				int t = terr[x, y];

				if (t != Grassland && t != Plains && t != River && t != Hills)
					continue;

				float score = H(x, y, 41) * 2;

				for (int dy = -2; dy <= 2; dy++)
				{
					for (int dx = -2; dx <= 2; dx++)
					{
						int n = Land(x + dx, y + dy) ? terr[(x + dx + MW) % MW, y + dy] : Water;
						score += n is Grassland or Plains or River ? 1 : n == Water ? 0.6f : n is Forest or Hills ? 0.5f : 0;
					}
				}

				candidates.Add((score, x, y));
			}
		}

		candidates.Sort((a, b) => b.Score.CompareTo(a.Score));

		foreach (var c in candidates)
		{
			if (cities.Count >= names.Length)
				break;

			if (cities.Exists(o => Math.Max(Math.Min(Math.Abs(o.X - c.X), MW - Math.Abs(o.X - c.X)), Math.Abs(o.Y - c.Y)) < 5))
				continue;

			cities.Add((c.X, c.Y));
		}

		var imp = new int[MW, MH];

		foreach ((int cx, int cy) in cities)
		{
			imp[cx, cy] = 0x9;
			int size = sizes[cities.IndexOf((cx, cy))];

			for (int dy = -2; dy <= 2; dy++)
			{
				for (int dx = -2; dx <= 2; dx++)
				{
					int x = (cx + dx + MW) % MW, y = cy + dy;

					if (y < 0 || y >= MH || (dx == 0 && dy == 0))
						continue;

					int t = terr[x, y];

					if ((t is Grassland or Plains or Desert or River) && H(x, y, 51) < 0.12f + (size * 0.035f))
						imp[x, y] |= 0x2;

					if ((t is Hills or Mountains) && H(x, y, 52) < 0.3f)
						imp[x, y] |= 0x4;
				}
			}
		}

		// roads: each city to its two nearest, by the cheapest way over land; a railroad joins the three largest
		float Cost(int x, int y) => terr[x, y] switch { Mountains => 4, Forest or Jungle or Swamp => 2.2f, Hills => 1.8f, River => 1.4f, _ => 1 };

		List<(int, int)>? Route((int X, int Y) a, (int X, int Y) b)
		{
			var dist = new Dictionary<(int, int), float> { [a] = 0 };
			var prev = new Dictionary<(int, int), (int, int)>();
			var open = new PriorityQueue<(int, int), float>();
			open.Enqueue(a, 0);
			(int, int)[] eight = { (1, 0), (0, 1), (-1, 0), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1) };

			while (open.TryDequeue(out var cur, out float d))
			{
				if (cur == b)
					break;

				if (d > dist[cur])
					continue;

				foreach ((int dx, int dy) in eight)
				{
					int nx = (cur.Item1 + dx + MW) % MW, ny = cur.Item2 + dy;

					if (ny < 0 || ny >= MH || terr[nx, ny] == Water)
						continue;

					float nd = d + (Cost(nx, ny) * (dx != 0 && dy != 0 ? 1.15f : 1)) - ((imp[nx, ny] & 0x8) != 0 ? 0.6f : 0);

					if (!dist.TryGetValue((nx, ny), out float old) || nd < old)
					{
						dist[(nx, ny)] = nd;
						prev[(nx, ny)] = cur;
						open.Enqueue((nx, ny), nd);
					}
				}
			}

			if (!prev.ContainsKey(b))
				return null;

			var path = new List<(int, int)>();

			for ((int, int) p = b; p != a; p = prev[p])
				path.Add(p);

			return path;
		}

		int Dist((int X, int Y) a, (int X, int Y) b) => Math.Max(Math.Min(Math.Abs(a.X - b.X), MW - Math.Abs(a.X - b.X)), Math.Abs(a.Y - b.Y));

		foreach (var c in cities)
		{
			foreach (var o in cities.Where(o => o != c).OrderBy(o => Dist(c, o)).Take(2))
			{
				if (Dist(c, o) > 14)
					continue;

				foreach ((int x, int y) in Route(c, o) ?? new List<(int, int)>())
					imp[x, y] |= 0x8;
			}
		}

		var largest = cities.OrderByDescending(c => sizes[cities.IndexOf(c)]).Take(3).ToList();

		for (int i = 0; i + 1 < largest.Count; i++)
			foreach ((int x, int y) in Route(largest[i], largest[i + 1]) ?? new List<(int, int)>())
				imp[x, y] |= 0x18;

		// a fortress on a hill, pollution beside the largest city
		foreach (var c in cities)
		{
			for (int dy = -3; dy <= 3; dy++)
				for (int dx = -3; dx <= 3; dx++)
				{
					int x = (c.X + dx + MW) % MW, y = c.Y + dy;

					if (y > 0 && y < MH && terr[x, y] == Hills && Math.Max(Math.Abs(dx), Math.Abs(dy)) == 3 && H(x, y, 61) < 0.04f)
						imp[x, y] |= 0x20;
				}
		}

		var big = largest[0];
		imp[(big.X + 1) % MW, big.Y + 1] |= 0x40;
		imp[(big.X + MW - 1) % MW, big.Y] |= 0x40;

		var showScene = new OpenCivOne.Graphics.IsoView.Scene
		{
			Terrain = (x, y) => terr[x, y],
			Improvements = (x, y) => imp[x, y],
			Feature = (x, y) =>
			{
				if (cities.Contains((x, y)))
					return 0;

				bool coast = false;

				foreach ((int dx, int dy) in four)
					coast |= Land(x + dx, y + dy);

				if (terr[x, y] == Water)
					return coast && H(x, y, 71) < 0.08f ? 1 : 0;

				return H(x, y, 72) < 0.035f ? 1 : H(x, y, 73) < 0.015f && cities.TrueForAll(c => Dist(c, (x, y)) > 3) ? 2 : 0;
			},
		};

		uint[] colours = { 0xffc43a2cu, 0xff3e5bd8u, 0xffe0b830u };

		// in the showcase, only the large cities have built City Walls
		showScene.Walls = (x, y) => cities.IndexOf((x, y)) is int wi && wi >= 0 && sizes[wi] >= 8;
		// a mix of the nations' architectural styles
		string[] showStyles = { "eu", "cl", "ne", "ea", "sa", "ma", "af" };
		showScene.CityLook = (x, y) => (showStyles[Math.Max(0, cities.IndexOf((x, y))) % showStyles.Length], 1);

		for (int i = 0; i < cities.Count; i++)
			showScene.Cities.Add((cities[i].X, cities[i].Y, names[i], sizes[i], colours[i % 3 == 2 ? 1 : 0]));

		int unitIndex = 0;

		foreach (var c in cities)
		{
			showScene.Units.Add(((c.X + 1) % MW, c.Y - 1, new[] { 2, 3, 7, 0, 4, 6 }[unitIndex % 6], colours[unitIndex % 3 == 2 ? 1 : 0]));
			unitIndex++;
		}

		Directory.CreateDirectory(showDir);

		void Shot(string file, int width, int height, float cx, float cy)
		{
			var view = OpenCivOne.Graphics.IsoView.Load(showTheme, width, height)!;
			view.Render(cx, cy, showScene);
			using var img = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
			Marshal.Copy(Array.ConvertAll(view.Pixels, v => unchecked((int)v)), 0, img.GetPixels(), view.Pixels.Length);
			using var f = File.Create(Path.Combine(showDir, file));
			img.Encode(f, SKEncodedImageFormat.Png, 100);
		}

		// the overview: the continent's middle, at the game's own zoom, four game screens wide
		Shot("overview.png", 1920, 1536, 40, 25);

		for (int i = 0; i < Math.Min(3, cities.Count); i++)
			Shot($"city{i + 1}.png", 960, 768, largest.Count > i ? largest[i].X + 1.5f : cities[i].X, largest.Count > i ? largest[i].Y + 1 : cities[i].Y);

		var counts = new int[12];
		foreach (int t in terr) counts[t]++;
		Console.WriteLine($"showcase written: {cities.Count} cities, {springs.Count} springs, terrain counts {string.Join(",", counts)}");
	}

	// ISO_SAMPLER renders one small patch for each terrain improvement (irrigation, mines, roads, railroads, a
	// fortress, pollution, huts, a city with its worked land) into a folder, one picture a panel, for a labelled sheet
	if (Environment.GetEnvironmentVariable("ISO_SAMPLER") is string samDir && Environment.GetEnvironmentVariable("ISO_THEME") is string samTheme)
	{
		const int SW = 80, SH = 50;
		var sTerr = new int[SW, SH];
		var sImp = new int[SW, SH];
		var sFeat = new int[SW, SH];

		for (int y = 0; y < SH; y++)
			for (int x = 0; x < SW; x++)
				sTerr[x, y] = (y < 3 || y > 46) ? 10 : 2;

		// name, terrain, improvement bits on the middle, line (roads/rails run across), feature
		var panels = new (string Name, int Terrain, int Imp, bool Line, int Feat)[]
		{
			("irrigation_grassland", 2, 0x2, false, 0), ("irrigation_plains", 1, 0x2, false, 0), ("irrigation_desert", 0, 0x2, false, 0),
			("mine_hills", 4, 0x4, false, 0), ("mine_mountains", 5, 0x4, false, 0), ("mine_desert", 0, 0x4, false, 0),
			("road_grassland", 2, 0x8, true, 0), ("road_forest", 3, 0x8, true, 0), ("railroad", 1, 0x18, true, 0),
			("road_irrigation", 2, 0xa, false, 0), ("fortress", 4, 0x20, false, 0), ("pollution", 2, 0x40, false, 0),
			("hut", 2, 0, false, 2), ("resources", 2, 0, false, 1), ("rail_mine_hills", 4, 0x1c, false, 0), ("city", 2, 0, false, 0),
		};

		var centres = new List<(int X, int Y)>();
		var sCities = new List<(int, int, string, int, uint)>();

		for (int i = 0; i < panels.Length; i++)
		{
			int cx = 6 + ((i % 4) * 18), cy = 8 + ((i / 4) * 10);
			centres.Add((cx, cy));
			var pn = panels[i];

			for (int dy = -2; dy <= 2; dy++)
				for (int dx = -3; dx <= 3; dx++)
				{
					int x = cx + dx, y = cy + dy;
					bool core = Math.Abs(dx) <= 2 && Math.Abs(dy) <= 1;
					sTerr[x, y] = pn.Terrain;

					if (pn.Line ? dy == 0 : core)
						sImp[x, y] = pn.Imp;

					if (pn.Feat != 0 && core && ((dx + dy) & 1) == 0)
						sFeat[x, y] = pn.Feat;
				}

			if (pn.Name == "city")
			{
				for (int dy = -2; dy <= 2; dy++)
					for (int dx = -3; dx <= 3; dx++)
						sImp[cx + dx, cy + dy] = Math.Abs(dx) <= 2 ? 0x2 : 0;

				for (int dx = -3; dx <= 3; dx++)
					sImp[cx + dx, cy] = 0x8;

				sImp[cx - 3, cy - 2] = 0x4;
				sTerr[cx - 3, cy - 2] = 4;
				sCities.Add((cx, cy, "Kyoto", 8, 0xffc43a2cu));
			}

			if (pn.Name == "resources")
			{
				// one of each terrain the resources sit on
				int[] ts = { 2, 1, 3, 4, 5, 0, 9, 8, 6 };
				int k = 0;

				for (int dy = -1; dy <= 1; dy++)
					for (int dx = -1; dx <= 1; dx++)
						sTerr[cx + dx, cy + dy] = ts[k++];
			}
		}

		var samScene = new OpenCivOne.Graphics.IsoView.Scene
		{
			Terrain = (x, y) => sTerr[x, y],
			Improvements = (x, y) => sImp[x, y],
			Feature = (x, y) => sFeat[x, y],
		};
		samScene.Cities.AddRange(sCities);
		Directory.CreateDirectory(samDir);

		for (int i = 0; i < panels.Length; i++)
		{
			var view = OpenCivOne.Graphics.IsoView.Load(samTheme, 520, 330)!;
			view.Render(centres[i].X + 0.5f, centres[i].Y + 0.5f, samScene);
			using var img = new SKBitmap(520, 330, SKColorType.Bgra8888, SKAlphaType.Premul);
			Marshal.Copy(Array.ConvertAll(view.Pixels, v => unchecked((int)v)), 0, img.GetPixels(), view.Pixels.Length);
			using var f = File.Create(Path.Combine(samDir, $"{i:00}_{panels[i].Name}.png"));
			img.Encode(f, SKEncodedImageFormat.Png, 100);
		}

		Console.WriteLine("sampler written");
	}

	// ISO_PREVIEW renders the isometric view of the test map with a theme's hd folder (ISO_THEME)
	if (Environment.GetEnvironmentVariable("ISO_PREVIEW") is string isoOut && Environment.GetEnvironmentVariable("ISO_THEME") is string isoTheme)
	{
		var iso = OpenCivOne.Graphics.IsoView.Load(isoTheme, 960, 768)!;
		var isoScene = new OpenCivOne.Graphics.IsoView.Scene
		{
			Terrain = (x, y) => map[x, y],
			Explored = (x, y) => x < 22,
			Improvements = (x, y) => (x, y) == (10, 7) ? 0x1 : ((x == 8 || x == 9) && y == 7) || (x, y) == (11, 7) ? 0x8 : (x == 9 && y == 8) ? 0x2 : 0,
			Feature = (x, y) => (x, y) == (7, 8) ? 1 : (x, y) == (13, 6) ? 2 : 0,
		};
		isoScene.Cities.Add((10, 7, "Kyoto", 5, 0xffc43a2cu));
		isoScene.Units.Add((9, 9, 0, 0xffc43a2cu));
		isoScene.Units.Add((12, 8, 1, 0xff345caau));
		var clock = System.Diagnostics.Stopwatch.StartNew();
		iso.Render(11.5f, 7.5f, isoScene);
		Console.WriteLine($"iso: rendered in {clock.ElapsedMilliseconds} ms");
		using var isoImage = new SKBitmap(960, 768, SKColorType.Bgra8888, SKAlphaType.Premul);
		Marshal.Copy(Array.ConvertAll(iso.Pixels, v => unchecked((int)v)), 0, isoImage.GetPixels(), iso.Pixels.Length);
		using var isoFile = File.OpenWrite(isoOut);
		isoImage.Encode(isoFile, SKEncodedImageFormat.Png, 100);
	}

	string? output = Environment.GetEnvironmentVariable("PAINTED_PREVIEW");
	if (output != null)
	{
		painted = featured;
		var clock = System.Diagnostics.Stopwatch.StartNew();
		using var image = new SKBitmap(22 * 64, 12 * 64, SKColorType.Bgra8888, SKAlphaType.Premul);
		for (int cy = 0; cy < 12; cy++)
			for (int cx = 0; cx < 22; cx++)
			{
				uint[] c = Cell(painted, cx + 4, cy + 2);
				for (int y = 0; y < 64; y++)
					for (int x = 0; x < 64; x++)
						image.SetPixel(cx * 64 + x, cy * 64 + y, new SKColor(c[y * 64 + x] | 0xff000000));
			}
		// 22 x 12 cells touch 9 chunks of 8 x 8 cells
		Console.WriteLine($"preview: painted in {clock.ElapsedMilliseconds} ms");
		using var file = File.OpenWrite(output);
		image.Encode(file, SKEncodedImageFormat.Png, 100);
	}
}

// ---------- 12. interface: smooth upscale, painted boxes and lines ----------
{
	uint[] Frame(GBitmap b)
	{
		int w = b.Width * GBitmap.HDScale, h = b.Height * GBitmap.HDScale;
		IntPtr mem = Marshal.AllocHGlobal(w * h * 4);
		b.WriteScaledPixels(mem, w * 4);
		int[] px = new int[w * h];
		Marshal.Copy(mem, px, 0, px.Length);
		Marshal.FreeHGlobal(mem);
		return px.Select(p => (uint)p & 0xffffff).ToArray();
	}

	GBitmap.HDScale = 4;
	var stairs = new GBitmap(8, 8);
	stairs.FillRectangle(new GRectangle(0, 0, 8, 8), 1, PixelWriteModeEnum.Normal);
	for (int i = 0; i < 8; i++)
		stairs.FillRectangle(new GRectangle(0, i, i + 1, 1), 15, PixelWriteModeEnum.Normal);
	uint[] nearest = Frame(stairs);
	GBitmap.SmoothUpscale = true;
	uint[] smooth = Frame(stairs);
	GBitmap.SmoothUpscale = false;
	int W = 32;
	Check(!nearest.SequenceEqual(smooth), "smooth: Scale4x changes a diagonal edge");
	// pixel (4, 3) is dark with light pixels to its left and below: its lower left corner turns light
	Check(smooth[(3 * 4 + 3) * W + 4 * 4] == nearest[0] && nearest[(3 * 4 + 3) * W + 4 * 4] != nearest[0], "smooth: the step beside the diagonal is filled in with the light color");
	Check(smooth[(6 * 4 + 2) * W + 1 * 4 + 2] == nearest[(6 * 4 + 2) * W + 1 * 4 + 2], "smooth: inside a flat area the color is unchanged");

	string grey = Path.Combine(dir, "grain.png");
	using (var g = new SKBitmap(16, 16))
	{
		for (int y = 0; y < 16; y++)
			for (int x = 0; x < 16; x++)
				g.SetPixel(x, y, (x + y) % 2 == 0 ? new SKColor(128, 128, 128) : new SKColor(100, 100, 100));
		using var f = File.OpenWrite(grey);
		g.Encode(f, SKEncodedImageFormat.Png, 100);
	}
	Check(GBitmap.LoadUIMaterial(grey), "material: grain texture loads");
	var box = new GBitmap(64, 64);
	box.UseUIMaterial = true;
	box.FillRectangle(new GRectangle(0, 0, 64, 64), 0, PixelWriteModeEnum.Normal);
	box.FillRectangle(new GRectangle(10, 10, 30, 30), 4, PixelWriteModeEnum.Normal);
	uint plain4 = PaletteColor(box, 4) & 0xffffff;
	uint[] f1 = Frame(box);
	uint centre = f1[(25 * 4) * 256 + 25 * 4];
	Check(centre != plain4, $"material: a box is drawn toned and grained ({centre:x6} vs flat {plain4:x6})");
	Check(f1[(25 * 4) * 256 + 25 * 4] != f1[(25 * 4) * 256 + 25 * 4 + 1], "material: neighbouring HD pixels carry the grain");
	Check(Brightness(f1[(10 * 4) * 256 + 25 * 4]) < Brightness(centre), "material: a box is darker at its edge");
	box.ReplaceColor(new GRectangle(10, 10, 30, 30), 4, 2);
	uint swapped = Frame(box)[(25 * 4) * 256 + 25 * 4];
	Check(swapped != centre && ((swapped >> 8) & 0xff) > ((swapped >> 16) & 0xff), $"material: a color swap (highlight) recolors the box ({swapped:x6})");
	bool Flat(uint[] f, int x, int y) => Enumerable.Range(0, 16).All(i => f[(y * 4 + i / 4) * 256 + x * 4 + i % 4] == f[(y * 4) * 256 + x * 4]);
	box.SetPixel(20, 20, 15);
	uint[] f2 = Frame(box);
	Check(Flat(f2, 20, 20) && Brightness(f2[(20 * 4) * 256 + 20 * 4]) > 200, "material: text drawn over a box is flat and keeps its brightness");
	box.FillRectangle(new GRectangle(50, 50, 3, 3), 4, PixelWriteModeEnum.Normal);
	uint[] f3 = Frame(box);
	Check(Flat(f3, 51, 51) && f3[(51 * 4) * 256 + 51 * 4] != plain4, "material: fills smaller than 6x6 are toned but not grained");
	box.DrawLine(0, 60, 40, 60, 15, PixelWriteModeEnum.Normal);
	uint line = Frame(box)[(60 * 4) * 256 + 5 * 4];
	Check(line != (PaletteColor(box, 15) & 0xffffff) && Brightness(line) > 120, $"material: lines take the toned color ({line:x6})");
	var plain = new GBitmap(16, 16);
	plain.FillRectangle(new GRectangle(0, 0, 16, 16), 4, PixelWriteModeEnum.Normal);
	Check(Frame(plain)[5 * 64 + 5] == plain4, "material: bitmaps without UseUIMaterial (sprite sheets) are not affected");

	string colors = Path.Combine(dir, "colors.txt");
	File.WriteAllText(colors, "# theme colors\n2 336699  # boxes\n15 F0E0C0\n");
	Check(GBitmap.LoadUIColors(colors) && GBitmap.UIColor(2) == 0xff336699 && GBitmap.UIColor(4) == null, "colors: a theme's interface colors load");
	uint[] f4 = Frame(box);
	uint themedBox = f4[(25 * 4) * 256 + 25 * 4];
	Check(((themedBox & 0xff) > ((themedBox >> 16) & 0xff)) && f4[(25 * 4) * 256 + 25 * 4] != f4[(25 * 4) * 256 + 25 * 4 + 1], $"colors: a box takes the theme color and keeps its grain ({themedBox:x6})");
	Check(Flat(f4, 20, 20) && (f4[(20 * 4) * 256 + 20 * 4] & 0xffffff) == 0xf0e0c0, "colors: text takes the theme color exactly");
	Check(Frame(plain)[5 * 64 + 5] == plain4, "colors: bitmaps without UseUIMaterial keep their palette");
	GBitmap.LoadUIColors(Path.Combine(dir, "none.txt"));
	Check(GBitmap.UIColor(2) == null, "colors: without the file the painted tone is used");
	GBitmap.ClearUIMaterial();
	GBitmap.HDScale = 2;
}

int Brightness(uint c) => (int)(((c >> 16) & 0xff) + ((c >> 8) & 0xff) + (c & 0xff)) / 3;

Directory.Delete(dir, true);
Console.WriteLine(failures == 0 ? "ALL PASSED" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;
