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

    // ── model → fallback logo (used when the exact datecode is unknown) ─
    private static readonly Dictionary<string, string> ModelLogos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["JDZ"] = "IIDX/AC_9th_style_logo",
        ["KDZ"] = "IIDX/AC_9th_style_logo",
        ["LDJ"] = "IIDX/AC_9th_style_logo",
        ["TDJ"] = "IIDX/AC_9th_style_logo",
        ["KFC"] = "SDVX/SDVX_BOOTH_logo",
        ["UFC"] = "SDVX/SDVX_NABLA_logo",
        ["JDX"] = "DDR/AC_DDR_X_logo",
        ["KDX"] = "DDR/AC_DDR_X_logo",
        ["MDX"] = "DDR/AC_DDR_A_logo",
        ["TDX"] = "DDR/AC_DDR_WORLD_logo",
    };

    // ── datecode ↁEarcade release (best-effort) ─────────────────────────
    private static readonly List<ArcadeRelease> ArcadeReleases =
    [
        // IIDX - LDJ model (contiguous: each release ends the day before the next launches)
        new("LDJ", "beatmania IIDX", "IIDX/AC_Lincle_logo",               2011091500, 2012091899, "19 Lincle"),
        new("LDJ", "beatmania IIDX", "IIDX/AC_tricoro_logo",              2012091900, 2013111299, "20 tricoro"),
        new("LDJ", "beatmania IIDX", "IIDX/AC_SPADA_logo",                2013111300, 2014091699, "21 SPADA"),
        new("LDJ", "beatmania IIDX", "IIDX/AC_PENDUAL_logo",              2014091700, 2015111099, "22 PENDUAL"),
        new("LDJ", "beatmania IIDX", "IIDX/AC_copula_logo",               2015111100, 2016102599, "23 copula"),
        new("LDJ", "beatmania IIDX", "IIDX/AC_SINOBUZ_logo",              2016102600, 2017122099, "24 SINOBUZ"),
        new("LDJ", "beatmania IIDX", "IIDX/AC_CANNON_BALLERS_logo",       2017122100, 2018110899, "25 CANNON BALLERS"),
        new("LDJ", "beatmania IIDX", "IIDX/AC_Rootage_logo",              2018110900, 2019100999, "26 Rootage"),
        new("LDJ", "beatmania IIDX", "IIDX/AC_HEROIC_VERSE_logo",         2019101000, 2020102799, "27 HEROIC VERSE"),
        new("LDJ", "beatmania IIDX", "IIDX/AC_BISTROVER_logo",            2020102800, 2021101299, "28 BISTROVER"),
        new("LDJ", "beatmania IIDX", "IIDX/AC_CastHour_logo",             2021101300, 2022101899, "29 CastHour"),
        new("LDJ", "beatmania IIDX", "IIDX/AC_RESIDENT_logo",             2022101900, 2023101799, "30 RESIDENT"),
        new("LDJ", "beatmania IIDX", "IIDX/AC_EPOLIS_logo",               2023101800, 2024100899, "31 EPOLIS"),
        new("LDJ", "beatmania IIDX", "IIDX/AC_Pinky_Crush_logo",          2024100900, 2025040999, "32 Pinky Crush"),
        new("LDJ", "beatmania IIDX", "IIDX/AC_Sparkle_Shower_logo",       2025041000, 2026021899, "33 Sparkle Shower"),
        new("LDJ", "beatmania IIDX", "IIDX/AC_ZINRAI_logo",               2026021900, 2999999999, "34 ZINRAI"),
        // older IIDX models (JDZ/KDZ) - release dates verified against RemyWiki;
        // each release spans its launch date to the day before the next launches.
        new("JDZ", "beatmania IIDX", "IIDX/AC_IIDX_RED_logo",             2004102800, 2005071299, "11 IIDX RED"),
        new("JDZ", "beatmania IIDX", "IIDX/AC_HAPPY_SKY_logo",            2005071300, 2006031499, "12 HAPPY SKY"),
        new("KDZ", "beatmania IIDX", "IIDX/AC_DistorteD_logo",            2006031500, 2007022099, "13 DistorteD"),
        new("KDZ", "beatmania IIDX", "IIDX/AC_GOLD_logo",                 2007022100, 2007121899, "14 GOLD"),
        new("LDJ", "beatmania IIDX", "IIDX/AC_DJ_TROOPERS_logo",          2007121900, 2008111899, "15 DJ TROOPERS"),
        new("LDJ", "beatmania IIDX", "IIDX/AC_EMPRESS_logo",              2008111900, 2009102099, "16 EMPRESS"),
        new("LDJ", "beatmania IIDX", "IIDX/AC_SIRIUS_logo",               2009102100, 2010091499, "17 SIRIUS"),
        new("LDJ", "beatmania IIDX", "IIDX/AC_Resort_Anthem_logo",        2010091500, 2011091499, "18 Resort Anthem"),
        // SDVX - KFC model (contiguous: each release ends the day before the next launches)
        new("KFC", "SOUND VOLTEX",    "SDVX/SDVX_BOOTH_logo",             2012011800, 2013060499, "BOOTH"),
        new("KFC", "SOUND VOLTEX",    "SDVX/SDVX_II_logo",                2013060500, 2014111999, "II -infinite infection-"),
        new("KFC", "SOUND VOLTEX",    "SDVX/SDVX_III_logo",               2014112000, 2016121499, "III GRAVITY WARS"),
        new("KFC", "SOUND VOLTEX",    "SDVX/SDVX_IV_logo",                2016121500, 2019022799, "IV HEAVENLY HAVEN"),
        new("KFC", "SOUND VOLTEX",    "SDVX/SDVX_VIVID_WAVE_logo",        2019022800, 2021040199, "VIVID WAVE"),
        new("KFC", "SOUND VOLTEX",    "SDVX/SDVX_EXCEED_GEAR_logo",       2021040200, 2025122399, "EXCEED GEAR"),
        new("UFC", "SOUND VOLTEX",    "SDVX/SDVX_NABLA_logo",             2025122400, 2999999999, "∁ENABLA"),
        // DDR - MDX model (contiguous: each release ends the day before the next launches)
        new("MDX", "DanceDanceRevolution", "DDR/AC_DDR_A_logo",          2016031700, 2019031999, "A"),
        new("MDX", "DanceDanceRevolution", "DDR/AC_DDR_A20_logo",        2019032000, 2022031699, "A20"),
        new("MDX", "DanceDanceRevolution", "DDR/AC_DDR_A3_logo-gold",    2022031700, 2024061199, "A3"),
        new("TDX", "DanceDanceRevolution", "DDR/AC_DDR_WORLD_logo",      2024061200, 2999999999, "WORLD"),
        // DDR - JDX/KDX models (older); X ran until X2 launched (2010-07-07),
        // X2 until X3 VS 2ndMIX (2011-11-16).
        new("JDX", "DanceDanceRevolution", "DDR/AC_DDR_X_logo",          2008122400, 2010070699, "X"),
        new("KDX", "DanceDanceRevolution", "DDR/AC_DDR_X2_logo",         2010070700, 2011111599, "X2"),
    ];

    // ── config file names (checked in order) ───────────────────────────
    // EA3 configs may sit directly under prop/ or under prop/defaults/
    // (the runtime copies defaults/ea3-config.xml into /dev/nvram at boot).
    private static readonly string[] Ea3ConfigNames =
        [
            "prop/ea3-config.xml",
            "prop/ea3-cfg.xml",
            "prop/eamuse-config.xml",
            "prop/ea3-ident.xml",
            "prop/defaults/ea3-config.xml",
            "prop/defaults/ea3-cfg.xml",
            "prop/defaults/eamuse-config.xml",
            "prop/defaults/ea3-ident.xml",
        ];

    // Bare filenames used to classify a folder as the prop/config support folder.
    private static readonly string[] Ea3ConfigFileNames =
        ["ea3-config.xml", "ea3-cfg.xml", "eamuse-config.xml", "ea3-ident.xml"];

    // Files that only ever appear in prop/, used to spot the config support folder.
    private static readonly string[] PropMarkerFiles =
        ["bootstrap.xml", "avs-config.xml", "share-config.xml", "avs-config_debug.xml"];

    public Task<DetectionResult> DetectAsync(string rootPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(rootPath) || !_fileSystem.DirectoryExists(rootPath))
        {
            return Task.FromResult(new DetectionResult(null, null, null, false));
        }

        var resolved = ResolveIdentity(rootPath, ct);
        if (resolved.Info is null)
        {
            return Task.FromResult(new DetectionResult(null, null, null, false));
        }

        var dataFolder = FindDataFolder(resolved.GameRootPath!, ct);
        var isGameRoot = IsGameRoot(rootPath, resolved.GameRootPath);

        return Task.FromResult(new DetectionResult(resolved.Info, resolved.GameRootPath, dataFolder, isGameRoot));
    }

    /// <summary>
    /// Locates the game identity, walking up from the selected folder. At each
    /// level the identity is probed directly and then in each immediate
    /// subdirectory, so a <c>contents</c> wrapper (which may or may not be
    /// present) is found by content, never by name. The returned game root is
    /// the folder that actually holds the identity files.
    /// </summary>
    private (GameVersionInfo? Info, string? GameRootPath) ResolveIdentity(string startPath, CancellationToken ct)
    {
        var candidate = startPath;

        for (var i = 0; i < 3; i++)
        {
            ct.ThrowIfCancellationRequested();

            var hit = TryDetect(candidate);
            if (hit.Info is not null)
            {
                return hit;
            }

            // A game wrapped in <contents> keeps the identity one level down;
            // probe immediate subdirectories by content, not by name.
            foreach (var sub in _fileSystem.EnumerateDirectories(candidate, recursive: false))
            {
                ct.ThrowIfCancellationRequested();
                hit = TryDetect(sub);
                if (hit.Info is not null)
                {
                    return hit;
                }
            }

            var parent = Path.GetDirectoryName(candidate);
            if (parent is null || parent == candidate) break;
            candidate = parent;
        }

        return (null, null);
    }

    private (GameVersionInfo? Info, string? GameRootPath) TryDetect(string dir)
    {
        var xmlInfo = TryReadEa3Config(dir);
        var dllInfo = TryScanDlls(dir);

        if (string.IsNullOrEmpty(xmlInfo.GameCode) && dllInfo.Title is null)
        {
            return (null, null);
        }

        var gameCode = !string.IsNullOrEmpty(xmlInfo.GameCode) ? xmlInfo.GameCode : dllInfo.ModelHint ?? string.Empty;
        var gameTitle = ResolveTitle(gameCode) ?? dllInfo.Title ?? string.Empty;
        var release = ResolveArcadeRelease(gameCode, xmlInfo.Ext);
        var logoKey = release?.LogoKey ?? ResolveModelLogo(gameCode);
        var displayTitle = release?.ReleaseName is { } releaseName
            ? $"{gameTitle} {releaseName}"
            : null;
        var peId = dllInfo.DllPath is not null ? TryReadPeIdentifier(dllInfo.DllPath, gameCode) : null;

        // Confidence is honest about the source of the identification: XML config
        // matched, or a known DLL alone. A PE identifier upgrades a config hit but
        // cannot promote a DLL-only match (there is no config to corroborate it).
        var confidence = !string.IsNullOrEmpty(xmlInfo.GameCode)
            ? DetectionConfidence.Xml
            : DetectionConfidence.DllOnly;
        if (peId is not null && !string.IsNullOrEmpty(xmlInfo.GameCode))
        {
            confidence = DetectionConfidence.XmlAndPe;
        }

        var info = new GameVersionInfo(
            gameCode, gameTitle,
            xmlInfo.Ext, peId,
            displayTitle, logoKey,
            confidence);

        return (info, dir);
    }

    /// <summary>
    /// Finds the game's data folder by content: among the game root's immediate
    /// subdirectories, the largest non-support one. Support folders are identified
    /// by what they contain (config xml, game DLL, nvram), never by name.
    /// </summary>
    private string? FindDataFolder(string gameRoot, CancellationToken ct)
    {
        string? best = null;
        var bestEntries = -1;

        foreach (var dir in _fileSystem.EnumerateDirectories(gameRoot, recursive: false))
        {
            ct.ThrowIfCancellationRequested();
            if (IsSupportFolder(dir)) continue;

            var entries = CountEntries(dir);
            if (entries > bestEntries)
            {
                bestEntries = entries;
                best = dir;
            }
        }

        return best is null ? null : Path.GetRelativePath(gameRoot, best);
    }

    private bool IsSupportFolder(string dir)
    {
        // prop/defaults nests the template config one level down, so search the
        // folder itself and its immediate subdirectories (bounded - never recurse
        // into data/, which can be huge).
        if (ContainsAny(dir, Ea3ConfigFileNames, depth: 2)
            || ContainsAny(dir, PropMarkerFiles, depth: 1))
        {
            return true;
        }

        if (HasDirectory(dir, "nvram"))
        {
            return true;
        }

        // modules/ holds the game's DLLs; a folder that contains a known game
        // DLL is support, not the data folder.
        foreach (var dllName in KnownDlls.Keys)
        {
            if (HasFile(dir, dllName))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when any of <paramref name="fileNames"/> exists at or below
    /// <paramref name="dir"/> within the given <paramref name="depth"/> (1 = direct
    /// children only). Config props are shallow, so a small bounded search is safe.
    /// </summary>
    private bool ContainsAny(string dir, string[] fileNames, int depth)
    {
        var folders = new[] { dir };
        for (var level = 1; level <= depth; level++)
        {
            var next = new List<string>();
            foreach (var folder in folders)
            {
                foreach (var fileName in fileNames)
                {
                    if (HasFile(folder, fileName))
                    {
                        return true;
                    }
                }

                if (level < depth)
                {
                    next.AddRange(_fileSystem.EnumerateDirectories(folder, recursive: false));
                }
            }

            folders = next.ToArray();
            if (folders.Length == 0) break;
        }

        return false;
    }

    /// <summary>
    /// Case-insensitive file probe: exact path first (fast), then a scan of the
    /// directory entries so configs are found on case-sensitive filesystems
    /// (Linux/macOS are registered in composition).
    /// </summary>
    private bool HasFile(string dir, string fileName)
    {
        if (_fileSystem.FileExists(Path.Combine(dir, fileName)))
        {
            return true;
        }

        return TryEnumerateFiles(dir)
            .Any(f => string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase));
    }

    private bool HasDirectory(string dir, string dirName)
    {
        if (_fileSystem.DirectoryExists(Path.Combine(dir, dirName)))
        {
            return true;
        }

        return TryEnumerateDirectories(dir)
            .Any(d => string.Equals(Path.GetFileName(d), dirName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resolves a slash-separated relative path under <paramref name="baseDir"/>,
    /// matching each segment case-insensitively against the real directory/file
    /// entries. Returns the exact on-disk path, or null when no segment matches.
    /// </summary>
    private string? ResolvePathCaseInsensitive(string baseDir, string relativePath)
    {
        var current = baseDir;
        var segments = relativePath.Split('/');

        for (var i = 0; i < segments.Length; i++)
        {
            var isFile = i == segments.Length - 1;
            var exact = Path.Combine(current, segments[i]);
            if ((isFile && _fileSystem.FileExists(exact)) || (!isFile && _fileSystem.DirectoryExists(exact)))
            {
                current = exact;
                continue;
            }

            var candidates = isFile ? TryEnumerateFiles(current) : TryEnumerateDirectories(current);
            var matched = candidates.FirstOrDefault(entry =>
                string.Equals(Path.GetFileName(entry), segments[i], StringComparison.OrdinalIgnoreCase));
            if (matched is null)
            {
                return null;
            }

            current = matched;
        }

        return current;
    }

    // Detection is best-effort: an unreadable folder (ACL-restricted, a missing
    // volume, an odd tree node) must never abort it. Non-recursive enumerations
    // used by the probes degrade to empty instead of throwing.
    private IReadOnlyList<string> TryEnumerateFiles(string dir)
    {
        try
        {
            return _fileSystem.EnumerateFiles(dir, recursive: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private IReadOnlyList<string> TryEnumerateDirectories(string dir)
    {
        try
        {
            return _fileSystem.EnumerateDirectories(dir, recursive: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private int CountEntries(string dir)
        => _fileSystem.EnumerateDirectories(dir, recursive: false).Count
           + _fileSystem.EnumerateFiles(dir, recursive: false).Count;

    private static string? ResolveTitle(string gameCode)
    {
        return ModelTitle.TryGetValue(gameCode, out var title) ? title : null;
    }

    private static string? ResolveModelLogo(string? gameCode)
    {
        return gameCode is not null && ModelLogos.TryGetValue(gameCode, out var logo) ? logo : null;
    }

    private static ArcadeRelease? ResolveArcadeRelease(string? gameCode, string? dateCode)
    {
        if (gameCode is null || dateCode is null) return null;
        if (!long.TryParse(dateCode, CultureInfo.InvariantCulture, out var dc)) return null;

        foreach (var r in ArcadeReleases)
        {
            if (string.Equals(r.Model, gameCode, StringComparison.OrdinalIgnoreCase) &&
                dc >= r.DateMin && dc <= r.DateMax)
            {
                return r;
            }
        }

        return null;
    }

    private static bool IsGameRoot(string selectedPath, string? gameRoot)
    {
        if (gameRoot is null) return false;

        var selected = Path.GetFullPath(selectedPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root = Path.GetFullPath(gameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(selected, root, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // <contents> wrapper: the user selected the folder that directly contains
        // the identity level (e.g. ...\Game when identity is ...\Game\contents).
        var parent = Path.GetDirectoryName(root);
        return parent is not null
               && string.Equals(selected, Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    // ── XML config ─────────────────────────────────────────────────────

    /// <summary>
    /// Locates the <c>&lt;soft&gt;</c> element carrying a <c>model</c> value. The
    /// value may be an attribute (<c>&lt;soft model="KFC" /&gt;</c>) or a child
    /// element (<c>&lt;soft&gt;&lt;model&gt;KFC&lt;/model&gt;&lt;/soft&gt;</c>),
    /// which is the shape real EA3 configs use. Various wrapper roots
    /// (<c>&lt;ea3&gt;</c>, <c>&lt;param&gt;&lt;ea3&gt;</c>, <c>&lt;ea3_conf&gt;</c>)
    /// are handled by descending.
    /// </summary>
    private static XElement? FindSoftNode(XElement? root)
    {
        if (root is null) return null;

        var candidates = root.DescendantsAndSelf("soft");
        foreach (var soft in candidates)
        {
            if (soft.Attribute("model") is not null || soft.Element("model") is not null)
            {
                return soft;
            }
        }

        return null;
    }

    /// <summary>Reads a soft field from its attribute or child element, in that order.</summary>
    private static string? ReadSoftField(XElement soft, string name)
    {
        var attribute = soft.Attribute(name)?.Value;
        if (!string.IsNullOrWhiteSpace(attribute))
        {
            return attribute;
        }

        var element = soft.Element(name)?.Value;
        return string.IsNullOrWhiteSpace(element) ? null : element;
    }

    private Ea3SoftInfo TryReadEa3Config(string candidatePath)
    {
        foreach (var name in Ea3ConfigNames)
        {
            var fullPath = ResolvePathCaseInsensitive(candidatePath, name);
            if (fullPath is null) continue;

            try
            {
                var text = _fileSystem.ReadAllText(fullPath);
                var doc = XDocument.Parse(text);

                var soft = FindSoftNode(doc.Root);
                if (soft is null) continue;

                var model = ReadSoftField(soft, "model");
                if (string.IsNullOrWhiteSpace(model)) continue;

                var ext    = ReadSoftField(soft, "ext");

                // bootstrap.xml override (release_code)
                var bootstrapPath = ResolvePathCaseInsensitive(candidatePath, "prop/bootstrap.xml");
                if (bootstrapPath is not null)
                {
                    try
                    {
                        var bText = _fileSystem.ReadAllText(bootstrapPath);
                        var bDoc = XDocument.Parse(bText);
                        // release_code is <config><release_code> under a <param>
                        // wrapper, or <config><release_code> with <config> as root.
                        var releaseCode = bDoc.Root?.Descendants("release_code").FirstOrDefault()?.Value;
                        if (releaseCode is not null &&
                            long.TryParse(releaseCode, CultureInfo.InvariantCulture, out var rc) &&
                            (!long.TryParse(ext, CultureInfo.InvariantCulture, out var extVal) || rc > extVal))
                        {
                            ext = releaseCode;
                        }
                    }
                    catch
                    {
                        // bootstrap parse failure is non-fatal
                    }
                }

                return new Ea3SoftInfo(model, ext);
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
        // Game DLLs live at the root or under modules/; probe both by content.
        var probes = new List<string> { candidatePath };
        if (ResolvePathCaseInsensitive(candidatePath, "modules") is { } modulesDir)
        {
            probes.Add(modulesDir);
        }

        foreach (var (dllName, (title, modelHint)) in KnownDlls)
        {
            foreach (var probe in probes)
            {
                var fullPath = ResolvePathCaseInsensitive(probe, dllName);
                if (fullPath is null || !HasMzHeader(fullPath)) continue;

                return new DllScanInfo(title, modelHint, fullPath);
            }
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

    private string? TryReadPeIdentifier(string dllPath, string gameCode)
    {
        if (gameCode.Length == 0) return null;

        try
        {
            using var stream = _fileSystem.OpenRead(dllPath);
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
        string? GameCode, string? Ext);

    private readonly record struct DllScanInfo(
        string? Title, string? ModelHint, string? DllPath);

    private sealed record ArcadeRelease(
        string Model, string GameTitle, string LogoKey,
        long DateMin, long DateMax, string ReleaseName);
}
