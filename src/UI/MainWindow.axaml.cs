using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using IRB.Collections.Generic;
using IRB.VirtualCPU;
using OpenCivOne.Graphics;
using System.Runtime.InteropServices;
using ThreadState = System.Threading.ThreadState;

namespace OpenCivOne.UI
{
	public partial class MainWindow : Window
	{
		private bool closing = false;

		private OpenCivOneGame game;
		private DispatcherTimer timer;
		private Thread gameThread;
		private Exception? gameException = null;

		// track how many screens, columns and rows we have to show
		private int screenCount = 0;
		private int screenColumns = 1;
		private int screenRows = 1;

		// Main screen dimensions
		private GSize screenSize = new GSize(320, 200);
		private GSize imageSize = new GSize(320, 200);

		// Mouse region
		private GRectangle mouseRect = new GRectangle();
		private MouseEvent lastMouseEvent = new MouseEvent(new GPoint(160, 100), MouseButtonsEnum.None);

		// We have two interchangeable buffer bitmaps
		private int bitmapIndex = 0;
		private WriteableBitmap[] bitmaps = new WriteableBitmap[2];

		// painted water moves at about ten frames a second, so screens that show it are drawn again that often
		private readonly System.Diagnostics.Stopwatch waterClock = System.Diagnostics.Stopwatch.StartNew();
		private const int WaterFrameMilliseconds = 115;

		// optional LIVE_SCRIPT automation for visual aesthetic-checks in the real running game (inactive unless set)
		private readonly Queue<string> liveScript = new Queue<string>();
		private readonly System.Diagnostics.Stopwatch liveClock = System.Diagnostics.Stopwatch.StartNew();
		private long liveWaitUntil = 0;

		public MainWindow()
		{
			InitializeComponent();

#if !DEBUG
			MessageBox.Show("This Alpha Release of OpenCivOne project " +
				"most certainly has bugs, but most functions should work normally, and has no sound at this point. " +
				"It is compatible with old civ.exe and can save/load original game files.\n\n\n" +
				"Technicalities:\n\nTHE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR " +
				"IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, " +
				"FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE " +
				"AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER " +
				"LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, " +
				"OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.",
				"Warning", MessageBoxIcon.Warning, MessageBoxButtons.OK);
#endif

			Thread.CurrentThread.Name = "OpenCivOne main thread";

			// Initialize game state
			this.game = new OpenCivOneGame(this);

			// the frame around the game is magenta (palette index 5), unless the theme recolors it
			if (GBitmap.UIColor(5) is uint frame)
				this.mainImageBorder.BorderBrush = new SolidColorBrush(frame);

			// a theme can give the mouse pointer its own picture (ui/cursor.png, the point at its top left corner)
			if (GBitmap.HDDirectory is string themePath && File.Exists(Path.Combine(themePath, "ui", "cursor.png")))
			{
				try
				{
					this.Cursor = new Cursor(new Bitmap(Path.Combine(themePath, "ui", "cursor.png")), new PixelPoint(2, 2));
				}
				catch (Exception)
				{
				}
			}

			// Open large enough to show 4x HD art at its own size, if the screen has the room
			if (GBitmap.HDScale > 2)
			{
				double width = 320 * GBitmap.HDScale + 4;
				double height = 200 * GBitmap.HDScale + 4;
				PixelRect? area = this.Screens.Primary?.WorkingArea;

				if (area == null || (area.Value.Width >= width && area.Value.Height >= height))
				{
					this.Width = width;
					this.Height = height;
				}
			}

			// Main Windows events
			this.Closing += this.MainWindow_Closing;
			this.KeyDown += this.MainWindow_KeyDown;

			this.PointerMoved += this.MainWindow_PointerMoved;
			this.PointerPressed += this.MainWindow_PointerPressed;
			this.PointerReleased += this.MainWindow_PointerReleased;

			//this.mainImageBorder.PointerMoved += this.MainWindow_PointerMoved;
			//this.mainImageBorder.PointerPressed += this.MainWindow_PointerPressed;
			//this.mainImageBorder.PointerReleased += this.MainWindow_PointerReleased;

			// Initialize Window refresh timer
			this.timer = new DispatcherTimer(DispatcherPriority.Normal);
			this.timer.Interval = TimeSpan.FromMilliseconds(20);
			this.timer.Tick += this.Timer_Tick;
			this.timer.Start();

			// Initialize game thread where we have all the fun ;)
			this.gameThread = new Thread(new ThreadStart(GameThread));
			this.gameThread.Name = "OpenCivOne game thread";
			this.gameThread.Start();

			if (Environment.GetEnvironmentVariable("LIVE_SCRIPT") is string script && script.Length > 0)
			{
				foreach (string step in script.Split(',', StringSplitOptions.RemoveEmptyEntries))
					this.liveScript.Enqueue(step.Trim());
			}
		}

