using System.Globalization;
using System.Text;

namespace DeeKeep.Common.Text;

/// <summary>
/// Équivalents .NET des primitives de texte Python sur lesquelles reposent
/// les portages de ce dossier (<c>casefold</c>, <c>unicodedata.combining</c>,
/// <c>str.strip</c>, <c>str.isdigit</c>, <c>ntpath</c>).
/// </summary>
/// <remarks>
/// Écarts résiduels, sans effet sur des titres ou des noms de fichiers réels :
/// <list type="bullet">
/// <item>la classe <c>\s</c> des regex .NET ne contient pas U+001C–U+001F, que Python tient pour des blancs ;</item>
/// <item><c>\w</c>/<c>\b</c> .NET comptent les marques non espaçantes (Mn) et toute la ponctuation de liaison (Pc) comme lettres, Python non ;</item>
/// <item>les tables ci-dessous viennent d'Unicode 15.1 (Python 3.13), .NET 10 suit Unicode 16.</item>
/// </list>
/// </remarks>
public static class PyText
{
    // Plages de points de code dont la classe de combinaison canonique est non nulle
    // (unicodedata.combining(c) != 0), générées depuis Python 3.13. Ce n'est PAS la
    // catégorie Mn : 1 089 marques Mn ont une classe nulle (signes vocaliques indiens…),
    // que Python conserve.
    private static readonly int[] CombiningRanges =
    [
        0x0300, 0x034E, 0x0350, 0x036F, 0x0483, 0x0487, 0x0591, 0x05BD, 0x05BF, 0x05BF, 0x05C1, 0x05C2,
        0x05C4, 0x05C5, 0x05C7, 0x05C7, 0x0610, 0x061A, 0x064B, 0x065F, 0x0670, 0x0670, 0x06D6, 0x06DC,
        0x06DF, 0x06E4, 0x06E7, 0x06E8, 0x06EA, 0x06ED, 0x0711, 0x0711, 0x0730, 0x074A, 0x07EB, 0x07F3,
        0x07FD, 0x07FD, 0x0816, 0x0819, 0x081B, 0x0823, 0x0825, 0x0827, 0x0829, 0x082D, 0x0859, 0x085B,
        0x0898, 0x089F, 0x08CA, 0x08E1, 0x08E3, 0x08FF, 0x093C, 0x093C, 0x094D, 0x094D, 0x0951, 0x0954,
        0x09BC, 0x09BC, 0x09CD, 0x09CD, 0x09FE, 0x09FE, 0x0A3C, 0x0A3C, 0x0A4D, 0x0A4D, 0x0ABC, 0x0ABC,
        0x0ACD, 0x0ACD, 0x0B3C, 0x0B3C, 0x0B4D, 0x0B4D, 0x0BCD, 0x0BCD, 0x0C3C, 0x0C3C, 0x0C4D, 0x0C4D,
        0x0C55, 0x0C56, 0x0CBC, 0x0CBC, 0x0CCD, 0x0CCD, 0x0D3B, 0x0D3C, 0x0D4D, 0x0D4D, 0x0DCA, 0x0DCA,
        0x0E38, 0x0E3A, 0x0E48, 0x0E4B, 0x0EB8, 0x0EBA, 0x0EC8, 0x0ECB, 0x0F18, 0x0F19, 0x0F35, 0x0F35,
        0x0F37, 0x0F37, 0x0F39, 0x0F39, 0x0F71, 0x0F72, 0x0F74, 0x0F74, 0x0F7A, 0x0F7D, 0x0F80, 0x0F80,
        0x0F82, 0x0F84, 0x0F86, 0x0F87, 0x0FC6, 0x0FC6, 0x1037, 0x1037, 0x1039, 0x103A, 0x108D, 0x108D,
        0x135D, 0x135F, 0x1714, 0x1715, 0x1734, 0x1734, 0x17D2, 0x17D2, 0x17DD, 0x17DD, 0x18A9, 0x18A9,
        0x1939, 0x193B, 0x1A17, 0x1A18, 0x1A60, 0x1A60, 0x1A75, 0x1A7C, 0x1A7F, 0x1A7F, 0x1AB0, 0x1ABD,
        0x1ABF, 0x1ACE, 0x1B34, 0x1B34, 0x1B44, 0x1B44, 0x1B6B, 0x1B73, 0x1BAA, 0x1BAB, 0x1BE6, 0x1BE6,
        0x1BF2, 0x1BF3, 0x1C37, 0x1C37, 0x1CD0, 0x1CD2, 0x1CD4, 0x1CE0, 0x1CE2, 0x1CE8, 0x1CED, 0x1CED,
        0x1CF4, 0x1CF4, 0x1CF8, 0x1CF9, 0x1DC0, 0x1DFF, 0x20D0, 0x20DC, 0x20E1, 0x20E1, 0x20E5, 0x20F0,
        0x2CEF, 0x2CF1, 0x2D7F, 0x2D7F, 0x2DE0, 0x2DFF, 0x302A, 0x302F, 0x3099, 0x309A, 0xA66F, 0xA66F,
        0xA674, 0xA67D, 0xA69E, 0xA69F, 0xA6F0, 0xA6F1, 0xA806, 0xA806, 0xA82C, 0xA82C, 0xA8C4, 0xA8C4,
        0xA8E0, 0xA8F1, 0xA92B, 0xA92D, 0xA953, 0xA953, 0xA9B3, 0xA9B3, 0xA9C0, 0xA9C0, 0xAAB0, 0xAAB0,
        0xAAB2, 0xAAB4, 0xAAB7, 0xAAB8, 0xAABE, 0xAABF, 0xAAC1, 0xAAC1, 0xAAF6, 0xAAF6, 0xABED, 0xABED,
        0xFB1E, 0xFB1E, 0xFE20, 0xFE2F, 0x101FD, 0x101FD, 0x102E0, 0x102E0, 0x10376, 0x1037A, 0x10A0D, 0x10A0D,
        0x10A0F, 0x10A0F, 0x10A38, 0x10A3A, 0x10A3F, 0x10A3F, 0x10AE5, 0x10AE6, 0x10D24, 0x10D27, 0x10EAB, 0x10EAC,
        0x10EFD, 0x10EFF, 0x10F46, 0x10F50, 0x10F82, 0x10F85, 0x11046, 0x11046, 0x11070, 0x11070, 0x1107F, 0x1107F,
        0x110B9, 0x110BA, 0x11100, 0x11102, 0x11133, 0x11134, 0x11173, 0x11173, 0x111C0, 0x111C0, 0x111CA, 0x111CA,
        0x11235, 0x11236, 0x112E9, 0x112EA, 0x1133B, 0x1133C, 0x1134D, 0x1134D, 0x11366, 0x1136C, 0x11370, 0x11374,
        0x11442, 0x11442, 0x11446, 0x11446, 0x1145E, 0x1145E, 0x114C2, 0x114C3, 0x115BF, 0x115C0, 0x1163F, 0x1163F,
        0x116B6, 0x116B7, 0x1172B, 0x1172B, 0x11839, 0x1183A, 0x1193D, 0x1193E, 0x11943, 0x11943, 0x119E0, 0x119E0,
        0x11A34, 0x11A34, 0x11A47, 0x11A47, 0x11A99, 0x11A99, 0x11C3F, 0x11C3F, 0x11D42, 0x11D42, 0x11D44, 0x11D45,
        0x11D97, 0x11D97, 0x11F41, 0x11F42, 0x16AF0, 0x16AF4, 0x16B30, 0x16B36, 0x16FF0, 0x16FF1, 0x1BC9E, 0x1BC9E,
        0x1D165, 0x1D169, 0x1D16D, 0x1D172, 0x1D17B, 0x1D182, 0x1D185, 0x1D18B, 0x1D1AA, 0x1D1AD, 0x1D242, 0x1D244,
        0x1E000, 0x1E006, 0x1E008, 0x1E018, 0x1E01B, 0x1E021, 0x1E023, 0x1E024, 0x1E026, 0x1E02A, 0x1E08F, 0x1E08F,
        0x1E130, 0x1E136, 0x1E2AE, 0x1E2AE, 0x1E2EC, 0x1E2EF, 0x1E4EC, 0x1E4EF, 0x1E8D0, 0x1E8D6, 0x1E944, 0x1E94A,
    ];

