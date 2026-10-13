# Prime.Architecture.Authorization

Autorização por **recurso e permissão** (`recurso:acao`) para ASP.NET Core, baseada em policies.
Não depende de nome de controller: funciona em MVC, Minimal APIs e em serviços (`IAuthorizationService`).

**Requisitos:** `net10.0` (ASP.NET Core). Versão 1.0.0. Veja o [CHANGELOG](../CHANGELOG.md) e o [checklist de produção](../PRODUCAO.md).
Para permissões no banco, use também `Prime.Architecture.Authorization.EntityFramework`.

## Instalação

```bash
dotnet add package Prime.Architecture.Authorization
```

```csharp
using Prime.Architecture.Authorization.Extensions;

builder.Services.AddAuthorization();
builder.Services.AddResourceAuthorization(o => o.ClaimType = "permissao"); // opcional
```

## Exemplo completo e token com permissões

Um projeto web mínimo e funcional (JWT, login, controller, Minimal API, `ExigirEndpointsProtegidos`) está no repositório em
`Prime.Architecture.Authorization.Examples` e no [README principal](../README.md#-authorization). O ponto essencial é
como o token carrega as permissões — **uma claim `permissao` por permissão**:

```csharp
var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, usuarioId) };
claims.AddRange(permissoes.Select(p => new Claim("permissao", p)));   // "produto:read", "produto:delete", ...

var jwt = new JwtSecurityToken(issuer, audience, claims,
    expires: DateTime.UtcNow.AddMinutes(30),
    signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));
```

O tipo da claim é configurável: `AddResourceAuthorization(o => o.ClaimType = "minha_claim")`.

## Uso

```csharp
using Prime.Architecture.Authorization.Attributes;

[ApiController, Route("api/perfil")]
public class PerfilController : ControllerBase
{
    [HttpGet]
    [RequerPermissao("perfil", "read")]       // exige a claim permissao = "perfil:read"
    public IActionResult Get() => Ok();

    [HttpPost("{id}/reativar")]
    [RequerPermissao("perfil", "reactivate")] // ação customizada
    public IActionResult Reativar(int id) => Ok();
}

// Minimal API
app.MapGet("/api/x", () => "ok").RequerPermissao("perfil", "read");

// Dentro de um serviço
var result = await authorizationService.AuthorizeAsync(User, "perm:perfil:read");
```

Recurso e ação não podem ser vazios nem conter `:`. As siglas são normalizadas em minúsculas. A comparação com as permissões do usuário ignora maiúsculas/minúsculas.
Endpoints com `[AllowAnonymous]` continuam públicos.

## Origem das permissões

Por padrão as permissões são lidas das claims do usuário (`IPermissaoResolver` → `ClaimsPermissaoResolver`).
Para consultar banco ou cache (e evitar JWT grande ou permissões desatualizadas), registre outra implementação:

```csharp
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
```

## Minimal API

```csharp
using Prime.Architecture.Authorization.Extensions;

app.MapGet("/relatorio", () => "ok").RequerPermissao("relatorio", "read");

var admin = app.MapGroup("/admin").RequerPermissao("admin", "manage");   // vale para o grupo todo
```

Prefira `.RequerPermissao(...)` a `RequireAuthorization("perm:...")` escrito à mão, embora ambos sejam encontrados pela descoberta.

## Descoberta de permissões (seed)

```csharp
using Prime.Architecture.Authorization.Discovery;

// Depois de mapear as rotas: controllers, Minimal API, policies "perm:..." e catálogos de constantes
IReadOnlyList<string> siglas = app.Descobrir(typeof(Program).Assembly);

// Só por atributos/catálogos nos assemblies (sem endpoints):
IReadOnlyList<string> doCodigo = PermissaoCatalogo.Descobrir(typeof(Program).Assembly);

var crud = PermissaoCatalogo.Crud("banco"); // banco:create, banco:read, banco:update, banco:delete
```

### Catálogo central de constantes (opcional)

```csharp
[CatalogoDePermissoes]
public static class Permissoes
{
    public const string RelatorioExportar = "relatorio:exportar";

    public static class Pedido            // classes aninhadas também são lidas
    {
        public const string Ler = "pedido:read";
        public const string Cancelar = "pedido:cancel";
    }
}

[RequerPermissao("pedido", "cancel")]    // ou use as constantes para montar recurso/ação
```

Constantes fora do formato `recurso:acao` fazem a descoberta falhar rapidamente.

## Garantir que nenhum endpoint ficou sem proteção

```csharp
app.MapControllers();
// ... demais rotas ...
app.ExigirEndpointsProtegidos(e => e.RoutePattern.RawText == "/health");   // ignora rotas técnicas
```

Falha a inicialização listando os endpoints sem `[RequerPermissao]`, `[Authorize]` ou `[AllowAnonymous]`.

## Seguro por padrão e respostas 401/403

```csharp
builder.Services.AddResourceAuthorization(o =>
{
    // Padrão: true. Endpoints sem nenhum atributo exigem usuário autenticado ([AllowAnonymous] continua público).
    o.ExigirAutenticacaoPorPadrao = true;

    // Opcional: escreve o corpo das respostas 401 (não autenticado) e 403 (sem permissão), ex.: o envelope da API
    o.EscreverResposta = (ctx, status) => ctx.Response.WriteAsJsonAsync(new { status });
});
```

Sem `EscreverResposta`, 401/403 saem sem corpo (padrão do ASP.NET Core).

## Auditoria

Negações são registradas via `ILogger` (nível Warning), sem token e sem claims:

- `Acesso negado: usuário {UsuarioId} sem a permissão {Permissao}`
- `Requisição {Metodo} {Caminho} negada: não autenticado (401) | sem permissão (403)`

Encaminhe esses logs para o seu sistema de monitoramento e alerte em picos de 403.

### Auditoria de alterações administrativas

Injete `IPermissaoAuditor` nos endpoints que concedem/negam permissões ou alteram perfis:

```csharp
await auditor.RegistrarAsync(new PermissaoAuditoriaEvento(
    PermissaoAuditoriaTipo.Negada, Ator: User.Identity?.Name, Alvo: $"usuario {id}", Permissao: "produto:delete"));
```

A implementação padrão grava em log (valores sanitizados). Substitua `IPermissaoAuditor` para gravar em tabela.

### Métricas

`System.Diagnostics.Metrics`, meter `Prime.Architecture.Authorization` (`AuthorizationMetrics.NomeDoMeter`):

| Instrumento | Tag |
|---|---|
| `prime.authorization.permissao_negada` | `permissao` |
| `prime.authorization.requisicao_negada` | `status_code` (401/403) |

Com OpenTelemetry: `.AddMeter(AuthorizationMetrics.NomeDoMeter)`.

## Atributo na classe e no método

Os atributos se **somam**: se a classe exige `x:read` e o método exige `x:update`, o usuário precisa das duas permissões.

## Segurança

- **A validade do token (assinatura, expiração, issuer, audience) é responsabilidade do seu `AddJwtBearer`**; o pacote
  confia no `ClaimsPrincipal` recebido.
- Com o resolver padrão (claims), as permissões valem até o token expirar. Use tokens de vida curta ou o pacote
  `Prime.Architecture.Authorization.EntityFramework`, que consulta o banco com cache.
- Falha fechada: sem permissão, sem autenticação ou se o resolver lançar exceção, o acesso **não** é concedido.
- Veja `PRODUCAO.md` na raiz do repositório para o checklist de implantação.

## Resolução de problemas

| Sintoma | Causa provável | O que fazer |
|---|---|---|
| **401** mesmo com token | Token inválido para o `JwtBearer`, ou `UseAuthentication()` faltando/depois de `UseAuthorization()` | Confira a validação do JWT e a ordem do pipeline |
| **403** com usuário logado | Falta a permissão exata `recurso:acao`, ou a claim tem tipo diferente de `ClaimType` | Veja o log `Acesso negado: usuário X sem a permissão Y` |
| Endpoint que era público agora dá **401** | Padrão seguro: endpoint sem atributo exige autenticação | Use `[AllowAnonymous]` ou `[RequerPermissao]` |
| App não sobe: `Endpoints sem autorização declarada` | `ExigirEndpointsProtegidos()` encontrou rotas sem declaração | Declare a autorização ou ignore rotas técnicas |
| `ArgumentException` no `[RequerPermissao]` | Recurso ou ação vazio, ou contendo `:` | Use `("produto", "read")` |
| 401/403 sem corpo | Padrão do ASP.NET Core | Defina `EscreverResposta` |
