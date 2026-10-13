using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prime.Architecture.Authorization.EntityFramework.Entities;

namespace Prime.Architecture.Authorization.EntityFramework.Implementation;

public record SincronizacaoResultado(int Criadas, int Reativadas, int Desativadas);

public class SincronizacaoOpcoes
{
    /// <summary>
    /// Fração máxima das permissões ativas que pode ser desativada de uma vez (padrão: 0,5).
    /// Protege contra sincronizar com o assembly errado ou uma lista vazia, o que deixaria todos sem acesso.
    /// Use 1 para permitir desativar tudo de forma consciente.
    /// </summary>
    public double LimiteDesativacao { get; set; } = 0.5;

    /// <summary>Apenas calcula o resultado, sem gravar nada.</summary>
    public bool Simular { get; set; }

    /// <summary>Quando informado, registra as permissões criadas, reativadas e desativadas (auditoria).</summary>
    public ILogger? Logger { get; set; }

    /// <summary>
    /// Quando informado (injete <see cref="IPermissaoCacheInvalidator"/>), invalida o cache de permissões de todas as
    /// instâncias se alguma permissão foi reativada ou desativada; sem isso, a mudança só vale quando o cache expira.
    /// Uma falha ao avisar as outras instâncias não desfaz a sincronização (já gravada): é registrada no <see cref="Logger"/>.
    /// </summary>
    public IPermissaoCacheInvalidator? Invalidador { get; set; }

    /// <summary>
    /// Tentativas máximas quando a gravação conflita com outra sincronização simultânea (ex.: duas instâncias subindo
    /// juntas violam o índice único, ou deadlock). A cada nova tentativa o estado é relido do banco. Padrão: 3.
    /// </summary>
    public int MaximoTentativas { get; set; } = 3;
}

/// <summary>
/// Sincroniza a tabela de permissões com as siglas existentes no código
/// (ex.: <c>PermissaoCatalogo.Descobrir</c>): cria as novas, reativa as que voltaram e desativa as órfãs.
/// </summary>
public static class PermissaoSincronizador
{
    /// <exception cref="InvalidOperationException">
    /// A sincronização desativaria mais permissões do que o <see cref="SincronizacaoOpcoes.LimiteDesativacao"/>; nada é gravado.
    /// </exception>
    public static async Task<SincronizacaoResultado> SincronizarAsync(
        this DbContext db,
        IEnumerable<string> siglas,
        Func<string, string>? descricao = null,
        SincronizacaoOpcoes? opcoes = null,
        CancellationToken cancellationToken = default)
    {
        opcoes ??= new SincronizacaoOpcoes();

        var esperadas = siglas
            .Select(s => s.Trim().ToLowerInvariant())
            .Where(s => s.Length > 0)
            .Distinct()
            .ToHashSet();

        var tentativas = Math.Max(1, opcoes.MaximoTentativas);
        for (var tentativa = 1; ; tentativa++)
        {
            try
            {
                var (resultado, novas, voltaram, desativadas) = await TentarAsync(db, esperadas, descricao, opcoes, cancellationToken);

                opcoes.Logger?.LogInformation(
                    "Sincronização de permissões{Simulada}: criadas [{Criadas}], reativadas [{Reativadas}], desativadas [{Desativadas}]",
                    opcoes.Simular ? " (simulada)" : "",
                    string.Join(", ", novas), string.Join(", ", voltaram), string.Join(", ", desativadas));

                if (!opcoes.Simular && opcoes.Invalidador != null && resultado.Reativadas + resultado.Desativadas > 0)
                    await InvalidarCacheAsync(opcoes, cancellationToken);

                return resultado;
            }
            catch (DbUpdateException ex) when (tentativa < tentativas && !cancellationToken.IsCancellationRequested)
            {
                // Outra sincronização gravou primeiro: descarta o que ficou pendente e relê o banco
                opcoes.Logger?.LogWarning(ex, "Conflito ao sincronizar permissões (tentativa {Tentativa} de {Total}); tentando novamente", tentativa, tentativas);
                foreach (var entrada in db.ChangeTracker.Entries<Permissao>().ToList())
                    entrada.State = EntityState.Detached;
            }
        }
    }

    private static async Task<(SincronizacaoResultado Resultado, List<string> Novas, List<string> Voltaram, List<string> Desativadas)> TentarAsync(
        DbContext db,
        HashSet<string> esperadas,
        Func<string, string>? descricao,
        SincronizacaoOpcoes opcoes,
        CancellationToken cancellationToken)
    {
        var existentes = await db.Set<Permissao>().ToListAsync(cancellationToken);
        var porSigla = existentes.ToDictionary(p => p.Sigla.ToLowerInvariant());

        var aDesativar = existentes
            .Where(p => p.Ativo && !esperadas.Contains(p.Sigla.ToLowerInvariant()))
            .ToList();

        var ativas = existentes.Count(p => p.Ativo);
        if (aDesativar.Count > 0 && aDesativar.Count > ativas * opcoes.LimiteDesativacao)
            throw new InvalidOperationException(
                $"Sincronização abortada: desativaria {aDesativar.Count} de {ativas} permissões ativas " +
                $"(limite {opcoes.LimiteDesativacao:P0}). Verifique os assemblies informados ou aumente o limite conscientemente.");

        var novas = new List<string>();
        var voltaram = new List<string>();

        foreach (var sigla in esperadas)
        {
            var texto = descricao?.Invoke(sigla) ?? sigla;

            if (!porSigla.TryGetValue(sigla, out var permissao))
            {
                novas.Add(sigla);
                if (!opcoes.Simular)
                    db.Set<Permissao>().Add(new Permissao { Sigla = sigla, Descricao = texto, Ativo = true });
                continue;
            }

            if (!permissao.Ativo)
                voltaram.Add(sigla);

            if (!opcoes.Simular)
            {
                permissao.Descricao = texto;
                permissao.Ativo = true;
            }
        }

        if (!opcoes.Simular)
        {
            foreach (var orfa in aDesativar)
                orfa.Ativo = false;

            await db.SaveChangesAsync(cancellationToken);
        }

        return (new SincronizacaoResultado(novas.Count, voltaram.Count, aDesativar.Count),
            novas, voltaram, aDesativar.Select(p => p.Sigla).ToList());
    }

    private static async Task InvalidarCacheAsync(SincronizacaoOpcoes opcoes, CancellationToken cancellationToken)
    {
        try
        {
            await opcoes.Invalidador!.InvalidarTudoAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // O banco já foi gravado: não falha a sincronização; as outras instâncias renovam o cache ao expirar
            opcoes.Logger?.LogError(ex, "Permissões sincronizadas, mas falhou ao invalidar o cache das outras instâncias");
        }
    }
}
