# Prime.Architecture.Authorization.EntityFramework

Persistência em banco (EF Core) para o `Prime.Architecture.Authorization`: tabelas de permissões, resolver com cache,
negação explícita por usuário e sincronização das permissões com limpeza de órfãs.

## Instalação

```csharp
// 1. Mapeie as tabelas no seu DbContext
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.AplicarResourceAuthorization();   // schema opcional: AplicarResourceAuthorization("auth")
}
// Depois: dotnet ef migrations add AddResourceAuthorization --context AppDbContext && dotnet ef database update

// 2. Informe como obter o perfil VIGENTE do usuário (lido do banco, não do token)
public class UsuarioPerfilDoBanco(AppDbContext db) : IUsuarioPerfilProvider
{
    public async Task<UsuarioPerfilInfo?> ObterAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (!int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return null;

        // null (inexistente, inativo ou bloqueado) => o usuário fica SEM nenhuma permissão
        return await db.Usuarios.AsNoTracking()
            .Where(u => u.Id == id && u.Ativo)
            .Select(u => new UsuarioPerfilInfo(u.Id, u.PerfilId))
            .FirstOrDefaultAsync(ct);
    }
}

// 3. Registre (ordem livre)
builder.Services.AddResourceAuthorization();
builder.Services.AddResourceAuthorizationEntityFramework<AppDbContext, UsuarioPerfilDoBanco>(o =>
{
    o.DuracaoCache = TimeSpan.FromMinutes(5);         // TimeSpan.Zero desativa o cache
});
```

Com isso, trocar o perfil de um usuário ou bloqueá-lo vale mesmo com o token antigo.

### Cache da consulta de usuário/perfil

Para não consultar o banco a cada requisição, o seu provider é envolvido por um cache automático (padrão: 1 minuto):

```csharp
builder.Services.AddResourceAuthorizationEntityFramework<AppDbContext, UsuarioPerfilDoBanco>(o =>
{
    o.DuracaoCachePerfilUsuario = TimeSpan.FromMinutes(1);   // TimeSpan.Zero = consulta a cada requisição
});
```

- Requisições simultâneas com cache vazio compartilham **uma única** consulta (proteção contra *stampede*).
- **Ao bloquear um usuário ou trocar o perfil dele, invalide:** `await invalidator.InvalidarUsuarioAsync(id)`
  (propaga às outras instâncias se houver `IPermissaoInvalidationBus`). Sem isso, a mudança vale quando o cache expira.
- Se o seu provider resolve o usuário por uma claim diferente da configurada em `ClaimUsuario`, o cache não é usado
  de forma insegura: ele descarta resultados cujo `UsuarioId` não coincide com a claim.
- A carga compartilhada não usa o `CancellationToken` de uma requisição específica.

> **Sem provider?** `AddResourceAuthorizationEntityFramework<AppDbContext>(o => o.ConfiarNoPerfilDoToken = true)` usa as
> claims `perfil_id` e `NameIdentifier` do token. É **menos seguro** (mudanças de perfil e bloqueios só valem quando o
> token expira), por isso exige o opt-in explícito. Sem o opt-in, o registro lança `InvalidOperationException`.

O token só precisa identificar o usuário. As permissões saem do banco.

## Tabelas

| Tabela | Conteúdo |
|---|---|
| `Permissoes` | `Sigla` (`recurso:acao`, única), `Descricao`, `Ativo` |
| `PerfisPermissoes` | `PerfilId`, `PermissaoId`, `Concedida` |
| `UsuariosPermissoes` | `UsuarioId`, `PermissaoId`, `Concedida` (exceção por usuário) |

`Perfil` e `Usuario` pertencem à sua aplicação; aqui eles aparecem só como ids (sem chave estrangeira).

## Regra de resolução

```
permissões = (concedidas ao perfil + concedidas ao usuário) − negadas ao usuário
```

- Somente permissões `Ativo = true` valem.
- `Concedida = false` em `PerfisPermissoes` equivale a não ter o registro.
- `Concedida = false` em `UsuariosPermissoes` **nega** a permissão e vence a concessão do perfil.

## Sincronização das permissões (seed)

```csharp
var siglas = app.Descobrir(typeof(Program).Assembly);   // controllers, Minimal API e catálogos

using var scope = app.Services.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

SincronizacaoResultado r = await db.SincronizarAsync(siglas, descricao: s => s);
// r.Criadas, r.Reativadas, r.Desativadas
```

