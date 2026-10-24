using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Media;
using IRB.Collections.Generic;
using OpenCivOne.Compression;
using SkiaSharp;

namespace OpenCivOne.Graphics
{
	public class GBitmap
	{
		/// <summary>
		/// Scale of the optional HD layer, relative to the indexed pixels: 2 (32x32 tiles) or 4 (64x64 tiles).
		/// It has to be set at start, before any HD art is loaded.
		/// </summary>
		public static int HDScale
		{
			get => _hdScale;
			set
			{
				if (value != 2 && value != 4)
					throw new ArgumentOutOfRangeException(nameof(value), "The HD scale has to be 2 or 4");

				_hdScale = value;
			}
		}

		private static int _hdScale = 2;

		private int iWidth;
		private int iHeight;
		private GSize oSize;
		private GRectangle oRectangle;
		private bool bModified = true;
		private bool bVisible = true;

		private byte[] aPixels;
		private GBitmapPalette oPalette;

		// Optional HD layer. Each indexed pixel owns an HDScale x HDScale block of ARGB pixels,
		// which is shown instead of the indexed pixel while its mask entry is set.
		// The layer is allocated only when HD content is drawn into this bitmap.
		private uint[]? _hdPixels = null;
		private bool[]? _hdMask = null;

		// HD art reaching above sprites (tall mountains, trees), by the sprite's top left corner in this bitmap
		private Dictionary<(int X, int Y), HDOverflow>? _hdOverflow = null;
		private GRectangle? _hdOverflowClip = null;
		private static bool _hasHDOverflow = false;

		private static readonly object _hdCacheLock = new object();
		private static readonly Dictionary<string, HDImage?> _hdCache = new Dictionary<string, HDImage?>();
		private static string? _hdTemplatePath = null;

		// Optional painted grain for flat fills on screens (hd/ui/material.png): shades around 128, one per HD pixel
		private static byte[]? _uiMaterial = null;
		private static int _uiMaterialSize = 0;
		private bool _useUIMaterial = false;
		private static bool _smoothUpscale = false;

		#region Palette 1
		public static Color[] Palette1 = new Color[] {
			Color.FromUInt32(0x000000),
			Color.FromUInt32(0x0000a7),
			Color.FromUInt32(0x00a700),
			Color.FromUInt32(0x00a7a7),
			Color.FromUInt32(0xa70000),
			Color.FromUInt32(0xa700a7),
			Color.FromUInt32(0xa75300),
			Color.FromUInt32(0xa7a7a7),
			Color.FromUInt32(0x535353),
			Color.FromUInt32(0x5353fb),
			Color.FromUInt32(0x53fb53),
			Color.FromUInt32(0x53fbfb),
			Color.FromUInt32(0xfb5353),
			Color.FromUInt32(0xfb53fb),
			Color.FromUInt32(0xfbfb53),
			Color.FromUInt32(0xfbfbfb)
		};
		#endregion

		public GBitmap() : this(new GSize(320, 200))
		{ }

		public GBitmap(int width, int height) : this(new GSize(width, height))
		{ }

		public GBitmap(GSize size)
		{
			// stride has to be a multiple of 4
			this.iWidth = size.Width;
			this.iHeight = size.Height;
			this.oSize = size.Clone();
			this.oRectangle = new GRectangle(0, 0, this.iWidth, this.iHeight);

			this.aPixels = new byte[this.iWidth * this.iHeight];

			for (int i = 0; i < this.aPixels.Length; i++)
			{
				this.aPixels[i] = 0;
			}

			this.oPalette = new GBitmapPalette(this);
		}

		public int Width
		{
			get => this.iWidth;
		}

		public int Height
		{
			get => this.iHeight;
		}

		public GSize Size
		{
			get => this.oSize;
		}

		public GRectangle Rectangle
		{
			get => this.oRectangle;
		}

		public bool Modified
		{
			get => this.bModified;
			set => this.bModified = value;
		}

		public bool Visible
		{
			get => this.bVisible;
			set => this.bVisible = value;
		}

		public GBitmapPalette Palette
		{
			get => this.oPalette;
		}

		internal byte[] Pixels
		{
			get => this.aPixels;
		}

		/// <summary>
		/// HD pixels in ARGB format, (Width * HDScale) x (Height * HDScale), or null if this bitmap has no HD content
		/// </summary>
		internal uint[]? HDPixels
		{
			get => this._hdPixels;
		}

		/// <summary>
		/// One entry per indexed pixel, true when that pixel is shown from the HD layer, or null if this bitmap has no HD content
		/// </summary>
		internal bool[]? HDMask
		{
			get => this._hdMask;
		}

		/// <summary>
		/// Area where HD art reaching above sprites may be drawn in this bitmap, for example the map view, or null for anywhere
		/// </summary>
		public GRectangle? HDOverflowClip
		{
			get => this._hdOverflowClip;
			set => this._hdOverflowClip = value;
		}

		/// <summary>
		/// True once any loaded HD art reaches above its sprite, the map then has to be drawn so that such art is not overdrawn
		/// </summary>
		public static bool HasHDOverflow
		{
			get => _hasHDOverflow;
		}

		/// <summary>
		/// When set, every loaded PIC file is also written to this folder as a 2x nearest-neighbour PNG,
		/// which can be used as a starting point for HD art
		/// </summary>
		public static string? HDTemplatePath
		{
			get => _hdTemplatePath;
			set => _hdTemplatePath = value;
		}

		/// <summary>
		/// Pixels without HD art are enlarged with Scale2x (Scale4x at 4x) instead of nearest-neighbour,
		/// which rounds the steps in text, lines and leftover pixel art while keeping its colors
		/// </summary>
		public static bool SmoothUpscale
		{
			get => _smoothUpscale;
			set => _smoothUpscale = value;
		}

		/// <summary>
		/// Flat fills of at least 6x6 pixels on this bitmap get the painted grain of the UI material, see LoadUIMaterial
		/// </summary>
		public bool UseUIMaterial
		{
			get => this._useUIMaterial;
			set => this._useUIMaterial = value;
		}

		/// <summary>
		/// Loads a square grain texture for flat fills: its brightness shades the fill's palette color (mid grey keeps it),
		/// as HD key pixels, so the fill still follows palette changes, color swaps and fades. Returns false without it.
		/// </summary>
		public static bool LoadUIMaterial(string path)
		{
			if (!File.Exists(path))
				return false;

			using SKBitmap? image = SKBitmap.Decode(path);

			if (image == null || image.Width != image.Height)
				return false;

			byte[] shades = new byte[image.Width * image.Height];

			for (int y = 0; y < image.Height; y++)
			{
				for (int x = 0; x < image.Width; x++)
				{
					SKColor c = image.GetPixel(x, y);
					// 128 grey keeps the color, darker and lighter grain shade it
					shades[(y * image.Width) + x] = (byte)Math.Clamp(((c.Red * 30) + (c.Green * 59) + (c.Blue * 11)) / 100, 1, 255);
				}
			}

			_uiMaterial = shades;
			_uiMaterialSize = image.Width;

			return true;
		}

		// HD pixels of painted boxes: the shade in red and blue, like a key color, marked by this alpha
		private const uint MaterialAlpha = 0xfe000000;

		/// <summary>
		/// Color of a painted box pixel: the box's palette color, toned down to the painted look (half desaturated,
		/// a little darker and warmer, so the bright EGA colors keep their hue but sit with the art), and shaded by the grain
		/// </summary>
		private static uint MaterialColor(uint color, uint indexColor)
		{
			float shade = ((color >> 16) & 0xff) / 128f;
			float r = (indexColor >> 16) & 0xff, g = (indexColor >> 8) & 0xff, b = indexColor & 0xff;
			float grey = (r * 0.3f) + (g * 0.59f) + (b * 0.11f);
			r = ((r + grey) * 0.5f * 0.82f + 6) * shade;
			g = ((g + grey) * 0.5f * 0.82f + 3) * shade;
			b = ((b + grey) * 0.5f * 0.82f - 2) * shade;

			return 0xff000000 | ((uint)Math.Clamp(r, 0, 255) << 16) | ((uint)Math.Clamp(g, 0, 255) << 8) | (uint)Math.Clamp(b, 0, 255);
		}

