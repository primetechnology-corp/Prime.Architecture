using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Prime.Architecture.Authorization.Diagnostics;
using Prime.Architecture.Authorization.Interfaces;
using Prime.Architecture.Authorization.Requirements;

namespace Prime.Architecture.Authorization.Handlers;

public class PermissaoHandler : AuthorizationHandler<PermissaoRequirement>
{
    private readonly IPermissaoResolver _resolver;
    private readonly ILogger<PermissaoHandler> _logger;

    public PermissaoHandler(IPermissaoResolver resolver, ILogger<PermissaoHandler> logger)
    {
        _resolver = resolver;
        _logger = logger;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissaoRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return;

        System.Collections.Generic.IReadOnlySet<string> permissoes;
        try
        {
            permissoes = await _resolver.ObterAsync(context.User);
        }
        catch (System.Exception ex) when (ex is not System.OperationCanceledException)
        {
            // Falha fechada: se não foi possível saber as permissões (ex.: banco fora do ar), o acesso é negado (403)
            AuthorizationMetrics.RegistrarFalhaNoResolver();
            _logger.LogError(ex, "Falha ao obter permissões do usuário {UsuarioId}: acesso negado a {Permissao}",
                LogSanitizer.Limpar(context.User.FindFirstValue(ClaimTypes.NameIdentifier)), requirement.Permissao);
            return;
        }

        if (permissoes.Contains(requirement.Permissao))
        {
            context.Succeed(requirement);
            return;
        }

        AuthorizationMetrics.RegistrarPermissaoNegada(requirement.Permissao);

        // Auditoria: acesso negado por falta de permissão (nunca registra token nem claims)
        _logger.LogWarning("Acesso negado: usuário {UsuarioId} sem a permissão {Permissao}",
            LogSanitizer.Limpar(context.User.FindFirstValue(ClaimTypes.NameIdentifier)), requirement.Permissao);
    }
}
