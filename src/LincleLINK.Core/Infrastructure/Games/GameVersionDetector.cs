using System.Globalization;
using System.Xml.Linq;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Abstractions.Games;
using LincleLINK.Core.Domain;

namespace LincleLINK.Core.Infrastructure.Games;

public sealed class GameVersionDetector : IGameVersionDetector
{
    private readonly IFileSystem _fileSystem;

    public GameVersionDetector(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    // ── known game DLLs (family + model hint) ──────────────────────────
    private static readonly Dictionary<string, (string Title, string ModelHint)> KnownDlls = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bm2dx.dll"]       = ("beatmania IIDX",  "LDJ"),
        ["soundvoltex.dll"] = ("SOUND VOLTEX",     "KFC"),
        ["gamemdx.dll"]     = ("DanceDanceRevolution", "MDX"),
        ["arkmdxbio2.dll"]  = ("DanceDanceRevolution", "MDX"),
        ["arkmdxp3.dll"]    = ("DanceDanceRevolution", "MDX"),
        ["arkmdxp4.dll"]    = ("DanceDanceRevolution", "MDX"),
        ["ddr.dll"]         = ("DanceDanceRevolution", "JDX"),
        ["mdxja_945.dll"]   = ("DanceDanceRevolution", "MDX"),
        ["mdxja_hm65.dll"]  = ("DanceDanceRevolution", "MDX"),
    };

    // ── model ↁEgame title ─────────────────────────────────────────────
    private static readonly Dictionary<string, string> ModelTitle = new(StringComparer.OrdinalIgnoreCase)
    {
        ["JDZ"] = "beatmania IIDX",
        ["KDZ"] = "beatmania IIDX",
        ["LDJ"] = "beatmania IIDX",
        ["TDJ"] = "beatmania IIDX",
        ["KFC"] = "SOUND VOLTEX",
        ["UFC"] = "SOUND VOLTEX",
        ["JDX"] = "DanceDanceRevolution",
        ["KDX"] = "DanceDanceRevolution",
        ["MDX"] = "DanceDanceRevolution",
        ["TDX"] = "DanceDanceRevolution",
    };

