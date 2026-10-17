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
const int S = GBitmap.HDScale;

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

Directory.Delete(dir, true);
Console.WriteLine(failures == 0 ? "ALL PASSED" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;
