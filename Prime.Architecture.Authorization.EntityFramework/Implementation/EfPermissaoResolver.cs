using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Prime.Architecture.Authorization.EntityFramework.Configuration;
using Prime.Architecture.Authorization.EntityFramework.Entities;
using Prime.Architecture.Authorization.Interfaces;

namespace Prime.Architecture.Authorization.EntityFramework.Implementation;

/// <summary>
/// Resolve as permissões consultando o banco, com cache:
/// (permissões concedidas ao perfil + concessões do usuário) − negações do usuário.
/// Somente permissões ativas valem.
/// </summary>
public class EfPermissaoResolver<TContext> : IPermissaoResolver where TContext : DbContext
{
    private readonly TContext _db;
    private readonly SingleFlightCache _cache;
    private readonly EfPermissaoOptions _options;
    private readonly IUsuarioPerfilProvider _perfilProvider;
    private readonly ILogger<EfPermissaoResolver<TContext>> _logger;

    public EfPermissaoResolver(
        TContext db,
        SingleFlightCache cache,
        IOptions<EfPermissaoOptions> options,
        IUsuarioPerfilProvider perfilProvider,
        ILogger<EfPermissaoResolver<TContext>> logger)
    {
        _db = db;
        _cache = cache;
        _options = options.Value;
        _perfilProvider = perfilProvider;
        _logger = logger;
    }

    public async Task<IReadOnlySet<string>> ObterAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var concedidas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Falha fechada: sem usuário/perfil vigente (inexistente, inativo, sem claims) não há permissão alguma
        var info = await _perfilProvider.ObterAsync(user, cancellationToken);
        if (info is null)
        {
            if (user.Identity?.IsAuthenticated == true)
                _logger.LogWarning("Usuário autenticado sem perfil vigente: nenhuma permissão concedida");
            return concedidas;
        }

        concedidas.UnionWith(await ObterCacheadoAsync(CacheKeys.Perfil(info.PerfilId), () => PermissoesDoPerfilAsync(info.PerfilId, cancellationToken)));

        var excecoes = await ObterCacheadoAsync(CacheKeys.Usuario(info.UsuarioId), () => ExcecoesDoUsuarioAsync(info.UsuarioId, cancellationToken));
        foreach (var excecao in excecoes)
        {
            // Formato "+sigla" (concede) ou "-sigla" (nega)
            if (excecao[0] == '+') concedidas.Add(excecao.Substring(1));
        }

        // Negações vencem qualquer concessão
        concedidas.ExceptWith(excecoes.Where(e => e[0] == '-').Select(e => e.Substring(1)));

        return concedidas;
    }

    private async Task<IReadOnlyList<string>> PermissoesDoPerfilAsync(int perfilId, CancellationToken ct)
        => await _db.Set<PerfilPermissao>()
            .AsNoTracking()
            .Where(pp => pp.PerfilId == perfilId && pp.Concedida && pp.Permissao.Ativo)
            .Select(pp => pp.Permissao.Sigla)
            .ToListAsync(ct);

    private async Task<IReadOnlyList<string>> ExcecoesDoUsuarioAsync(int usuarioId, CancellationToken ct)
        => (await _db.Set<UsuarioPermissao>()
                .AsNoTracking()
                .Where(up => up.UsuarioId == usuarioId && up.Permissao.Ativo)
                .Select(up => new { up.Permissao.Sigla, up.Concedida })
                .ToListAsync(ct))
            .Select(x => (x.Concedida ? "+" : "-") + x.Sigla)
            .ToList();

    private Task<IReadOnlyList<string>> ObterCacheadoAsync(string key, Func<Task<IReadOnlyList<string>>> carregar)
        => _cache.GetOrCreateAsync(key, _options.DuracaoCache, carregar);
}

