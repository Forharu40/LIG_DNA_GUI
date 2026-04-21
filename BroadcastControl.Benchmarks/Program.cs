using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using OpenCvSharp;

var options = BenchmarkOptions.Parse(args);
if (!File.Exists(options.SampleAPath) || !File.Exists(options.SampleBPath))
{
    Console.Error.WriteLine("Sample JPEG files were not found.");
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  dotnet run --project .\\BroadcastControl.Benchmarks\\BroadcastControl.Benchmarks.csproj -- --a <1280.jpg> --b <current.jpg>");
    return 1;
}

var sampleA = File.ReadAllBytes(options.SampleAPath);
var sampleB = File.ReadAllBytes(options.SampleBPath);

Console.WriteLine("GUI decode and network burden benchmark");
Console.WriteLine($"Sample A: {options.SampleAPath}");
Console.WriteLine($"Sample B: {options.SampleBPath}");
Console.WriteLine($"Measured frames: {options.Frames}");
Console.WriteLine($"Warmup frames: {options.WarmupFrames}");
Console.WriteLine($"Network bandwidth estimate: {options.NetworkMbps:0.###} Mbps");
Console.WriteLine();

var resultA = Measure("Sample A", sampleA, options);
Console.WriteLine();
var resultB = Measure("Sample B", sampleB, options);
Console.WriteLine();
PrintDifference(resultA, resultB, options);

return 0;

static BenchmarkResult Measure(string label, byte[] jpegBytes, BenchmarkOptions options)
{
    for (var i = 0; i < options.WarmupFrames; i++)
    {
        DecodeLikeGui(jpegBytes);
    }

    var elapsedMs = new List<double>(options.Frames);
    for (var i = 0; i < options.Frames; i++)
    {
        var stopwatch = Stopwatch.StartNew();
        DecodeLikeGui(jpegBytes);
        stopwatch.Stop();
        elapsedMs.Add(stopwatch.Elapsed.TotalMilliseconds);
    }

    var sorted = elapsedMs.Order().ToArray();
    var averageMs = elapsedMs.Average();
    var medianMs = Percentile(sorted, 0.50);
    var p95Ms = Percentile(sorted, 0.95);
    var networkMs = EstimateNetworkMilliseconds(jpegBytes.Length, options.NetworkMbps);

    Console.WriteLine(label);
    Console.WriteLine($"  JPEG size              : {jpegBytes.Length / 1024.0:0.0} KiB");
    Console.WriteLine($"  decode average         : {averageMs:0.###} ms ({averageMs / 1000.0:0.000000} sec)");
    Console.WriteLine($"  decode median          : {medianMs:0.###} ms ({medianMs / 1000.0:0.000000} sec)");
    Console.WriteLine($"  decode p95             : {p95Ms:0.###} ms ({p95Ms / 1000.0:0.000000} sec)");
    Console.WriteLine($"  network burden estimate: {networkMs:0.###} ms ({networkMs / 1000.0:0.000000} sec)");

    return new BenchmarkResult(jpegBytes.Length, averageMs, medianMs, p95Ms, networkMs);
}

static BitmapSource DecodeLikeGui(byte[] jpegBytes)
{
    using var encoded = Mat.FromImageData(jpegBytes, ImreadModes.Color);
    if (encoded.Empty())
    {
        throw new InvalidOperationException("JPEG decode failed.");
    }

    using var adjusted = new Mat();
    encoded.ConvertTo(adjusted, MatType.CV_8UC3, 1.0, 0.0);

    using var converted = new Mat();
    Cv2.CvtColor(adjusted, converted, ColorConversionCodes.BGR2BGRA);

    var bufferSize = checked((int)(converted.Step() * converted.Rows));
    var stride = checked((int)converted.Step());

    var bitmap = BitmapSource.Create(
        converted.Width,
        converted.Height,
        96,
        96,
        System.Windows.Media.PixelFormats.Bgra32,
        null,
        converted.Data,
        bufferSize,
        stride);

    bitmap.Freeze();
    return bitmap;
}