Cria as novas, reativa as que voltaram e **desativa** as que não existem mais no código (nunca apaga, preservando os vínculos).

**Trava de segurança:** se a sincronização desativaria mais de 50% das permissões ativas (assembly errado, lista vazia),
ela lança `InvalidOperationException` e **não grava nada**. Para ajustar:

```csharp
await db.SincronizarAsync(siglas, opcoes: new SincronizacaoOpcoes
{
    LimiteDesativacao = 0.5,  // 1 = permite desativar tudo, conscientemente
    Simular = true,           // só calcula, sem gravar (útil em CI/homologação)
    Logger = logger,          // registra o que foi criado, reativado e desativado (auditoria)
    Invalidador = invalidator // IPermissaoCacheInvalidator: renova o cache de todas as instâncias (veja abaixo)
});
```

**Várias instâncias subindo juntas:** se a gravação conflita com outra sincronização simultânea (índice único, deadlock),
o estado é relido do banco e a gravação é repetida (`MaximoTentativas`, padrão 3). Não é preciso lock externo.

**Cache após sincronizar:** sem `Invalidador`, uma permissão reativada ou desativada só vale quando o cache expira
(`DuracaoCache`, padrão 5 min). Com ele, o cache de permissões é renovado na hora (e nas outras instâncias, com um
`IPermissaoInvalidationBus`) sempre que alguma permissão foi reativada ou desativada. Só o cache do pacote é afetado:
outros itens do `IMemoryCache` da aplicação não são tocados. Se o aviso às outras instâncias falhar (ex.: Redis fora do ar),
a sincronização, já gravada, não é desfeita: o erro é registrado no `Logger`.

**Collation do banco:** em bancos com collation insensível a acento (padrão do MySQL 8, `utf8mb4_0900_ai_ci`),
`acao:ler` e `ação:ler` são a mesma sigla para o índice único; sincronizar as duas falha com `DbUpdateException`.
Evite siglas que só diferem por acento. Siglas acima de 150 caracteres também são rejeitadas pelo banco.

## Cache

Alterou permissões de um perfil ou usuário? Invalide para valer imediatamente:

```csharp
await invalidator.InvalidarPerfilAsync(perfilId);     // IPermissaoCacheInvalidator
await invalidator.InvalidarUsuarioAsync(usuarioId);
await invalidator.InvalidarTudoAsync();               // todo o cache de permissões (não toca no resto do IMemoryCache)
```

`InvalidarPerfil`/`InvalidarUsuario` (síncronos) limpam **somente a instância atual**. `InvalidarUsuarioAsync` limpa também o cache de perfil/situação do usuário.

### Várias instâncias (invalidação distribuída)

O cache é local a cada instância. Para que a invalidação alcance todas, registre um canal de mensagens:

```csharp
public class RedisInvalidationBus : IPermissaoInvalidationBus
{
    public Task PublicarAsync(PermissaoInvalidacao mensagem, CancellationToken ct = default) { /* PUBLISH no canal */ }
    public IDisposable Assinar(Func<PermissaoInvalidacao, Task> tratador) { /* SUBSCRIBE e chamar tratador */ }
}

builder.Services.AddPermissaoInvalidationBus<RedisInvalidationBus>();
// ou: builder.Services.AddPermissaoInvalidationBus(minhaInstanciaDoBus);
```

Com o bus registrado, `InvalidarPerfilAsync`/`InvalidarUsuarioAsync` limpam o cache local e avisam as demais instâncias.
Mensagens publicadas pela própria instância não são reaplicadas. Sem bus, mantenha `DuracaoCache` curto.
`InMemoryInvalidationBus` serve para testes ou vários hosts no mesmo processo.

## Migrations

```bash
dotnet ef migrations add AddResourceAuthorization --context AppDbContext
dotnet ef database update --context AppDbContext
```

Cria `Permissoes` (`Sigla` única), `PerfisPermissoes` e `UsuariosPermissoes` (índices únicos por par). Se o app não registra o
`DbContext` no host, crie um `IDesignTimeDbContextFactory<AppDbContext>`.

## Associar permissões a perfis (seed)

O pacote não impõe perfis; você decide. Depois de sincronizar as permissões:

```csharp
var ids = db.Set<Permissao>().ToDictionary(p => p.Sigla, p => p.Id);
db.Set<PerfilPermissao>().Add(new PerfilPermissao { PerfilId = 1, PermissaoId = ids["produto:read"] });
await db.SaveChangesAsync();
```

