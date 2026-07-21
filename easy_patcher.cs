// Langrisser Dramatic Edition Korean easy patcher v0.13.17
//
// This file intentionally targets the conservative .NET Framework 4.x C#
// compiler included with Windows.  It has no third-party dependencies.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace LangrisserDramaticEasyPatcher
{
    internal static class PatchConstants
    {
        internal const string Version = "v0.13.17";
        internal const string PatchFileName = "langrisser_de_ko_v0.13.17.ldp";
        internal const string EmbeddedPatchName = "langrisser_de_ko_v0.13.17.ldp";
        internal const string PatchSha256 =
            "177345cbb1daa535b304dd30d5bfa1f771df84b6c28f5b7394e913feae5e8e71";
        internal const string OutputBaseName = "Langrisser_Dramatic_Edition_Korean_v0.13.17";
        internal const string OutputBinName = OutputBaseName + ".bin";
        internal const string OutputCueName = OutputBaseName + ".cue";
        internal const string ResultFileName = "easy-patcher-result.json";

        internal const string SourceSha256 =
            "1a9d479d3238bd1932fe2faee0c2b146c6333127a5b39d83e7d3d81a067505c1";
        internal const string PatchedMdfSha256 =
            "af158cb060b045b0342441e7632f09a67522c6b506a583c10b795b9767353c60";
        internal const string OutputBinSha256 =
            "2d74af6cb105ee7a52dbc7543abf8e3a2e5696e69117dd65d0608461a7b755d7";
        internal const string Mode1GapSha256 =
            "ab2480bf935e1bd21f6217aa7f689d1017ff9bee87a85c709f5457185c6ed1d8";
        internal const string Mode2GapSha256 =
            "d70194a7c37bd7044df7a83a42e6bde9e4e1bd89e5484112b74fe6262a43034c";

        internal const int MdfSectorSize = 2448;
        internal const int BinSectorSize = 2352;
        internal const int SourceSectors = 278863;
        internal const int OutputSectors = 279163;
        internal const int GapSectors = 150;
        internal const int Track2SourceSector = 167075;
        internal const int Track3SourceSector = 235445;
        internal const int Track2FileSector = 167225;
        internal const int Track3FileSector = 235745;
        internal const int ExpectedRecordCount = 70613;
        internal const long ExpectedReplacementBytes = 6673154L;
        internal const long ExpectedSourceSize = 682656624L;
        internal const long ExpectedOutputSize = 656591376L;
        internal const long MinimumFreeBytes = 800L * 1024L * 1024L;
        internal const ulong EndOffset = UInt64.MaxValue;
    }

    internal sealed class EasyPatchException : Exception
    {
        internal readonly int ExitCode;

        internal EasyPatchException(int exitCode, string message)
            : base(message)
        {
            ExitCode = exitCode;
        }

        internal EasyPatchException(int exitCode, string message, Exception inner)
            : base(message, inner)
        {
            ExitCode = exitCode;
        }
    }

    internal sealed class PatchRecord
    {
        internal long Offset;
        internal byte[] Data;
    }

    internal sealed class PatchPackage
    {
        internal List<PatchRecord> Records;
        internal string Description;
        internal int RecordCount;
        internal long ReplacementBytes;
    }

    internal sealed class PatchProgress
    {
        internal int Percent;
        internal string Message;

        internal PatchProgress(int percent, string message)
        {
            Percent = percent;
            Message = message;
        }
    }

    internal sealed class PatchResult
    {
        internal bool Success;
        internal int ExitCode;
        internal string Message;
        internal string SourcePath;
        internal string OutputDirectory;
        internal string OutputBinPath;
        internal string OutputCuePath;
        internal string PatchSource;
        internal string SourceSha256;
        internal string PatchedMdfSha256;
        internal string OutputBinSha256;
        internal long OutputBinSize;
        internal int RecordCount;
        internal long ReplacementBytes;
        internal bool AlreadyComplete;
        internal bool CueRepaired;
        internal DateTime StartedUtc;
        internal DateTime FinishedUtc;
    }

    internal static class HashTools
    {
        internal static string ToHex(byte[] bytes)
        {
            StringBuilder builder = new StringBuilder(bytes.Length * 2);
            int index;
            for (index = 0; index < bytes.Length; index++)
                builder.Append(bytes[index].ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        internal static string FileSha256(
            string path,
            Action<long, long> progress)
        {
            const int blockSize = 4 * 1024 * 1024;
            byte[] buffer = new byte[blockSize];
            long completed = 0;
            int read;
            using (FileStream input = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, blockSize,
                FileOptions.SequentialScan))
            using (SHA256 digest = SHA256.Create())
            {
                while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
                {
                    digest.TransformBlock(buffer, 0, read, buffer, 0);
                    completed += read;
                    if (progress != null)
                        progress(completed, input.Length);
                }
                digest.TransformFinalBlock(new byte[0], 0, 0);
                return ToHex(digest.Hash);
            }
        }

        internal static string StreamSha256(Stream input)
        {
            if (!input.CanSeek)
                throw new EasyPatchException(4, "패치 데이터 스트림을 검증할 수 없습니다.");
            long originalPosition = input.Position;
            byte[] buffer = new byte[1024 * 1024];
            int read;
            using (SHA256 digest = SHA256.Create())
            {
                while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
                    digest.TransformBlock(buffer, 0, read, buffer, 0);
                digest.TransformFinalBlock(new byte[0], 0, 0);
                input.Position = originalPosition;
                return ToHex(digest.Hash);
            }
        }
    }

    internal static class ManifestReader
    {
        private static Match OneMatch(string text, string field, string valuePattern)
        {
            string pattern = "\\\"" + Regex.Escape(field)
                + "\\\"\\s*:\\s*" + valuePattern;
            MatchCollection matches = Regex.Matches(
                text, pattern, RegexOptions.CultureInvariant);
            if (matches.Count != 1)
                throw new EasyPatchException(
                    4,
                    "패치 파일의 정보가 올바르지 않습니다. (" + field + ")");
            return matches[0];
        }

        internal static string StringValue(string text, string field)
        {
            Match match = OneMatch(text, field, "\\\"([^\\\"\\r\\n]*)\\\"");
            return match.Groups[1].Value;
        }

        internal static long IntegerValue(string text, string field)
        {
            Match match = OneMatch(text, field, "([0-9]+)");
            long value;
            if (!Int64.TryParse(
                match.Groups[1].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out value))
            {
                throw new EasyPatchException(
                    4,
                    "패치 파일의 숫자 정보가 올바르지 않습니다. (" + field + ")");
            }
            return value;
        }

        internal static bool BooleanValue(string text, string field)
        {
            Match match = OneMatch(text, field, "(true|false)");
            return String.Equals(
                match.Groups[1].Value, "true", StringComparison.Ordinal);
        }
    }

    internal static class PatchLoader
    {
        private const int MaximumManifestLength = 1024 * 1024;

        private static Stream EnsureSeekable(Stream input)
        {
            if (input.CanSeek)
                return input;
            try
            {
                MemoryStream copy = new MemoryStream();
                input.CopyTo(copy);
                input.Dispose();
                copy.Position = 0;
                return copy;
            }
            catch
            {
                input.Dispose();
                throw;
            }
        }

        private static Stream OpenPatch(out string description)
        {
            string externalPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                PatchConstants.PatchFileName);
            if (File.Exists(externalPath))
            {
                description = externalPath;
                try
                {
                    return new FileStream(
                        externalPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        1024 * 1024,
                        FileOptions.SequentialScan);
                }
                catch (Exception error)
                {
                    throw new EasyPatchException(
                        4,
                        "패치 파일을 열 수 없습니다.\r\n" + externalPath,
                        error);
                }
            }

            Assembly assembly = Assembly.GetExecutingAssembly();
            Stream embedded = assembly.GetManifestResourceStream(
                PatchConstants.EmbeddedPatchName);
            if (embedded == null)
            {
                string[] names = assembly.GetManifestResourceNames();
                int index;
                for (index = 0; index < names.Length; index++)
                {
                    if (names[index].EndsWith(
                        "." + PatchConstants.EmbeddedPatchName,
                        StringComparison.OrdinalIgnoreCase)
                        || String.Equals(
                            names[index],
                            PatchConstants.EmbeddedPatchName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        embedded = assembly.GetManifestResourceStream(names[index]);
                        break;
                    }
                }
            }
            if (embedded == null)
            {
                throw new EasyPatchException(
                    4,
                    "패치 데이터가 없습니다. 프로그램을 다시 내려받아 주세요.");
            }
            description = "EXE에 내장된 " + PatchConstants.PatchFileName;
            return embedded;
        }

        private static byte[] ReadExactly(BinaryReader reader, int length, string error)
        {
            byte[] data = reader.ReadBytes(length);
            if (data.Length != length)
                throw new EasyPatchException(4, error);
            return data;
        }

        private static void ValidateRecordRange(long offset, int length)
        {
            if (offset < 0 || length <= 0)
                throw new EasyPatchException(4, "패치 레코드의 범위가 올바르지 않습니다.");
            if (offset > PatchConstants.ExpectedSourceSize - length)
                throw new EasyPatchException(4, "패치 레코드가 원본 파일 범위를 벗어납니다.");

            long firstSector = offset / PatchConstants.MdfSectorSize;
            long lastByte = offset + length - 1L;
            long lastSector = lastByte / PatchConstants.MdfSectorSize;
            int firstInSector = (int)(offset % PatchConstants.MdfSectorSize);
            int lastInSector = (int)(lastByte % PatchConstants.MdfSectorSize);

            // A valid release record must fit wholly in one main-channel sector.
            // This simple rule also prevents a record from jumping over a 96-byte
            // subchannel area into the following sector.
            if (firstSector != lastSector
                || firstInSector >= PatchConstants.BinSectorSize
                || lastInSector >= PatchConstants.BinSectorSize)
            {
                throw new EasyPatchException(
                    4,
                    "안전을 위해 서브채널을 건드리는 패치 레코드를 거부했습니다.");
            }
            if (firstSector >= PatchConstants.Track2SourceSector)
            {
                throw new EasyPatchException(
                    4,
                    "안전을 위해 음성·음악 트랙을 건드리는 패치 레코드를 거부했습니다.");
            }
        }

        internal static PatchPackage Load()
        {
            string description;
            using (Stream stream = EnsureSeekable(OpenPatch(out description)))
            using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
            {
                string patchHash = HashTools.StreamSha256(stream);
                if (!String.Equals(
                    patchHash,
                    PatchConstants.PatchSha256,
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new EasyPatchException(
                        4,
                        "패치 데이터가 손상되었거나 다른 버전입니다. 프로그램을 다시 내려받아 주세요.");
                }
                byte[] magic = ReadExactly(reader, 4, "패치 파일 머리글이 잘렸습니다.");
                if (magic[0] != (byte)'L' || magic[1] != (byte)'D'
                    || magic[2] != (byte)'P' || magic[3] != (byte)'1')
                {
                    throw new EasyPatchException(4, "지원하지 않는 패치 파일입니다. (LDP1 필요)");
                }

                uint rawManifestLength;
                try
                {
                    rawManifestLength = reader.ReadUInt32();
                }
                catch (EndOfStreamException)
                {
                    throw new EasyPatchException(4, "패치 파일 머리글이 잘렸습니다.");
                }
                if (rawManifestLength == 0 || rawManifestLength > MaximumManifestLength)
                    throw new EasyPatchException(4, "패치 정보의 크기가 올바르지 않습니다.");

                byte[] manifestBytes = ReadExactly(
                    reader,
                    (int)rawManifestLength,
                    "패치 정보가 잘렸습니다.");
                string manifest;
                try
                {
                    UTF8Encoding strictUtf8 = new UTF8Encoding(false, true);
                    manifest = strictUtf8.GetString(manifestBytes);
                }
                catch (DecoderFallbackException)
                {
                    throw new EasyPatchException(4, "패치 정보가 올바른 UTF-8이 아닙니다.");
                }

                string format = ManifestReader.StringValue(manifest, "format");
                string version = ManifestReader.StringValue(manifest, "version");
                string sourceHash = ManifestReader.StringValue(manifest, "source_sha256");
                string targetHash = ManifestReader.StringValue(manifest, "target_sha256");
                long expectedSize = ManifestReader.IntegerValue(manifest, "expected_size");
                long expectedRecords = ManifestReader.IntegerValue(manifest, "record_count");
                long expectedReplacementBytes = ManifestReader.IntegerValue(
                    manifest, "replacement_bytes");
                bool containsDisc = ManifestReader.BooleanValue(
                    manifest, "contains_full_disc_image");

                if (!String.Equals(format, "LDP1", StringComparison.Ordinal)
                    || !String.Equals(version, PatchConstants.Version, StringComparison.Ordinal)
                    || !String.Equals(
                        sourceHash,
                        PatchConstants.SourceSha256,
                        StringComparison.OrdinalIgnoreCase)
                    || !String.Equals(
                        targetHash,
                        PatchConstants.PatchedMdfSha256,
                        StringComparison.OrdinalIgnoreCase)
                    || expectedSize != PatchConstants.ExpectedSourceSize
                    || expectedRecords != PatchConstants.ExpectedRecordCount
                    || expectedReplacementBytes != PatchConstants.ExpectedReplacementBytes
                    || containsDisc)
                {
                    throw new EasyPatchException(
                        4,
                        "이 프로그램용 v0.13.17 패치 파일이 아닙니다.");
                }
                if (expectedRecords < 0 || expectedRecords > 10000000L
                    || expectedReplacementBytes < 0
                    || expectedReplacementBytes > PatchConstants.ExpectedSourceSize)
                {
                    throw new EasyPatchException(4, "패치 레코드 수가 올바르지 않습니다.");
                }

                List<PatchRecord> records = new List<PatchRecord>(
                    (int)Math.Min(expectedRecords, 1000000L));
                long replacementBytes = 0;
                long previousEnd = -1;
                bool ended = false;
                while (true)
                {
                    ulong rawOffset;
                    uint rawLength;
                    try
                    {
                        rawOffset = reader.ReadUInt64();
                        rawLength = reader.ReadUInt32();
                    }
                    catch (EndOfStreamException)
                    {
                        throw new EasyPatchException(4, "패치 레코드가 잘렸습니다.");
                    }

                    if (rawOffset == PatchConstants.EndOffset && rawLength == 0)
                    {
                        ended = true;
                        break;
                    }
                    if (rawOffset > Int64.MaxValue || rawLength > Int32.MaxValue)
                        throw new EasyPatchException(4, "패치 레코드가 너무 큽니다.");

                    long offset = (long)rawOffset;
                    int length = (int)rawLength;
                    ValidateRecordRange(offset, length);
                    if (previousEnd >= 0 && offset < previousEnd)
                    {
                        throw new EasyPatchException(
                            4,
                            "패치 레코드가 정렬되지 않았거나 서로 겹칩니다.");
                    }
                    byte[] data = ReadExactly(reader, length, "패치 교체 데이터가 잘렸습니다.");
                    records.Add(new PatchRecord { Offset = offset, Data = data });
                    previousEnd = offset + length;
                    replacementBytes += length;
                    if (records.Count > expectedRecords
                        || replacementBytes > expectedReplacementBytes)
                    {
                        throw new EasyPatchException(4, "패치 정보와 실제 레코드가 다릅니다.");
                    }
                }

                if (!ended || records.Count != expectedRecords
                    || replacementBytes != expectedReplacementBytes)
                {
                    throw new EasyPatchException(4, "패치 manifest와 실제 레코드가 다릅니다.");
                }
                if (stream.ReadByte() != -1)
                    throw new EasyPatchException(4, "패치 끝에 알 수 없는 데이터가 있습니다.");

                return new PatchPackage
                {
                    Records = records,
                    Description = description,
                    RecordCount = records.Count,
                    ReplacementBytes = replacementBytes
                };
            }
        }
    }

    internal static class CdSectorBuilder
    {
        private static readonly byte[] Sync = new byte[]
        {
            0x00, 0xff, 0xff, 0xff, 0xff, 0xff,
            0xff, 0xff, 0xff, 0xff, 0xff, 0x00
        };
        private static readonly UInt32[] EdcLut = new UInt32[256];
        private static readonly byte[] EccForwardLut = new byte[256];
        private static readonly byte[] EccBackwardLut = new byte[256];

        static CdSectorBuilder()
        {
            int value;
            for (value = 0; value < 256; value++)
            {
                UInt32 edc = (UInt32)value;
                int bit;
                for (bit = 0; bit < 8; bit++)
                    edc = (edc >> 1) ^ (((edc & 1) != 0) ? 0xD8018001U : 0U);
                EdcLut[value] = edc;

                int forward = (value << 1) ^ (((value & 0x80) != 0) ? 0x11D : 0);
                EccForwardLut[value] = (byte)forward;
                EccBackwardLut[value ^ forward] = (byte)value;
            }
        }

        private static UInt32 ComputeEdc(byte[] data, int offset, int count)
        {
            UInt32 edc = 0;
            int end = offset + count;
            int index;
            for (index = offset; index < end; index++)
                edc = (edc >> 8) ^ EdcLut[(edc ^ data[index]) & 0xff];
            return edc;
        }

        private static byte[] ComputeEcc(
            byte[] source,
            int sourceOffset,
            int majorCount,
            int minorCount,
            int majorMult,
            int minorInc)
        {
            int size = majorCount * minorCount;
            byte[] result = new byte[majorCount * 2];
            int major;
            for (major = 0; major < majorCount; major++)
            {
                int index = (major >> 1) * majorMult + (major & 1);
                int eccA = 0;
                int eccB = 0;
                int minor;
                for (minor = 0; minor < minorCount; minor++)
                {
                    int current = source[sourceOffset + index];
                    index += minorInc;
                    if (index >= size)
                        index -= size;
                    eccA ^= current;
                    eccB ^= current;
                    eccA = EccForwardLut[eccA];
                }
                eccA = EccBackwardLut[EccForwardLut[eccA] ^ eccB];
                result[major] = (byte)eccA;
                result[major + majorCount] = (byte)(eccA ^ eccB);
            }
            return result;
        }

        private static byte Bcd(int value)
        {
            return (byte)(((value / 10) << 4) | (value % 10));
        }

        private static void PutAddress(byte[] sector, int fileSector, byte mode)
        {
            int fad = fileSector + 150;
            int minute = fad / (75 * 60);
            int remainder = fad % (75 * 60);
            int second = remainder / 75;
            int frame = remainder % 75;
            Buffer.BlockCopy(Sync, 0, sector, 0, Sync.Length);
            sector[12] = Bcd(minute);
            sector[13] = Bcd(second);
            sector[14] = Bcd(frame);
            sector[15] = mode;
        }

        internal static byte[] EmptyMode1Sector(int fileSector)
        {
            byte[] sector = new byte[PatchConstants.BinSectorSize];
            PutAddress(sector, fileSector, 1);
            UInt32 edc = ComputeEdc(sector, 0, 2064);
            sector[2064] = (byte)edc;
            sector[2065] = (byte)(edc >> 8);
            sector[2066] = (byte)(edc >> 16);
            sector[2067] = (byte)(edc >> 24);
            // 2068..2075 are already zero.
            byte[] p = ComputeEcc(sector, 12, 86, 24, 2, 86);
            Buffer.BlockCopy(p, 0, sector, 2076, p.Length);
            byte[] q = ComputeEcc(sector, 12, 52, 43, 86, 88);
            Buffer.BlockCopy(q, 0, sector, 2248, q.Length);
            return sector;
        }

        internal static byte[] EmptyMode2Sector(int fileSector)
        {
            byte[] sector = new byte[PatchConstants.BinSectorSize];
            PutAddress(sector, fileSector, 2);
            return sector;
        }
    }

    internal static class CueWriter
    {
        internal static string Text()
        {
            return
                "FILE \"" + PatchConstants.OutputBinName + "\" BINARY\n"
                + "  TRACK 01 MODE1/2352\n"
                + "    INDEX 01 00:00:00\n"
                + "  TRACK 02 MODE2/2352\n"
                + "    INDEX 01 37:09:50\n"
                + "  TRACK 03 AUDIO\n"
                + "    INDEX 01 52:23:20\n"
                + "  TRACK 04 AUDIO\n"
                + "    INDEX 01 53:20:00\n"
                + "  TRACK 05 AUDIO\n"
                + "    INDEX 01 54:56:58\n"
                + "  TRACK 06 AUDIO\n"
                + "    INDEX 01 61:54:13\n";
        }
    }

    internal static class EasyPatcher
    {
        private static void Report(
            Action<PatchProgress> callback,
            int percent,
            string message)
        {
            if (callback != null)
                callback(new PatchProgress(percent, message));
        }

        internal static string ResolveSourcePath(string selectedPath)
        {
            if (String.IsNullOrWhiteSpace(selectedPath))
                return selectedPath;
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(selectedPath);
            }
            catch (Exception error)
            {
                throw new EasyPatchException(3, "선택한 파일 경로가 올바르지 않습니다.", error);
            }

            string extension = Path.GetExtension(fullPath);
            if (String.Equals(extension, ".mdf", StringComparison.OrdinalIgnoreCase))
                return fullPath;
            if (String.Equals(extension, ".mds", StringComparison.OrdinalIgnoreCase)
                || String.Equals(extension, ".cue", StringComparison.OrdinalIgnoreCase)
                || String.Equals(extension, ".bin", StringComparison.OrdinalIgnoreCase))
            {
                string sibling = Path.ChangeExtension(fullPath, ".mdf");
                if (File.Exists(sibling))
                    return sibling;
                throw new EasyPatchException(
                    3,
                    "BIN/CUE/MDS 자체는 패치할 수 없습니다.\r\n"
                    + "음성과 CD 음악을 안전하게 보존하려면 같은 이름의 일본판 원본 MDF를 선택해 주세요.");
            }
            throw new EasyPatchException(
                3,
                "BIN/CUE가 아니라 일본판 원본 MDF 파일을 선택해 주세요.");
        }

        private static void CheckSourceFile(string sourcePath)
        {
            if (String.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                throw new EasyPatchException(
                    3,
                    "원본 MDF 파일을 찾을 수 없습니다.\r\n"
                    + "정품 디스크에서 추출한 langDramaticEdition.mdf를 선택해 주세요.");
            }
            FileInfo info = new FileInfo(sourcePath);
            if (info.Length != PatchConstants.ExpectedSourceSize)
            {
                throw new EasyPatchException(
                    3,
                    "선택한 파일은 지원되는 원본 MDF가 아닙니다.\r\n"
                    + "파일 크기가 다릅니다. 다른 MDF를 선택해 주세요.");
            }
        }

        private static void CheckOutputDirectory(
            string sourcePath,
            string outputDirectory,
            out string outputBin,
            out string outputCue,
            out string partialBin)
        {
            if (String.IsNullOrWhiteSpace(outputDirectory))
                throw new EasyPatchException(5, "저장할 폴더를 선택해 주세요.");
            try
            {
                Directory.CreateDirectory(outputDirectory);
                outputDirectory = Path.GetFullPath(outputDirectory);
            }
            catch (Exception error)
            {
                throw new EasyPatchException(
                    5,
                    "저장할 폴더를 만들 수 없습니다. 다른 폴더를 선택해 주세요.",
                    error);
            }

            outputBin = Path.Combine(outputDirectory, PatchConstants.OutputBinName);
            outputCue = Path.Combine(outputDirectory, PatchConstants.OutputCueName);
            partialBin = Path.Combine(
                outputDirectory,
                "." + PatchConstants.OutputBinName + "."
                + Guid.NewGuid().ToString("N") + ".partial");
            if (String.Equals(
                Path.GetFullPath(sourcePath),
                Path.GetFullPath(outputBin),
                StringComparison.OrdinalIgnoreCase))
            {
                throw new EasyPatchException(5, "원본 파일 위에는 저장할 수 없습니다.");
            }
            if (!File.Exists(outputBin) && File.Exists(outputCue))
            {
                throw new EasyPatchException(
                    5,
                    "같은 이름의 CUE만 있고 BIN이 없습니다.\r\n"
                    + "그 CUE를 다른 곳으로 옮기거나 삭제한 뒤 다시 눌러 주세요.");
            }
            if (File.Exists(outputBin)
                && new FileInfo(outputBin).Length != PatchConstants.ExpectedOutputSize)
            {
                throw new EasyPatchException(
                    5,
                    "저장 폴더에 같은 이름의 다른 BIN이 있습니다.\r\n"
                    + "안전을 위해 덮어쓰지 않았습니다. 다른 폴더를 선택해 주세요.");
            }

            try
            {
                string root = Path.GetPathRoot(outputDirectory);
                if (!String.IsNullOrEmpty(root))
                {
                    DriveInfo drive = new DriveInfo(root);
                    if (!File.Exists(outputBin)
                        && drive.IsReady
                        && drive.AvailableFreeSpace < PatchConstants.MinimumFreeBytes)
                    {
                        throw new EasyPatchException(
                            5,
                            "저장 공간이 부족합니다. 약 800MB의 빈 공간이 필요합니다.");
                    }
                }
            }
            catch (EasyPatchException)
            {
                throw;
            }
            catch
            {
                // Some network paths do not expose free-space information.  The
                // actual create/write operation will still fail safely if full.
            }
        }

        private static void HashAndWrite(
            FileStream output,
            HashAlgorithm outputHash,
            byte[] data,
            int offset,
            int length)
        {
            output.Write(data, offset, length);
            outputHash.TransformBlock(data, offset, length, data, offset);
        }

        private static string FinishHash(HashAlgorithm hash)
        {
            hash.TransformFinalBlock(new byte[0], 0, 0);
            return HashTools.ToHex(hash.Hash);
        }

        private static bool CueIsExact(string outputCue)
        {
            if (!File.Exists(outputCue))
                return false;
            try
            {
                byte[] actual = File.ReadAllBytes(outputCue);
                byte[] expected = new ASCIIEncoding().GetBytes(CueWriter.Text());
                if (actual.Length != expected.Length)
                    return false;
                int index;
                for (index = 0; index < actual.Length; index++)
                {
                    if (actual[index] != expected[index])
                        return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void WriteCueSafely(string outputCue)
        {
            string temporary = Path.Combine(
                Path.GetDirectoryName(outputCue),
                "." + Path.GetFileName(outputCue) + "."
                + Guid.NewGuid().ToString("N") + ".partial");
            bool ownsTemporary = false;
            try
            {
                using (FileStream stream = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None))
                using (StreamWriter writer = new StreamWriter(stream, new ASCIIEncoding()))
                {
                    ownsTemporary = true;
                    writer.Write(CueWriter.Text());
                }
                if (File.Exists(outputCue))
                    File.Replace(temporary, outputCue, null);
                else
                    File.Move(temporary, outputCue);
                ownsTemporary = false;
            }
            catch
            {
                try
                {
                    if (ownsTemporary && File.Exists(temporary))
                        File.Delete(temporary);
                }
                catch
                {
                }
                throw;
            }
        }

        private static bool ReuseCompletedOutput(
            string outputBin,
            string outputCue,
            PatchResult result,
            Action<PatchProgress> progress)
        {
            if (!File.Exists(outputBin))
            {
                if (File.Exists(outputCue))
                {
                    throw new EasyPatchException(
                        5,
                        "같은 이름의 CUE만 있고 BIN이 없습니다.\r\n"
                        + "그 CUE를 다른 곳으로 옮기거나 삭제한 뒤 다시 눌러 주세요.");
                }
                return false;
            }

            FileInfo info = new FileInfo(outputBin);
            if (info.Length != PatchConstants.ExpectedOutputSize)
            {
                throw new EasyPatchException(
                    5,
                    "저장 폴더에 같은 이름의 다른 BIN이 있습니다.\r\n"
                    + "안전을 위해 덮어쓰지 않았습니다. 다른 폴더를 선택해 주세요.");
            }
            Report(progress, 20, "기존 완성 BIN을 확인하는 중...");
            string hash = HashTools.FileSha256(outputBin, null);
            if (!String.Equals(
                hash,
                PatchConstants.OutputBinSha256,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new EasyPatchException(
                    5,
                    "저장 폴더에 같은 이름의 다른 BIN이 있습니다.\r\n"
                    + "안전을 위해 덮어쓰지 않았습니다. 다른 폴더를 선택해 주세요.");
            }

            bool repaired = !CueIsExact(outputCue);
            if (repaired)
            {
                Report(progress, 96, "CUE 파일을 복구하는 중...");
                WriteCueSafely(outputCue);
            }
            result.Success = true;
            result.ExitCode = 0;
            result.AlreadyComplete = true;
            result.CueRepaired = repaired;
            result.Message = repaired
                ? "완성 BIN은 정상이며 CUE를 다시 만들었습니다."
                : "이미 정상적으로 완성되어 있습니다.";
            result.OutputBinSha256 = hash;
            result.PatchedMdfSha256 = PatchConstants.PatchedMdfSha256;
            result.OutputBinSize = info.Length;
            result.FinishedUtc = DateTime.UtcNow;
            Report(progress, 100, result.Message);
            return true;
        }

        private static void BuildBin(
            string sourcePath,
            string partialBin,
            PatchPackage package,
            Action<PatchProgress> progress,
            ref bool ownsPartialBin,
            out string patchedMdfHash,
            out string outputBinHash)
        {
            byte[] sector = new byte[PatchConstants.MdfSectorSize];
            byte[] empty = new byte[0];
            int recordIndex = 0;
            int outputSector = 0;

            using (FileStream source = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 1024,
                FileOptions.SequentialScan))
            using (FileStream output = new FileStream(
                partialBin,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                1024 * 1024,
                FileOptions.SequentialScan))
            {
                ownsPartialBin = true;
                using (SHA256 virtualMdfDigest = SHA256.Create())
                using (SHA256 outputDigest = SHA256.Create())
                using (SHA256 mode1Digest = SHA256.Create())
                using (SHA256 mode2Digest = SHA256.Create())
                {
                int sourceSector;
                for (sourceSector = 0;
                    sourceSector < PatchConstants.SourceSectors;
                    sourceSector++)
                {
                    if (sourceSector == PatchConstants.Track2SourceSector)
                    {
                        if (outputSector != PatchConstants.Track2SourceSector)
                            throw new EasyPatchException(6, "트랙 2 pregap 위치 검증에 실패했습니다.");
                        int gap;
                        for (gap = 0; gap < PatchConstants.GapSectors; gap++)
                        {
                            byte[] generated = CdSectorBuilder.EmptyMode1Sector(outputSector);
                            HashAndWrite(output, outputDigest, generated, 0, generated.Length);
                            mode1Digest.TransformBlock(
                                generated, 0, generated.Length, generated, 0);
                            outputSector++;
                        }
                        if (outputSector != PatchConstants.Track2FileSector)
                            throw new EasyPatchException(6, "트랙 2 물리 위치 검증에 실패했습니다.");
                    }

                    if (sourceSector == PatchConstants.Track3SourceSector)
                    {
                        if (outputSector
                            != PatchConstants.Track3FileSector - PatchConstants.GapSectors)
                        {
                            throw new EasyPatchException(6, "트랙 3 pregap 위치 검증에 실패했습니다.");
                        }
                        int gap;
                        for (gap = 0; gap < PatchConstants.GapSectors; gap++)
                        {
                            byte[] generated = CdSectorBuilder.EmptyMode2Sector(outputSector);
                            HashAndWrite(output, outputDigest, generated, 0, generated.Length);
                            mode2Digest.TransformBlock(
                                generated, 0, generated.Length, generated, 0);
                            outputSector++;
                        }
                        if (outputSector != PatchConstants.Track3FileSector)
                            throw new EasyPatchException(6, "트랙 3 물리 위치 검증에 실패했습니다.");
                    }

                    int received = 0;
                    while (received < sector.Length)
                    {
                        int count = source.Read(sector, received, sector.Length - received);
                        if (count == 0)
                        {
                            throw new EasyPatchException(
                                3,
                                "원본 MDF를 읽는 중 파일이 예상보다 일찍 끝났습니다.");
                        }
                        received += count;
                    }

                    long sectorStart = (long)sourceSector * PatchConstants.MdfSectorSize;
                    long sectorEnd = sectorStart + PatchConstants.MdfSectorSize;
                    while (recordIndex < package.Records.Count
                        && package.Records[recordIndex].Offset < sectorEnd)
                    {
                        PatchRecord record = package.Records[recordIndex];
                        if (record.Offset < sectorStart)
                            throw new EasyPatchException(4, "패치 레코드 적용 순서가 올바르지 않습니다.");
                        int relative = (int)(record.Offset - sectorStart);
                        Buffer.BlockCopy(
                            record.Data, 0, sector, relative, record.Data.Length);
                        recordIndex++;
                    }

                    virtualMdfDigest.TransformBlock(
                        sector, 0, sector.Length, sector, 0);
                    HashAndWrite(
                        output,
                        outputDigest,
                        sector,
                        0,
                        PatchConstants.BinSectorSize);
                    outputSector++;

                    if ((sourceSector & 0x1ff) == 0)
                    {
                        int percent = 25 + (int)(
                            70L * sourceSector / PatchConstants.SourceSectors);
                        Report(
                            progress,
                            percent,
                            "한글판 BIN을 만드는 중... "
                            + (sourceSector * 100 / PatchConstants.SourceSectors)
                                .ToString(CultureInfo.InvariantCulture)
                            + "%");
                    }
                }

                if (source.ReadByte() != -1)
                    throw new EasyPatchException(3, "원본 MDF 뒤에 알 수 없는 데이터가 있습니다.");
                if (recordIndex != package.Records.Count)
                    throw new EasyPatchException(4, "일부 패치 레코드를 적용하지 못했습니다.");
                if (outputSector != PatchConstants.OutputSectors)
                    throw new EasyPatchException(6, "완성 BIN의 섹터 수가 올바르지 않습니다.");

                virtualMdfDigest.TransformFinalBlock(empty, 0, 0);
                outputDigest.TransformFinalBlock(empty, 0, 0);
                mode1Digest.TransformFinalBlock(empty, 0, 0);
                mode2Digest.TransformFinalBlock(empty, 0, 0);

                patchedMdfHash = HashTools.ToHex(virtualMdfDigest.Hash);
                outputBinHash = HashTools.ToHex(outputDigest.Hash);
                string mode1Hash = HashTools.ToHex(mode1Digest.Hash);
                string mode2Hash = HashTools.ToHex(mode2Digest.Hash);
                if (!String.Equals(
                    mode1Hash,
                    PatchConstants.Mode1GapSha256,
                    StringComparison.OrdinalIgnoreCase)
                    || !String.Equals(
                        mode2Hash,
                        PatchConstants.Mode2GapSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new EasyPatchException(6, "CD pregap 생성 검증에 실패했습니다.");
                }
                }
            }
        }

        internal static PatchResult Run(
            string sourcePath,
            string outputDirectory,
            Action<PatchProgress> progress)
        {
            sourcePath = ResolveSourcePath(sourcePath);
            PatchResult result = new PatchResult();
            result.StartedUtc = DateTime.UtcNow;
            result.SourcePath = sourcePath == null ? null : Path.GetFullPath(sourcePath);
            result.OutputDirectory = outputDirectory == null
                ? null : Path.GetFullPath(outputDirectory);

            string outputBin = null;
            string outputCue = null;
            string partialBin = null;
            bool ownsPartialBin = false;
            CheckSourceFile(sourcePath);

            try
            {
                Report(progress, 1, "원본 MDF를 확인하는 중...");
                string sourceHash = HashTools.FileSha256(
                    sourcePath,
                    delegate(long completed, long total)
                    {
                        int percent = 1 + (int)(18L * completed / total);
                        Report(progress, percent, "원본 MDF를 확인하는 중...");
                    });
                result.SourceSha256 = sourceHash;
                if (!String.Equals(
                    sourceHash,
                    PatchConstants.SourceSha256,
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new EasyPatchException(
                        3,
                        "선택한 MDF는 지원되는 일본판 원본이 아닙니다.\r\n"
                        + "이미 패치된 파일이거나 다른 버전일 수 있습니다.");
                }

                // Only a fully verified retail source may cause any output-side
                // directory creation, existing-output inspection, or temp file.
                CheckOutputDirectory(
                    sourcePath,
                    outputDirectory,
                    out outputBin,
                    out outputCue,
                    out partialBin);
                result.OutputBinPath = outputBin;
                result.OutputCuePath = outputCue;

                Report(progress, 20, "내장 패치 데이터를 확인하는 중...");
                PatchPackage package = PatchLoader.Load();
                result.PatchSource = package.Description;
                result.RecordCount = package.RecordCount;
                result.ReplacementBytes = package.ReplacementBytes;

                if (ReuseCompletedOutput(
                    outputBin, outputCue, result, progress))
                {
                    return result;
                }

                Report(progress, 25, "한글판 BIN을 만드는 중...");
                string virtualHash;
                string binHash;
                BuildBin(
                    sourcePath,
                    partialBin,
                    package,
                    progress,
                    ref ownsPartialBin,
                    out virtualHash,
                    out binHash);
                result.PatchedMdfSha256 = virtualHash;
                result.OutputBinSha256 = binHash;

                Report(progress, 96, "완성 파일을 마지막으로 확인하는 중...");
                FileInfo partialInfo = new FileInfo(partialBin);
                if (partialInfo.Length != PatchConstants.ExpectedOutputSize)
                {
                    throw new EasyPatchException(6, "완성 BIN의 파일 크기가 올바르지 않습니다.");
                }
                if (!String.Equals(
                    virtualHash,
                    PatchConstants.PatchedMdfSha256,
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new EasyPatchException(6, "가상 패치 MDF의 SHA-256 검증에 실패했습니다.");
                }
                if (!String.Equals(
                    binHash,
                    PatchConstants.OutputBinSha256,
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new EasyPatchException(6, "완성 BIN의 SHA-256 검증에 실패했습니다.");
                }

                File.Move(partialBin, outputBin);
                ownsPartialBin = false;
                try
                {
                    WriteCueSafely(outputCue);
                }
                catch
                {
                    if (File.Exists(outputBin))
                        File.Delete(outputBin);
                    throw;
                }

                result.Success = true;
                result.ExitCode = 0;
                result.Message = "완료! CUE 파일을 에뮬레이터에서 열어 주세요.";
                result.OutputBinSize = new FileInfo(outputBin).Length;
                result.FinishedUtc = DateTime.UtcNow;
                Report(progress, 100, result.Message);
                return result;
            }
            catch
            {
                try
                {
                    if (ownsPartialBin
                        && !String.IsNullOrEmpty(partialBin)
                        && File.Exists(partialBin))
                        File.Delete(partialBin);
                }
                catch
                {
                }
                throw;
            }
        }
    }

    internal static class ResultJson
    {
        private static string Escape(string value)
        {
            if (value == null)
                return "null";
            StringBuilder builder = new StringBuilder(value.Length + 8);
            builder.Append('"');
            int index;
            for (index = 0; index < value.Length; index++)
            {
                char c = value[index];
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            builder.Append("\\u" + ((int)c).ToString("x4"));
                        else
                            builder.Append(c);
                        break;
                }
            }
            builder.Append('"');
            return builder.ToString();
        }

        internal static void Write(string directory, PatchResult result)
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, PatchConstants.ResultFileName);
            StringBuilder json = new StringBuilder();
            json.Append("{\n");
            json.Append("  \"success\": ").Append(result.Success ? "true" : "false").Append(",\n");
            json.Append("  \"exit_code\": ").Append(result.ExitCode).Append(",\n");
            json.Append("  \"message\": ").Append(Escape(result.Message)).Append(",\n");
            json.Append("  \"version\": ").Append(Escape(PatchConstants.Version)).Append(",\n");
            json.Append("  \"source_path\": ").Append(Escape(result.SourcePath)).Append(",\n");
            json.Append("  \"output_directory\": ").Append(Escape(result.OutputDirectory)).Append(",\n");
            json.Append("  \"output_bin\": ").Append(Escape(result.OutputBinPath)).Append(",\n");
            json.Append("  \"output_cue\": ").Append(Escape(result.OutputCuePath)).Append(",\n");
            json.Append("  \"patch_source\": ").Append(Escape(result.PatchSource)).Append(",\n");
            json.Append("  \"source_sha256\": ").Append(Escape(result.SourceSha256)).Append(",\n");
            json.Append("  \"patched_virtual_mdf_sha256\": ").Append(Escape(result.PatchedMdfSha256)).Append(",\n");
            json.Append("  \"output_bin_sha256\": ").Append(Escape(result.OutputBinSha256)).Append(",\n");
            json.Append("  \"output_bin_size\": ").Append(result.OutputBinSize).Append(",\n");
            json.Append("  \"record_count\": ").Append(result.RecordCount).Append(",\n");
            json.Append("  \"replacement_bytes\": ").Append(result.ReplacementBytes).Append(",\n");
            json.Append("  \"already_complete\": ").Append(
                result.AlreadyComplete ? "true" : "false").Append(",\n");
            json.Append("  \"cue_repaired\": ").Append(
                result.CueRepaired ? "true" : "false").Append(",\n");
            json.Append("  \"started_utc\": ").Append(Escape(
                result.StartedUtc.ToString("o", CultureInfo.InvariantCulture))).Append(",\n");
            json.Append("  \"finished_utc\": ").Append(Escape(
                result.FinishedUtc.ToString("o", CultureInfo.InvariantCulture))).Append("\n");
            json.Append("}\n");
            File.WriteAllText(path, json.ToString(), new UTF8Encoding(false));
        }
    }

    internal static class OriginalFinder
    {
        private static void AddDirectory(
            List<KeyValuePair<string, int>> roots,
            HashSet<string> seen,
            string path,
            int depth)
        {
            if (String.IsNullOrEmpty(path))
                return;
            try
            {
                string full = Path.GetFullPath(path);
                if (Directory.Exists(full) && seen.Add(full))
                    roots.Add(new KeyValuePair<string, int>(full, depth));
            }
            catch
            {
            }
        }

        private static IEnumerable<string> CandidateFiles(
            string root,
            int depth,
            HashSet<string> visited)
        {
            string full;
            try
            {
                full = Path.GetFullPath(root);
            }
            catch
            {
                yield break;
            }
            if (!visited.Add(full))
                yield break;

            string preferred = Path.Combine(full, "langDramaticEdition.mdf");
            if (File.Exists(preferred))
                yield return preferred;

            string[] files;
            try
            {
                files = Directory.GetFiles(full, "*.mdf", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                files = new string[0];
            }
            int index;
            for (index = 0; index < files.Length; index++)
            {
                if (!String.Equals(files[index], preferred, StringComparison.OrdinalIgnoreCase))
                    yield return files[index];
            }
            if (depth <= 0)
                yield break;

            string[] directories;
            try
            {
                directories = Directory.GetDirectories(full);
            }
            catch
            {
                directories = new string[0];
            }
            for (index = 0; index < directories.Length; index++)
            {
                string name = Path.GetFileName(directories[index]);
                if (name.StartsWith("$", StringComparison.Ordinal)
                    || String.Equals(name, "System Volume Information", StringComparison.OrdinalIgnoreCase)
                    || String.Equals(name, "Windows", StringComparison.OrdinalIgnoreCase)
                    || String.Equals(name, "Program Files", StringComparison.OrdinalIgnoreCase)
                    || String.Equals(name, "Program Files (x86)", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                foreach (string file in CandidateFiles(
                    directories[index], depth - 1, visited))
                {
                    yield return file;
                }
            }
        }

        internal static string Find(Action<string> status)
        {
            List<KeyValuePair<string, int>> roots = new List<KeyValuePair<string, int>>();
            HashSet<string> rootSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Keep automatic discovery deliberately shallow so the window never
            // appears to hang while walking an entire disk.  A user can always
            // choose or drag the file from any other location.
            AddDirectory(roots, rootSeen, AppDomain.CurrentDomain.BaseDirectory, 1);
            AddDirectory(roots, rootSeen, Environment.CurrentDirectory, 1);
            AddDirectory(roots, rootSeen, Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory), 1);
            AddDirectory(roots, rootSeen, Environment.GetFolderPath(
                Environment.SpecialFolder.MyDocuments), 1);
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            AddDirectory(roots, rootSeen, Path.Combine(profile, "Downloads"), 1);

            HashSet<string> visitedDirectories = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> checkedFiles = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            int index;
            for (index = 0; index < roots.Count; index++)
            {
                if (status != null)
                    status("원본을 찾는 중... " + roots[index].Key);
                foreach (string candidate in CandidateFiles(
                    roots[index].Key, roots[index].Value, visitedDirectories))
                {
                    if (!checkedFiles.Add(candidate))
                        continue;
                    try
                    {
                        FileInfo info = new FileInfo(candidate);
                        if (info.Length != PatchConstants.ExpectedSourceSize)
                            continue;
                        if (status != null)
                            status("원본 후보를 확인하는 중... " + candidate);
                        string hash = HashTools.FileSha256(candidate, null);
                        if (String.Equals(
                            hash,
                            PatchConstants.SourceSha256,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            return candidate;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            return null;
        }
    }

    internal sealed class MainForm : Form
    {
        private TextBox sourceBox;
        private TextBox outputBox;
        private Button sourceButton;
        private Button findButton;
        private Button outputButton;
        private Button startButton;
        private ProgressBar progressBar;
        private Label statusLabel;
        private Label dropLabel;
        private volatile bool busy;
        private volatile bool patching;
        private string initialSelectionError;

        internal MainForm(string initialSource)
        {
            Text = "랑그릿사 드라마틱 에디션 한글패치 " + PatchConstants.Version;
            ClientSize = new Size(660, 390);
            MinimumSize = new Size(680, 430);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("맑은 고딕", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            AllowDrop = true;

            Label title = new Label();
            title.Text = "랑그릿사 드라마틱 에디션 한글패치";
            title.Font = new Font(Font.FontFamily, 17f, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(24, 20);
            Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "원본 MDF만 고르면 나머지는 자동입니다.";
            subtitle.AutoSize = true;
            subtitle.Location = new Point(27, 60);
            Controls.Add(subtitle);

            Label step1 = new Label();
            step1.Text = "1. 일본판 원본 MDF";
            step1.AutoSize = true;
            step1.Font = new Font(Font, FontStyle.Bold);
            step1.Location = new Point(27, 96);
            Controls.Add(step1);

            sourceBox = new TextBox();
            sourceBox.Location = new Point(30, 120);
            sourceBox.Size = new Size(432, 26);
            sourceBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(sourceBox);

            sourceButton = new Button();
            sourceButton.Text = "파일 선택";
            sourceButton.Location = new Point(472, 117);
            sourceButton.Size = new Size(82, 31);
            sourceButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            sourceButton.Click += delegate { ChooseSource(); };
            Controls.Add(sourceButton);

            findButton = new Button();
            findButton.Text = "자동 찾기";
            findButton.Location = new Point(562, 117);
            findButton.Size = new Size(74, 31);
            findButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            findButton.Click += delegate { StartAutoFind(); };
            Controls.Add(findButton);

            dropLabel = new Label();
            dropLabel.Text = "MDF 파일을 이 창에 끌어다 놓아도 됩니다.";
            dropLabel.ForeColor = Color.DimGray;
            dropLabel.AutoSize = true;
            dropLabel.Location = new Point(30, 153);
            Controls.Add(dropLabel);

            Label step2 = new Label();
            step2.Text = "2. 완성 파일을 저장할 폴더";
            step2.AutoSize = true;
            step2.Font = new Font(Font, FontStyle.Bold);
            step2.Location = new Point(27, 184);
            Controls.Add(step2);

            outputBox = new TextBox();
            outputBox.Location = new Point(30, 208);
            outputBox.Size = new Size(524, 26);
            outputBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(outputBox);

            outputButton = new Button();
            outputButton.Text = "폴더 선택";
            outputButton.Location = new Point(562, 205);
            outputButton.Size = new Size(74, 31);
            outputButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            outputButton.Click += delegate { ChooseOutput(); };
            Controls.Add(outputButton);

            startButton = new Button();
            startButton.Text = "3. 한글판 만들기";
            startButton.Font = new Font(Font.FontFamily, 12f, FontStyle.Bold);
            startButton.Location = new Point(30, 255);
            startButton.Size = new Size(606, 48);
            startButton.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            startButton.Click += delegate { StartPatch(); };
            Controls.Add(startButton);

            progressBar = new ProgressBar();
            progressBar.Location = new Point(30, 318);
            progressBar.Size = new Size(606, 20);
            progressBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(progressBar);

            statusLabel = new Label();
            statusLabel.Text = "준비되었습니다.";
            statusLabel.AutoEllipsis = true;
            statusLabel.Location = new Point(30, 347);
            statusLabel.Size = new Size(606, 28);
            statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(statusLabel);

            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            FormClosing += OnFormClosing;
            if (!String.IsNullOrWhiteSpace(initialSource))
            {
                try
                {
                    SetSelectedSource(initialSource);
                }
                catch (EasyPatchException error)
                {
                    initialSelectionError = error.Message;
                }
            }
            Shown += delegate
            {
                if (!String.IsNullOrEmpty(initialSelectionError))
                {
                    MessageBox.Show(
                        this,
                        initialSelectionError,
                        "원본 MDF가 필요합니다",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    statusLabel.Text = "일본판 원본 MDF를 선택해 주세요.";
                }
                else if (String.IsNullOrWhiteSpace(sourceBox.Text))
                    StartAutoFind();
            };
        }

        private void OnFormClosing(object sender, FormClosingEventArgs args)
        {
            if (args.CloseReason != CloseReason.UserClosing || !patching)
                return;
            args.Cancel = true;
            MessageBox.Show(
                this,
                "지금은 파일을 확인하거나 한글판을 만드는 중입니다.\r\n"
                + "작업이 끝난 뒤 창을 닫아 주세요.",
                "작업 중입니다",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void SetSelectedSource(string selectedPath)
        {
            string resolved = EasyPatcher.ResolveSourcePath(selectedPath);
            sourceBox.Text = resolved;
            string parent = Path.GetDirectoryName(resolved);
            outputBox.Text = Path.Combine(
                parent,
                "Langrisser_Dramatic_Korean_v0.13.17");
            statusLabel.Text = String.Equals(
                selectedPath, resolved, StringComparison.OrdinalIgnoreCase)
                ? "원본을 선택했습니다. '한글판 만들기'를 눌러 주세요."
                : "같은 이름의 원본 MDF를 자동으로 선택했습니다.";
        }

        private void OnDragEnter(object sender, DragEventArgs args)
        {
            if (!busy && args.Data.GetDataPresent(DataFormats.FileDrop))
                args.Effect = DragDropEffects.Copy;
        }

        private void OnDragDrop(object sender, DragEventArgs args)
        {
            if (busy)
                return;
            string[] files = args.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0)
            {
                try
                {
                    SetSelectedSource(files[0]);
                }
                catch (EasyPatchException error)
                {
                    MessageBox.Show(
                        this,
                        error.Message,
                        "원본 MDF가 필요합니다",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
            else
            {
                MessageBox.Show(
                    this,
                    "MDF 파일을 끌어다 놓아 주세요.",
                    "파일 확인",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private void ChooseSource()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "일본판 원본 MDF 선택";
                dialog.Filter = "디스크 이미지 (*.mdf;*.mds;*.cue;*.bin)|*.mdf;*.mds;*.cue;*.bin|모든 파일 (*.*)|*.*";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        SetSelectedSource(dialog.FileName);
                    }
                    catch (EasyPatchException error)
                    {
                        MessageBox.Show(
                            this,
                            error.Message,
                            "원본 MDF가 필요합니다",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                }
            }
        }

        private void ChooseOutput()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "완성된 BIN/CUE를 저장할 폴더";
                dialog.SelectedPath = outputBox.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    outputBox.Text = dialog.SelectedPath;
            }
        }

        private void SetBusy(bool value)
        {
            busy = value;
            sourceBox.Enabled = !value;
            outputBox.Enabled = !value;
            sourceButton.Enabled = !value;
            findButton.Enabled = !value;
            outputButton.Enabled = !value;
            startButton.Enabled = !value;
            AllowDrop = !value;
        }

        private void UiStatus(string message)
        {
            SafeBeginInvoke(delegate { statusLabel.Text = message; });
        }

        private bool SafeBeginInvoke(MethodInvoker action)
        {
            if (IsDisposed || Disposing || !IsHandleCreated)
                return false;
            try
            {
                BeginInvoke(action);
                return true;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private void StartAutoFind()
        {
            if (busy)
                return;
            SetBusy(true);
            progressBar.Style = ProgressBarStyle.Marquee;
            statusLabel.Text = "원본 MDF를 자동으로 찾는 중...";
            ThreadPool.QueueUserWorkItem(delegate
            {
                string found = OriginalFinder.Find(UiStatus);
                SafeBeginInvoke(delegate
                {
                    progressBar.Style = ProgressBarStyle.Blocks;
                    progressBar.Value = 0;
                    SetBusy(false);
                    if (found != null)
                    {
                        SetSelectedSource(found);
                        statusLabel.Text = "원본을 찾았습니다. '한글판 만들기'만 누르면 됩니다.";
                    }
                    else
                    {
                        statusLabel.Text = "자동으로 찾지 못했습니다. '파일 선택'으로 MDF를 골라 주세요.";
                    }
                });
            });
        }

        private void StartPatch()
        {
            if (busy)
                return;
            string source = sourceBox.Text.Trim();
            string output = outputBox.Text.Trim();
            patching = true;
            SetBusy(true);
            progressBar.Style = ProgressBarStyle.Blocks;
            progressBar.Value = 0;
            statusLabel.Text = "시작합니다...";
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    PatchResult result = EasyPatcher.Run(
                        source,
                        output,
                        delegate(PatchProgress item)
                        {
                            SafeBeginInvoke(delegate
                            {
                                progressBar.Value = Math.Max(0, Math.Min(100, item.Percent));
                                statusLabel.Text = item.Message;
                            });
                        });
                    SafeBeginInvoke(delegate
                    {
                        patching = false;
                        SetBusy(false);
                        progressBar.Value = 100;
                        statusLabel.Text = result.Message;
                        try
                        {
                            System.Diagnostics.Process.Start(
                                "explorer.exe",
                                "/select,\"" + result.OutputCuePath + "\"");
                        }
                        catch
                        {
                            // The patch is still complete even if Explorer could
                            // not be opened (for example on a locked-down PC).
                        }
                        MessageBox.Show(
                            this,
                            "한글판 만들기가 끝났습니다!\r\n\r\n"
                            + "RetroArch에서 Beetle Saturn 코어를 선택한 뒤\r\n"
                            + "다음 CUE 파일을 열어 주세요.\r\n"
                            + result.OutputCuePath,
                            "완료",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    });
                }
                catch (Exception error)
                {
                    string message = error is EasyPatchException
                        ? error.Message
                        : "작업 중 문제가 생겼습니다.\r\n" + error.Message;
                    SafeBeginInvoke(delegate
                    {
                        patching = false;
                        SetBusy(false);
                        progressBar.Value = 0;
                        statusLabel.Text = "중단됨: " + message.Replace("\r\n", " ");
                        MessageBox.Show(
                            this,
                            message,
                            "한글판을 만들지 못했습니다",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    });
                }
            });
        }
    }

    internal static class Program
    {
        private static int ExitCodeFor(Exception error)
        {
            EasyPatchException easy = error as EasyPatchException;
            if (easy != null)
                return easy.ExitCode;
            if (error is IOException || error is UnauthorizedAccessException)
                return 5;
            return 1;
        }

        private static int RunBatch(string source, string outputDirectory)
        {
            PatchResult result = new PatchResult();
            result.StartedUtc = DateTime.UtcNow;
            result.SourcePath = source;
            result.OutputDirectory = outputDirectory;
            try
            {
                result = EasyPatcher.Run(
                    source,
                    outputDirectory,
                    delegate(PatchProgress item)
                    {
                        Console.Error.WriteLine(
                            "[{0,3}%] {1}", item.Percent, item.Message);
                    });
                ResultJson.Write(outputDirectory, result);
                Console.WriteLine(result.Message);
                Console.WriteLine(result.OutputCuePath);
                return 0;
            }
            catch (Exception error)
            {
                result.Success = false;
                result.ExitCode = ExitCodeFor(error);
                result.Message = error.Message;
                result.FinishedUtc = DateTime.UtcNow;
                try
                {
                    result.SourcePath = Path.GetFullPath(source);
                }
                catch
                {
                    result.SourcePath = source;
                }
                try
                {
                    result.OutputDirectory = Path.GetFullPath(outputDirectory);
                    result.OutputBinPath = Path.Combine(
                        result.OutputDirectory, PatchConstants.OutputBinName);
                    result.OutputCuePath = Path.Combine(
                        result.OutputDirectory, PatchConstants.OutputCueName);
                    ResultJson.Write(result.OutputDirectory, result);
                }
                catch
                {
                    // If even the selected output directory cannot be created,
                    // there is nowhere safe to place the requested JSON report.
                }
                Console.Error.WriteLine("오류: " + error.Message);
                return result.ExitCode;
            }
        }

        [STAThread]
        internal static int Main(string[] args)
        {
            if (args.Length == 3
                && String.Equals(args[0], "--batch", StringComparison.OrdinalIgnoreCase))
            {
                return RunBatch(args[1], args[2]);
            }
            if (args.Length > 1
                || (args.Length == 1
                    && String.Equals(args[0], "--batch", StringComparison.OrdinalIgnoreCase)))
            {
                Console.Error.WriteLine(
                    "사용법: easy_patcher.exe [source.mdf]\r\n"
                    + "        easy_patcher.exe --batch <source.mdf> <output-dir>");
                return 2;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(args.Length == 1 ? args[0] : null));
            return 0;
        }
    }
}