    // Repliements de casse de Python (str.casefold) qui diffèrent de la minuscule
    // simple : « ß » → « ss », « ς » → « σ », ligatures, cherokee… Le reste suit
    // Rune.ToLowerInvariant. Format « code:remplacement ; ».
    private static readonly Dictionary<int, string> FoldSpecial = ParseFold(
        "B5:3BC;DF:73 73;130:69 307;149:2BC 6E;17F:73;1F0:6A 30C;345:3B9;390:3B9 308 301;3B0:3C5 308 301;" +
        "3C2:3C3;3D0:3B2;3D1:3B8;3D5:3C6;3D6:3C0;3F0:3BA;3F1:3C1;3F5:3B5;587:565 582;13F8:13F0;13F9:13F1;" +
        "13FA:13F2;13FB:13F3;13FC:13F4;13FD:13F5;1C80:432;1C81:434;1C82:43E;1C83:441;1C84:442;1C85:442;" +
        "1C86:44A;1C87:463;1C88:A64B;1E96:68 331;1E97:74 308;1E98:77 30A;1E99:79 30A;1E9A:61 2BE;" +
        "1E9B:1E61;1E9E:73 73;1F50:3C5 313;1F52:3C5 313 300;1F54:3C5 313 301;1F56:3C5 313 342;" +
        "1F80:1F00 3B9;1F81:1F01 3B9;1F82:1F02 3B9;1F83:1F03 3B9;1F84:1F04 3B9;1F85:1F05 3B9;" +
        "1F86:1F06 3B9;1F87:1F07 3B9;1F88:1F00 3B9;1F89:1F01 3B9;1F8A:1F02 3B9;1F8B:1F03 3B9;" +
        "1F8C:1F04 3B9;1F8D:1F05 3B9;1F8E:1F06 3B9;1F8F:1F07 3B9;1F90:1F20 3B9;1F91:1F21 3B9;" +
        "1F92:1F22 3B9;1F93:1F23 3B9;1F94:1F24 3B9;1F95:1F25 3B9;1F96:1F26 3B9;1F97:1F27 3B9;" +
        "1F98:1F20 3B9;1F99:1F21 3B9;1F9A:1F22 3B9;1F9B:1F23 3B9;1F9C:1F24 3B9;1F9D:1F25 3B9;" +
        "1F9E:1F26 3B9;1F9F:1F27 3B9;1FA0:1F60 3B9;1FA1:1F61 3B9;1FA2:1F62 3B9;1FA3:1F63 3B9;" +
        "1FA4:1F64 3B9;1FA5:1F65 3B9;1FA6:1F66 3B9;1FA7:1F67 3B9;1FA8:1F60 3B9;1FA9:1F61 3B9;" +
        "1FAA:1F62 3B9;1FAB:1F63 3B9;1FAC:1F64 3B9;1FAD:1F65 3B9;1FAE:1F66 3B9;1FAF:1F67 3B9;" +
        "1FB2:1F70 3B9;1FB3:3B1 3B9;1FB4:3AC 3B9;1FB6:3B1 342;1FB7:3B1 342 3B9;1FBC:3B1 3B9;1FBE:3B9;" +
        "1FC2:1F74 3B9;1FC3:3B7 3B9;1FC4:3AE 3B9;1FC6:3B7 342;1FC7:3B7 342 3B9;1FCC:3B7 3B9;" +
        "1FD2:3B9 308 300;1FD3:3B9 308 301;1FD6:3B9 342;1FD7:3B9 308 342;1FE2:3C5 308 300;" +
        "1FE3:3C5 308 301;1FE4:3C1 313;1FE6:3C5 342;1FE7:3C5 308 342;1FF2:1F7C 3B9;1FF3:3C9 3B9;" +
        "1FF4:3CE 3B9;1FF6:3C9 342;1FF7:3C9 342 3B9;1FFC:3C9 3B9;AB70:13A0;AB71:13A1;AB72:13A2;AB73:13A3;" +
        "AB74:13A4;AB75:13A5;AB76:13A6;AB77:13A7;AB78:13A8;AB79:13A9;AB7A:13AA;AB7B:13AB;AB7C:13AC;" +
        "AB7D:13AD;AB7E:13AE;AB7F:13AF;AB80:13B0;AB81:13B1;AB82:13B2;AB83:13B3;AB84:13B4;AB85:13B5;" +
        "AB86:13B6;AB87:13B7;AB88:13B8;AB89:13B9;AB8A:13BA;AB8B:13BB;AB8C:13BC;AB8D:13BD;AB8E:13BE;" +
        "AB8F:13BF;AB90:13C0;AB91:13C1;AB92:13C2;AB93:13C3;AB94:13C4;AB95:13C5;AB96:13C6;AB97:13C7;" +
        "AB98:13C8;AB99:13C9;AB9A:13CA;AB9B:13CB;AB9C:13CC;AB9D:13CD;AB9E:13CE;AB9F:13CF;ABA0:13D0;" +
        "ABA1:13D1;ABA2:13D2;ABA3:13D3;ABA4:13D4;ABA5:13D5;ABA6:13D6;ABA7:13D7;ABA8:13D8;ABA9:13D9;" +
        "ABAA:13DA;ABAB:13DB;ABAC:13DC;ABAD:13DD;ABAE:13DE;ABAF:13DF;ABB0:13E0;ABB1:13E1;ABB2:13E2;" +
        "ABB3:13E3;ABB4:13E4;ABB5:13E5;ABB6:13E6;ABB7:13E7;ABB8:13E8;ABB9:13E9;ABBA:13EA;ABBB:13EB;" +
        "ABBC:13EC;ABBD:13ED;ABBE:13EE;ABBF:13EF;FB00:66 66;FB01:66 69;FB02:66 6C;FB03:66 66 69;" +
        "FB04:66 66 6C;FB05:73 74;FB06:73 74;FB13:574 576;FB14:574 565;FB15:574 56B;FB16:57E 576;" +
        "FB17:574 56D;");
    private static Dictionary<int, string> ParseFold(string table)
    {
        var map = new Dictionary<int, string>();
        foreach (var item in table.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var (code, target) = (item[..item.IndexOf(':')], item[(item.IndexOf(':') + 1)..]);
            var sb = new StringBuilder();
            foreach (var hex in target.Split(' ')) sb.Append(char.ConvertFromUtf32(int.Parse(hex, NumberStyles.HexNumber)));
            map[int.Parse(code, NumberStyles.HexNumber)] = sb.ToString();
        }
        return map;
    }

