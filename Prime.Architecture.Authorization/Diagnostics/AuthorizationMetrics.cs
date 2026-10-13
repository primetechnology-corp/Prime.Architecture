using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace Prime.Architecture.Authorization.Diagnostics;

/// <summary>
/// Métricas (System.Diagnostics.Metrics) para monitorar negações. Com OpenTelemetry: <c>.AddMeter(AuthorizationMetrics.NomeDoMeter)</c>.
/// </summary>
public static class AuthorizationMetrics
{
    public const string NomeDoMeter = "Prime.Architecture.Authorization";

    private static readonly Meter Meter = new(NomeDoMeter, "1.0.0");

    private static readonly Counter<long> PermissaoNegada =
        Meter.CreateCounter<long>("prime.authorization.permissao_negada", description: "Verificações de permissão negadas, por permissão.");

    private static readonly Counter<long> RequisicaoNegada =
        Meter.CreateCounter<long>("prime.authorization.requisicao_negada", description: "Requisições respondidas com 401 ou 403.");

    private static readonly Counter<long> FalhaNoResolver =
        Meter.CreateCounter<long>("prime.authorization.falha_no_resolver", description: "Verificações negadas porque o IPermissaoResolver lançou exceção (ex.: banco fora do ar).");

    internal static void RegistrarFalhaNoResolver() => FalhaNoResolver.Add(1);

    internal static void RegistrarPermissaoNegada(string permissao)
        => PermissaoNegada.Add(1, new KeyValuePair<string, object?>("permissao", permissao));

    internal static void RegistrarRequisicaoNegada(int statusCode)
        => RequisicaoNegada.Add(1, new KeyValuePair<string, object?>("status_code", statusCode));
}