		private void Timer_Tick(object? sender, EventArgs e)
		{
			if (!this.closing && this.gameThread.ThreadState == ThreadState.Stopped)
			{
				if (this.gameException != null)
				{
					Exception ex = this.gameException;
					if (ex is ResourceMissingException)
					{
						MessageBox.Show(this, ex.Message, "Game resource error", MessageBoxIcon.Error, MessageBoxButtons.OK);
					}
					else
					{
						MessageBox.Show(this, "There was an error in the OpenCivOne game engine, " +
							"the details about the error will be in the Exception.log file.", "Game engine error", MessageBoxIcon.Error, MessageBoxButtons.OK);

						try
						{
							StreamWriter writer = new StreamWriter($"{VCPU.AssemblyPath}Exception.log", true);

							writer.WriteLine("---------------------------");
							writer.WriteLine($"Message: {ex.Message}");
							writer.WriteLine($"Source: {ex.Source}");
							writer.WriteLine($"Stack trace: {ex.StackTrace}");

							writer.Close();
						}
						catch (Exception ex1)
						{
							MessageBox.Show(this, $"Could not write the Exception.log file. The exception (when trying to open a file) was: {ex1.Message}",
								"Game engine error", MessageBoxIcon.Error, MessageBoxButtons.OK);
						}
					}
				}

				this.closing = true;
				this.Close();
			}

			if (!this.closing)
			{
				if (this.game.CPU.Pause && !this.gamePaused.IsVisible)
				{
					this.gamePaused.IsVisible = true;
				}
				else if (!this.game.CPU.Pause && this.gamePaused.IsVisible)
				{
					this.gamePaused.IsVisible = false;
				}

				RedrawScreens(false);

				if (this.liveScript.Count > 0 && this.liveClock.ElapsedMilliseconds >= this.liveWaitUntil)
				{
					string cmd = this.liveScript.Peek();

					if (cmd == "bootmap")
					{
						GBitmap? s0 = this.game.Graphics.Screens.ContainsKey(0) ? this.game.Graphics.Screens.GetValueByKey(0) : null;

						if (s0?.Over != null)
						{
							this.liveScript.Dequeue();
							this.liveWaitUntil = this.liveClock.ElapsedMilliseconds + 1200;
						}
						else
						{
							lock (OpenCivOneGame.KeyboardAndMouseLock)
								if (this.game.Keys.Count == 0)
									this.game.Keys.Enqueue(13);
							this.liveWaitUntil = this.liveClock.ElapsedMilliseconds + 500;
						}
					}
					else
					{
						this.liveScript.Dequeue();

						if (cmd.StartsWith("w:") && long.TryParse(cmd.Substring(2), out long ms))
						{
							this.liveWaitUntil = this.liveClock.ElapsedMilliseconds + ms;
						}
						else if (cmd.StartsWith("k:") && int.TryParse(cmd.Substring(2), out int key))
						{
							lock (OpenCivOneGame.KeyboardAndMouseLock)
								this.game.Keys.Enqueue(key);
						}
						else if (cmd == "reveal")
						{
							int human = this.game.GameData.HumanPlayerID;
							ushort mask = (ushort)(1 << human);
							for (int y = 0; y < 50; y++)
								for (int x = 0; x < 80; x++)
									this.game.GameData.MapVisibility[x, y] |= mask;

							int vx = this.game.MapManagement.AdjustXPosition(this.game.Var_d4cc_MapViewX);
							int vy = Math.Clamp(this.game.Var_d75e_MapViewY, 0, 38);
							this.game.MapManagement.F0_2aea_0008_DrawVisibleMap(human, vx, vy);
							this.liveWaitUntil = this.liveClock.ElapsedMilliseconds + 600;
						}
						else if (cmd == "coastal")
						{
							try
							{
								int human = this.game.GameData.HumanPlayerID;
								int cx = this.game.MapManagement.AdjustXPosition(this.game.Var_d4cc_MapViewX + 7);
								int cy = Math.Clamp(this.game.Var_d75e_MapViewY + 6, 4, 45);
								var found = new List<(int x, int y, int dir)>();
								for (int r = 1; r <= 7 && found.Count < 4; r++)
								{
									for (int dy = -r; dy <= r && found.Count < 4; dy++)
									{
										for (int dx = -r; dx <= r && found.Count < 4; dx++)
										{
											if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
											int tx = this.game.MapManagement.AdjustXPosition(cx + dx);
											int ty = cy + dy;
											if (ty < 3 || ty > 46) continue;
											TerrainTypeEnum t = this.game.MapManagement.GetTerrainType(tx, ty);
											if (t == TerrainTypeEnum.Water || t == TerrainTypeEnum.Mountains) continue;
											if ((this.game.MapManagement.F0_2aea_1585_GetVisibleTerrainImprovements(tx, ty) & TerrainImprovementFlagsEnum.City) != 0) continue;
											if (found.Any(p => Math.Abs(p.x - tx) + Math.Abs(p.y - ty) < 3)) continue;
											(int D, int Dx, int Dy)[] cDirs = { (0, 1, 0), (1, -1, 0), (3, 0, -1), (2, 0, 1) };
											int dir = -1;
											foreach (var (d, ox, oy) in cDirs)
											{
												if (this.game.MapManagement.GetTerrainType(tx + ox, ty + oy) == TerrainTypeEnum.Water &&
													this.game.MapManagement.GetTerrainType(tx + (2 * ox), ty + (2 * oy)) == TerrainTypeEnum.Water &&
													this.game.MapManagement.GetTerrainType(tx + ox - oy, ty + oy + ox) == TerrainTypeEnum.Water &&
													this.game.MapManagement.GetTerrainType(tx + ox + oy, ty + oy - ox) == TerrainTypeEnum.Water)
												{
													dir = d;
													break;
												}
											}
											if (dir < 0) continue;
											if (found.Any(p => p.dir == dir) && r < 5) continue;
											found.Add((tx, ty, dir));
										}
									}
								}
								sbyte[] sizes = { 12, 7, 4, 2 };
								int nextSlot = 0;
								for (int i = 0; i < found.Count; i++)
								{
									while (nextSlot < 128 && this.game.GameData.Cities[nextSlot].Status != CityStatusEnum.Invalid)
										nextSlot++;
									if (nextSlot >= 128) break;
									var (tx, ty, _) = found[i];
									City c = this.game.GameData.Cities[nextSlot];
									c.Status = CityStatusEnum.WaterAccess;
									c.PlayerID = (short)human;
									c.Position = new GPoint(tx, ty);
									c.ActualSize = sizes[i];
									c.VisibleSize = c.ActualSize;
									c.NameID = (byte)(nextSlot + 1);
									if (i == 0 || i == 2) c.AddImprovement(ImprovementEnum.CityWalls);
									this.game.GameData.Players[human].CityCount++;
									this.game.MapManagement.F0_2aea_1653_SetTerrainImprovements(tx, ty, TerrainImprovementFlagsEnum.City | TerrainImprovementFlagsEnum.Road);
									this.game.MapManagement.F0_2aea_1601_UpdateVisibleCellStatus(tx, ty);
									for (int oy = -1; oy <= 1; oy++)
										for (int ox = -1; ox <= 1; ox++)
										{
											if (ox == 0 && oy == 0) continue;
											int rx = this.game.MapManagement.AdjustXPosition(tx + ox);
											int ry = ty + oy;
											if (ry >= 2 && ry <= 47 && this.game.MapManagement.GetTerrainType(rx, ry) != TerrainTypeEnum.Water &&
												(Math.Abs(ox) + Math.Abs(oy) == 1))
											{
												this.game.MapManagement.F0_2aea_1653_SetTerrainImprovements(rx, ry, TerrainImprovementFlagsEnum.Road);
												this.game.MapManagement.F0_2aea_1601_UpdateVisibleCellStatus(rx, ry);
											}
										}
									nextSlot++;
								}
								// centre the live camera on the centroid of placed coastal cities so all 4 are visible together
								int sumDx = 0, sumY = 0;
								foreach (var (fx, fy, _) in found)
								{
									int diff = fx - cx;
									if (diff > 40) diff -= 80;
									if (diff < -40) diff += 80;
									sumDx += diff;
									sumY += fy;
								}
								int avgX = found.Count > 0 ? cx + (sumDx / found.Count) : cx;
								int avgY = found.Count > 0 ? sumY / found.Count : cy;
								int vx = this.game.MapManagement.AdjustXPosition(avgX - 7);
								int vy = Math.Clamp(avgY - 6, 0, 38);
								int savedPopFlag = this.game.Var_3484;
								this.game.Var_3484 = -2;
								this.game.MapManagement.F0_2aea_0008_DrawVisibleMap(human, vx, vy);
								this.game.Var_3484 = savedPopFlag;
							}
							catch (Exception ex)
							{
								Console.Error.WriteLine($"LIVE_SCRIPT coastal error: {ex.Message}");
							}
							this.liveWaitUntil = this.liveClock.ElapsedMilliseconds + 600;
						}
						else if (cmd.StartsWith("shot:"))
						{
							SaveLiveShot(cmd.Substring(5));
						}
						else if (cmd == "quit")
						{
							Environment.Exit(0);
						}
					}
				}
			}
		}

