using OpenCivOne.Graphics;

// Writes the stand-in data set's pictures as PIC files with the game's own encoder, and its palettes as PAL files.
// Usage: PicWriter OUT_DIR  (reads OUT_DIR/raw/*.idx, OUT_DIR/raw/pals.txt and OUT_DIR/palette.bin from make_standin.py)

string outDir = args[0];
string rawDir = Path.Combine(outDir, "raw");
byte[] palette = File.ReadAllBytes(Path.Combine(outDir, "palette.bin"));

// palette block: signature "M0", length, first and last color index, then 6 bit red, green, blue for each color
byte[] paletteBlock = new byte[4 + 2 + palette.Length];
int blockLength = 2 + palette.Length;
paletteBlock[0] = 0x4d;
paletteBlock[1] = 0x30;
paletteBlock[2] = (byte)(blockLength & 0xff);
paletteBlock[3] = (byte)(blockLength >> 8);
paletteBlock[4] = 0;
paletteBlock[5] = (byte)(palette.Length / 3 - 1);
Array.Copy(palette, 0, paletteBlock, 6, palette.Length);

int pictures = 0;

foreach (string rawPath in Directory.GetFiles(rawDir, "*.idx").OrderBy(p => p, StringComparer.Ordinal))
{
	byte[] raw = File.ReadAllBytes(rawPath);
	int width = raw[0] | (raw[1] << 8);
	int height = raw[2] | (raw[3] << 8);
	GBitmap bitmap = new GBitmap(width, height);

	for (int y = 0; y < height; y++)
	{
		for (int x = 0; x < width; x++)
		{
			bitmap.SetPixel(x, y, raw[4 + (y * width) + x]);
		}
	}

	string temp = Path.Combine(rawDir, "image.tmp");
	bitmap.SaveToPIC(temp, false);

	using (FileStream output = new FileStream(Path.Combine(outDir, Path.GetFileNameWithoutExtension(rawPath) + ".PIC"), FileMode.Create))
	{
		output.Write(paletteBlock);
		output.Write(File.ReadAllBytes(temp));
	}

	File.Delete(temp);
	pictures++;
}

string[] pals = File.ReadAllLines(Path.Combine(rawDir, "pals.txt")).Where(l => l.Length > 0).ToArray();

foreach (string name in pals)
{
	File.WriteAllBytes(Path.Combine(outDir, name + ".PAL"), paletteBlock);
}

// check every picture reads back with the game's decoder
foreach (string picPath in Directory.GetFiles(outDir, "*.PIC"))
{
	if (GBitmap.FromPICFile(picPath, false) == null)
	{
		throw new Exception($"{picPath} does not read back");
	}
}

Console.WriteLine($"{pictures} PIC files and {pals.Length} PAL files written to {outDir}");
