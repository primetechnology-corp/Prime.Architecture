using System.Security.Claims;
using Prime.Architecture.Authorization.Extensions;
using Prime.Architecture.Authorization.Interfaces;

namespace Prime.Architecture.Authorization.Examples.Custom;

/// <summary>Exemplo de origem própria de permissões (ex.: serviço de identidade, cache distribuído, arquivo).</summary>
public interface IMeuServicoDePermissoes
{
    Task<IReadOnlyCollection<string>> ObterPermissoesAsync(string usuarioId, CancellationToken ct);
}

public class MeuPermissaoResolver(IMeuServicoDePermissoes servico) : IPermissaoResolver
{
    public async Task<IReadOnlySet<string>> ObterAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id is null)
            return new HashSet<string>();   // sem identidade: nenhuma permissão (falha fechada)

        var permissoes = await servico.ObterPermissoesAsync(id, cancellationToken);
        return permissoes.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}

public static class RegistroDoResolverPersonalizado
{
    public static void Registrar(IServiceCollection services)
    {
        services.AddResourceAuthorization();
        services.AddScoped<IPermissaoResolver, MeuPermissaoResolver>();   // substitui o resolver padrão (claims)
    }
}
