using System.Text.RegularExpressions;

namespace WPSupportTools.Core.Telemetry;

/// <summary>
/// Limpa o log de um atendimento antes de ir para o painel: tira a licença do UniLab (código ADM + nome),
/// usuários (RDP/UniLab) e o nome do usuário do Windows nos caminhos. Senhas e tokens nunca vão para o log.
/// O log continua dizendo o que aconteceu (impressoras, portas, filas, etapas e erros).
/// </summary>
public static partial class LogRedactor
{
    /// <summary>Tamanho máximo enviado (o banco aceita até 20000 caracteres).</summary>
    public const int MaxLength = 16000;

    /// <summary>Licença do UniLab: 5 dígitos do código ADM seguidos do nome (ex.: "@01451fulano21").</summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9])@?\d{5}[A-Za-z][A-Za-z0-9._-]*")]
    private static partial Regex License();

    /// <summary>"usuário 'fulano'" (usuário RDP / do UniLab).</summary>
    [GeneratedRegex(@"(usu[aá]rio\s+)'[^']*'", RegexOptions.IgnoreCase)]
    private static partial Regex QuotedUser();

    /// <summary>"Usuario=fulano" (linha do .ini do UniLab).</summary>
    [GeneratedRegex(@"(Usuario=)[^\s;,]+", RegexOptions.IgnoreCase)]
    private static partial Regex IniUser();

    /// <summary>Pasta do usuário do Windows: C:\Users\fulano\...</summary>
    [GeneratedRegex(@"([A-Za-z]:[\\/](?:Users|Usuários)[\\/])[^\\/\s'"",;]+", RegexOptions.IgnoreCase)]
    private static partial Regex UserFolder();

    public static string Redact(string text)
    {
        var result = UserFolder().Replace(text, "$1***");
        result = QuotedUser().Replace(result, "$1'***'");
        result = IniUser().Replace(result, "$1***");
        result = License().Replace(result, "[licença]");
        return Trim(result);
    }

    /// <summary>Log muito grande: mantém o começo (o que foi encontrado) e o fim (onde falhou).</summary>
    private static string Trim(string text)
    {
        if (text.Length <= MaxLength)
            return text;

        const string cut = "\n[... trecho do meio omitido ...]\n";
        var head = MaxLength / 4;
        var tail = MaxLength - head - cut.Length;
        return string.Concat(text.AsSpan(0, head), cut, text.AsSpan(text.Length - tail));
    }
}
