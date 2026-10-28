using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AshenVoxel.Exploration.Editor
{
    // Engine-free frozen-input reader. No Unity serializer or dynamic assembly metadata.
    public static class RootPreviewData
    {
        public const int MaximumManifestBytes = 65536;
        public const int MaximumLineCharacters = 4096;
        public const int MaximumEntries = 64;

        public readonly struct ManifestEntry
        {
            public string Path { get; }
            public string Sha256 { get; }
            internal ManifestEntry(string path, string sha256) { Path = path; Sha256 = sha256; }
        }

        public readonly struct Site
        {
            public int Id { get; }
            public double X { get; }
            public double Z { get; }
            public double OriginalY { get; }
            public double Yaw { get; }
            internal Site(int id, double x, double z, double y, double yaw)
            { Id = id; X = x; Z = z; OriginalY = y; Yaw = yaw; }
        }

        public static ReadOnlyCollection<ManifestEntry> ValidateManifest(string path, int expectedCount, string packPath)
        {
            if (expectedCount < 1 || expectedCount > MaximumEntries)
                throw new ArgumentOutOfRangeException(nameof(expectedCount));
            string pack = NormalizePath(packPath, false);
            var entries = new List<ManifestEntry>(expectedCount);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var stream = OpenRead(path))
            {
                if (stream.Length < 1 || stream.Length > MaximumManifestBytes)
                    throw new InvalidDataException("Manifest byte limit exceeded or empty manifest.");
                using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), false, 1024))
                {
                    string line;
                    while ((line = ReadBoundedLine(reader)) != null)
                    {
                        if (entries.Count == expectedCount) throw new InvalidDataException("Too many manifest rows.");
                        int tab = line.IndexOf('\t');
                        if (tab < 1 || line.IndexOf('\t', tab + 1) >= 0)
                            throw new InvalidDataException("Expected one path/hash TSV pair per row.");
                        string normalized = NormalizePath(line.Substring(0, tab), true);
                        string hash = line.Substring(tab + 1);
                        if (hash.Length != 64) throw new InvalidDataException("Expected 64 hexadecimal SHA256 characters.");
                        foreach (char c in hash)
                            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                                throw new InvalidDataException("Invalid SHA256 hexadecimal character.");
                        if (!paths.Add(normalized)) throw new InvalidDataException("Duplicate normalized manifest path: " + normalized);
                        entries.Add(new ManifestEntry(normalized, hash.ToLowerInvariant()));
                    }
                }
            }
            if (entries.Count != expectedCount) throw new InvalidDataException("Unexpected manifest row count.");
            if (!paths.Contains(pack)) throw new InvalidDataException("Opened pack is missing from manifest.");
            // Validate all metadata first, then stream file bytes without retaining entire assets.
            foreach (var entry in entries)
            {
                using (var sha = SHA256.Create())
                using (var stream = OpenRead(entry.Path))
                {
                    string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                    if (!String.Equals(actual, entry.Sha256, StringComparison.Ordinal))
                        throw new InvalidDataException("Frozen input changed: " + entry.Path);
                }
            }
            return entries.AsReadOnly();
        }

        public static ReadOnlyCollection<Site> ReadSites(string binaryPath)
        {
            using (var stream = OpenRead(binaryPath))
            using (var reader = new BinaryReader(stream))
            {
                const int bytes = 4 + 2 * (4 + 4 * 8);
                if (stream.Length != bytes) throw new InvalidDataException("Frozen sites must contain exactly 76 bytes.");
                if (reader.ReadInt32() != 2) throw new InvalidDataException("Expected exactly two frozen sites.");
                var sites = new Site[2];
                for (int i = 0; i < sites.Length; i++)
                {
                    int id = reader.ReadInt32();
                    double x = reader.ReadDouble(), z = reader.ReadDouble(), y = reader.ReadDouble(), yaw = reader.ReadDouble();
                    if (id != 10 + i) throw new InvalidDataException("Expected ordered site IDs 10 and 11.");
                    if (!Finite(x) || !Finite(z) || !Finite(y) || !Finite(yaw))
                        throw new InvalidDataException("Nonfinite frozen site coordinate.");
                    sites[i] = new Site(id, x, z, y, yaw);
                }
                if (reader.Read() != -1) throw new InvalidDataException("Trailing frozen site bytes.");
                return Array.AsReadOnly(sites);
            }
        }

        static bool Finite(double value) => !Double.IsNaN(value) && !Double.IsInfinity(value);
        static FileStream OpenRead(string path) => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
        static string NormalizePath(string path, bool requireAbsolute)
        {
            if (String.IsNullOrWhiteSpace(path) || path.Length > MaximumLineCharacters)
                throw new InvalidDataException("Missing or excessive input path.");
            foreach (char c in path) if (Char.IsControl(c)) throw new InvalidDataException("Control character in input path.");
            if (requireAbsolute && !System.IO.Path.IsPathFullyQualified(path))
                throw new InvalidDataException("Manifest paths must be fully qualified.");
            return System.IO.Path.GetFullPath(path);
        }
        static string ReadBoundedLine(StreamReader reader)
        {
            var line = new StringBuilder();
            while (true)
            {
                int next = reader.Read();
                if (next == -1) return line.Length == 0 ? null : line.ToString();
                if (next == '\n') return line.ToString();
                if (next == '\r')
                {
                    if (reader.Read() != '\n') throw new InvalidDataException("Expected LF or CRLF line ending.");
                    return line.ToString();
                }
                if (line.Length == MaximumLineCharacters) throw new InvalidDataException("Manifest line limit exceeded.");
                line.Append((char)next);
            }
        }
    }
}
