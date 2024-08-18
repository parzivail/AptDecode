using NWaves.Audio;
using NWaves.Filters.Base;
using NWaves.Filters.Fda;
using NWaves.Operations;
using NWaves.Signals;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AptDecode;

class Program
{
	private static readonly bool[] SyncA =
	[
		// quiet zone
		false, false, false, false,
		// pulse train
		true, true, false, false,
		true, true, false, false,
		true, true, false, false,
		true, true, false, false,
		true, true, false, false,
		true, true, false, false,
		true, true, false, false,
		// quiet zone
		false, false, false, false,
		false, false, false, false,
	];

	private static readonly bool[] SyncB =
	[
		// quiet zone
		false, false, false, false,
		// pulse train
		true, true, true, false, false,
		true, true, true, false, false,
		true, true, true, false, false,
		true, true, true, false, false,
		true, true, true, false, false,
		true, true, true, false, false,
		true, true, true, false, false,
		// quiet zone
		false,
	];

	private const int SyncLength = 40;

	static void Main(string[] args)
	{
		WaveFile waveContainer;

		using (var stream = new FileStream(args[0], FileMode.Open))
		{
			waveContainer = new WaveFile(stream);
		}

		var samples = waveContainer[Channels.Left];

		var lowPass = new FirFilter(DesignFilter.FirWinLp(50, 2080f / samples.SamplingRate));
		var carrierReject = new IirFilter(DesignFilter.IirNotch(2400f / samples.SamplingRate, 10));

		var max = 0f;
		var min = float.MaxValue;
		var avgLevel = 0f;

		for (var i = 0; i < samples.Length; i++)
		{
			var sample = MathF.Pow(samples[i], 2);
			sample = MathF.Sqrt(sample);
			sample = lowPass.Process(sample);
			sample = carrierReject.Process(sample);

			samples[i] = sample;

			avgLevel += sample / samples.Length;

			max = Math.Max(sample, max);
			min = Math.Min(sample, min);
		}

		const float secondsPerLine = 0.5f;
		var samplesPerLine = (int)(samples.SamplingRate * secondsPerLine);
		var lines = samples.Length / samplesPerLine + 1;

		var image = new Image<Rgb24>(samplesPerLine, lines);

		var syncX = 0;
		var samplesSinceLastMarker = 0;

		for (var i = 0; i < samples.Length; i++)
		{
			var sample = (samples[i] - min) / (max - min);
			var pixel = (byte)(255 * sample);

			var (isMarkerA, isMarkerB) = IsSync(samples, i, avgLevel);

			var x = i % image.Width;
			var y = i / image.Width;
			
			if (isMarkerA && samplesSinceLastMarker > 500)
			{
				syncX = x;
				samplesSinceLastMarker = 0;
			}

			samplesSinceLastMarker++;
			
			image[(x - syncX + image.Width) % image.Width, y] = new Rgb24(pixel, pixel, pixel);
		}

		image.Mutate(context => context.Resize(2080, image.Height));

		image.Save(args[1]);
	}

	private static (bool isSyncA, bool isSyncB) IsSync(DiscreteSignal samples, int firstSample, float avgLevel)
	{
		const float targetSampleRate = 4160f;
		var actualSampleRate = samples.SamplingRate;
		var sampleAccumulatorSize = (int)(actualSampleRate / targetSampleRate);

		var totalSamples = sampleAccumulatorSize * SyncLength;
		if (firstSample + totalSamples >= samples.Length)
			return (false, false);

		var parityA = 0;
		var parityB = 0;

		var step = sampleAccumulatorSize / 2;
		var requiredPulseQuantity = 370 / step;
		
		for (var i = 0; i < totalSamples; i += step)
		{
			var sample = samples[firstSample + i];
			parityA += (sample > avgLevel) == SyncA[i / sampleAccumulatorSize] ? 1 : 0;
			parityB += (sample > avgLevel) == SyncB[i / sampleAccumulatorSize] ? 1 : 0;
		}

		return (parityA > requiredPulseQuantity, parityB > requiredPulseQuantity);
	}
}