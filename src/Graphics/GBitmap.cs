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
		/// Scale of the optional HD layer, relative to the indexed pixels
		/// </summary>
		public const int HDScale = 2;

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

		private static readonly object _hdCacheLock = new object();
		private static readonly Dictionary<string, uint[]?> _hdCache = new Dictionary<string, uint[]?>();
		private static string? _hdTemplatePath = null;

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
		/// When set, every loaded PIC file is also written to this folder as a 2x nearest-neighbour PNG,
		/// which can be used as a starting point for HD art
		/// </summary>
		public static string? HDTemplatePath
		{
			get => _hdTemplatePath;
			set => _hdTemplatePath = value;
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
				this.SetPixel(x1, y1, colorIndex, mode);
			}
			else if (iWidth > 1 && iHeight == 1)
			{
				// a horizontal line
				for (int i = 0; i < iWidth; i++)
				{
					this.SetPixel(x1 + i * iWidthDir, y1, colorIndex, mode);
				}
			}
			else if (iWidth == 1 && iHeight > 1)
			{
				// a vertical line
				for (int i = 0; i < iHeight; i++)
				{
					this.SetPixel(x1, y1 + i * iHeightDir, colorIndex, mode);
				}
			}
			else if (iWidth > iHeight)
			{
				double dYStep = (double)iHeight / (double)iWidth;

				for (int i = 0; i < iWidth; i++)
				{
					this.SetPixel(x1 + i * iWidthDir, y1 + (int)(dYStep * i) * iHeightDir, colorIndex, mode);
				}
			}
			else
			{
				double dXStep = (double)iWidth / (double)iHeight;

				for (int i = 0; i < iHeight; i++)
				{
					this.SetPixel(x1 + (int)(dXStep * i) * iWidthDir, y1 + i * iHeightDir, colorIndex, mode);
				}
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
							this.aPixels[iPixelAddress + j] = newColorIndex;
							this.ClearHDPixel(iPixelAddress + j);

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

		public void DrawBitmap(GPoint destPoint, GBitmap srcBitmap, GRectangle srcRect, bool transparent)
		{
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
								this.BlendHDBlock(destX, destY, srcBitmap, srcX, srcY);

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
							uint pixel = color;

							if (isHD)
							{
								// keep the alpha of the palette color, so HD pixels are presented the same way as indexed ones
								pixel = (BlendColor(this._hdPixels![hdAddress + (k * hdWidth) + l], color) & 0xffffff) | (color & 0xff000000);
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

		private void BlendHDBlock(int x, int y, GBitmap srcBitmap, int srcX, int srcY)
		{
			int hdAddress = this.GetHDAddress(x, y);
			int srcHDAddress = srcBitmap.GetHDAddress(srcX, srcY);

			for (int i = 0; i < HDScale; i++)
			{
				for (int j = 0; j < HDScale; j++)
				{
					this._hdPixels![hdAddress + j] = BlendColor(srcBitmap._hdPixels![srcHDAddress + j], this._hdPixels[hdAddress + j]);
				}

				hdAddress += this.iWidth * HDScale;
				srcHDAddress += srcBitmap.iWidth * HDScale;
			}
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
		/// Looks for an HD version of a PIC file in the 'hd' folder next to it, a PNG image with the same name
		/// and HDScale times the size, and attaches it to this bitmap
		/// </summary>
		private void LoadHDLayer(string picPath)
		{
			uint[]? hdPixels;

			lock (_hdCacheLock)
			{
				if (!_hdCache.TryGetValue(picPath, out hdPixels))
				{
					hdPixels = ReadHDImage(picPath, this.iWidth * HDScale, this.iHeight * HDScale);
					_hdCache.Add(picPath, hdPixels);
				}
			}

			if (hdPixels != null)
			{
				this.EnsureHDLayer();
				Array.Copy(hdPixels, this._hdPixels!, hdPixels.Length);
				Array.Fill(this._hdMask!, true);
			}
		}

		private static uint[]? ReadHDImage(string picPath, int width, int height)
		{
			string? directory = Path.GetDirectoryName(picPath);
			string name = Path.GetFileNameWithoutExtension(picPath);

			if (directory == null)
				return null;

			string hdDirectory = Path.Combine(directory, "hd");
			string[] candidates = new string[] { $"{name}.png", $"{name.ToUpperInvariant()}.PNG", $"{name.ToUpperInvariant()}.png", $"{name.ToLowerInvariant()}.png" };

			foreach (string candidate in candidates)
			{
				string hdPath = Path.Combine(hdDirectory, candidate);

				if (File.Exists(hdPath))
				{
					using SKBitmap? image = SKBitmap.Decode(hdPath);

					if (image == null || image.Width != width || image.Height != height)
					{
						System.Diagnostics.Debug.WriteLine($"HD image '{hdPath}' is ignored, it has to be a {width}x{height} PNG image");
						return null;
					}

					uint[] pixels = new uint[width * height];

					for (int i = 0; i < height; i++)
					{
						for (int j = 0; j < width; j++)
						{
							pixels[(i * width) + j] = (uint)image.GetPixel(j, i);
						}
					}

					return pixels;
				}
			}

			return null;
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