    /// <summary><c>unicodedata.combining(c) != 0</c>.</summary>
    public static bool IsCombining(int codePoint)
    {
        int lo = 0, hi = CombiningRanges.Length / 2 - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (codePoint < CombiningRanges[2 * mid]) hi = mid - 1;
            else if (codePoint > CombiningRanges[2 * mid + 1]) lo = mid + 1;
            else return true;
        }
        return false;
    }

    /// <summary>Normalisation Unicode tolérante : une demi-paire de substitution (nom de fichier abîmé) devient U+FFFD au lieu de lever une exception.</summary>
    public static string Normalize(string text, NormalizationForm form)
    {
        try
        {
            return text.Normalize(form);
        }
        catch (ArgumentException)
        {
            // .NET refuse les demi-paires et certains points de code (non-caractères) que
            // Python laisse passer tels quels : on normalise les tronçons valides et on
            // recopie les autres à l'identique.
            var sb = new StringBuilder(text.Length);
            var segment = new StringBuilder();
            for (var i = 0; i < text.Length; i++)
            {
                var length = char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? 2 : 1;
                var piece = text.Substring(i, length);
                i += length - 1;
                if (IsNormalizable(piece, form)) { segment.Append(piece); continue; }
                sb.Append(segment.ToString().Normalize(form)).Append(piece);
                segment.Clear();
            }
            return sb.Append(segment.ToString().Normalize(form)).ToString();
        }
    }

    private static bool IsNormalizable(string piece, NormalizationForm form)
    {
        try
        {
            piece.Normalize(form);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary><c>"".join(c for c in text if not unicodedata.combining(c))</c>.</summary>
    public static string RemoveCombining(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
            if (!IsCombining(rune.Value)) sb.Append(rune.ToString());
        return sb.ToString();
    }

    /// <summary><c>str.casefold()</c> de Python (repliement complet, « ß » → « ss »).</summary>
    public static string CaseFold(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            if (FoldSpecial.TryGetValue(rune.Value, out var folded)) sb.Append(folded);
            // Le cherokee se replie sur ses majuscules : casefold les laisse telles quelles.
            else if (rune.Value is >= 0x13A0 and <= 0x13F5) sb.Append(rune.ToString());
            else sb.Append(Rune.ToLowerInvariant(rune).ToString());
        }
        return sb.ToString();
    }

    /// <summary>Blanc au sens de <c>str.isspace()</c>.</summary>
    public static bool IsSpace(char c) => char.IsWhiteSpace(c) || c is >= '\x1c' and <= '\x1f';

    /// <summary><c>str.strip()</c> sans argument.</summary>
    public static string Strip(string text)
    {
        int start = 0, end = text.Length;
        while (start < end && IsSpace(text[start])) start++;
        while (end > start && IsSpace(text[end - 1])) end--;
        return text[start..end];
    }

    /// <summary><c>str.strip(chars)</c> : retire aux deux bouts tout caractère de l'ensemble.</summary>
    public static string Strip(string text, string chars) => text.Trim(chars.ToCharArray());

    /// <summary><c>str.isdigit()</c> : chiffres décimaux et chiffres « exposants » (« ² »).</summary>
    public static bool IsDigit(string text)
    {
        if (text.Length == 0) return false;
        for (var i = 0; i < text.Length; i += char.IsSurrogatePair(text, i) ? 2 : 1)
            if (CharUnicodeInfo.GetDigitValue(text, i) < 0) return false;
        return true;
    }
}
