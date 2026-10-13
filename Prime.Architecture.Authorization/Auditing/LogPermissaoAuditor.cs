using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Prime.Architecture.Authorization.Handlers;

namespace Prime.Architecture.Authorization.Auditing;

public class LogPermissaoAuditor : IPermissaoAuditor
{
    private readonly ILogger<LogPermissaoAuditor> _logger;

    public LogPermissaoAuditor(ILogger<LogPermissaoAuditor> logger)
    {
        _logger = logger;
    }

    public Task RegistrarAsync(PermissaoAuditoriaEvento evento, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Auditoria de permissões: {Tipo} por {Ator} em {Alvo}, permissão {Permissao}. {Detalhe}",
            evento.Tipo, LogSanitizer.Limpar(evento.Ator), LogSanitizer.Limpar(evento.Alvo),
            LogSanitizer.Limpar(evento.Permissao), LogSanitizer.Limpar(evento.Detalhe));
        return Task.CompletedTask;
    }
}
