using Silk.NET.OpenAL;

namespace OpenCivOne.Sound
{
	/// <summary>
	/// Plays the game's sounds through OpenAL (OpenAL Soft ships with the game for Windows, macOS and Linux). A theme
	/// can give its own sound for any tune as sounds/tuneNN.wav (16 bit PCM); otherwise Synth makes one. With no
	/// sound device the game stays silent and carries on.
	/// </summary>
	public static unsafe class AudioOut
	{
		/// <summary>
		/// The theme's sounds folder, if any
		/// </summary>
		public static string? SoundDirectory;

		private static readonly object Lock = new object();
		private static bool tried;
		private static AL? al;
		private static ALContext? alc;
		private static Device* device;
		private static Context* context;
		private static uint musicSource;
		private static readonly List<uint> effectSources = new List<uint>();
		private static readonly Dictionary<int, uint> buffers = new Dictionary<int, uint>();

		private static bool Open()
		{
			if (tried)
				return al != null;

			tried = true;

			try
			{
				alc = ALContext.GetApi(true);
				al = AL.GetApi(true);
				device = alc.OpenDevice("");

				if (device == null)
					throw new InvalidOperationException("no sound device");

				context = alc.CreateContext(device, null);
				alc.MakeContextCurrent(context);
				musicSource = al.GenSource();

				for (int i = 0; i < 4; i++)
					effectSources.Add(al.GenSource());

				return true;
			}
			catch (Exception e)
			{
				Console.WriteLine($"Sound is off: {e.Message}");
				al = null;
				return false;
			}
		}

		/// <summary>
		/// Starts a tune or effect and returns at once; music replaces the music playing, effects play over it
		/// </summary>
		public static void Play(int tune)
		{
			lock (Lock)
			{
				if (!Open() || al == null)
					return;

				uint? buffer = BufferFor(tune);

				if (buffer == null)
					return;

				uint source;

				if (Synth.IsMusic(tune))
				{
					source = musicSource;
					al.SourceStop(source);
				}
				else
				{
					// a free effect source, or the first one
					source = effectSources[0];

					foreach (uint s in effectSources)
					{
						al.GetSourceProperty(s, GetSourceInteger.SourceState, out int state);

						if (state != (int)SourceState.Playing)
						{
							source = s;
							break;
						}
					}

					al.SourceStop(source);
				}

				al.SetSourceProperty(source, SourceInteger.Buffer, (int)buffer.Value);
				al.SourcePlay(source);
			}
		}

		/// <summary>
		/// Stops the music (the game's tune 1)
		/// </summary>
		public static void StopMusic()
		{
			lock (Lock)
			{
				if (al != null)
					al.SourceStop(musicSource);
			}
		}

		public static void Close()
		{
			lock (Lock)
			{
				if (al == null || alc == null)
					return;

				al.SourceStop(musicSource);
				al.DeleteSource(musicSource);

				foreach (uint s in effectSources)
				{
					al.SourceStop(s);
					al.DeleteSource(s);
				}

				foreach (uint b in buffers.Values)
					al.DeleteBuffer(b);

				effectSources.Clear();
				buffers.Clear();
				alc.MakeContextCurrent(null);
				alc.DestroyContext(context);
				alc.CloseDevice(device);
				al = null;
				tried = false;
			}
		}

		private static uint? BufferFor(int tune)
		{
			if (buffers.TryGetValue(tune, out uint existing))
				return existing;

			short[]? samples = null;
			int rate = Synth.Rate, channels = 1;

			if (SoundDirectory != null)
			{
				string path = Path.Combine(SoundDirectory, $"tune{tune:00}.wav");

				if (File.Exists(path))
					samples = ReadWav(path, out rate, out channels);
			}

			samples ??= Synth.Make(tune);

			if (samples == null || samples.Length == 0)
				return null;

			uint buffer = al!.GenBuffer();

			fixed (short* data = samples)
			{
				al.BufferData(buffer, (channels == 2) ? BufferFormat.Stereo16 : BufferFormat.Mono16, data, samples.Length * 2, rate);
			}

			buffers[tune] = buffer;
			return buffer;
		}

		/// <summary>
		/// The samples of a 16 bit PCM WAV file, or null if it is another kind
		/// </summary>
		public static short[]? ReadWav(string path, out int rate, out int channels)
		{
			rate = Synth.Rate;
			channels = 1;

			try
			{
				using var reader = new BinaryReader(File.OpenRead(path));

				if (new string(reader.ReadChars(4)) != "RIFF")
					return null;

				reader.ReadInt32();

				if (new string(reader.ReadChars(4)) != "WAVE")
					return null;

				int bits = 0;

				while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
				{
					string id = new string(reader.ReadChars(4));
					int size = reader.ReadInt32();

					if (id == "fmt ")
					{
						short format = reader.ReadInt16();
						channels = reader.ReadInt16();
						rate = reader.ReadInt32();
						reader.ReadInt32();
						reader.ReadInt16();
						bits = reader.ReadInt16();
						reader.BaseStream.Seek(size - 16, SeekOrigin.Current);

						if (format != 1 || bits != 16 || channels < 1 || channels > 2)
							return null;
					}
					else if (id == "data" && bits == 16)
					{
						short[] samples = new short[size / 2];

						for (int i = 0; i < samples.Length; i++)
							samples[i] = reader.ReadInt16();

						return samples;
					}
					else
					{
						reader.BaseStream.Seek(size + (size & 1), SeekOrigin.Current);
					}
				}
			}
			catch (Exception)
			{
			}

			return null;
		}
	}
}