		#region Keyboard and Mouse events
		private void MainWindow_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
		{
			lock (OpenCivOneGame.KeyboardAndMouseLock)
			{
				e.Handled = true;

				if (e.KeyModifiers == KeyModifiers.None)
				{
					switch (e.Key)
					{
						/*case Key.NumPad0:
							// for testing
							this.game.Keys.Enqueue(0x475c);
							break;*/
						/*case Key.Tab:
							this.game.Keys.Enqueue(0x9);
							break;*/

						case Key.Escape:
							this.game.Keys.Enqueue(0x1b);
							break;

						case Key.F1:
							this.game.Keys.Enqueue(0x3b00);
							break;

						case Key.F2:
							this.game.Keys.Enqueue(0x3c00);
							break;

						case Key.F3:
							this.game.Keys.Enqueue(0x3d00);
							break;

						case Key.F4:
							this.game.Keys.Enqueue(0x3e00);
							break;

						case Key.F5:
							this.game.Keys.Enqueue(0x3f00);
							break;

						case Key.F6:
							this.game.Keys.Enqueue(0x4000);
							break;

						case Key.F7:
							this.game.Keys.Enqueue(0x4100);
							break;

						case Key.F8:
							this.game.Keys.Enqueue(0x4200);
							break;

						case Key.F9:
							this.game.Keys.Enqueue(0x4300);
							break;

						case Key.F10:
							this.game.Keys.Enqueue(0x4400);
							break;

						case Key.Down:
						case Key.NumPad2:
							this.game.Keys.Enqueue(0x5000);
							break;

						case Key.Left:
						case Key.NumPad4:
							this.game.Keys.Enqueue(0x4b00);
							break;

						case Key.Right:
						case Key.NumPad6:
							this.game.Keys.Enqueue(0x4d00);
							break;

						case Key.Up:
						case Key.NumPad8:
							this.game.Keys.Enqueue(0x4800);
							break;

						case Key.Home:
						case Key.NumPad7:
							this.game.Keys.Enqueue(0x4700);
							break;

						case Key.End:
						case Key.NumPad1:
							this.game.Keys.Enqueue(0x4f00);
							break;

						case Key.PageUp:
						case Key.NumPad9:
							this.game.Keys.Enqueue(0x4900);
							break;

						case Key.PageDown:
						case Key.NumPad3:
							this.game.Keys.Enqueue(0x5100);
							break;

						default:
							if (e.KeySymbol != null && e.KeySymbol.Length > 0)
							{
								this.game.Keys.Enqueue((int)e.KeySymbol[0]);
							}
							else
							{
								e.Handled = false;
							}
							break;
					}
				}
				else if ((e.KeyModifiers & KeyModifiers.Shift) == KeyModifiers.Shift)
				{
					switch (e.Key)
					{
						case Key.Down:
							this.game.Keys.Enqueue(0x5032);
							break;

						case Key.Left:
							this.game.Keys.Enqueue(0x4b34);
							break;

						case Key.Right:
							this.game.Keys.Enqueue(0x4d36);
							break;

						case Key.Up:
							this.game.Keys.Enqueue(0x4838);
							break;

						case Key.Home:
							this.game.Keys.Enqueue(0x4737);
							break;

						case Key.End:
							this.game.Keys.Enqueue(0x4f31);
							break;

						case Key.PageUp:
							this.game.Keys.Enqueue(0x4939);
							break;

						case Key.PageDown:
							this.game.Keys.Enqueue(0x5133);
							break;

						default:
							if (e.KeySymbol != null && e.KeySymbol.Length > 0)
							{
								this.game.Keys.Enqueue((int)e.KeySymbol[0]);
							}
							else
							{
								e.Handled = false;
							}
							break;
					}
				}
				else if ((e.KeyModifiers & KeyModifiers.Alt) == KeyModifiers.Alt)
				{
					switch (e.Key)
					{
						case Key.D1:
							ToggleScreen(0);
							break;

						case Key.D2:
							ToggleScreen(1);
							break;

						case Key.D3:
							ToggleScreen(2);
							break;

						case Key.D4:
							ToggleScreen(3);
							break;

						case Key.A:
							this.game.Keys.Enqueue(0x1e00);
							break;

						case Key.D:
							this.game.Keys.Enqueue(0x2000);
							break;

						case Key.E:
							this.game.Keys.Enqueue(0x2e00);
							break;

						case Key.F:
							this.game.Keys.Enqueue(0x2001);
							break;

						case Key.G:
							this.game.Keys.Enqueue(0x2200);
							break;

						case Key.H:
							this.game.Keys.Enqueue(0x2300);
							break;

						case Key.M:
							this.game.Keys.Enqueue(0x3200);
							break;

						case Key.O:
							this.game.Keys.Enqueue(0x1800);
							break;

						case Key.P:
							this.game.CPU.Pause = !this.game.CPU.Pause;
							break;

						case Key.Q:
							this.game.Keys.Enqueue(0x1000);
							break;

						case Key.R:
							this.game.Keys.Enqueue(0x1300);
							break;

						case Key.V:
							this.game.Keys.Enqueue(0x2f00);
							break;

						case Key.W:
							this.game.Keys.Enqueue(0x1100);
							break;

						default:
							e.Handled = false;
							break;
					}
				}
			}
		}

