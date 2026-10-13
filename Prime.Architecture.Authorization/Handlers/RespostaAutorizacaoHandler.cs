using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Prime.Architecture.Authorization.Configuration;
using Prime.Architecture.Authorization.Diagnostics;

namespace Prime.Architecture.Authorization.Handlers;

/// <summary>
/// Padroniza as respostas 401/403 quando <see cref="ResourceAuthorizationOptions.EscreverResposta"/> está definido.
/// Caso contrário, mantém o comportamento padrão do ASP.NET Core.
/// </summary>
public class RespostaAutorizacaoHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _padrao = new();
    private readonly ResourceAuthorizationOptions _options;
    private readonly ILogger<RespostaAutorizacaoHandler> _logger;

    public RespostaAutorizacaoHandler(IOptions<ResourceAuthorizationOptions> options, ILogger<RespostaAutorizacaoHandler> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (!authorizeResult.Succeeded)
            AuthorizationMetrics.RegistrarRequisicaoNegada(authorizeResult.Challenged ? StatusCodes.Status401Unauthorized : StatusCodes.Status403Forbidden);

        if (!authorizeResult.Succeeded)
            _logger.LogWarning("Requisição {Metodo} {Caminho} negada: {Motivo}",
                LogSanitizer.Limpar(context.Request.Method), LogSanitizer.Limpar(context.Request.Path.Value),
                authorizeResult.Challenged ? "não autenticado (401)" : "sem permissão (403)");

        if (_options.EscreverResposta != null && !authorizeResult.Succeeded)
        {
            var status = authorizeResult.Challenged ? StatusCodes.Status401Unauthorized : StatusCodes.Status403Forbidden;
            context.Response.StatusCode = status;
            await _options.EscreverResposta(context, status);
            return;
        }

        await _padrao.HandleAsync(next, context, policy, authorizeResult);
    }
}