internal static class CacheKeys
{
    public static string Perfil(int id) => $"prime:perm:perfil:{id}";
    public static string Usuario(int id) => $"prime:perm:usuario:{id}";
    public static string UsuarioPerfil(int id) => $"prime:perm:usuarioperfil:{id}";
}

internal sealed class PermissaoCacheInvalidator : IPermissaoCacheInvalidator
{
    private readonly SingleFlightCache _cache;
    private readonly IServiceProvider _services;
    private readonly InstanciaId _instancia;

    public PermissaoCacheInvalidator(SingleFlightCache cache, IServiceProvider services, InstanciaId instancia)
    {
        _cache = cache;
        _services = services;
        _instancia = instancia;
    }

    public void InvalidarPerfil(int perfilId) => _cache.Invalidar(CacheKeys.Perfil(perfilId));
    public void InvalidarUsuario(int usuarioId)
    {
        _cache.Invalidar(CacheKeys.Usuario(usuarioId));
        _cache.Invalidar(CacheKeys.UsuarioPerfil(usuarioId));
    }

    public void InvalidarTudo() => _cache.InvalidarTudo();

    public async Task InvalidarTudoAsync(CancellationToken cancellationToken = default)
    {
        InvalidarTudo();
        await PublicarAsync(PermissaoInvalidacaoTipo.Todos, 0, cancellationToken);
    }

    public async Task InvalidarPerfilAsync(int perfilId, CancellationToken cancellationToken = default)
    {
        InvalidarPerfil(perfilId);
        await PublicarAsync(PermissaoInvalidacaoTipo.Perfil, perfilId, cancellationToken);
    }

    public async Task InvalidarUsuarioAsync(int usuarioId, CancellationToken cancellationToken = default)
    {
        InvalidarUsuario(usuarioId);
        await PublicarAsync(PermissaoInvalidacaoTipo.Usuario, usuarioId, cancellationToken);
    }

    private async Task PublicarAsync(PermissaoInvalidacaoTipo tipo, int id, CancellationToken ct)
    {
        var bus = _services.GetService(typeof(IPermissaoInvalidationBus)) as IPermissaoInvalidationBus;
        if (bus != null)
            await bus.PublicarAsync(new PermissaoInvalidacao(tipo, id, _instancia.Valor), ct);
    }
}

/// <summary>Identifica esta instância da aplicação (um valor por contêiner de DI).</summary>
internal sealed class InstanciaId
{
    public string Valor { get; } = Guid.NewGuid().ToString("N");
}

/// <summary>Recebe invalidações publicadas por outras instâncias e limpa o cache local.</summary>
internal sealed class PermissaoInvalidacaoSubscriber : Microsoft.Extensions.Hosting.IHostedService
{
    private readonly IPermissaoInvalidationBus _bus;
    private readonly IPermissaoCacheInvalidator _invalidator;
    private readonly InstanciaId _instancia;
    private IDisposable? _assinatura;

    public PermissaoInvalidacaoSubscriber(IPermissaoInvalidationBus bus, IPermissaoCacheInvalidator invalidator, InstanciaId instancia)
    {
        _bus = bus;
        _invalidator = invalidator;
        _instancia = instancia;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _assinatura = _bus.Assinar(mensagem =>
        {
            // Mensagens que esta própria instância publicou já foram aplicadas localmente
            if (mensagem.Origem == _instancia.Valor) return Task.CompletedTask;

            // Tipos desconhecidos (de uma versão mais nova) são ignorados, nunca tratados como outro tipo
            switch (mensagem.Tipo)
            {
                case PermissaoInvalidacaoTipo.Perfil: _invalidator.InvalidarPerfil(mensagem.Id); break;
                case PermissaoInvalidacaoTipo.Usuario: _invalidator.InvalidarUsuario(mensagem.Id); break;
                case PermissaoInvalidacaoTipo.Todos: _invalidator.InvalidarTudo(); break;
            }
            return Task.CompletedTask;
        });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _assinatura?.Dispose();
        return Task.CompletedTask;
    }
}