		private void MainWindow_PointerMoved(object? sender, PointerEventArgs e)
		{
			PointerPoint pointer = e.GetCurrentPoint(this.mainImage);
			Point point = pointer.Position;
			GPoint location = new GPoint((int)point.X, (int)point.Y);

			if (this.mouseRect.Contains(location))
			{
				GPoint currentPosition = new GPoint(location.X / (this.screenSize.Width / 320), location.Y / (this.screenSize.Height / 200));
				MouseButtonsEnum currentButtons = (pointer.Properties.IsLeftButtonPressed ? MouseButtonsEnum.Left : MouseButtonsEnum.None) |
					(pointer.Properties.IsRightButtonPressed ? MouseButtonsEnum.Right : MouseButtonsEnum.None);

				// prevent repetitive events
				if (this.lastMouseEvent.Position != currentPosition || this.lastMouseEvent.Buttons != currentButtons)
				{
					this.lastMouseEvent = new MouseEvent(currentPosition, currentButtons);

					lock (OpenCivOneGame.KeyboardAndMouseLock)
					{
						if (currentButtons == MouseButtonsEnum.None)
						{
							if (this.game.MouseEvents.Count > 0)
							{
								MouseEvent mouseEvent = this.game.MouseEvents[this.game.MouseEvents.Count - 1];

								if (mouseEvent.Buttons == MouseButtonsEnum.None)
								{
									//Debug.WriteLine($"Removed mouse event [{mouseEvent.Position}, {mouseEvent.Buttons}] ({this.game.MouseEvents.Count})");
									this.game.MouseEvents.RemoveAt(this.game.MouseEvents.Count - 1);
								}
							}

							//Debug.WriteLine($"Move [{currentPosition}, {currentButtons}] ({this.game.MouseEvents.Count})");
							this.game.MouseEvents.Add(new MouseEvent(currentPosition, currentButtons));
						}
						else
						{
							if (this.game.MouseEvents.Count > 0)
							{
								MouseEvent mouseEvent = this.game.MouseEvents[this.game.MouseEvents.Count - 1];

								if (mouseEvent.Buttons != currentButtons)
								{
									//Debug.WriteLine($"Move [{currentPosition}, {currentButtons}] ({this.game.MouseEvents.Count})");
									this.game.MouseEvents.Add(new MouseEvent(currentPosition, currentButtons));
								}
							}
							else
							{
								//Debug.WriteLine($"Move [{currentPosition}, {currentButtons}] ({this.game.MouseEvents.Count})");
								this.game.MouseEvents.Add(new MouseEvent(currentPosition, currentButtons));
							}
						}
					}
				}
			}

			e.Handled = true;
		}