		/// <summary>
		/// Turns the UI material off again
		/// </summary>
		public static void ClearUIMaterial()
		{
			_uiMaterial = null;
			_uiMaterialSize = 0;
		}

		private void FillUIMaterial(GRectangle rect)
		{
			if (_uiMaterial == null || rect.Width < 6 || rect.Height < 6)
				return;

			this.EnsureHDLayer();

			int hdWidth = this.iWidth * HDScale;
			int size = _uiMaterialSize;
			// boxes are darker toward their edges, over about three original pixels
			float edge = 3f * HDScale;

			for (int y = rect.Y * HDScale; y < (rect.Y + rect.Height) * HDScale; y++)
			{
				float dy = Math.Min(y - (rect.Y * HDScale), ((rect.Y + rect.Height) * HDScale) - 1 - y);

				for (int x = rect.X * HDScale; x < (rect.X + rect.Width) * HDScale; x++)
				{
					float dx = Math.Min(x - (rect.X * HDScale), ((rect.X + rect.Width) * HDScale) - 1 - x);
					float rim = Math.Clamp(Math.Min(dx, dy) / edge, 0, 1);
					float shade = _uiMaterial[((y % size) * size) + (x % size)] * (0.8f + (0.2f * rim));
					uint s = (uint)Math.Clamp(shade, 1, 255);

					this._hdPixels![(y * hdWidth) + x] = MaterialAlpha | (s << 16) | s;
				}
			}

			for (int y = rect.Y; y < rect.Y + rect.Height; y++)
				Array.Fill(this._hdMask!, true, (y * this.iWidth) + rect.X, rect.Width);
		}

		public static Color Color18ToColor(int red, int green, int blue)
		{
			return Color.FromArgb(0, (byte)((255 * (red & 0x3f)) / 63), (byte)((255 * (green & 0x3f)) / 63), (byte)((255 * (blue & 0x3f)) / 63));
		}

		public static Color ColorToColor18(Color color)
		{
			return Color.FromArgb(0, (byte)((63 * color.R) / 255), (byte)((63 * color.G) / 255), (byte)((63 * color.B) / 255));
		}

		public Color GetPaletteColor(byte colorIndex)
		{
			return this.oPalette[colorIndex];
		}

		public void SetPaletteColor(byte colorIndex, Color color)
		{
			this.oPalette[colorIndex] = color;

			this.bModified = true;
		}

		public void SetPaletteFromColorStruct(byte[] colorStruct)
		{
			int iStructPos = 0;

			if (colorStruct.Length > 0 && colorStruct[iStructPos] == 0x4d && colorStruct[iStructPos + 1] == 0x30)
			{
				int iFrom = colorStruct[iStructPos + 4];
				int iTo = colorStruct[iStructPos + 5];
				int iCount = (iTo - iFrom) + 1;
				iStructPos += 6;

				Color[] aColors = new Color[iCount];

				for (int i = 0; i < iCount; i++)
				{
					aColors[i] = GBitmap.Color18ToColor(colorStruct[iStructPos + (i * 3)],
						colorStruct[iStructPos + (i * 3) + 1],
						colorStruct[iStructPos + (i * 3) + 2]);
				}

				this.SetPaletteFromColorArray(aColors, iFrom, iFrom, iCount);
			}
		}

		public void SetPaletteFromColorArray(Color[] colors, int sourceIndex, int destinationIndex, int length)
		{
			for (int i = 0; i < length; i++)
			{
				this.oPalette[(byte)(destinationIndex + i)] = colors[sourceIndex + i];
			}

			this.bModified = true;
		}

		/// <summary>
		/// Copies the palette of this bitmap to the referenced bitmap
		/// </summary>
		/// <param name="bitmap">referenced bitmap</param>
		public void CopyPalette(GBitmap bitmap)
		{
			for (int i = 0; i < this.oPalette.Length; i++)
			{
				this.oPalette[(byte)i] = bitmap.Palette[(byte)i];
			}

			this.bModified = true;
		}

		public byte GetPixel(int x, int y)
		{
			if (this.oRectangle.Contains(x, y))
			{
				return this.aPixels[(y * this.iWidth) + x];
			}

			return 0;
		}

		public void SetPixel(int x, int y, byte colorIndex)
		{
			if (this.oRectangle.Contains(x, y))
			{
				this.aPixels[(y * this.iWidth) + x] = colorIndex;
				this.ClearHDPixel((y * this.iWidth) + x);

				this.bModified = true;
			}
		}

		public void SetPixel(int x, int y, byte colorIndex, PixelWriteModeEnum mode)
		{
			if (this.oRectangle.Contains(x, y))
			{
				switch (mode)
				{
					case PixelWriteModeEnum.Normal:
						this.aPixels[(y * this.iWidth) + x] = colorIndex;
						break;

					case PixelWriteModeEnum.And:
						this.aPixels[(y * this.iWidth) + x] &= colorIndex;
						break;

					case PixelWriteModeEnum.Or:
						this.aPixels[(y * this.iWidth) + x] |= colorIndex;
						break;

					case PixelWriteModeEnum.Xor:
						this.aPixels[(y * this.iWidth) + x] ^= colorIndex;
						break;
				}

				this.ClearHDPixel((y * this.iWidth) + x);
				this.bModified = true;
			}
		}

		public void DrawLine(int x1, int y1, int x2, int y2, byte colorIndex, PixelWriteModeEnum mode)
		{
			int iWidth;
			int iWidthDir = 1;
			int iHeight;
			int iHeightDir = 1;

			if (x1 > x2)
			{
				iWidth = (x1 - x2) + 1;
				iWidthDir = -1;
			}
			else
			{
				iWidth = (x2 - x1) + 1;
			}

			if (y1 > y2)
			{
				iHeight = (y1 - y2) + 1;
				iHeightDir = -1;
			}
			else
			{
				iHeight = (y2 - y1) + 1;
			}

			if (iWidth == 1 && iHeight == 1)
			{
				// a single point
				this.SetLinePixel(x1, y1, colorIndex, mode);
			}
			else if (iWidth > 1 && iHeight == 1)
			{
				// a horizontal line
				for (int i = 0; i < iWidth; i++)
				{
					this.SetLinePixel(x1 + i * iWidthDir, y1, colorIndex, mode);
				}
			}
			else if (iWidth == 1 && iHeight > 1)
			{
				// a vertical line
				for (int i = 0; i < iHeight; i++)
				{
					this.SetLinePixel(x1, y1 + i * iHeightDir, colorIndex, mode);
				}
			}
			else if (iWidth > iHeight)
			{
				double dYStep = (double)iHeight / (double)iWidth;

				for (int i = 0; i < iWidth; i++)
				{
					this.SetLinePixel(x1 + i * iWidthDir, y1 + (int)(dYStep * i) * iHeightDir, colorIndex, mode);
				}
			}
			else
			{
				double dXStep = (double)iWidth / (double)iHeight;

				for (int i = 0; i < iHeight; i++)
				{
					this.SetLinePixel(x1 + (int)(dXStep * i) * iWidthDir, y1 + i * iHeightDir, colorIndex, mode);
				}
			}
		}

