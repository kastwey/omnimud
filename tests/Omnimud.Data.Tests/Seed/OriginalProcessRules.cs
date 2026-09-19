using System.Text.RegularExpressions;

namespace Omnimud.Data.Tests.Seed;

/// <summary>
/// ORACLE: the four processing rules of the original client (trunk\ProcessRules\C*.cs) ported to
/// C# statement by statement, bugs included, to compare the Lua scripts against. The original client
/// (FrmCliente.cs) called ProcessMessage inside try/catch and treated an exception as "no message";
/// <see cref="Run"/> does the same. Comparisons are ordinal where the original used the culture
/// (no difference for these texts).
/// </summary>
internal static class OriginalProcessRules
{
    public static string? Run(Func<string, string?> rule, string msg)
    {
        try
        {
            return rule(msg);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static readonly string[] CanalesFormato1 =
        ["Clan", "Faccion", "Novatos", "Orden", "Concilio", "Gremio", "Raza", "Avatar", "Reino", "Trivial", "GOSOCIAL"];

    public static string? Balzhur(string msg)
    {
        string[] lineas;
        string linea;
        bool formato1;
        string[] palabras;
        string msgFinal = "";

        if (string.IsNullOrEmpty(msg)) return null;
        lineas = msg.Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries);
        if (lineas.Length == 0) return null;
        for (int i = 0; i < lineas.Length; i++)
        {
            formato1 = false;
            linea = lineas[i];
            if (string.IsNullOrEmpty(linea)) continue;
            palabras = linea.Split([' '], StringSplitOptions.RemoveEmptyEntries);
            if (palabras.Length > 1 && palabras[0].StartsWith('['))
            {
                foreach (string fc in CanalesFormato1)
                {
                    if ((palabras[0] == "[" + fc + ":" && palabras[1].EndsWith("]:", StringComparison.Ordinal) && palabras[2].StartsWith('\''))
                        || (palabras[0] == "[" + fc + "]:" || palabras[0] == "[" + fc + "]"))
                    {
                        formato1 = true;
                        break;
                    }
                }
            }

            if (formato1)
            {
                msgFinal += lineas[i] + "\r\n";
                continue;
            }
            if (linea.StartsWith("Charlas '", StringComparison.Ordinal)
                || linea.StartsWith("Cuentas a ", StringComparison.Ordinal)
                || linea.StartsWith("Dices '", StringComparison.Ordinal)
                || linea.StartsWith("Respondes a ", StringComparison.Ordinal)
                || linea.StartsWith("Susurras a ", StringComparison.Ordinal)
                || (palabras.Length > 2 && palabras[1] == "charla" && palabras[2].StartsWith('\''))
                || (palabras.Length > 3 && palabras[1] == "te" && palabras[2] == "responde" && palabras[3].StartsWith('\''))
                || (palabras.Length > 3 && palabras[1] == "te" && palabras[2] == "cuenta" && palabras[3].StartsWith('\''))
                || (palabras.Length > 3 && palabras[1] == "te" && palabras[2] == "susurra" && palabras[3].StartsWith('\'')))
            {
                msgFinal += linea;
            }
        }
        if (msgFinal == string.Empty) return null;
        return msgFinal;
    }

    public static string? Callandor(string msg)
    {
        string[] lineas, palabras;
        int i, index;
        string mensaje;

        if (string.IsNullOrEmpty(msg)) return msg;
        lineas = msg.Split(['\n'], StringSplitOptions.None);
        for (i = 0; i < lineas.Length; i++)
        {
            if (string.IsNullOrEmpty(lineas[i])) continue;
            if (S(lineas[i], "Solicitas '") || S(lineas[i], "Instruyes") || S(lineas[i], "Dices '") || S(lineas[i], "Charlas '")
                || S(lineas[i], "Transmites a ") || S(lineas[i], "<Comunicas> '") || S(lineas[i], "Susurras '") || S(lineas[i], "Gritas '")
                || S(lineas[i], "Conspiras '") || S(lineas[i], "Gruñes '") || S(lineas[i], "Transmites cariñosamente a "))
            {
                mensaje = string.Join("\n", lineas, i, lineas.Length - i);
                if ((index = mensaje.IndexOf("'\n", StringComparison.Ordinal)) == -1) return mensaje;
                return mensaje.Substring(0, index + 3);
            }
            palabras = lineas[i].Split([' '], StringSplitOptions.None);
            if ((palabras.Length > 3 && palabras[1] == "te" && palabras[2] == "transmite" && palabras[3] != string.Empty && palabras[3].Substring(0, 1) == "'")
                || (palabras.Length > 4 && palabras[1] == "dice" && palabras[2] == "al" && palabras[3] == "grupo" && palabras[4] != string.Empty && palabras[4].Substring(0, 1) == "'")
                || (palabras.Length > 4 && palabras[1] == "gruñe" && palabras[2] == "al" && palabras[3] == "grupo" && palabras[4] != string.Empty && palabras[4].Substring(0, 1) == "'")
                || (palabras.Length > 2 && (palabras[1] == "dice" || palabras[1] == "charla" || palabras[1] == "conspira" || palabras[1] == "comunica" || palabras[1] == "gruñe") && palabras[2] != string.Empty && palabras[2].StartsWith('\''))
                || (palabras.Length > 2 && palabras[1] == "susurra" && palabras[2] != string.Empty && palabras[2][0] == '\'')
                || (palabras.Length > 5 && palabras[1] == "grita" && palabras[2] == "cerca" && palabras[3] == "de" && palabras[4] == "aqui" && palabras[5] != string.Empty && palabras[5][0] == '\'')
                || (palabras.Length > 2 && palabras[1] == "grita" && palabras[2] != string.Empty && palabras[2][0] == '\'')
                || (palabras.Length >= 5 && palabras[1] == "dice" && palabras[2] == "al" && palabras[3] == "equipo" && !string.IsNullOrEmpty(palabras[4]) && palabras[4].StartsWith('\''))
                || (palabras.Length >= 3 && palabras[0].StartsWith('<') && palabras[0].EndsWith('>') && palabras[1] == "conversa" && !string.IsNullOrEmpty(palabras[2]) && palabras[2].StartsWith('\''))
                || (palabras.Length >= 5 && palabras[1] == "te" && palabras[2] == "transmite" && palabras[3] == "cariñosamente" && !string.IsNullOrEmpty(palabras[4]) && palabras[4].StartsWith('\''))
                || (palabras.Length >= 3 && palabras[1] == "solicita" && !string.IsNullOrEmpty(palabras[2]) && palabras[2].StartsWith('\''))
                || (palabras.Length >= 3 && palabras[0] == "Trivial:" && !string.IsNullOrEmpty(palabras[2]) && palabras[2].StartsWith('\'')))
            {
                mensaje = string.Join("\n", lineas, i, lineas.Length - i);
                if ((index = mensaje.IndexOf("'\n", StringComparison.Ordinal)) == -1) return mensaje;
                return mensaje.Substring(0, index + 3);
            }
        }
        return null;

        static bool S(string text, string prefix) => text.StartsWith(prefix, StringComparison.Ordinal);
    }

    public static string? Simauria(string msg)
    {
        string[] lineas, palabras;
        int i, lengthCount = 0;

        if (string.IsNullOrEmpty(msg)) return msg;
        lineas = msg.Split(['\n'], StringSplitOptions.None);
        for (i = 0; i < lineas.Length; i++)
        {
            if (i > 0 && lineas[i - 1] == string.Empty && lineas[i] != string.Empty && lineas[i].Substring(0, 1) == "[" && msg.IndexOf(']', lengthCount) > -1)
            {
                return string.Join("\n", lineas, i, lineas.Length - i);
            }
            palabras = lineas[i].Split([' '], StringSplitOptions.None);
            if ((palabras.Length > 2 && palabras[1] == "dice:" && (!string.IsNullOrEmpty(palabras[2]) && palabras[2].Substring(0, 1) == "'"))
                || (palabras.Length > 3 && palabras[1] == "te" && palabras[2] == "dice:" && (!string.IsNullOrEmpty(palabras[3]) && palabras[3].Substring(0, 1) == "'"))
                || (palabras.Length > 1 && palabras[0] == "Dices:" && (!string.IsNullOrEmpty(palabras[1]) && palabras[1].Substring(0, 1) == "'"))
                || (palabras.Length > 3 && palabras[0] == "Dijiste" && palabras[1] == "a" && palabras[2][palabras[2].Length - 1] == ':'))
            {
                int apostrof;
                string mensaje = string.Join("\n", lineas, i, lineas.Length - i);
                if (palabras.Length > 1 && (palabras[1] == "a" || palabras[1] == "te")) return mensaje;

                apostrof = mensaje.LastIndexOf('\'');
                if (apostrof == -1) return mensaje;
                return mensaje.Substring(0, apostrof + 1);
            }
            lengthCount += lineas[i].Length + 1;
        }
        return null;
    }

    private static readonly Regex[] CyberlifeRegexes = new[]
    {
        "^(Murmuras|Dices) con acento .+?, \".+?\"$",
        "^(Murmuras|Dices): \".+?\"$",
        "^gritas: \".+?\"$",
        "^.+? grita: \".+?\"$",
        "^.+? grita cerca de aquí: \".+?\"$",
        "^\\[.+?\\] .+?: \".+?\"$",
        "^\\[.+?:\\] \".+?\"$",
        "^.+? (Murmura|Dice) con acento .+?, \".+?\"$",
        "^.+? (Murmura|Dice): \".+?\"$",
        "^\".+? chatea: \".+?\"$",
        "^Transmites a .+?, \".+?\"$",
        "^.+? te transmite, \".+?\"$",
        "^\\*{2,} .+? Ha solicitado asistencia con el siguiente motivo: .+?\\*{2,}$",
        "^.+?(te)? dice por teléfono, \".+?\"$",
        "^dices por teléfono, \".+?\"$"
    }.Select(p => new Regex(p, RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray();

    public static string? Cyberlife(string msg)
    {
        foreach (var r in CyberlifeRegexes)
        {
            Match m = r.Match(msg);
            if (!m.Success)
            {
                continue;
            }
            return m.Value;
        }
        return null;
    }

    /// <summary>Every line of the block that the original could have returned (some regex matches it).</summary>
    public static IReadOnlyList<string> CyberlifeAllLines(string msg) =>
        msg.Split('\n').Where(line => CyberlifeRegexes.Any(r => r.IsMatch(line))).ToList();
}