static void PrintDifference(BenchmarkResult a, BenchmarkResult b, BenchmarkOptions options)
{
    var sizeDelta = a.JpegBytes - b.JpegBytes;
    var decodeAverageDelta = a.DecodeAverageMs - b.DecodeAverageMs;
    var decodeMedianDelta = a.DecodeMedianMs - b.DecodeMedianMs;
    var decodeP95Delta = a.DecodeP95Ms - b.DecodeP95Ms;
    var networkDelta = a.NetworkMs - b.NetworkMs;
    var totalAverageDelta = decodeAverageDelta + networkDelta;

    Console.WriteLine("Difference: Sample A minus Sample B");
    Console.WriteLine($"  JPEG size delta        : {sizeDelta / 1024.0:0.0} KiB");
    Console.WriteLine($"  decode average delta   : {decodeAverageDelta:0.###} ms ({decodeAverageDelta / 1000.0:0.000000} sec)");
    Console.WriteLine($"  decode median delta    : {decodeMedianDelta:0.###} ms ({decodeMedianDelta / 1000.0:0.000000} sec)");
    Console.WriteLine($"  decode p95 delta       : {decodeP95Delta:0.###} ms ({decodeP95Delta / 1000.0:0.000000} sec)");
    Console.WriteLine($"  network burden delta   : {networkDelta:0.###} ms ({networkDelta / 1000.0:0.000000} sec)");
    Console.WriteLine($"  decode+network avg diff: {totalAverageDelta:0.###} ms ({totalAverageDelta / 1000.0:0.000000} sec)");
    Console.WriteLine();
    Console.WriteLine("Note: network burden is a serialization-time estimate from JPEG bytes and configured bandwidth.");
    Console.WriteLine($"      Current bandwidth setting: {options.NetworkMbps:0.###} Mbps");
}

static double EstimateNetworkMilliseconds(int bytes, double networkMbps)
{
    if (networkMbps <= 0)
    {
        return 0;
    }

    return bytes * 8.0 / (networkMbps * 1_000_000.0) * 1000.0;
}

static double Percentile(double[] sorted, double percentile)
{
    if (sorted.Length == 0)
    {
        return 0;
    }

    var index = Math.Clamp((int)Math.Ceiling(sorted.Length * percentile) - 1, 0, sorted.Length - 1);
    return sorted[index];
}

sealed record BenchmarkResult(
    int JpegBytes,
    double DecodeAverageMs,
    double DecodeMedianMs,
    double DecodeP95Ms,
    double NetworkMs);

sealed record BenchmarkOptions(
    string SampleAPath,
    string SampleBPath,
    int Frames,
    int WarmupFrames,
    double NetworkMbps)
{
    public static BenchmarkOptions Parse(string[] args)
    {
        string? sampleA = null;
        string? sampleB = null;
        var frames = 180;
        var warmupFrames = 20;
        var networkMbps = 100.0;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            var value = i + 1 < args.Length ? args[i + 1] : null;
            switch (arg)
            {
                case "--a" when value is not null:
                    sampleA = value;
                    i++;
                    break;
                case "--b" when value is not null:
                    sampleB = value;
                    i++;
                    break;
                case "--frames" when value is not null:
                    frames = int.Parse(value, CultureInfo.InvariantCulture);
                    i++;
                    break;
                case "--warmup" when value is not null:
                    warmupFrames = int.Parse(value, CultureInfo.InvariantCulture);
                    i++;
                    break;
                case "--network-mbps" when value is not null:
                    networkMbps = double.Parse(value, CultureInfo.InvariantCulture);
                    i++;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(sampleA) || string.IsNullOrWhiteSpace(sampleB))
        {
            sampleA = "";
            sampleB = "";
        }

        return new BenchmarkOptions(
            sampleA,
            sampleB,
            Math.Max(1, frames),
            Math.Max(0, warmupFrames),
            Math.Max(0, networkMbps));
    }
}