		// Lines on a bitmap with the UI material take the toned colors of the painted boxes, without grain
		private void SetLinePixel(int x, int y, byte colorIndex, PixelWriteModeEnum mode)
		{
			this.SetPixel(x, y, colorIndex, mode);

			if (this._useUIMaterial && _uiMaterial != null && mode == PixelWriteModeEnum.Normal && this.oRectangle.Contains(x, y))
			{
				this.EnsureHDLayer();

				int hdAddress = this.GetHDAddress(x, y);

				for (int k = 0; k < HDScale; k++)
					Array.Fill(this._hdPixels!, MaterialAlpha | (0x80u << 16) | 0x80u, hdAddress + (k * this.iWidth * HDScale), HDScale);

				this._hdMask![(y * this.iWidth) + x] = true;
			}
		}

		public void ReplaceColor(GRectangle rect, byte oldColorIndex, byte newColorIndex)
		{
			if (rect.IntersectsWith(this.oRectangle))
			{
				GRectangle newRect = new GRectangle(rect.Location, rect.Size);
				newRect.Intersect(this.oRectangle);

				int iPixelAddress = (newRect.Y * this.iWidth) + newRect.X;

				for (int i = 0; i < newRect.Height; i++)
				{
					for (int j = 0; j < newRect.Width; j++)
					{
						if (this.aPixels[iPixelAddress + j] == oldColorIndex)
						{
							// HD pixels are kept, HD key colors follow the new color index
							this.aPixels[iPixelAddress + j] = newColorIndex;

							this.bModified = true;
						}
					}

					iPixelAddress += this.iWidth;
				}
			}
		}

		public void FillRectangle(GRectangle rect, byte colorIndex, PixelWriteModeEnum mode)
		{
			if (rect.IntersectsWith(this.oRectangle))
			{
				GRectangle newRect = new GRectangle(rect.Location, rect.Size);
				newRect.Intersect(this.oRectangle);

				int iPixelAddress = (newRect.Y * this.iWidth) + newRect.X;

				for (int i = 0; i < newRect.Height; i++)
				{
					for (int j = 0; j < newRect.Width; j++)
					{
						switch (mode)
						{
							case PixelWriteModeEnum.Normal:
								this.aPixels[iPixelAddress + j] = colorIndex;
								break;

							case PixelWriteModeEnum.And:
								this.aPixels[iPixelAddress + j] &= colorIndex;
								break;

							case PixelWriteModeEnum.Or:
								this.aPixels[iPixelAddress + j] |= colorIndex;
								break;

							case PixelWriteModeEnum.Xor:
								this.aPixels[iPixelAddress + j] ^= colorIndex;
								break;
						}

						this.ClearHDPixel(iPixelAddress + j);
					}

					iPixelAddress += this.iWidth;
				}

				if (this._useUIMaterial && mode == PixelWriteModeEnum.Normal)
				{
					this.FillUIMaterial(newRect);
				}

				this.bModified = true;
			}
		}

		public void DrawBitmap(GBitmap srcBitmap)
		{
			this.DrawBitmap(new GPoint(0, 0), srcBitmap, srcBitmap.Rectangle, false);
		}

		public void DrawBitmap(int x, int y, GBitmap srcBitmap, bool transparent)
		{
			DrawBitmap(new GPoint(x, y), srcBitmap, srcBitmap.Rectangle, transparent);
		}

		public void DrawBitmap(int x, int y, GBitmap srcBitmap, GRectangle srcRect, bool transparent)
		{
			DrawBitmap(new GPoint(x, y), srcBitmap, srcRect, transparent);
		}

		/// <summary>
		/// Shows the painting of a map cell (see PaintedMap) over the 16x16 indexed pixels at (x, y)
		/// </summary>
		public void DrawPaintedCell(int x, int y, PaintedMap map, int mapX, int mapY)
		{
			if (map.CellSize != 16 * HDScale || x < 0 || y < 0 || x + 16 > this.iWidth || y + 16 > this.iHeight)
				return;

			this.EnsureHDLayer();
			map.CopyCell(mapX, mapY, this._hdPixels!, this.GetHDAddress(x, y), this.iWidth * HDScale);

			for (int i = 0; i < 16; i++)
			{
				Array.Fill(this._hdMask!, true, ((y + i) * this.iWidth) + x, 16);
			}

			this.bModified = true;
		}

		public void DrawBitmap(GPoint destPoint, GBitmap srcBitmap, GRectangle srcRect, bool transparent)
		{
			DrawBitmap(destPoint, srcBitmap, srcRect, transparent, false);
		}

		/// <param name="greyHD">HD pixels of the source are drawn in grey (transparent drawing only)</param>
		public void DrawBitmap(GPoint destPoint, GBitmap srcBitmap, GRectangle srcRect, bool transparent, bool greyHD)
		{
			if (srcBitmap._hdOverflow != null || (!transparent && this._hdOverflow != null))
			{
				this.CopyOrDrawHDOverflow(destPoint, srcBitmap, srcRect, transparent, greyHD);
			}

			GRectangle srcRect1 = new GRectangle(srcRect.Location, srcRect.Size);
			srcRect1.Intersect(srcBitmap.Rectangle);

			if (destPoint.X < 0)
			{
				srcRect1.X -= destPoint.X;
				srcRect1.Width += destPoint.X;
				destPoint = new( 0, destPoint.Y);
			}
			if (destPoint.Y < 0)
			{
				srcRect1.Y -= destPoint.Y;
				srcRect1.Height += destPoint.Y;
				destPoint = new(destPoint.X, 0);
			}

			GRectangle destRect = new GRectangle(destPoint, srcRect1.Size);
			destRect.Intersect(this.oRectangle);

			if (destRect.Width > 0 && destRect.Height > 0)
			{
				srcRect1.Size = destRect.Size;

				int iSrcPosition = (srcRect1.Y * srcBitmap.iWidth) + srcRect1.X;
				int iDestPosition = (destRect.Y * this.iWidth) + destRect.X;
				bool[]? srcHDMask = srcBitmap._hdMask;

				if (srcHDMask != null)
				{
					this.EnsureHDLayer();
				}

				for (int i = 0; i < destRect.Height; i++)
				{
					for (int j = 0; j < destRect.Width; j++)
					{
						byte colorIndex = srcBitmap.Pixels[iSrcPosition + j];

						if (srcHDMask != null && srcHDMask[iSrcPosition + j])
						{
							int srcX = srcRect1.X + j;
							int srcY = srcRect1.Y + i;
							int destX = destRect.X + j;
							int destY = destRect.Y + i;

							if (!transparent)
							{
								this.aPixels[iDestPosition + j] = colorIndex;
								this.CopyHDBlock(destX, destY, srcBitmap, srcX, srcY);
							}
							else if (colorIndex != 0 || srcBitmap.HDBlockIsVisible(srcX, srcY))
							{
								// HD art may cover pixels that are transparent in the original sprite, and the other way around
								this.MaterializeHDPixel(destX, destY);

								if (colorIndex != 0)
								{
									// the indexed pixel changes, key colors already there keep the color they have now
									this.ResolveHDKeys(destX, destY);
								}

								this.BlendHDBlock(destX, destY, srcBitmap, srcX, srcY, greyHD);

								if (colorIndex != 0)
								{
									this.aPixels[iDestPosition + j] = colorIndex;
								}
							}
						}
						else if (colorIndex != 0 || !transparent)
						{
							this.aPixels[iDestPosition + j] = colorIndex;
							this.ClearHDPixel(iDestPosition + j);
						}
					}

					iSrcPosition += srcBitmap.iWidth;
					iDestPosition += this.iWidth;
				}

				this.bModified = true;
			}
		}

		/// <summary>
		/// Copies a single pixel, together with its HD block, from the same position in another bitmap
		/// </summary>
		public void CopyPixel(int x, int y, GBitmap srcBitmap)
		{
			if (this.oRectangle.Contains(x, y) && srcBitmap.Rectangle.Contains(x, y))
			{
				int pixelAddress = (y * this.iWidth) + x;
				int srcPixelAddress = (y * srcBitmap.iWidth) + x;

				this.aPixels[pixelAddress] = srcBitmap.aPixels[srcPixelAddress];

				if (srcBitmap._hdMask != null && srcBitmap._hdMask[srcPixelAddress])
				{
					this.EnsureHDLayer();
					this.CopyHDBlock(x, y, srcBitmap, x, y);
				}
				else
				{
					this.ClearHDPixel(pixelAddress);
				}

				this.bModified = true;
			}
		}