		private void MainWindow_PointerPressed(object? sender, PointerPressedEventArgs e)
		{
			PointerPoint pointer = e.GetCurrentPoint(this.mainImage);
			Point point = pointer.Position;
			GPoint location = new GPoint((int)point.X, (int)point.Y);

			if (this.mouseRect.Contains(location))
			{
				GPoint currentPosition = new GPoint(location.X / (this.screenSize.Width / 320), location.Y / (this.screenSize.Height / 200));
				MouseButtonsEnum currentButtons = (pointer.Properties.IsLeftButtonPressed ? MouseButtonsEnum.Left : MouseButtonsEnum.None) |
					(pointer.Properties.IsRightButtonPressed ? MouseButtonsEnum.Right : MouseButtonsEnum.None);

				// prevent repetitive events
				if (this.lastMouseEvent.Position != currentPosition || this.lastMouseEvent.Buttons != currentButtons)
				{
					this.lastMouseEvent = new MouseEvent(currentPosition, currentButtons);

					lock (OpenCivOneGame.KeyboardAndMouseLock)
					{

						if (currentButtons == MouseButtonsEnum.None)
						{
							if (this.game.MouseEvents.Count > 0)
							{
								MouseEvent mouseEvent = this.game.MouseEvents[this.game.MouseEvents.Count - 1];

								if (mouseEvent.Buttons == MouseButtonsEnum.None)
								{
									//Debug.WriteLine($"Removed mouse event [{mouseEvent.Position}, {mouseEvent.Buttons}] ({this.game.MouseEvents.Count})");
									this.game.MouseEvents.RemoveAt(this.game.MouseEvents.Count - 1);
								}
							}

							//Debug.WriteLine($"Press [{currentPosition}, {currentButtons}] ({this.game.MouseEvents.Count})");
							this.game.MouseEvents.Add(new MouseEvent(currentPosition, currentButtons));
						}
						else
						{
							if (this.game.MouseEvents.Count > 0)
							{
								MouseEvent mouseEvent = this.game.MouseEvents[this.game.MouseEvents.Count - 1];

								if (mouseEvent.Buttons != currentButtons)
								{
									//Debug.WriteLine($"Press [{currentPosition}, {currentButtons}] ({this.game.MouseEvents.Count})");
									this.game.MouseEvents.Add(new MouseEvent(currentPosition, currentButtons));
								}
							}
							else
							{
								//Debug.WriteLine($"Press [{currentPosition}, {currentButtons}] ({this.game.MouseEvents.Count})");
								this.game.MouseEvents.Add(new MouseEvent(currentPosition, currentButtons));
							}
						}
					}
				}
			}

			e.Handled = true;
		}

