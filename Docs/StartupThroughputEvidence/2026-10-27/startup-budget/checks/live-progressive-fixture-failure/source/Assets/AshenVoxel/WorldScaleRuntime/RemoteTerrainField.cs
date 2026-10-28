using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AshenVoxel.WorldScale;
using UnityEngine;

namespace AshenVoxel.WorldScaleRuntime
{
    /// <summary>Version-pinned asynchronous model-region cache. Sample never performs
    /// I/O. Missing samples use an explicitly provisional rough visual source;
    /// callers must require IsReady before creating authoritative local matter.</summary>
    public sealed class RemoteTerrainField : IWorldField, IWorldFieldBounds, IClimateWorldField, IDisposable
    {
        public const double MinimumSupportedHeight = -12000;
        public const double MaximumSupportedHeight = 48000;
        [Serializable] public sealed class ServerStatus
        {
            public bool ready;
            public string world_id;
            public string backend;
            public double spacing_m;
            public int max_region_side, max_region_samples;
        }
        [Serializable] public sealed class Window
        {
            public long row0, col0;
            public int rows, cols;
        }
        [Serializable] public sealed class Metadata
        {
            public string world_id, layout, schema, height_units, backend;
            public Window window;
            public double spacing_m, origin_x_m, origin_z_m;
            public string[] climate_channels;
            public string climate_encoding;
            public string[] climate_value_units;
        }
        sealed class Region
        {
            public Metadata Metadata;
            public float[] Heights;
            public float[] Climate;
            public byte[] Biomes, Land;
            public long LastUse;
            public long PayloadBytes => Heights.LongLength * 26;
            public bool Contains(double x, double z)
            {
                Metadata m = Metadata;
                return x >= m.origin_x_m && z >= m.origin_z_m &&
                    x <= m.origin_x_m + (m.window.cols - 1) * m.spacing_m &&
                    z <= m.origin_z_m + (m.window.rows - 1) * m.spacing_m;
            }
        }
        readonly IWorldField rough;
        readonly bool forestOverride;
        readonly HttpClient client;
        readonly string endpoint;
        readonly CancellationTokenSource cancel = new CancellationTokenSource();
        readonly CancellationToken token;
        readonly object gate = new object();
        readonly List<Region> regions = new List<Region>();
        readonly Dictionary<string, Task> requests = new Dictionary<string, Task>();
        readonly int maximumRegions;
        readonly long maximumResidentBytes;
        long residentBytes;
        readonly bool allowTestBackend;
        public IWorldField RoughField => rough;
        long uses;
        public ServerStatus Status { get; private set; }
        public int Revision { get; private set; }
        public string TerminalFailure { get; private set; }
        public bool Connected => Status != null && Status.ready && TerminalFailure == null;
        public int ResidentRegions { get { lock (gate) return regions.Count; } }
        /// <summary>Resident channel payload only; immutable snapshots can retain an evicted region.</summary>
        public long ResidentPayloadBytes { get { lock (gate) return residentBytes; } }
        public string SourceIdentity => "remote-model/" + (Status == null ? "unconnected" : Status.world_id) + "/rough=" + rough.SourceIdentity;
        public RemoteTerrainField(IWorldField rough, string endpoint = "http://127.0.0.1:8787", bool forestOverride = true,
            int maximumRegions = 64, HttpMessageHandler handler = null, bool allowTestBackend = false,
            long maximumResidentBytes = 128L * 1024 * 1024)
        {
            this.rough = rough ?? throw new ArgumentNullException(nameof(rough));
            if (maximumRegions < 1 || maximumRegions > 256) throw new ArgumentOutOfRangeException(nameof(maximumRegions));
            if (maximumResidentBytes < 1024 || maximumResidentBytes > 1024L * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maximumResidentBytes));
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri uri) || (uri.Scheme != "http" && uri.Scheme != "https")) throw new ArgumentException("Model endpoint must be HTTP(S).");
            this.endpoint = endpoint.TrimEnd('/'); this.forestOverride = forestOverride; this.maximumRegions = maximumRegions;
            this.allowTestBackend = allowTestBackend;
            this.maximumResidentBytes = maximumResidentBytes;
            token = cancel.Token;
            client = handler == null ? new HttpClient() : new HttpClient(handler);
            client.Timeout = TimeSpan.FromMinutes(5);
        }
        public async Task InitializeAsync()
        {
            using (HttpResponseMessage response = await client.GetAsync(endpoint + "/status", HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                string text = Encoding.UTF8.GetString(await ReadBounded(response, 65536));
                ServerStatus value = JsonUtility.FromJson<ServerStatus>(text);
                if (value == null || !value.ready || !IsHash(value.world_id) || !Finite(value.spacing_m) || value.spacing_m <= 0 || value.spacing_m > 10000 ||
                    value.max_region_side < 2 || value.max_region_side > 4096 || value.max_region_samples < 4 || value.max_region_samples > 4194304)
                    throw new InvalidDataException("Model server is not ready or its protocol limits are invalid.");
                if (string.IsNullOrEmpty(value.backend) || (!allowTestBackend && !value.backend.StartsWith("upstream-pretrained", StringComparison.Ordinal)))
                    throw new InvalidDataException("The server is not reporting a pretrained model backend; test/fake output is not accepted as live neural terrain.");
                Status = value;
            }
        }
        static bool IsHash(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char c in value) if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f') && !(c >= 'A' && c <= 'F')) return false;
            return true;
        }
        static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
        public bool IsReady(double minX, double minZ, double maxX, double maxZ)
        {
            lock (gate) foreach (Region region in regions)
                if (region.Contains(minX, minZ) && region.Contains(maxX, maxZ)) return true;
            return false;
        }
        public Task PrefetchAsync(double minX, double minZ, double maxX, double maxZ)
        {
            if (!Connected) throw new InvalidOperationException("Initialize the model connection first.");
            if (!Finite(minX) || !Finite(minZ) || !Finite(maxX) || !Finite(maxZ) || maxX < minX || maxZ < minZ || Math.Abs(minX) > 1e12 || Math.Abs(maxX) > 1e12 || Math.Abs(minZ) > 1e12 || Math.Abs(maxZ) > 1e12)
                throw new ArgumentOutOfRangeException("Model window bounds must be finite supported world meters.");
            if (IsReady(minX, minZ, maxX, maxZ)) return Task.CompletedTask;
            double step = Status.spacing_m;
            long col0 = (long)Math.Floor(minX / step) - 1, row0 = (long)Math.Floor(minZ / step) - 1;
            long cols = (long)Math.Ceiling(maxX / step) + 2 - col0, rows = (long)Math.Ceiling(maxZ / step) + 2 - row0;
            if (rows < 2 || cols < 2 || rows > Status.max_region_side || cols > Status.max_region_side || rows * cols > Math.Min(262144, Status.max_region_samples))
                throw new ArgumentOutOfRangeException("Model window exceeds the server's bounded native-resolution region limits.");
            if (rows * cols * 26 > maximumResidentBytes)
                throw new ArgumentOutOfRangeException("Model window exceeds the client's decoded channel budget.");
            var window = new Window { row0 = row0, col0 = col0, rows = (int)rows, cols = (int)cols };
            string key = row0 + "/" + col0 + "/" + rows + "/" + cols;
            lock (gate)
            {
                if (requests.TryGetValue(key, out Task current)) return current;
                if (requests.Count >= 4) throw new InvalidOperationException("The model client request limit is reached.");
                Task task = FetchAsync(window, key);
                requests[key] = task;
                return task;
            }
        }
        async Task FetchAsync(Window window, string key)
        {
            // Start asynchronously before touching the request dictionary, including
            // test transports that complete HTTP calls synchronously.
            await Task.Yield();
            try
            {
                string body = FormattableString.Invariant($"{{\"world_id\":\"{Status.world_id}\",\"row0\":{window.row0},\"col0\":{window.col0},\"rows\":{window.rows},\"cols\":{window.cols}}}");
                using (var message = new HttpRequestMessage(HttpMethod.Post, endpoint + "/region") { Content = new StringContent(body, Encoding.UTF8, "application/json") })
                using (HttpResponseMessage response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token))
                {
                    if (response.StatusCode == HttpStatusCode.Conflict) throw new InvalidDataException("Model world identity changed. Rebuild the world source.");
                    if ((int)response.StatusCode == 429) throw new InvalidOperationException("Model queue is full; retry the region later.");
                    if ((int)response.StatusCode >= 500)
                    {
                        TerminalFailure = "Inference service failed; fix the service and rebuild before requesting more regions.";
                        throw new InvalidOperationException(TerminalFailure);
                    }
                    response.EnsureSuccessStatusCode();
                    int limit = 8 + 4 + 65536 + window.rows * window.cols * 26 + 4;
                    byte[] bytes = await ReadBounded(response, limit);
                    Region region = Decode(bytes, Status, window);
                    lock (gate)
                    {
                        if (token.IsCancellationRequested) return;
                        while (regions.Count > 0 && (regions.Count >= maximumRegions || residentBytes + region.PayloadBytes > maximumResidentBytes))
                        {
                            int oldest = 0;
                            for (int i = 1; i < regions.Count; i++) if (regions[i].LastUse < regions[oldest].LastUse) oldest = i;
                            residentBytes -= regions[oldest].PayloadBytes; regions.RemoveAt(oldest);
                        }
                        region.LastUse = ++uses; regions.Add(region); residentBytes += region.PayloadBytes; Revision++;
                    }
                }
            }
            catch (InvalidDataException exception) { TerminalFailure = exception.Message; throw; }
            finally { lock (gate) requests.Remove(key); }
        }
        async Task<byte[]> ReadBounded(HttpResponseMessage response, int limit)
        {
            if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Model response exceeds the client's payload bound.");
            using (Stream input = await response.Content.ReadAsStreamAsync())
            using (var output = new MemoryStream())
            {
                var buffer = new byte[16384];
                while (true)
                {
                    int count = await input.ReadAsync(buffer, 0, buffer.Length, token);
                    if (count == 0) break;
                    if (output.Length + count > limit) throw new InvalidDataException("Model response exceeds the client's payload bound.");
                    output.Write(buffer, 0, count);
                }
                return output.ToArray();
            }
        }
        static Region Decode(byte[] bytes, ServerStatus status, Window requested)
        {
            if (bytes.Length < 20) throw new InvalidDataException("Truncated model region.");
            uint crc = 0xffffffff;
            for (int i = 0; i < bytes.Length - 4; i++)
            {
                crc ^= bytes[i];
                for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320);
            }
            uint expected = (uint)bytes[bytes.Length - 4] | (uint)bytes[bytes.Length - 3] << 8 | (uint)bytes[bytes.Length - 2] << 16 | (uint)bytes[bytes.Length - 1] << 24;
            if (~crc != expected) throw new InvalidDataException("Model region CRC mismatch.");
            using (var reader = new BinaryReader(new MemoryStream(bytes, false), Encoding.UTF8))
            {
                if (Encoding.ASCII.GetString(reader.ReadBytes(8)) != "ATDR0001") throw new InvalidDataException("Unsupported model region protocol.");
                uint length = reader.ReadUInt32();
                if (length < 2 || length > 65536 || length > bytes.Length - 16) throw new InvalidDataException("Invalid model metadata size.");
                Metadata meta = JsonUtility.FromJson<Metadata>(Encoding.UTF8.GetString(reader.ReadBytes((int)length)));
                if (meta == null || meta.window == null || meta.world_id != status.world_id || meta.spacing_m != status.spacing_m ||
                    meta.schema != "ashen-terrain-region-v1" || meta.height_units != "meters" || meta.backend != status.backend ||
                    meta.window.rows != requested.rows || meta.window.cols != requested.cols || meta.window.row0 != requested.row0 || meta.window.col0 != requested.col0 ||
                    meta.origin_x_m != requested.col0 * status.spacing_m || meta.origin_z_m != requested.row0 * status.spacing_m ||
                    meta.layout != "height_f32_planar,climate5_f32_planar,source_biome_u8,land_u8" || meta.climate_channels == null || meta.climate_channels.Length != 5)
                    throw new InvalidDataException("Model response metadata does not match the requested immutable world window.");
                string[] channels = { "temperature_c", "temperature_std_c", "precipitation_mm", "precipitation_cv_percent", "beta" };
                for (int i = 0; i < channels.Length; i++) if (meta.climate_channels[i] != channels[i]) throw new InvalidDataException("Model climate channel order is incompatible.");
                if (meta.climate_encoding != null && meta.climate_encoding != "upstream-native-v1" && meta.climate_encoding != "physical-si-v2")
                    throw new InvalidDataException("Unknown model climate encoding; explicit units are required.");
                string[] units = { "degC", meta.climate_encoding == "physical-si-v2" ? "degC" : "centi_degC", "mm/year", "percent", "degC/m" };
                if (meta.climate_value_units != null)
                {
                    if (meta.climate_value_units.Length != units.Length) throw new InvalidDataException("Incomplete model climate units.");
                    for (int i = 0; i < units.Length; i++) if (meta.climate_value_units[i] != units[i]) throw new InvalidDataException("Model climate encoding and declared units disagree.");
                }
                int count = checked(requested.rows * requested.cols);
                if (bytes.LongLength != 12 + length + count * 26L + 4) throw new InvalidDataException("Model region payload length is inconsistent.");
                var heights = new float[count];
                for (int i = 0; i < count; i++)
                {
                    heights[i] = reader.ReadSingle();
                    if (!Finite(heights[i]) || heights[i] < MinimumSupportedHeight || heights[i] > MaximumSupportedHeight)
                        throw new InvalidDataException("Model height leaves the conservative supported LOD range [-12000,48000] meters.");
                }
                var climate = new float[count * 5];
                for (int i = 0; i < climate.Length; i++)
                {
                    climate[i] = reader.ReadSingle();
                    if (!Finite(climate[i])) throw new InvalidDataException("Invalid model climate value.");
                    int channel = i / count;
                    double physical = climate[i];
                    if (channel == 1 && meta.climate_encoding != "physical-si-v2") physical *= .01;
                    bool valid = channel == 0 ? physical >= -150 && physical <= 100 :
                        channel == 1 ? physical >= 0 && physical <= 150 :
                        channel == 2 ? physical >= 0 && physical <= 100000 :
                        channel == 3 ? physical >= 0 && physical <= 10000 : Math.Abs(physical) <= .1;
                    if (!valid) throw new InvalidDataException("Model climate leaves the supported physical range after explicit unit conversion.");
                }
                byte[] biomes = reader.ReadBytes(count), land = reader.ReadBytes(count);
                for (int i = 0; i < count; i++) if (land[i] > 1) throw new InvalidDataException("Invalid model land mask.");
                return new Region { Metadata = meta, Heights = heights, Climate = climate, Biomes = biomes, Land = land };
            }
        }
        public SurfaceSample Sample(double xMeters, double zMeters)
        {
            lock (gate) foreach (Region region in regions)
            {
                if (!region.Contains(xMeters, zMeters)) continue;
                region.LastUse = ++uses;
                return SampleRegion(region, xMeters, zMeters, forestOverride);
            }
            return rough.Sample(xMeters, zMeters);
        }
        static SurfaceSample SampleRegion(Region region, double xMeters, double zMeters, bool forestOverride)
        {
                Metadata m = region.Metadata;
                double x = (xMeters - m.origin_x_m) / m.spacing_m, z = (zMeters - m.origin_z_m) / m.spacing_m;
                int ix = (int)Math.Floor(x), iz = (int)Math.Floor(z), xx = Math.Min(ix + 1, m.window.cols - 1), zz = Math.Min(iz + 1, m.window.rows - 1);
                double fx = x - ix, fz = z - iz;
                double h = (region.Heights[ix + iz * m.window.cols] * (1 - fx) + region.Heights[xx + iz * m.window.cols] * fx) * (1 - fz) +
                    (region.Heights[ix + zz * m.window.cols] * (1 - fx) + region.Heights[xx + zz * m.window.cols] * fx) * fz;
                int nearest = (int)Math.Round(x) + (int)Math.Round(z) * m.window.cols;
                byte biome = region.Biomes[nearest]; bool land = region.Land[nearest] != 0;
                return new SurfaceSample(h, biome, land && forestOverride ? SurfaceSample.ForestBiome : biome, land);
        }
        static SurfaceClimate ClimateRegion(Region region, double xMeters, double zMeters)
        {
            Metadata m = region.Metadata;
            double x = (xMeters - m.origin_x_m) / m.spacing_m, z = (zMeters - m.origin_z_m) / m.spacing_m;
            int ix = (int)Math.Floor(x), iz = (int)Math.Floor(z), xx = Math.Min(ix + 1, m.window.cols - 1), zz = Math.Min(iz + 1, m.window.rows - 1);
            double fx = x - ix, fz = z - iz; int count = m.window.rows * m.window.cols;
            double Channel(int channel)
            {
                int start = count * channel;
                return (region.Climate[start + ix + iz * m.window.cols] * (1 - fx) + region.Climate[start + xx + iz * m.window.cols] * fx) * (1 - fz) +
                    (region.Climate[start + ix + zz * m.window.cols] * (1 - fx) + region.Climate[start + xx + zz * m.window.cols] * fx) * fz;
            }
            // Original ATDR files labelled channel1 as Celsius while retaining
            // the upstream centi-C values. The declared encoding, never value
            // magnitude, chooses exactly one conversion. Surface temperature
            // channel0 has already applied the upstream altitude adjustment.
            double seasonality = Channel(1) * (region.Metadata.climate_encoding == "physical-si-v2" ? 1 : .01);
            return new SurfaceClimate(Channel(0), seasonality, Channel(2), Channel(3), Channel(4));
        }
        public bool TrySampleClimate(double xMeters, double zMeters, out SurfaceClimate climate)
        {
            lock (gate) foreach (Region region in regions)
            {
                if (!region.Contains(xMeters, zMeters)) continue;
                region.LastUse = ++uses; climate = ClimateRegion(region, xMeters, zMeters); return true;
            }
            if (rough is IClimateWorldField fallback) return fallback.TrySampleClimate(xMeters, zMeters, out climate);
            climate = default; return false;
        }
        sealed class Snapshot : IWorldField, IClimateWorldField
        {
            readonly Region region;
            readonly bool forest;
            public string SourceIdentity { get; }
            public Snapshot(Region region, bool forest) { this.region = region; this.forest = forest; SourceIdentity = "model-region/" + region.Metadata.world_id; }
            public SurfaceSample Sample(double xMeters, double zMeters)
            {
                if (!region.Contains(xMeters, zMeters)) throw new ArgumentOutOfRangeException("Point leaves the immutable prefetched model region.");
                return SampleRegion(region, xMeters, zMeters, forest);
            }
            public bool TrySampleClimate(double xMeters, double zMeters, out SurfaceClimate climate)
            {
                if (!region.Contains(xMeters, zMeters)) { climate = default; return false; }
                climate = ClimateRegion(region, xMeters, zMeters); return true;
            }
        }
        public IWorldField ReadySnapshot(double minX, double minZ, double maxX, double maxZ)
        {
            lock (gate) foreach (Region region in regions)
                if (region.Contains(minX, minZ) && region.Contains(maxX, maxZ))
                    return new Snapshot(region, forestOverride);
            throw new InvalidOperationException("The complete authoritative model region has not arrived.");
        }
        public HeightBounds QueryBounds(double minX, double minZ, double maxX, double maxZ)
        {
            // Unknown model interiors cannot be enclosed by a few fallback samples.
            // Keep conservative configured model bounds until a proper extrema pyramid
            // is available, so high mountains do not disappear from LOD selection.
            return new HeightBounds(MinimumSupportedHeight, MaximumSupportedHeight);
        }
        public void Dispose()
        {
            cancel.Cancel(); client.Dispose(); cancel.Dispose();
            lock (gate) { regions.Clear(); requests.Clear(); residentBytes = 0; }
        }
    }
}