		#region HD layer
		private class HDOverflow
		{
			/// <summary>Width, in original pixels</summary>
			public int Width;
			/// <summary>Rows above the sprite, in original pixels</summary>
			public int Height;
			/// <summary>ARGB pixels, (Width * HDScale) x (Height * HDScale)</summary>
			public uint[] Pixels;

			public HDOverflow(int width, int height)
			{
				this.Width = width;
				this.Height = height;
				this.Pixels = new uint[width * HDScale * height * HDScale];
			}
		}

		/// <summary>
		/// Draws only the HD art of a sprite that reaches above it, as drawing the sprite at destPoint would
		/// </summary>
		public void DrawHDOverflow(GPoint destPoint, GBitmap srcBitmap)
		{
			if (srcBitmap._hdOverflow != null)
			{
				this.CopyOrDrawHDOverflow(destPoint, srcBitmap, srcBitmap.Rectangle, true, false);
			}
		}

		/// <summary>
		/// Art reaching above sprites travels with the pixels it belongs to: an opaque copy (loading a sheet, cutting a sprite,
		/// copying screens) carries it to the destination, a transparent copy (drawing a sprite) draws it above the sprite.
		/// </summary>
		private void CopyOrDrawHDOverflow(GPoint destPoint, GBitmap srcBitmap, GRectangle srcRect, bool transparent, bool grey)
		{
			if (!transparent && this._hdOverflow != null)
			{
				// what was in the overwritten area is gone
				GRectangle destRect = new GRectangle(destPoint, srcRect.Size);

				foreach ((int X, int Y) key in this._hdOverflow.Keys.Where(k => destRect.Contains(k.X, k.Y)).ToList())
				{
					this._hdOverflow.Remove(key);
				}
			}

			if (srcBitmap._hdOverflow == null)
				return;

			foreach (KeyValuePair<(int X, int Y), HDOverflow> item in srcBitmap._hdOverflow.ToList())
			{
				if (!srcRect.Contains(item.Key.X, item.Key.Y))
					continue;

				int x = destPoint.X + item.Key.X - srcRect.X;
				int y = destPoint.Y + item.Key.Y - srcRect.Y;

				if (!transparent)
				{
					this._hdOverflow ??= new Dictionary<(int X, int Y), HDOverflow>();
					this._hdOverflow[(x, y)] = item.Value;
					continue;
				}

				HDOverflow overflow = item.Value;
				int overflowWidth = overflow.Width * HDScale;

				for (int i = 0; i < overflow.Height; i++)
				{
					int destY = y - overflow.Height + i;

					for (int j = 0; j < overflow.Width; j++)
					{
						int destX = x + j;

						if (!this.oRectangle.Contains(destX, destY) || (this._hdOverflowClip is not null && !this._hdOverflowClip.Contains(destX, destY)))
							continue;

						int srcAddress = (i * HDScale * overflowWidth) + (j * HDScale);
						bool visible = false;

						for (int k = 0; k < HDScale && !visible; k++)
						{
							for (int l = 0; l < HDScale && !visible; l++)
							{
								visible = (overflow.Pixels[srcAddress + (k * overflowWidth) + l] >> 24) != 0;
							}
						}

						if (!visible)
							continue;

						this.EnsureHDLayer();
						this.MaterializeHDPixel(destX, destY);
						this.ResolveHDKeys(destX, destY);

						int hdAddress = this.GetHDAddress(destX, destY);
						uint indexColor = this.oPalette[this.aPixels[(destY * this.iWidth) + destX]].ToUInt32();

						for (int k = 0; k < HDScale; k++)
						{
							for (int l = 0; l < HDScale; l++)
							{
								uint color = overflow.Pixels[srcAddress + (k * overflowWidth) + l];

								if (IsKeyColor(color))
								{
									// above a sprite there is no original pixel of its own, a key color takes the color already there
									color = ShadeColor(indexColor, color);
								}

								if (grey)
								{
									color = GreyColor(color);
								}

								if ((color >> 24) != 0)
								{
									this._hdPixels![hdAddress + (k * this.iWidth * HDScale) + l] =
										BlendColor(color, this._hdPixels[hdAddress + (k * this.iWidth * HDScale) + l]);
								}
							}
						}
					}
				}

				this.bModified = true;
			}
		}

		/// <summary>
		/// Writes this bitmap, scaled by HDScale, to a 32 bit BGRA buffer.
		/// Pixels with HD content are taken from the HD layer, all other pixels are scaled with nearest-neighbour.
		/// </summary>
		/// <param name="destination">Address of the top left pixel</param>
		/// <param name="stride">Length of one destination row, in bytes</param>
		public void WriteScaledPixels(IntPtr destination, int stride)
		{
			int hdWidth = this.iWidth * HDScale;
			int[] rows = new int[hdWidth * HDScale];
			int pixelAddress = 0;
			byte[]? smooth = _smoothUpscale ? this.ScaleIndexes() : null;

			for (int i = 0; i < this.iHeight; i++)
			{
				for (int j = 0; j < this.iWidth; j++)
				{
					uint color = this.oPalette[this.aPixels[pixelAddress]].ToUInt32();
					bool isHD = this._hdMask != null && this._hdMask[pixelAddress];
					int hdAddress = this.GetHDAddress(j, i);

					for (int k = 0; k < HDScale; k++)
					{
						for (int l = 0; l < HDScale; l++)
						{
							uint pixel = (smooth != null && !isHD) ? this.oPalette[smooth[hdAddress + (k * hdWidth) + l]].ToUInt32() : color;

							if (isHD)
							{
								// keep the alpha of the palette color, so HD pixels are presented the same way as indexed ones
								pixel = (ResolveHDColor(this._hdPixels![hdAddress + (k * hdWidth) + l], color) & 0xffffff) | (color & 0xff000000);
							}

							rows[(k * hdWidth) + (j * HDScale) + l] = (int)pixel;
						}
					}

					pixelAddress++;
				}

				for (int k = 0; k < HDScale; k++)
				{
					Marshal.Copy(rows, k * hdWidth, destination, hdWidth);
					destination += stride;
				}
			}
		}

		/// <summary>
		/// The indexed pixels enlarged to HD size with Scale2x, applied twice at 4x
		/// </summary>
		private byte[] ScaleIndexes()
		{
			byte[] source = this.aPixels;
			int width = this.iWidth, height = this.iHeight;

			for (int scale = 1; scale < HDScale; scale *= 2)
			{
				byte[] target = new byte[width * 2 * height * 2];
				int targetWidth = width * 2;

				for (int y = 0; y < height; y++)
				{
					for (int x = 0; x < width; x++)
					{
						byte p = source[(y * width) + x];
						byte a = (y > 0) ? source[((y - 1) * width) + x] : p;
						byte b = (x < width - 1) ? source[(y * width) + x + 1] : p;
						byte c = (x > 0) ? source[(y * width) + x - 1] : p;
						byte d = (y < height - 1) ? source[((y + 1) * width) + x] : p;
						int t = (y * 2 * targetWidth) + (x * 2);

						target[t] = (c == a && c != d && a != b) ? a : p;
						target[t + 1] = (a == b && a != c && b != d) ? b : p;
						target[t + targetWidth] = (d == c && d != b && c != a) ? c : p;
						target[t + targetWidth + 1] = (b == d && b != a && d != c) ? d : p;
					}
				}

				source = target;
				width *= 2;
				height *= 2;
			}

			return source;
		}