		private void MainWindow_PointerReleased(object? sender, PointerReleasedEventArgs e)
		{
			PointerPoint pointer = e.GetCurrentPoint(this.mainImage);
			Point point = pointer.Position;
			GPoint location = new GPoint((int)point.X, (int)point.Y);

			if (this.mouseRect.Contains(location))
			{
				GPoint currentPosition = new GPoint(location.X / (this.screenSize.Width / 320), location.Y / (this.screenSize.Height / 200));
				MouseButtonsEnum currentButtons = (pointer.Properties.IsLeftButtonPressed ? MouseButtonsEnum.Left : MouseButtonsEnum.None) |
					(pointer.Properties.IsRightButtonPressed ? MouseButtonsEnum.Right : MouseButtonsEnum.None);

				// prevent repetitive events
				if (this.lastMouseEvent.Position != currentPosition || this.lastMouseEvent.Buttons != currentButtons)
				{
					this.lastMouseEvent = new MouseEvent(currentPosition, currentButtons);

					lock (OpenCivOneGame.KeyboardAndMouseLock)
					{
						if (currentButtons == MouseButtonsEnum.None)
						{
							if (this.game.MouseEvents.Count > 0)
							{
								MouseEvent mouseEvent = this.game.MouseEvents[this.game.MouseEvents.Count - 1];

								if (mouseEvent.Buttons == MouseButtonsEnum.None)
								{
									//Debug.WriteLine($"Removed mouse event [{mouseEvent.Position}, {mouseEvent.Buttons}] ({this.game.MouseEvents.Count})");
									this.game.MouseEvents.RemoveAt(this.game.MouseEvents.Count - 1);
								}
							}

							//Debug.WriteLine($"Release [{currentPosition}, {currentButtons}] ({this.game.MouseEvents.Count})");
							this.game.MouseEvents.Add(new MouseEvent(currentPosition, currentButtons));
						}
						else
						{
							if (this.game.MouseEvents.Count > 0)
							{
								MouseEvent mouseEvent = this.game.MouseEvents[this.game.MouseEvents.Count - 1];

								if (mouseEvent.Buttons != currentButtons)
								{
									//Debug.WriteLine($"Release [{currentPosition}, {currentButtons}] ({this.game.MouseEvents.Count})");
									this.game.MouseEvents.Add(new MouseEvent(currentPosition, currentButtons));
								}
							}
							else
							{
								//Debug.WriteLine($"Release [{currentPosition}, {currentButtons}] ({this.game.MouseEvents.Count})");
								this.game.MouseEvents.Add(new MouseEvent(currentPosition, currentButtons));
							}
						}
					}
				}
			}

			e.Handled = true;
		}
		#endregion

