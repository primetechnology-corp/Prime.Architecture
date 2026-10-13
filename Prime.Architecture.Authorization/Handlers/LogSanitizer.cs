using System.Text;

namespace Prime.Architecture.Authorization.Handlers;

/// <summary>
/// Neutraliza valores controlados pelo cliente antes de registrá-los em log (evita forjar linhas de log
/// com quebras de linha ou caracteres de controle) e limita o tamanho.
/// </summary>
internal static class LogSanitizer
{
    private const int Maximo = 100;

    public static string Limpar(string? valor)
    {
        if (string.IsNullOrEmpty(valor))
            return "(vazio)";

        var sb = new StringBuilder(valor!.Length > Maximo ? Maximo : valor.Length);
        foreach (var c in valor)
        {
            if (sb.Length >= Maximo)
            {
                sb.Append('…');
                break;
            }

            sb.Append(char.IsControl(c) || c == (char)0x2028 || c == (char)0x2029 ? '_' : c);
        }

        return sb.ToString();
    }
}