		private void EnsureHDLayer()
		{
			if (this._hdMask == null)
			{
				this._hdPixels = new uint[this.iWidth * HDScale * this.iHeight * HDScale];
				this._hdMask = new bool[this.iWidth * this.iHeight];
			}
		}

		private void ClearHDPixel(int pixelAddress)
		{
			if (this._hdMask != null)
			{
				this._hdMask[pixelAddress] = false;
			}
		}

		private int GetHDAddress(int x, int y)
		{
			return (y * HDScale * this.iWidth * HDScale) + (x * HDScale);
		}

		/// <summary>
		/// Fills the HD block of an indexed pixel with its palette color, so HD content can be blended over it
		/// </summary>
		private void MaterializeHDPixel(int x, int y)
		{
			int pixelAddress = (y * this.iWidth) + x;

			if (!this._hdMask![pixelAddress])
			{
				uint color = this.oPalette[this.aPixels[pixelAddress]].ToUInt32() | 0xff000000;
				int hdAddress = this.GetHDAddress(x, y);

				for (int i = 0; i < HDScale; i++)
				{
					for (int j = 0; j < HDScale; j++)
					{
						this._hdPixels![hdAddress + j] = color;
					}

					hdAddress += this.iWidth * HDScale;
				}

				this._hdMask[pixelAddress] = true;
			}
		}

		private void CopyHDBlock(int x, int y, GBitmap srcBitmap, int srcX, int srcY)
		{
			int hdAddress = this.GetHDAddress(x, y);
			int srcHDAddress = srcBitmap.GetHDAddress(srcX, srcY);

			for (int i = 0; i < HDScale; i++)
			{
				for (int j = 0; j < HDScale; j++)
				{
					this._hdPixels![hdAddress + j] = srcBitmap._hdPixels![srcHDAddress + j];
				}

				hdAddress += this.iWidth * HDScale;
				srcHDAddress += srcBitmap.iWidth * HDScale;
			}

			this._hdMask![(y * this.iWidth) + x] = true;
		}

		private void BlendHDBlock(int x, int y, GBitmap srcBitmap, int srcX, int srcY, bool grey)
		{
			int hdAddress = this.GetHDAddress(x, y);
			int srcHDAddress = srcBitmap.GetHDAddress(srcX, srcY);
			uint indexColor = this.oPalette[this.aPixels[(y * this.iWidth) + x]].ToUInt32();
			uint srcIndexColor = srcBitmap.oPalette[srcBitmap.aPixels[(srcY * srcBitmap.iWidth) + srcX]].ToUInt32();

			for (int i = 0; i < HDScale; i++)
			{
				for (int j = 0; j < HDScale; j++)
				{
					uint color = srcBitmap._hdPixels![srcHDAddress + j];

					if (grey)
					{
						color = GreyColor(IsKeyColor(color) ? ShadeColor(srcIndexColor, color) : color);
					}

					if (IsKeyColor(color))
					{
						// key colors stay unresolved, they take the color of the indexed pixel when shown
						this._hdPixels![hdAddress + j] = color;
					}
					else if ((color >> 24) != 0)
					{
						this._hdPixels![hdAddress + j] = BlendColor(color, ResolveHDColor(this._hdPixels[hdAddress + j], indexColor));
					}
				}

				hdAddress += this.iWidth * HDScale;
				srcHDAddress += srcBitmap.iWidth * HDScale;
			}
		}

		/// <summary>
		/// Replaces key colors in the HD block of a pixel with the color they currently show
		/// </summary>
		private void ResolveHDKeys(int x, int y)
		{
			int hdAddress = this.GetHDAddress(x, y);
			uint indexColor = this.oPalette[this.aPixels[(y * this.iWidth) + x]].ToUInt32();

			for (int i = 0; i < HDScale; i++)
			{
				for (int j = 0; j < HDScale; j++)
				{
					if (IsKeyColor(this._hdPixels![hdAddress + j]))
					{
						this._hdPixels[hdAddress + j] = ShadeColor(indexColor, this._hdPixels[hdAddress + j]);
					}
				}

				hdAddress += this.iWidth * HDScale;
			}
		}

		private static uint GreyColor(uint color)
		{
			uint grey = ((((color >> 16) & 0xff) * 3) + (((color >> 8) & 0xff) * 6) + (color & 0xff)) / 10;

			return (color & 0xff000000) | (grey << 16) | (grey << 8) | grey;
		}

		/// <summary>
		/// HD key colors take the color of the indexed pixel under them, so palette color swaps (player colors) also apply to HD art.
		/// Primary key: magenta shades (red == blue, no green); secondary key: cyan shades (green == blue, no red).
		/// A channel value of 128 means the exact palette color, lower is darker, higher is lighter.
		/// </summary>
		private static bool IsKeyColor(uint color)
		{
			uint red = (color >> 16) & 0xff;
			uint green = (color >> 8) & 0xff;
			uint blue = color & 0xff;

			return (color >> 24) == 0xff && ((green == 0 && red == blue && red != 0) || (red == 0 && green == blue && green != 0));
		}

		private static bool IsSecondaryKeyColor(uint color)
		{
			return IsKeyColor(color) && ((color >> 16) & 0xff) == 0;
		}

		private static uint ShadeColor(uint baseColor, uint keyColor)
		{
			uint shade = Math.Max((keyColor >> 16) & 0xff, keyColor & 0xff);
			uint red = Math.Min(0xffu, (((baseColor >> 16) & 0xff) * shade) / 0x80);
			uint green = Math.Min(0xffu, (((baseColor >> 8) & 0xff) * shade) / 0x80);
			uint blue = Math.Min(0xffu, ((baseColor & 0xff) * shade) / 0x80);

			return 0xff000000 | (red << 16) | (green << 8) | blue;
		}

		/// <summary>
		/// Color shown for an HD pixel over the color of its indexed pixel
		/// </summary>
		private static uint ResolveHDColor(uint color, uint indexColor)
		{
			if ((color & 0xff000000) == MaterialAlpha)
				return MaterialColor(color, indexColor);

			return IsKeyColor(color) ? ShadeColor(indexColor, color) : BlendColor(color, indexColor);
		}

		private bool HDBlockIsVisible(int x, int y)
		{
			int hdAddress = this.GetHDAddress(x, y);

			for (int i = 0; i < HDScale; i++)
			{
				for (int j = 0; j < HDScale; j++)
				{
					if ((this._hdPixels![hdAddress + j] & 0xff000000) != 0)
					{
						return true;
					}
				}

				hdAddress += this.iWidth * HDScale;
			}

			return false;
		}

		/// <summary>
		/// Blends ARGB color over an opaque ARGB background color
		/// </summary>
		private static uint BlendColor(uint color, uint background)
		{
			uint alpha = color >> 24;

			if (alpha == 0xff)
				return color;

			if (alpha == 0)
				return background | 0xff000000;

			uint inverseAlpha = 0xff - alpha;
			uint red = ((((color >> 16) & 0xff) * alpha) + (((background >> 16) & 0xff) * inverseAlpha)) / 0xff;
			uint green = ((((color >> 8) & 0xff) * alpha) + (((background >> 8) & 0xff) * inverseAlpha)) / 0xff;
			uint blue = (((color & 0xff) * alpha) + ((background & 0xff) * inverseAlpha)) / 0xff;

			return 0xff000000 | (red << 16) | (green << 8) | blue;
		}

