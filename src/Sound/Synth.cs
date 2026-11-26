namespace OpenCivOne.Sound
{
	/// <summary>
	/// Makes the game's tunes and effects as sound (16 bit mono samples at Rate): original melodies on a small
	/// plucked/bowed voice for the music cues, and noise and metal for the battle effects. Each tune number is the
	/// one the game asks for (see NSound): 0 and 3 the title, 4 a new world, 5 to 18 each nation's short tune,
	/// 19 to 32 its long tune, 33 an audience, 34 a celebration, 35 a solemn moment, 36 bad news, 37 a refused move,
	/// 38 to 41 battles, 42 a nuclear blast, 43 a bombing run and 44 a new city.
	/// </summary>
	public static class Synth
	{
		public const int Rate = 22050;

		/// <summary>
		/// True for the cues that are music (one at a time, stopped by the game's tune 1), false for effects
		/// </summary>
		public static bool IsMusic(int tune) => tune <= 36;

		public static short[]? Make(int tune)
		{
			return tune switch
			{
				0 or 3 => Melody(TitleTheme(), 0.32, Voice.Koto, 3),
				4 => Melody(Phrase(4, 16, Scales.Yo, 60), 0.28, Voice.Shakuhachi, 4),
				>= 5 and <= 18 => Melody(Phrase(tune, 8, ScaleFor(tune), 62), 0.22, Voice.Koto, tune),
				>= 19 and <= 32 => Melody(Phrase(tune, 24, ScaleFor(tune), 60), 0.26, Voice.Koto, tune),
				33 => Melody(Fanfare(), 0.2, Voice.Koto, 33),
				34 => Melody(Celebration(), 0.14, Voice.Koto, 34),
				35 => Melody(Phrase(35, 10, Scales.In, 55), 0.5, Voice.Shakuhachi, 35),
				36 => Melody(new[] { (67, 1.0), (63, 1.0), (60, 2.0), (59, 3.0) }, 0.28, Voice.Shakuhachi, 36),
				37 => Thud(),
				38 or 39 => Clash(tune),
				40 or 41 => Gunfire(tune),
				42 => Blast(4.0, 1.0),
				43 => Bombing(),
				44 => Melody(new[] { (60, 1.0), (64, 1.0), (67, 1.0), (72, 3.0) }, 0.16, Voice.Koto, 44),
				_ => null
			};
		}

		private enum Voice { Koto, Shakuhachi }

		private static class Scales
		{
			// semitone steps of pentatonic modes
			public static readonly int[] Yo = { 0, 2, 5, 7, 9 };
			public static readonly int[] In = { 0, 1, 5, 7, 8 };
			public static readonly int[] Major = { 0, 2, 4, 7, 9 };
			public static readonly int[] Minor = { 0, 3, 5, 7, 10 };
		}

		private static int[] ScaleFor(int tune) => (tune % 4) switch { 0 => Scales.Yo, 1 => Scales.Major, 2 => Scales.Minor, _ => Scales.In };

		/// <summary>
		/// A phrase walking the scale from a seed: mostly steps, a few leaps, ending on the home note
		/// </summary>
		private static (int Note, double Beats)[] Phrase(int seed, int length, int[] scale, int home)
		{
			var random = new Random(seed * 7919);
			var notes = new List<(int, double)>();
			int degree = 5;

			for (int i = 0; i < length - 1; i++)
			{
				int step = random.Next(10) switch { < 4 => 1, < 8 => -1, 8 => 2, _ => -2 };
				degree = Math.Clamp(degree + step, 0, 9);
				double beats = (i % 4 == 3) ? 2 : (random.Next(3) == 0 ? 0.5 : 1);
				notes.Add((home + (12 * (degree / 5)) + scale[degree % 5], beats));
			}

			notes.Add((home, 3));
			return notes.ToArray();
		}

		private static (int, double)[] TitleTheme()
		{
			var all = new List<(int, double)>();
			all.AddRange(Phrase(100, 12, Scales.Yo, 57));
			all.AddRange(Phrase(101, 12, Scales.Yo, 62));
			all.AddRange(Phrase(102, 8, Scales.Yo, 57));
			return all.ToArray();
		}

		private static (int, double)[] Fanfare() => new[] { (60, 1.0), (60, 0.5), (67, 1.5), (65, 0.5), (67, 0.5), (72, 3.0) };

		private static (int, double)[] Celebration()
		{
			var notes = new List<(int, double)>();
			int[] up = { 60, 62, 64, 67, 69, 72, 69, 67 };

			for (int r = 0; r < 2; r++)
				foreach (int n in up)
					notes.Add((n + (r * 5), 1));

			notes.Add((72, 4));
			return notes.ToArray();
		}

		private static double Frequency(int midi) => 440.0 * Math.Pow(2, (midi - 69) / 12.0);

		/// <summary>
		/// Plays notes one after another (each one rings on under the next), with a soft drum on the strong beats
		/// </summary>
		private static short[] Melody((int Note, double Beats)[] notes, double beat, Voice voice, int seed)
		{
			double total = 0;

			foreach (var n in notes)
				total += n.Beats;

			int length = (int)((total * beat + 1.5) * Rate);
			float[] mix = new float[length];
			double time = 0;
			int index = 0;

			foreach (var (note, beats) in notes)
			{
				int start = (int)(time * Rate);
				double ring = (voice == Voice.Koto) ? Math.Max(beats * beat, 0.25) + 0.9 : beats * beat * 1.05;
				AddNote(mix, start, Frequency(note), ring, voice);

				// a taiko on every fourth note
				if (index % 4 == 0)
					AddDrum(mix, start, 0.18f, seed + index);

				time += beats * beat;
				index++;
			}

			return ToPcm(mix, 0.8f);
		}

		private static void AddNote(float[] mix, int start, double frequency, double seconds, Voice voice)
		{
			int count = Math.Min((int)(seconds * Rate), mix.Length - start);

			for (int i = 0; i < count; i++)
			{
				double t = i / (double)Rate;
				double phase = 2 * Math.PI * frequency * t;
				double sample;

				if (voice == Voice.Koto)
				{
					// plucked: bright attack, harmonics fading faster than the fundamental
					double decay = Math.Exp(-t * 3.2);
					sample = (Math.Sin(phase) + (0.5 * Math.Exp(-t * 6) * Math.Sin(2 * phase)) + (0.25 * Math.Exp(-t * 10) * Math.Sin(3 * phase))) * decay;
					sample *= Math.Min(1, t * 400);
				}
				else
				{
					// breathy flute: slow swell, vibrato, a little noise
					double swell = Math.Min(1, t * 8) * Math.Min(1, (seconds - t) * 10);
					double vibrato = Math.Sin(2 * Math.PI * 5 * t) * 0.006 * Math.Min(1, t * 2);
					sample = (Math.Sin(phase * (1 + vibrato)) + (0.15 * Math.Sin(2 * phase))) * swell;
					sample += (Noise(start + i) * 0.04 * swell);
				}

				mix[start + i] += (float)(sample * 0.35);
			}
		}

		private static void AddDrum(float[] mix, int start, float volume, int seed)
		{
			int count = Math.Min((int)(0.35 * Rate), mix.Length - start);

			for (int i = 0; i < count; i++)
			{
				double t = i / (double)Rate;
				double pitch = 70 * Math.Exp(-t * 8) + 45;
				mix[start + i] += (float)((Math.Sin(2 * Math.PI * pitch * t) + (Noise(seed * 977 + i) * 0.2 * Math.Exp(-t * 40))) * Math.Exp(-t * 9) * volume);
			}
		}

		/// <summary>
		/// A cheap repeatable white noise sample in -1..1
		/// </summary>
		private static double Noise(int n)
		{
			unchecked
			{
				uint x = (uint)n * 747796405u + 2891336453u;
				x = ((x >> (int)((x >> 28) + 4u)) ^ x) * 277803737u;
				x = (x >> 22) ^ x;
				return (x / (double)uint.MaxValue * 2) - 1;
			}
		}

		/// <summary>
		/// Noise through a one pole low-pass filter (lower cutoff, darker rumble)
		/// </summary>
		private static void AddRumble(float[] mix, int start, double seconds, double cutoff, float volume, double attack, double decay, int seed)
		{
			int count = Math.Min((int)(seconds * Rate), mix.Length - start);
			double a = Math.Exp(-2 * Math.PI * cutoff / Rate), y = 0;

			for (int i = 0; i < count; i++)
			{
				double t = i / (double)Rate;
				y = ((1 - a) * Noise(seed + i)) + (a * y);
				double envelope = Math.Min(1, t / attack) * Math.Exp(-t * decay);
				mix[start + i] += (float)(y * envelope * volume * (1 + (cutoff < 400 ? 3 : 0)));
			}
		}

		/// <summary>
		/// Metal striking metal: inharmonic partials ringing out over a noise hit
		/// </summary>
		private static void AddClang(float[] mix, int start, double baseFrequency, float volume, int seed)
		{
			double[] ratios = { 1, 2.76, 5.40, 8.93, 13.34 };
			int count = Math.Min((int)(0.6 * Rate), mix.Length - start);

			for (int i = 0; i < count; i++)
			{
				double t = i / (double)Rate, sample = 0;

				for (int p = 0; p < ratios.Length; p++)
					sample += Math.Sin(2 * Math.PI * baseFrequency * ratios[p] * t) * Math.Exp(-t * (6 + (p * 5))) / (p + 1);

				sample += Noise(seed + i) * Math.Exp(-t * 60) * 0.8;
				mix[start + i] += (float)(sample * volume);
			}
		}

		private static short[] Clash(int tune)
		{
			var random = new Random(tune);
			float[] mix = new float[(int)(2.4 * Rate)];

			// the armies' shout, then blades
			AddRumble(mix, 0, 2.2, 900, 0.25f, 0.3, 1.2, 11);

			for (int k = 0; k < 7; k++)
				AddClang(mix, (int)((0.15 + (k * 0.26) + (random.NextDouble() * 0.08)) * Rate), 520 + random.Next(300), 0.35f, k * 4001);

			// the win (38) ends on a high ring, the loss (39) on a fall
			AddClang(mix, (int)(1.95 * Rate), (tune == 38) ? 900 : 300, 0.5f, 99);
			return ToPcm(mix, 0.9f);
		}

		private static short[] Gunfire(int tune)
		{
			var random = new Random(tune * 3);
			float[] mix = new float[(int)(2.6 * Rate)];

			for (int k = 0; k < 9; k++)
			{
				int start = (int)((0.05 + (k * 0.22) + (random.NextDouble() * 0.1)) * Rate);
				AddRumble(mix, start, 0.5, 5000, 0.9f, 0.002, 18, k * 7001);
				AddRumble(mix, start, 0.6, 250, 0.25f, 0.004, 8, k * 7001 + 3);
			}

			// a cannon to finish
			AddRumble(mix, (int)(1.9 * Rate), 0.7, 180, 0.6f, 0.005, 4, 555);
			return ToPcm(mix, 0.9f);
		}

		private static short[] Blast(double seconds, double size)
		{
			float[] mix = new float[(int)((seconds + 0.5) * Rate)];
			AddRumble(mix, 0, 0.4, 6000, (float)(0.9 * size), 0.003, 12, 1);
			AddRumble(mix, 0, seconds, 120, (float)(0.8 * size), 0.02, 0.9, 2);
			AddRumble(mix, (int)(0.1 * Rate), seconds * 0.8, 500, (float)(0.3 * size), 0.2, 1.5, 3);
			return ToPcm(mix, 1f);
		}

		private static short[] Bombing()
		{
			float[] mix = new float[(int)(3.4 * Rate)];

			// engines: a low buzz rising and passing
			for (int i = 0; i < (int)(3.2 * Rate); i++)
			{
				double t = i / (double)Rate;
				double f = 95 + (12 * Math.Sin(t * 1.3));
				double env = Math.Sin(Math.PI * t / 3.2);
				mix[i] += (float)(((Math.Sin(2 * Math.PI * f * t) > 0 ? 1 : -1) * 0.08 + (Noise(i) * 0.03)) * env);
			}

			for (int k = 0; k < 4; k++)
				AddRumble(mix, (int)((1.0 + (k * 0.45)) * Rate), 1.2, 160, 0.5f, 0.01, 3, 900 + (k * 31));

			return ToPcm(mix, 0.9f);
		}

		private static short[] Thud()
		{
			float[] mix = new float[(int)(0.4 * Rate)];
			AddDrum(mix, 0, 0.8f, 7);
			AddDrum(mix, (int)(0.14 * Rate), 0.6f, 8);
			return ToPcm(mix, 0.7f);
		}

		/// <summary>
		/// Normalises a mix to a peak level and converts it to 16 bit samples
		/// </summary>
		private static short[] ToPcm(float[] mix, float peak)
		{
			float max = 0.0001f;

			foreach (float s in mix)
				max = Math.Max(max, Math.Abs(s));

			float gain = peak / max;
			short[] pcm = new short[mix.Length];

			for (int i = 0; i < mix.Length; i++)
				pcm[i] = (short)Math.Clamp(mix[i] * gain * 32767f, -32767f, 32767f);

			return pcm;
		}
	}
}