		private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
		{
			if (!this.closing)
			{
				if (MessageBox.Show(this, "Are you sure you want to exit OpenCivOne?\nCurrent game, if any, will not be automatically saved!", "Exit application",
					MessageBoxIcon.Question, MessageBoxButtons.OKCancel, MessageBoxDefaultButton.Button2) == MessageBoxResult.OK)
				{
					this.closing = true;
					this.game.CPU.OnApplicationExit();
				}
				else
				{
					e.Cancel = true;
				}
			}
		}

		private void ResizeWindowAndImage()
		{
			switch (this.screenCount)
			{
				case 0:
				case 1:
					this.screenColumns = 1;
					this.screenRows = 1;
					break;

				case 2:
					this.screenColumns = 2;
					this.screenRows = 1;
					break;

				case 3:
					this.screenColumns = 2;
					this.screenRows = 2;
					break;
			}
			this.imageSize = new GSize(this.screenSize.Width * this.screenColumns, this.screenSize.Height * this.screenRows);

			this.mainImage.Width = this.imageSize.Width;
			this.mainImage.Height = this.imageSize.Height;

			this.mouseRect = new GRectangle(0, 0, this.screenSize);

			// Bitmaps are rendered at HD scale, the image control keeps the original size, so the mouse mapping is unchanged
			PixelSize bitmapSize = new PixelSize(this.imageSize.Width * GBitmap.HDScale, this.imageSize.Height * GBitmap.HDScale);

			this.bitmaps[0] = new WriteableBitmap(bitmapSize, new Vector(96, 96), PixelFormat.Bgra8888);
			this.bitmaps[1] = new WriteableBitmap(bitmapSize, new Vector(96, 96), PixelFormat.Bgra8888);
		}