		/// <summary>
		/// Looks for HD versions of a PIC file in the 'hd' folder next to it and attaches them to this bitmap:
		/// <list type="bullet">
		/// <item>hd/NAME.png, the whole image at HDScale times the size,</item>
		/// <item>hd/NAME/X_Y.png, one region with its top left corner at X, Y in the original image, applied over the whole image.
		/// The region also replaces the original pixels under it, so they match the HD art.
		/// The name may end with _kP-S, the color indexes that primary and secondary key colors stand for (default 10 and 2).</item>
		/// </list>
		/// </summary>
		private void LoadHDLayer(string picPath)
		{
			HDImage? hdImage;

			lock (_hdCacheLock)
			{
				if (!_hdCache.TryGetValue(picPath, out hdImage))
				{
					hdImage = ReadHDImage(picPath, this);
					_hdCache.Add(picPath, hdImage);
				}
			}

			if (hdImage != null)
			{
				this.EnsureHDLayer();
				Array.Copy(hdImage.Pixels, this._hdPixels!, hdImage.Pixels.Length);
				Array.Copy(hdImage.Mask, this._hdMask!, hdImage.Mask.Length);

				for (int i = 0; i < hdImage.Indexes.Length; i++)
				{
					if (hdImage.Indexes[i] >= 0)
					{
						this.aPixels[i] = (byte)hdImage.Indexes[i];
					}
				}

				if (hdImage.Overflow.Count > 0)
				{
					this._hdOverflow = new Dictionary<(int X, int Y), HDOverflow>(hdImage.Overflow);
					_hasHDOverflow = true;
				}
			}
		}

		private class HDImage
		{
			public uint[] Pixels;
			public bool[] Mask;
			public short[] Indexes;
			public Dictionary<(int X, int Y), HDOverflow> Overflow = new Dictionary<(int X, int Y), HDOverflow>();

			public HDImage(int width, int height)
			{
				this.Pixels = new uint[width * HDScale * height * HDScale];
				this.Mask = new bool[width * height];
				this.Indexes = new short[width * height];
				Array.Fill(this.Indexes, (short)-1);
			}
		}

		// X_Y.png, with optional _tN (N original rows of art above the region) and _kP-S (key color indexes)
		private static readonly System.Text.RegularExpressions.Regex _hdRegionName =
			new System.Text.RegularExpressions.Regex(@"^(\d+)_(\d+)(?:_t(\d+))?(?:_k(\d+)-(\d+))?\.png$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

		private static HDImage? ReadHDImage(string picPath, GBitmap bitmap)
		{
			string? directory = Path.GetDirectoryName(picPath);
			string name = Path.GetFileNameWithoutExtension(picPath);

			if (directory == null || !Directory.Exists(Path.Combine(directory, "hd")))
				return null;

			string hdDirectory = Path.Combine(directory, "hd");
			string[] names = new string[] { name, name.ToUpperInvariant(), name.ToLowerInvariant() };
			HDImage hdImage = new HDImage(bitmap.iWidth, bitmap.iHeight);
			bool found = false;
			int hdWidth = bitmap.iWidth * HDScale;

			// the whole image
			foreach (string candidate in names.SelectMany(n => new string[] { $"{n}.png", $"{n}.PNG" }).Distinct())
			{
				string hdPath = Path.Combine(hdDirectory, candidate);

				if (File.Exists(hdPath))
				{
					using SKBitmap? image = SKBitmap.Decode(hdPath);

					if (image == null || image.Width != hdWidth || image.Height != bitmap.iHeight * HDScale)
					{
						System.Diagnostics.Debug.WriteLine($"HD image '{hdPath}' is ignored, it has to be a {hdWidth}x{bitmap.iHeight * HDScale} PNG image");
					}
					else
					{
						for (int i = 0; i < image.Height; i++)
						{
							for (int j = 0; j < image.Width; j++)
							{
								hdImage.Pixels[(i * hdWidth) + j] = (uint)image.GetPixel(j, i);
							}
						}

						Array.Fill(hdImage.Mask, true);
						found = true;
					}

					break;
				}
			}

			// regions
			string? regionDirectory = names.Select(n => Path.Combine(hdDirectory, n)).FirstOrDefault(Directory.Exists);

			if (regionDirectory != null)
			{
				foreach (string regionPath in Directory.GetFiles(regionDirectory).OrderBy(p => p, StringComparer.Ordinal))
				{
					System.Text.RegularExpressions.Match match = _hdRegionName.Match(Path.GetFileName(regionPath));

					if (!match.Success)
						continue;

					int left = int.Parse(match.Groups[1].Value);
					int top = int.Parse(match.Groups[2].Value);
					int overflowRows = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
					int primaryIndex = match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : 10;
					int secondaryIndex = match.Groups[5].Success ? int.Parse(match.Groups[5].Value) : 2;

					using SKBitmap? image = SKBitmap.Decode(regionPath);
					int regionWidth = (image?.Width ?? 0) / HDScale;
					int regionHeight = (image?.Height ?? 0) / HDScale - overflowRows;

					if (image == null || (image.Width % HDScale) != 0 || (image.Height % HDScale) != 0 || regionHeight <= 0 ||
						left + regionWidth > bitmap.iWidth || top + regionHeight > bitmap.iHeight)
					{
						System.Diagnostics.Debug.WriteLine($"HD image '{regionPath}' is ignored, its size has to be a multiple of {HDScale} and it has to fit the original image");
						continue;
					}

					// the top rows of a tall image are drawn above the sprite
					if (overflowRows > 0)
					{
						HDOverflow overflow = new HDOverflow(regionWidth, overflowRows);

						for (int i = 0; i < overflowRows * HDScale; i++)
						{
							for (int j = 0; j < regionWidth * HDScale; j++)
							{
								overflow.Pixels[(i * regionWidth * HDScale) + j] = (uint)image.GetPixel(j, i);
							}
						}

						hdImage.Overflow[(left, top)] = overflow;
					}

					for (int i = 0; i < regionHeight; i++)
					{
						for (int j = 0; j < regionWidth; j++)
						{
							int x = left + j;
							int y = top + i;
							int hdAddress = (y * HDScale * hdWidth) + (x * HDScale);
							uint visibleColor = 0;
							int visibleCount = 0;
							int primaryCount = 0;
							int secondaryCount = 0;

							for (int k = 0; k < HDScale; k++)
							{
								for (int l = 0; l < HDScale; l++)
								{
									uint color = (uint)image.GetPixel((j * HDScale) + l, ((i + overflowRows) * HDScale) + k);

									hdImage.Pixels[hdAddress + (k * hdWidth) + l] = color;

									if (IsSecondaryKeyColor(color))
									{
										secondaryCount++;
									}
									else if (IsKeyColor(color))
									{
										primaryCount++;
									}
									else if ((color >> 24) >= 0x80)
									{
										visibleColor = color;
										visibleCount++;
									}
								}
							}

							// the original pixel under the region follows the majority of its HD pixels:
							// a key color index, the closest palette color, or transparent
							int index = 0;

							if (primaryCount + secondaryCount > 0 && primaryCount + secondaryCount >= visibleCount)
							{
								index = (secondaryCount > primaryCount) ? secondaryIndex : primaryIndex;
							}
							else if (visibleCount > 0)
							{
								index = bitmap.GetClosestPaletteIndex(visibleColor);
							}

							hdImage.Indexes[(y * bitmap.iWidth) + x] = (short)index;
							hdImage.Mask[(y * bitmap.iWidth) + x] = true;
						}
					}

					found = true;
				}
			}

			return found ? hdImage : null;
		}

		private int GetClosestPaletteIndex(uint color)
		{
			int closestIndex = 1;
			int closestDistance = int.MaxValue;

			// index 0 is transparent in sprites, so it is not used for visible pixels
			for (int i = 1; i < 256; i++)
			{
				uint paletteColor = this.oPalette[(byte)i].ToUInt32();
				int red = (int)((color >> 16) & 0xff) - (int)((paletteColor >> 16) & 0xff);
				int green = (int)((color >> 8) & 0xff) - (int)((paletteColor >> 8) & 0xff);
				int blue = (int)(color & 0xff) - (int)(paletteColor & 0xff);
				int distance = (red * red) + (green * green) + (blue * blue);

				if (distance < closestDistance)
				{
					closestDistance = distance;
					closestIndex = i;
				}
			}

			return closestIndex;
		}

		/// <summary>
		/// Writes this bitmap as a nearest-neighbour upscaled PNG, color index 0 is written as transparent
		/// </summary>
		private void SaveHDTemplate(string path)
		{
			int width = this.iWidth * HDScale;
			int height = this.iHeight * HDScale;

			using SKBitmap image = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);

			for (int i = 0; i < height; i++)
			{
				for (int j = 0; j < width; j++)
				{
					byte colorIndex = this.aPixels[((i / HDScale) * this.iWidth) + (j / HDScale)];
					uint color = (colorIndex == 0) ? 0 : (this.oPalette[colorIndex].ToUInt32() | 0xff000000);

					image.SetPixel(j, i, new SKColor(color));
				}
			}

			Directory.CreateDirectory(Path.GetDirectoryName(path)!);

			using FileStream stream = new FileStream(path, FileMode.Create);
			image.Encode(stream, SKEncodedImageFormat.Png, 100);
		}
		#endregion

