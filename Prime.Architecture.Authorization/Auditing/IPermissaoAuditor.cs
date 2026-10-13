using System.Threading;
using System.Threading.Tasks;

namespace Prime.Architecture.Authorization.Auditing;

public enum PermissaoAuditoriaTipo
{
    Concedida,
    Negada,
    Revogada,
    PerfilAlterado,
    UsuarioBloqueado,
    UsuarioDesbloqueado,
    Outro
}

/// <summary>Alteração administrativa de permissões (quem fez, em quem, o quê).</summary>
public record PermissaoAuditoriaEvento(
    PermissaoAuditoriaTipo Tipo,
    string? Ator,
    string? Alvo,
    string? Permissao = null,
    string? Detalhe = null);

/// <summary>
/// Registra alterações administrativas de permissões. A implementação padrão grava em log;
/// substitua para gravar em tabela de auditoria. Chame nos endpoints/serviços que alteram perfis e permissões.
/// </summary>
public interface IPermissaoAuditor
{
    Task RegistrarAsync(PermissaoAuditoriaEvento evento, CancellationToken cancellationToken = default);
}