		private void ToggleScreen(int screen)
		{
			if (this.game != null)
			{
				if (this.game.Graphics.Screens.ContainsKey(screen))
				{
					GBitmap oScreen = this.game.Graphics.Screens.GetValueByKey(screen);

					oScreen.Visible = !oScreen.Visible;
				}
			}
		}

		private void SaveLiveShot(string path)
		{
			lock (VCPU.GraphicsLock)
			{
				if (!this.game.Graphics.Screens.ContainsKey(0))
					return;

				GBitmap s0 = this.game.Graphics.Screens.GetValueByKey(0);
				int scale = GBitmap.HDScale;
				int w = s0.Width * scale, h = s0.Height * scale;
				using var sk = new SkiaSharp.SKBitmap(new SkiaSharp.SKImageInfo(w, h, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Opaque));
				s0.WriteScaledPixels(sk.GetPixels(), w * 4);
				using var fs = File.Create(path);
				sk.Encode(fs, SkiaSharp.SKEncodedImageFormat.Png, 100);
			}
		}

		private void RedrawScreens(bool forceRedraw)
		{
			if (this.game != null)
			{
				lock (VCPU.GraphicsLock)
				{
					int iColumn = 0;
					int iRow = 0;

					int iNewScreenCount = 0;
					bool bNeedsRedraw = false;
					bool waterDue = GBitmap.AnimateWater && this.waterClock.ElapsedMilliseconds >= WaterFrameMilliseconds;

					for (int i = 0; i < this.game.Graphics.Screens.Count; i++)
					{
						if (this.game.Graphics.Screens[i].Value.Visible)
						{
							iNewScreenCount++;

							if (this.game.Graphics.Screens[i].Value.Modified || (waterDue && this.game.Graphics.Screens[i].Value.ShowsWater))
								bNeedsRedraw = true;
						}
					}

					if (this.screenCount != iNewScreenCount)
					{
						this.screenCount = iNewScreenCount;
						ResizeWindowAndImage();
						forceRedraw = true;
					}

					if (bNeedsRedraw || forceRedraw)
					{
						this.waterClock.Restart();
						WriteableBitmap currentBitmap = this.bitmaps[this.bitmapIndex];
						int scale = GBitmap.HDScale;
						int iPixelByteSize = 4;
						int iStride = this.imageSize.Width * scale * iPixelByteSize;

						using (ILockedFramebuffer l = currentBitmap.Lock())
						{
							for (int i = 0; i < this.game.Graphics.Screens.Count; i++)
							{
								BKeyValuePair<int, GBitmap> item = this.game.Graphics.Screens[i];

								if (item.Value.Visible)
								{
									int iBitmapAddress0 = ((iRow * this.screenSize.Height * scale) * iStride) +
										((iColumn * this.screenSize.Width * scale) * iPixelByteSize);

									item.Value.WriteScaledPixels(l.Address + iBitmapAddress0, iStride);

									item.Value.Modified = false;

									iColumn++;
									if (iColumn >= this.screenColumns)
									{
										iColumn = 0;
										iRow++;
									}
								}
							}
						}

						this.mainImage.Source = currentBitmap;

						this.bitmapIndex++;
						this.bitmapIndex %= 2;
					}
				}
			}
		}

		private void GameThread()
		{
			AppDomain.CurrentDomain.UnhandledException += (_, e) =>
				Console.WriteLine("GAME EXCEPTION " + (e.ExceptionObject is Exception ex ? ex.Message + "\n" + new System.Diagnostics.StackTrace(ex, true) : e.ExceptionObject));
			bool savePreferences = true;

			try
			{
				this.game.Start();
			}
			catch (ApplicationExitException)
			{
			}
			catch (ResourceMissingException ex)
			{
				// Show exceptions on UI Thread
				this.gameException = ex;
				savePreferences = false;
			}
#if !DEBUG
			catch (Exception ex)
			{
				// Show exceptions on UI Thread
				this.gameException = ex;
				savePreferences = false;
			}
#endif
			if (savePreferences)
			{
				this.game.GlobalPreferences.SerializeToJSON(this.game, this.game.MainPath);
			}
		}
	}
}