		public void DrawString(string text, GFont font, GRectangle rect, byte frontColor, byte backColor, PixelWriteModeEnum writeMode)
		{
			GRectangle rect1 = new GRectangle(rect.Location, rect.Size);
			rect1.Intersect(this.oRectangle);

			if (rect1.Width > 0 && rect1.Height > 0)
			{
				int iPixelAddress0 = (rect1.Y * this.iWidth) + rect1.X;

				for (int i = 0; i < text.Length; i++)
				{
					char ch = text[i];
					frontColor = (byte)((ch > 0x7f) ? (ch - 0x80) : frontColor);
					GFontChar fontCh;

					if (ch > 0x7f)
						continue;

					if (i > 0)
						iPixelAddress0 += font.CharacterWidthSpacing;

					if (font.Characters.ContainsKey(ch))
					{
						fontCh = font.Characters.GetValueByKey(ch);
					}
					else
					{
						// unknown char, use '?'
						fontCh = font.Characters.GetValueByKey('?');
					}

					int iPixelAddress1 = iPixelAddress0;

					for (int j = 0; j < fontCh.Height; j++)
					{
						for (int k = 0; k < fontCh.Width; k++)
						{
							byte color = (byte)((fontCh.Bitmap[j][k] != 0) ? frontColor : backColor);

							if (fontCh.Bitmap[j][k] != 0 && iPixelAddress1 + k < this.aPixels.Length) //|| backColor != 0)
							{
								switch (writeMode)
								{
									case PixelWriteModeEnum.Normal:
										this.aPixels[iPixelAddress1 + k] = color;
										break;

									case PixelWriteModeEnum.And:
										this.aPixels[iPixelAddress1 + k] &= color;
										break;

									case PixelWriteModeEnum.Or:
										this.aPixels[iPixelAddress1 + k] |= color;
										break;

									case PixelWriteModeEnum.Xor:
										this.aPixels[iPixelAddress1 + k] ^= color;
										break;
								}

								this.ClearHDPixel(iPixelAddress1 + k);
							}
						}

						iPixelAddress1 += this.iWidth;
					}

					iPixelAddress0 += fontCh.Width;
				}

				this.bModified = true;
			}
		}

		public void SaveToPIC(string path)
		{
			SaveToPIC(path, true);
		}

		public void SaveToPIC(string path, bool savePalette)
		{
			FileStream writer = new FileStream(path, FileMode.Create);
			int iLength;

			if (savePalette)
			{
				iLength = 2 + 255 * 3; // we can't write full 256 colors!

				// write signature
				writer.WriteByte(0x4d);
				writer.WriteByte(0x30);

				// write block length
				writer.WriteByte((byte)(iLength & 0xff));
				writer.WriteByte((byte)((iLength & 0xff00) >> 8));

				// write block contents
				writer.WriteByte(0); // from colorIndex
				writer.WriteByte(255); // to colorIndex

				for (int i = 0; i < 256; i++)
				{
					Color color = ColorToColor18(this.oPalette[(byte)i]);
					writer.WriteByte(color.R);
					writer.WriteByte(color.G);
					writer.WriteByte(color.B);
				}
			}

			MemoryStream rleInput = new MemoryStream();
			int iBitmapAddress = 0;

			for (int i = 0; i < this.iHeight; i++)
			{
				for (int j = 0; j < this.iWidth; j++)
				{
					rleInput.WriteByte(this.aPixels[iBitmapAddress + j]);
				}
				iBitmapAddress += this.iWidth;
			}

			// compress the bitmap
			MemoryStream lzwInput = new MemoryStream();
			rleInput.Position = 0;
			RLE.Compress(lzwInput, rleInput, 4);

			MemoryStream lzwOutput = new MemoryStream();
			lzwInput.Position = 0;
			LZW.Compress(lzwOutput, lzwInput, 9, 11);

			// write block signature
			writer.WriteByte(0x58);
			writer.WriteByte(0x30);

			// write block contents
			byte[] buffer = lzwOutput.ToArray();
			iLength = buffer.Length + 4;

			// write block length
			writer.WriteByte((byte)(iLength & 0xff));
			writer.WriteByte((byte)((iLength & 0xff00) >> 8));

			// write bitmap width and height
			writer.WriteByte((byte)(this.iWidth & 0xff));
			writer.WriteByte((byte)((this.iWidth & 0xff00) >> 8));
			writer.WriteByte((byte)(this.iHeight & 0xff));
			writer.WriteByte((byte)((this.iHeight & 0xff00) >> 8));

			// write compressed content
			writer.Write(buffer, 0, buffer.Length);

			writer.Close();
		}

		public bool LoadPIC(string filename, int xPos, int yPos, out byte[] palette)
		{
			// function body
			GBitmap? bitmap = GBitmap.FromPICFile(filename, out palette, true);

			if (bitmap != null)
			{
				this.DrawBitmap(xPos, yPos, bitmap, false);

				/*for (int i = 0; i < bitmap.Height; i++)
				{
					for (int j = 0; j < bitmap.Width; j++)
					{
						this.SetPixel(xPos + j, yPos + i, bitmap.GetPixel(j, i));
					}
				}*/
				return true;
			}

			return false;
		}

		public static GBitmap? FromPICFile(string path, bool preferHiColor)
		{
			byte[] palette;

			return FromPICFile(path, out palette, preferHiColor);
		}

		public static GBitmap? FromPICFile(string path, out byte[] palette, bool preferHiColor)
		{
			GBitmap? bitmap = null;
			FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read);
			List<BKeyValuePair<int, Color>> aPalette = new List<BKeyValuePair<int, Color>>();
			palette = new byte[0];
			