## Exemplo completo (DbContext, provider, auditoria em tabela, registro, sincronização, bloqueio)

Este código está em `Prime.Architecture.Authorization.Examples` e é compilado junto com a solution:

```csharp
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Prime.Architecture.Authorization.Auditing;
using Prime.Architecture.Authorization.Discovery;
using Prime.Architecture.Authorization.EntityFramework;
using Prime.Architecture.Authorization.EntityFramework.Configuration;
using Prime.Architecture.Authorization.EntityFramework.Extensions;
using Prime.Architecture.Authorization.EntityFramework.Implementation;
using Prime.Architecture.Authorization.Extensions;
using StackExchange.Redis;

namespace Prime.Architecture.Authorization.Examples.Ef;

// ---- Suas entidades (o pacote só conhece ids de usuário e de perfil) ----
public class Usuario
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    public int PerfilId { get; set; }
    public bool Ativo { get; set; } = true;
}

public class AuditoriaPermissao
{
    public int Id { get; set; }
    public DateTime Quando { get; set; } = DateTime.UtcNow;
    public string Tipo { get; set; } = "";
    public string? Ator { get; set; }
    public string? Alvo { get; set; }
    public string? Permissao { get; set; }
    public string? Detalhe { get; set; }
}

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<AuditoriaPermissao> Auditoria => Set<AuditoriaPermissao>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AplicarResourceAuthorization();   // Permissoes, PerfisPermissoes, UsuariosPermissoes
    }
}

// ---- Necessário para os comandos "dotnet ef" (o app pode não registrar o DbContext no host) ----
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=app.db").Options);
}

// ---- Perfil VIGENTE do usuário, lido do banco (não do token) ----
public class UsuarioPerfilDoBanco(AppDbContext db) : IUsuarioPerfilProvider
{
    public async Task<UsuarioPerfilInfo?> ObterAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (!int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
            return null;

        // null (inexistente, inativo ou bloqueado) => o usuário fica SEM nenhuma permissão
        return await db.Usuarios.AsNoTracking()
            .Where(u => u.Id == id && u.Ativo)
            .Select(u => new UsuarioPerfilInfo(u.Id, u.PerfilId))
            .FirstOrDefaultAsync(ct);
    }
}

// ---- Auditoria das alterações administrativas gravada em tabela ----
public class AuditoriaEmTabela(AppDbContext db) : IPermissaoAuditor
{
    public async Task RegistrarAsync(PermissaoAuditoriaEvento evento, CancellationToken ct = default)
    {
        db.Auditoria.Add(new AuditoriaPermissao
        {
            Tipo = evento.Tipo.ToString(), Ator = evento.Ator, Alvo = evento.Alvo,
            Permissao = evento.Permissao, Detalhe = evento.Detalhe
        });
        await db.SaveChangesAsync(ct);
    }
}

public static class ExemploComBanco
{
    /// <summary>Registro completo no modo com banco.</summary>
    public static void Registrar(WebApplicationBuilder builder, bool variasInstancias)
    {
        builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite("Data Source=app.db"));

        builder.Services.AddAuthorization();
        builder.Services.AddResourceAuthorization();
        builder.Services.AddResourceAuthorizationEntityFramework<AppDbContext, UsuarioPerfilDoBanco>(o =>
        {
            o.DuracaoCache = TimeSpan.FromMinutes(5);               // permissões do perfil/usuário
            o.DuracaoCachePerfilUsuario = TimeSpan.FromMinutes(1);  // consulta de usuário/perfil
        });

        builder.Services.AddScoped<IPermissaoAuditor, AuditoriaEmTabela>();   // substitui o auditor de log

        if (variasInstancias)
        {
            builder.Services.AddSingleton<IConnectionMultiplexer>(
                _ => ConnectionMultiplexer.Connect(builder.Configuration["Redis:Connection"]!));
            builder.Services.AddPermissaoInvalidationBus<Custom.RedisInvalidationBus>();
        }
    }

    /// <summary>Sincroniza a tabela de permissões com o código no startup (depois de mapear as rotas).</summary>
    public static async Task SincronizarPermissoesAsync(WebApplication app, bool somenteSimular)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();

        var siglas = app.Descobrir(typeof(ExemploComBanco).Assembly);   // controllers, Minimal API, policies, catálogos

        // Lança InvalidOperationException (e não grava nada) se desativaria mais da metade das permissões
        var resultado = await db.SincronizarAsync(siglas, opcoes: new SincronizacaoOpcoes
        {
            Simular = somenteSimular,   // true em CI/homologação: só calcula
            Logger = logger,
            Invalidador = scope.ServiceProvider.GetRequiredService<IPermissaoCacheInvalidator>()   // renova o cache após reativar/desativar
        });

        logger.LogInformation("Permissões: {Criadas} criadas, {Reativadas} reativadas, {Desativadas} desativadas",
            resultado.Criadas, resultado.Reativadas, resultado.Desativadas);
    }

    /// <summary>Ao bloquear um usuário ou trocar o perfil dele: grave e invalide o cache.</summary>
    public static async Task BloquearUsuarioAsync(AppDbContext db, IPermissaoCacheInvalidator cache, IPermissaoAuditor auditor,
        int usuarioId, string ator)
    {
        var usuario = await db.Usuarios.FindAsync(usuarioId) ?? throw new KeyNotFoundException();
        usuario.Ativo = false;
        await db.SaveChangesAsync();

        await cache.InvalidarUsuarioAsync(usuarioId);   // vale na hora (e nas outras instâncias, com o bus)
        await auditor.RegistrarAsync(new PermissaoAuditoriaEvento(
            PermissaoAuditoriaTipo.UsuarioBloqueado, ator, $"usuario {usuarioId}"));
    }
}
```

