using Microsoft.AspNetCore.Mvc;
using Prime.Architecture.Authorization.Auditing;

namespace Prime.Architecture.Authorization.Examples.Custom;

public class ExemploAuditoria(IPermissaoAuditor auditor) : ControllerBase
{
    public async Task RegistrarNegacaoAsync()
    {
        await auditor.RegistrarAsync(new PermissaoAuditoriaEvento(
            PermissaoAuditoriaTipo.Negada, Ator: User.Identity?.Name, Alvo: "usuario 7", Permissao: "produto:delete"));
    }
}