			// PIC file is written in blocks
			while (true)
			{
				int iSignature = ReadUInt16(stream);
				int iBlockLength = ReadUInt16(stream);

				if (iSignature < 0 || iBlockLength < 0)
					break;

				byte[] aBlock = new byte[iBlockLength];
				int iBlockSize = stream.Read(aBlock, 0, iBlockLength);

				if (iBlockLength != iBlockSize)
				{
					throw new Exception(
						$"Block type 0x{iSignature:x4} data missing, expected {iBlockLength} bytes, " +
						$"but read only {iBlockSize} bytes");
				}

				MemoryStream blockReader;
				int iWidth;
				int iHeight;
				int iMaxBits;

				switch (iSignature)
				{
					case 0x3045:
						// 8bit colorIndex palette
						if (aPalette.Count == 0 || !preferHiColor)
						{
							aPalette.Clear();
						}
						break;

					case 0x304d:
						if (aPalette.Count == 0 || preferHiColor)
						{
							// 18bit colorIndex palette
							aPalette.Clear();
							List<byte> aTemp = new List<byte>();
							aTemp.Add(0x4d);
							aTemp.Add(0x30);
							aTemp.Add((byte)(iBlockLength & 0xff));
							aTemp.Add((byte)((iBlockLength & 0xff00) >> 8));
							aTemp.AddRange(aBlock);
							palette = aTemp.ToArray();

							blockReader = new MemoryStream(aBlock);
							int iIndex = blockReader.ReadByte();
							int iColorCount = blockReader.ReadByte();

							if (iIndex >= 0 && iColorCount >= 0)
							{
								iColorCount -= iIndex;
								iColorCount++;

								for (int i = 0; i < iColorCount; i++)
								{
									int iRed = blockReader.ReadByte();
									int iGreen = blockReader.ReadByte();
									int iBlue = blockReader.ReadByte();

									if (iRed < 0 || iGreen < 0 || iBlue < 0)
									{
										throw new Exception($"Palette block type 0x{iSignature:x4} malformed");
									}
									aPalette.Add(new BKeyValuePair<int, Color>(iIndex + i,
										GBitmap.Color18ToColor(iRed, iGreen, iBlue)));
								}
							}
							else
							{
								throw new Exception($"Palette block type 0x{iSignature:x4} malformed");
							}
						}
						break;

					case 0x3058:
						// Image data encoded by RLE and LZW
						if (bitmap == null || preferHiColor)
						{
							blockReader = new MemoryStream(aBlock);
							iWidth = ReadUInt16(blockReader);
							iHeight = ReadUInt16(blockReader);
							iMaxBits = blockReader.ReadByte();

							if (iWidth >= 0 && iHeight >= 0 && iMaxBits > 7)
							{
								// decode LZW
								MemoryStream lzwOutput = new MemoryStream();
								LZW.Decompress(lzwOutput, blockReader, 9, iMaxBits);
								lzwOutput.Position = 0;

								// decode RLE
								MemoryStream rleOutput = new MemoryStream();
								RLE.Decompress(rleOutput, lzwOutput);
								rleOutput.Position = 0;

								// construct bitmap with our raw data
								int iPixelAddress = 0;
								bitmap = new GBitmap(iWidth, iHeight);

								// set bitmap palette
								for (int i = 0; i < aPalette.Count; i++)
								{
									bitmap.oPalette[(byte)aPalette[i].Key] = aPalette[i].Value;
								}

								// set bitmap pixels
								for (int i = 0; i < iHeight; i++)
								{
									for (int j = 0; j < iWidth; j++)
									{
										int c = rleOutput.ReadByte();
										if (c < 0)
											break;

										bitmap.Pixels[iPixelAddress] = (byte)c;
										iPixelAddress++;
									}
								}
							}
							else
							{
								throw new Exception($"Image block type 0x{iSignature:x4} malformed");
							}
						}
						break;

					case 0x3158:
						if (bitmap == null || !preferHiColor)
						{
							// Image data encoded by two pixels packed into one, RLE and LZW
							blockReader = new MemoryStream(aBlock);
							iWidth = ReadUInt16(blockReader);
							iHeight = ReadUInt16(blockReader);
							iMaxBits = blockReader.ReadByte();

							if (iWidth >= 0 && iHeight >= 0 && iMaxBits > 7)
							{
								// decode LZW
								MemoryStream lzwOutput = new MemoryStream();
								LZW.Decompress(lzwOutput, blockReader, 9, iMaxBits);
								lzwOutput.Position = 0;

								// decode RLE
								MemoryStream rleOutput = new MemoryStream();
								RLE.Decompress(rleOutput, lzwOutput);
								rleOutput.Position = 0;

								// construct bitmap with our raw data
								int iPixelAddress = 0;

								bitmap = new GBitmap(iWidth, iHeight);

								// set bitmap palette
								/*for (int i = 0; i < aPalette.Count; i++)
								{
									bitmap.oPalette[(byte)aPalette[i].Key] = aPalette[i].Value;
								}*/

								// set bitmap pixels
								for (int i = 0; i < iHeight; i++)
								{
									for (int j = 0; j < iWidth; j++)
									{
										int c = rleOutput.ReadByte();
										if (c < 0)
											break;

										// for some reason the first pixel is discarded
										//if (j == 0)
										//{
										bitmap.Pixels[iPixelAddress] = (byte)((c & 0xf));
										iPixelAddress++;
										//}
										if (j + 1 < iWidth)
										{
											bitmap.Pixels[iPixelAddress] = (byte)((c & 0xf0) >> 4);
											iPixelAddress++;
											j++;
										}
									}
								}
							}
							else
							{
								throw new Exception($"Image block type 0x{iSignature:x4} malformed");
							}
						}
						break;

					default:
						throw new Exception($"Undefined block type 0x{iSignature:x4}");
				}
			}

			stream.Close();

			if (bitmap != null)
			{
				if (_hdTemplatePath != null)
				{
					bitmap.SaveHDTemplate(Path.Combine(_hdTemplatePath, $"{Path.GetFileNameWithoutExtension(path).ToUpperInvariant()}.png"));
				}

				bitmap.LoadHDLayer(path);
			}

			return bitmap;
		}

		public static List<BKeyValuePair<int, Color>> ReadPaletteFromPICFile(string path, out byte[] palette)
		{
			FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read);
			List<BKeyValuePair<int, Color>> aPalette = new List<BKeyValuePair<int, Color>>();
			palette = new byte[0];

			// PIC file is written in blocks
			while (true)
			{
				int iSignature = ReadUInt16(stream);
				int iBlockLength = ReadUInt16(stream);

				if (iSignature < 0 || iBlockLength < 0)
					break;

				byte[] aBlock = new byte[iBlockLength];
				int iBlockSize = stream.Read(aBlock, 0, iBlockLength);

				if (iBlockLength != iBlockSize)
				{
					throw new Exception(
						$"Block type 0x{iSignature:x4} data missing, expected {iBlockLength} bytes, " +
						$"but read only {iBlockSize} bytes");
				}

				MemoryStream blockReader;

				switch (iSignature)
				{
					case 0x3045:
						// 8bit colorIndex palette
						break;

					case 0x304d:
						// 18bit colorIndex palette
						List<byte> aTemp = new List<byte>();
						aTemp.Add(0x4d);
						aTemp.Add(0x30);
						aTemp.Add((byte)(iBlockLength & 0xff));
						aTemp.Add((byte)((iBlockLength & 0xff00) >> 8));
						aTemp.AddRange(aBlock);
						palette = aTemp.ToArray();

						blockReader = new MemoryStream(aBlock);
						int iIndex = blockReader.ReadByte();
						int iColorCount = blockReader.ReadByte();

						if (iIndex >= 0 && iColorCount >= 0)
						{
							iColorCount -= iIndex;
							iColorCount++;

							for (int i = 0; i < iColorCount; i++)
							{
								int iRed = blockReader.ReadByte();
								int iGreen = blockReader.ReadByte();
								int iBlue = blockReader.ReadByte();

								if (iRed < 0 || iGreen < 0 || iBlue < 0)
								{
									throw new Exception($"Palette block type 0x{iSignature:x4} malformed");
								}
								aPalette.Add(new BKeyValuePair<int, Color>(iIndex + i,
									GBitmap.Color18ToColor(iRed, iGreen, iBlue)));
							}
						}
						else
						{
							throw new Exception($"Palette block type 0x{iSignature:x4} malformed");
						}
						break;

					case 0x3058:
						// Image data encoded by RLE and LZW
						break;

					case 0x3158:
						// Image data encoded by two pixels packed into one, RLE and LZW
						break;

					default:
						throw new Exception($"Undefined block type 0x{iSignature:x4}");
				}
			}

			stream.Close();

			return aPalette;
		}

		private static int ReadUInt16(Stream stream)
		{
			int iByte0 = stream.ReadByte();
			int iByte1 = stream.ReadByte();

			if (iByte0 < 0 || iByte1 < 0)
			{
				// end of stream, return -1
				return -1;
			}

			return iByte0 | (iByte1 << 8);
		}
	}
}
