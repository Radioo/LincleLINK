using LincleLINK.App.Converters;

namespace LincleLINK.App.Logos;

public sealed record LogoEntry(string LogoKey, string AssetPath, string DisplayName);

public sealed class LogoCatalog
{
    public IReadOnlyList<LogoEntry> AllLogos { get; }

    private readonly Dictionary<string, string> _keyToPath;

    public LogoCatalog()
    {
        var list = new List<LogoEntry>();
        _keyToPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, name) in BuiltinLogos)
        {
            var path = $"avares://LincleLINK/Assets/{key}.png";
            list.Add(new LogoEntry(key, path, name));
            _keyToPath[key] = path;
        }

        AllLogos = list;
    }

    public string? GetLogoPath(string? logoKey)
    {
        if (logoKey is null) return null;
        return _keyToPath.TryGetValue(logoKey, out var path) ? path : null;
    }

    public static string? GetCustomLogoFilePath(string dataDirectory, string nameKey)
    {
        var filePath = Path.Combine(dataDirectory, "custom_logos", nameKey + ".png");
        return File.Exists(filePath) ? filePath : null;
    }

    public static void SaveCustomLogo(string dataDirectory, string nameKey, string sourceFilePath)
    {
        var dir = Path.Combine(dataDirectory, "custom_logos");
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, nameKey + ".png");
        File.Copy(sourceFilePath, dest, overwrite: true);

        // Custom logos always overwrite the same path; a cached bitmap for it
        // would be stale until restart, so evict it for the next conversion.
        LogoSourceConverter.Evict(dest);
    }

    public static void DeleteCustomLogo(string dataDirectory, string nameKey)
    {
        var filePath = Path.Combine(dataDirectory, "custom_logos", nameKey + ".png");
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            LogoSourceConverter.Evict(filePath);
        }
    }

    private static readonly (string Key, string Name)[] BuiltinLogos =
    [
        ("IIDX/AC_9th_style_logo",        "beatmania IIDX 9th style"),
        ("IIDX/AC_10th_style_logo",       "beatmania IIDX 10th style"),
        ("IIDX/AC_IIDX_RED_logo",         "beatmania IIDX 11 IIDX RED"),
        ("IIDX/AC_HAPPY_SKY_logo",        "beatmania IIDX 12 HAPPY SKY"),
        ("IIDX/AC_DistorteD_logo",        "beatmania IIDX 13 DistorteD"),
        ("IIDX/AC_GOLD_logo",             "beatmania IIDX 14 GOLD"),
        ("IIDX/AC_DJ_TROOPERS_logo",      "beatmania IIDX 15 DJ TROOPERS"),
        ("IIDX/AC_EMPRESS_logo",          "beatmania IIDX 16 EMPRESS"),
        ("IIDX/AC_SIRIUS_logo",           "beatmania IIDX 17 SIRIUS"),
        ("IIDX/AC_Resort_Anthem_logo",    "beatmania IIDX 18 Resort Anthem"),
        ("IIDX/AC_Lincle_logo",           "beatmania IIDX 19 Lincle"),
        ("IIDX/AC_tricoro_logo",          "beatmania IIDX 20 tricoro"),
        ("IIDX/AC_SPADA_logo",            "beatmania IIDX 21 SPADA"),
        ("IIDX/AC_PENDUAL_logo",          "beatmania IIDX 22 PENDUAL"),
        ("IIDX/AC_copula_logo",           "beatmania IIDX 23 copula"),
        ("IIDX/AC_SINOBUZ_logo",          "beatmania IIDX 24 SINOBUZ"),
        ("IIDX/AC_CANNON_BALLERS_logo",   "beatmania IIDX 25 CANNON BALLERS"),
        ("IIDX/AC_Rootage_logo",          "beatmania IIDX 26 Rootage"),
        ("IIDX/AC_HEROIC_VERSE_logo",     "beatmania IIDX 27 HEROIC VERSE"),
        ("IIDX/AC_BISTROVER_logo",        "beatmania IIDX 28 BISTROVER"),
        ("IIDX/AC_CastHour_logo",         "beatmania IIDX 29 CastHour"),
        ("IIDX/AC_RESIDENT_logo",         "beatmania IIDX 30 RESIDENT"),
        ("IIDX/AC_EPOLIS_logo",           "beatmania IIDX 31 EPOLIS"),
        ("IIDX/AC_Pinky_Crush_logo",      "beatmania IIDX 32 Pinky Crush"),
        ("IIDX/AC_Sparkle_Shower_logo",   "beatmania IIDX 33 Sparkle Shower"),
        ("IIDX/AC_ZINRAI_logo",           "beatmania IIDX 34 ZINRAI"),
        ("SDVX/SDVX_BOOTH_logo",          "SOUND VOLTEX BOOTH"),
        ("SDVX/SDVX_II_logo",             "SOUND VOLTEX II"),
        ("SDVX/SDVX_III_logo",            "SOUND VOLTEX III GRAVITY WARS"),
        ("SDVX/SDVX_IV_logo",             "SOUND VOLTEX IV HEAVENLY HAVEN"),
        ("SDVX/SDVX_VIVID_WAVE_logo",     "SOUND VOLTEX VIVID WAVE"),
        ("SDVX/SDVX_EXCEED_GEAR_logo",    "SOUND VOLTEX EXCEED GEAR"),
        ("SDVX/SDVX_NABLA_logo",          "SOUND VOLTEX NABLA"),
        ("DDR/AC_DDR_1st_logo",           "DanceDanceRevolution 1st"),
        ("DDR/AC_DDR_2nd_logo",           "DanceDanceRevolution 2ndMIX"),
        ("DDR/AC_DDR_3rd_logo",           "DanceDanceRevolution 3rdMIX"),
        ("DDR/AC_DDR_4th_logo",           "DanceDanceRevolution 4thMIX"),
        ("DDR/AC_DDR_5th_logo",           "DanceDanceRevolution 5thMIX"),
        ("DDR/AC_DDRMAX_logo",            "DanceDanceRevolution DDRMAX"),
        ("DDR/AC_DDRMAX2_logo",           "DanceDanceRevolution DDRMAX2"),
        ("DDR/AC_DDR_EXTREME_logo",       "DanceDanceRevolution EXTREME"),
        ("DDR/AC_DDR_SuperNOVA_logo",     "DanceDanceRevolution SuperNOVA"),
        ("DDR/AC_DDR_SuperNOVA2_logo",    "DanceDanceRevolution SuperNOVA2"),
        ("DDR/AC_DDR_X_logo",             "DanceDanceRevolution X"),
        ("DDR/AC_DDR_X2_logo",            "DanceDanceRevolution X2"),
        ("DDR/AC_DDR_X3_logo",            "DanceDanceRevolution X3 VS 2ndMIX"),
        ("DDR/AC_DDR_2013_logo",          "DanceDanceRevolution (2013)"),
        ("DDR/AC_DDR_2014_logo",          "DanceDanceRevolution (2014)"),
        ("DDR/AC_DDR_A_logo",             "DanceDanceRevolution A"),
        ("DDR/AC_DDR_A20_logo",           "DanceDanceRevolution A20"),
        ("DDR/AC_DDR_A3_logo-gold",       "DanceDanceRevolution A3"),
        ("DDR/AC_DDR_WORLD_logo",         "DanceDanceRevolution WORLD"),
    ];
}