    // ── per-game data folder names ─────────────────────────────────────
    private static readonly Dictionary<string, string> DataFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["beatmania IIDX"]       = "data",
        ["SOUND VOLTEX"]         = Path.Combine("contents", "data"),
        ["DanceDanceRevolution"] = "data",
    };

    // ── datecode ↁEarcade release (best-effort) ─────────────────────────
    private static readonly List<ArcadeRelease> ArcadeReleases =
    [
        // IIDX - LDJ model
        new("LDJ", "beatmania IIDX", "IIDX/AC_Lincle_logo",             2011091500, 2012060100, "19 Lincle"),
        new("LDJ", "beatmania IIDX", "IIDX/AC_tricoro_logo",              2012091900, 2013110100, "20 tricoro"),
        new("LDJ", "beatmania IIDX", "IIDX/AC_SPADA_logo",                2013111300, 2014090100, "21 SPADA"),
        new("LDJ", "beatmania IIDX", "IIDX/AC_PENDUAL_logo",              2014091700, 2015110100, "22 PENDUAL"),
        new("LDJ", "beatmania IIDX", "IIDX/AC_copula_logo",               2015111100, 2016100100, "23 copula"),
        new("LDJ", "beatmania IIDX", "IIDX/AC_SINOBUZ_logo",              2016102600, 2017120100, "24 SINOBUZ"),
        new("LDJ", "beatmania IIDX", "IIDX/AC_CANNON_BALLERS_logo",       2017122100, 2018110100, "25 CANNON BALLERS"),
        new("LDJ", "beatmania IIDX", "IIDX/AC_Rootage_logo",              2018110900, 2019100100, "26 Rootage"),
        new("LDJ", "beatmania IIDX", "IIDX/AC_HEROIC_VERSE_logo",         2019101000, 2020100100, "27 HEROIC VERSE"),
        new("LDJ", "beatmania IIDX", "IIDX/AC_BISTROVER_logo",            2020102800, 2021100100, "28 BISTROVER"),
        new("LDJ", "beatmania IIDX", "IIDX/AC_CastHour_logo",             2021101300, 2022100100, "29 CastHour"),
        new("LDJ", "beatmania IIDX", "IIDX/AC_RESIDENT_logo",             2022101900, 2023100100, "30 RESIDENT"),
        new("LDJ", "beatmania IIDX", "IIDX/AC_EPOLIS_logo",               2023101800, 2024100100, "31 EPOLIS"),
        new("LDJ", "beatmania IIDX", "IIDX/AC_Pinky_Crush_logo",          2024100900, 2025040100, "32 Pinky Crush"),
        new("LDJ", "beatmania IIDX", "IIDX/AC_Sparkle_Shower_logo",       2025041000, 2026020100, "33 Sparkle Shower"),
        new("LDJ", "beatmania IIDX", "IIDX/AC_ZINRAI_logo",               2026021900, 2999999999, "34 ZINRAI"),
        // older IIDX models (JDZ/KDZ)
        new("JDZ", "beatmania IIDX", "IIDX/AC_IIDX_RED_logo",             2006052400, 2007070100, "11 IIDX RED"),
        new("JDZ", "beatmania IIDX", "IIDX/AC_HAPPY_SKY_logo",            2005121400, 2006052300, "12 HAPPY SKY"),
        new("KDZ", "beatmania IIDX", "IIDX/AC_DistorteD_logo",            2007032800, 2008110100, "13 DistorteD"),
        new("KDZ", "beatmania IIDX", "IIDX/AC_GOLD_logo",                 2008111900, 2009120100, "14 GOLD"),
        // SDVX - KFC model
        new("KFC", "SOUND VOLTEX",    "SDVX/SDVX_BOOTH_logo",             2012011800, 2013060100, "BOOTH"),
        new("KFC", "SOUND VOLTEX",    "SDVX/SDVX_II_logo",                2013060500, 2014110100, "II -infinite infection-"),
        new("KFC", "SOUND VOLTEX",    "SDVX/SDVX_III_logo",               2014112000, 2016120100, "III GRAVITY WARS"),
        new("KFC", "SOUND VOLTEX",    "SDVX/SDVX_IV_logo",                2016121500, 2019020100, "IV HEAVENLY HAVEN"),
        new("KFC", "SOUND VOLTEX",    "SDVX/SDVX_VIVID_WAVE_logo",        2019022800, 2021040100, "VIVID WAVE"),
        new("KFC", "SOUND VOLTEX",    "SDVX/SDVX_EXCEED_GEAR_logo",       2021040200, 2024060100, "EXCEED GEAR"),
        new("UFC", "SOUND VOLTEX",    "SDVX/SDVX_NABLA_logo",             2024061300, 2999999999, "∁ENABLA"),
        // DDR - MDX model
        new("MDX", "DanceDanceRevolution", "DDR/AC_DDR_A_logo",          2016031700, 2019030100, "A"),
        new("MDX", "DanceDanceRevolution", "DDR/AC_DDR_A20_logo",        2019032000, 2022030100, "A20"),
        new("MDX", "DanceDanceRevolution", "DDR/AC_DDR_A3_logo-gold",    2022031700, 2024060100, "A3"),
        new("TDX", "DanceDanceRevolution", "DDR/AC_DDR_WORLD_logo",      2024061200, 2999999999, "WORLD"),
        // DDR - JDX/KDX models (older)
        new("JDX", "DanceDanceRevolution", "DDR/AC_DDR_X_logo",          2008122400, 2009120100, "X"),
        new("KDX", "DanceDanceRevolution", "DDR/AC_DDR_X2_logo",         2010070700, 2011110100, "X2"),
    ];

    // ── config file names (checked in order) ───────────────────────────
    private static readonly string[] Ea3ConfigNames =
        ["prop/ea3-config.xml", "prop/ea3-cfg.xml", "prop/eamuse-config.xml", "prop/ea3-ident.xml"];

    public Task<DetectionResult> DetectAsync(string rootPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(rootPath) || !_fileSystem.DirectoryExists(rootPath))
        {
            return Task.FromResult(new DetectionResult(null, null, null, false));
        }

        var resolved = ResolveUpwards(rootPath, ct);
        if (resolved.Info is null)
        {
            return Task.FromResult(new DetectionResult(null, null, null, false));
        }

        var dataFolder = DataFolders.TryGetValue(resolved.Info.GameTitle, out var df) ? df : null;
        var isGameRoot = IsGameRoot(rootPath, resolved.GameRootPath);

        return Task.FromResult(new DetectionResult(resolved.Info, resolved.GameRootPath, dataFolder, isGameRoot));
    }

    private (GameVersionInfo? Info, string? GameRootPath) ResolveUpwards(string startPath, CancellationToken ct)
    {
        var candidate = startPath;

        for (var i = 0; i < 3; i++)
        {
            ct.ThrowIfCancellationRequested();

            var xmlInfo = TryReadEa3Config(candidate);
            var dllInfo = TryScanDlls(candidate);

            if (xmlInfo.GameCode.Length > 0 || dllInfo.Title is not null)
            {
                var gameCode = xmlInfo.GameCode.Length > 0 ? xmlInfo.GameCode : dllInfo.ModelHint ?? string.Empty;
                var gameTitle = ResolveTitle(gameCode) ?? dllInfo.Title ?? string.Empty;
                var logoKey = ResolveArcadeRelease(gameCode, xmlInfo.Ext);
                var displayTitle = logoKey is not null ? ParseDisplayTitle(logoKey) : null;
                var peId = dllInfo.DllPath is not null ? TryReadPeIdentifier(dllInfo.DllPath, gameCode) : null;

                var confidence = DetectionConfidence.Xml;
                if (peId is not null) confidence = DetectionConfidence.XmlAndPe;

                var info = new GameVersionInfo(
                    gameCode, gameTitle,
                    xmlInfo.Dest, xmlInfo.Spec, xmlInfo.Rev,
                    xmlInfo.Ext, peId,
                    displayTitle, logoKey,
                    confidence);

                return (info, candidate);
            }

            var parent = Path.GetDirectoryName(candidate);
            if (parent is null || parent == candidate) break;
            candidate = parent;
        }

        return (null, null);
    }

    private static string? ResolveTitle(string gameCode)
    {
        return ModelTitle.TryGetValue(gameCode, out var title) ? title : null;
    }

    private static string? ResolveArcadeRelease(string? gameCode, string? dateCode)
    {
        if (gameCode is null || dateCode is null) return null;
        if (!long.TryParse(dateCode, CultureInfo.InvariantCulture, out var dc)) return null;

        foreach (var r in ArcadeReleases)
        {
            if (string.Equals(r.Model, gameCode, StringComparison.OrdinalIgnoreCase) &&
                dc >= r.DateMin && dc <= r.DateMax)
            {
                return r.LogoKey;
            }
        }

        return null;
    }

    private static string? ParseDisplayTitle(string logoKey)
    {
        // logoKey format: "AC_27_HEROIC_VERSE_logo" or "SDVX_VIVID_WAVE_logo" etc.
        // extract the human title portion
        var name = Path.GetFileNameWithoutExtension(logoKey);
        return name.Replace('_', ' ');
    }

    private static bool IsGameRoot(string selectedPath, string? gameRoot)
    {
        if (gameRoot is null) return false;
        return string.Equals(
            Path.GetFullPath(selectedPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(gameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    // ── XML config ─────────────────────────────────────────────────────

    private Ea3SoftInfo TryReadEa3Config(string candidatePath)
    {
        foreach (var name in Ea3ConfigNames)
        {
            var fullPath = Path.Combine(candidatePath, name);
            if (!_fileSystem.FileExists(fullPath)) continue;

            try
            {
                var text = _fileSystem.ReadAllText(fullPath);
                var doc = XDocument.Parse(text);

                var soft = doc.Root?.Element("soft");
                if (soft is null)
                {
                    // ea3-ident.xml uses <ea3_conf><soft>
                    soft = doc.Root?.Element("ea3_conf")?.Element("soft");
                }

                if (soft is null)
                {
                    // ea3-config.xml uses <ea3><soft>
                    soft = doc.Root?.Element("ea3")?.Element("soft");
                }

                // direct ea3-config structure: just <soft> inside the root? No, ea3-config.xml root is <ea3>
                // and <soft> is under it. Actually, re-checking the parser in avs/ea3.cpp: the root is parsed
                // as property list, and then /ea3/soft is located. The XML structure is <ea3><soft .../>.
                // The XDocument parser sees <ea3> as root, <soft> as child.

                if (soft is null) continue;

                var model = (string?)soft.Attribute("model");
                if (string.IsNullOrWhiteSpace(model)) continue;

                var dest   = (string?)soft.Attribute("dest");
                var spec   = (string?)soft.Attribute("spec");
                var rev    = (string?)soft.Attribute("rev");
                var ext    = (string?)soft.Attribute("ext");

                // bootstrap.xml override (release_code)
                var bootstrapPath = Path.Combine(candidatePath, "prop", "bootstrap.xml");
                if (_fileSystem.FileExists(bootstrapPath))
                {
                    try
                    {
                        var bText = _fileSystem.ReadAllText(bootstrapPath);
                        var bDoc = XDocument.Parse(bText);
                        var releaseCode = (string?)bDoc.Root?.Element("config")?.Element("release_code");
                        if (releaseCode is not null &&
                            long.TryParse(releaseCode, CultureInfo.InvariantCulture, out var rc) &&
                            long.TryParse(ext, CultureInfo.InvariantCulture, out var extVal) &&
                            rc > extVal)
                        {
                            ext = releaseCode;
                        }
                    }
                    catch
                    {
                        // bootstrap parse failure is non-fatal
                    }
                }

                return new Ea3SoftInfo(model, dest ?? string.Empty, spec ?? string.Empty, rev ?? string.Empty, ext ?? string.Empty);
            }
            catch
            {
                // parse failure on one config; try next
            }
        }

        return default;
    }

    // ── DLL scan ───────────────────────────────────────────────────────

    private DllScanInfo TryScanDlls(string candidatePath)
    {
        foreach (var (dllName, (title, modelHint)) in KnownDlls)
        {
            var fullPath = Path.Combine(candidatePath, dllName);
            if (!_fileSystem.FileExists(fullPath)) continue;
            if (!HasMzHeader(fullPath)) continue;

            return new DllScanInfo(title, modelHint, fullPath);
        }

        return default;
    }

    private bool HasMzHeader(string path)
    {
        try
        {
            using var stream = _fileSystem.OpenRead(path);
            var buf = new byte[2];
            return stream.Read(buf, 0, 2) == 2 && buf[0] == 'M' && buf[1] == 'Z';
        }
        catch
        {
            return false;
        }
    }

    // ── PE identifier ──────────────────────────────────────────────────

    private static string? TryReadPeIdentifier(string dllPath, string gameCode)
    {
        if (gameCode.Length == 0) return null;

        try
        {
            using var stream = File.OpenRead(dllPath);
            using var reader = new BinaryReader(stream);

            // DOS header
            if (reader.BaseStream.Length < 64) return null;
            reader.BaseStream.Seek(0x3C, SeekOrigin.Begin);
            var peOffset = reader.ReadInt32();
            if (peOffset <= 0 || peOffset > (int)(reader.BaseStream.Length - 80)) return null;

            // PE signature
            reader.BaseStream.Seek(peOffset, SeekOrigin.Begin);
            var sig = reader.ReadUInt32();
            if (sig != 0x00004550) return null; // "PE\0\0"

            // IMAGE_FILE_HEADER (20 bytes)
            var machine = reader.ReadUInt16();
            reader.ReadUInt16(); // NumberOfSections
            var timeDateStamp = reader.ReadUInt32();
            reader.ReadUInt32(); // PointerToSymbolTable
            reader.ReadUInt32(); // NumberOfSymbols
            var sizeOfOptionalHeader = reader.ReadUInt16();
            reader.ReadUInt16(); // Characteristics

            // IMAGE_OPTIONAL_HEADER
            if (sizeOfOptionalHeader < 20) return null;
            var optionalHeaderStart = reader.BaseStream.Position;
            var magic = reader.ReadUInt16(); // 0x10B = PE32, 0x20B = PE32+

            // AddressOfEntryPoint is at offset 16 from start of optional header
            reader.BaseStream.Seek(optionalHeaderStart + 16, SeekOrigin.Begin);
            var addressOfEntryPoint = reader.ReadUInt32();

            var lowerGameCode = gameCode.ToLowerInvariant();
            return $"{lowerGameCode}-{timeDateStamp:x}_{addressOfEntryPoint:x}";
        }
        catch
        {
            return null;
        }
    }

    // ── internal data types ────────────────────────────────────────────

    private readonly record struct Ea3SoftInfo(
        string GameCode, string Dest, string Spec, string Rev, string Ext);

    private readonly record struct DllScanInfo(
        string? Title, string? ModelHint, string? DllPath);

    private sealed record ArcadeRelease(
        string Model, string GameTitle, string LogoKey,
        long DateMin, long DateMax, string ReleaseName);
}