## Invalidação entre instâncias com Redis

```csharp
using System.Text.Json;
using Prime.Architecture.Authorization.EntityFramework.Implementation;
using StackExchange.Redis;

namespace Prime.Architecture.Authorization.Examples.Custom;

/// <summary>
/// Invalidação de cache entre instâncias via Redis pub/sub. Proteja o Redis (senha/TLS, rede privada):
/// quem consegue publicar no canal consegue forçar a limpeza do cache (não concede acesso, mas pode degradar o desempenho).
/// </summary>
public sealed class RedisInvalidationBus(IConnectionMultiplexer redis) : IPermissaoInvalidationBus
{
    private static readonly RedisChannel Canal = RedisChannel.Literal("prime:permissoes:invalidacao");

    public Task PublicarAsync(PermissaoInvalidacao mensagem, CancellationToken cancellationToken = default)
        => redis.GetSubscriber().PublishAsync(Canal, JsonSerializer.Serialize(mensagem));

    public IDisposable Assinar(Func<PermissaoInvalidacao, Task> tratador)
    {
        var subscriber = redis.GetSubscriber();
        subscriber.Subscribe(Canal, (canal, valor) =>
        {
            var mensagem = JsonSerializer.Deserialize<PermissaoInvalidacao>(valor.ToString());
            if (mensagem != null)
                _ = tratador(mensagem);
        });

        return new Descartavel(() => subscriber.Unsubscribe(Canal));
    }

    private sealed class Descartavel(Action aoDescartar) : IDisposable
    {
        public void Dispose() => aoDescartar();
    }
}
```

```csharp
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(builder.Configuration["Redis:Connection"]!));
builder.Services.AddPermissaoInvalidationBus<RedisInvalidationBus>();
```

## Resolução de problemas

| Sintoma | Causa provável | O que fazer |
|---|---|---|
| **403** para um usuário que deveria ter acesso | Provider devolveu `null` (inativo/inexistente), permissão inativa, ou falta o vínculo em `PerfisPermissoes` | Confira o usuário, a tabela `Permissoes` (`Ativo`) e os vínculos |
| Mudei o perfil / bloqueei o usuário e ele ainda acessa | Cache (`DuracaoCachePerfilUsuario`, `DuracaoCache`) | `InvalidarUsuarioAsync(id)`; com várias instâncias, registre um bus |
| `Sincronização abortada: desativaria N de M...` | Trava de segurança (assembly errado ou lista vazia) | Verifique os assemblies; só aumente `LimiteDesativacao` se for intencional |
| `InvalidOperationException` ao registrar | Falta informar de onde vem o perfil | Use `AddResourceAuthorizationEntityFramework<TContext, TProvider>()` ou `ConfiarNoPerfilDoToken = true` |
| Permissão nova não aparece na tabela | Sincronização antes de `MapControllers`, ou policy fora do formato `perm:recurso:acao` | Sincronize depois de mapear as rotas e use `app.Descobrir(...)` |
