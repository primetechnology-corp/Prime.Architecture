# Prime Architecture

Uma suíte de bibliotecas arquiteturais para .NET, focada em performance, simplicidade e código limpo.

**Versão da suíte: 2.0** — Mediator 1.0, Mapper 1.0 e Authorization 1.0.

Esta solução contém as seguintes bibliotecas principais:

1. **[Mediator](#-mediator)**: Implementação do padrão Mediator (Standard e HighPerformance).
2. **[Mapper](#-mapper)**: Mapeador de objetos leve e flexível.
3. **[Authorization](#-authorization)**: Autorização por recurso e permissão (`recurso:acao`) para ASP.NET Core, com persistência opcional em banco (EF Core).

### 📦 Pacotes NuGet

| Pacote | Versão | Conteúdo | Target |
|---|---|---|---|
| `Prime.Architecture` | 2.0.0 | Mediator e Mapper (sem dependência do ASP.NET Core) | `netstandard2.1` |
| `Prime.Architecture.Authorization` | 1.0.0 | Núcleo da autorização (policies, atributo, descoberta, auditoria, métricas) | `net10.0` |
| `Prime.Architecture.Authorization.EntityFramework` | 1.0.0 | Tabelas de permissões, resolver com cache, sincronização | `net10.0` |

O Authorization é um pacote separado porque depende do ASP.NET Core; quem usa só o Mediator ou o Mapper não herda essa dependência.

---

## 🚀 Mediator

Uma biblioteca leve e poderosa para implementação do padrão Mediator em .NET, oferecendo duas opções de implementação baseadas em performance e desacoplamento.

### 📦 Instalação e Configuração

Escolha o namespace que melhor se adapta às suas necessidades e registre no seu `Program.cs`.

#### Opção A: Standard (Equilíbrio entre Pureza e Performance)
Ideal para 95% das aplicações. Mantém seus DTOs limpos.

**Arquivo: `Program.cs`**
```csharp
using Prime.Architecture.Mediator.Standard;

var builder = WebApplication.CreateBuilder(args);

// Registra o Mediator e todos os Handlers no assembly atual
builder.Services.AddMediatorStandard(typeof(Program).Assembly);

var app = builder.Build();
```

#### Opção B: HighPerformance (Zero Reflection)
Ideal para sistemas de ultra-baixa latência.

**Arquivo: `Program.cs`**
```csharp
using Prime.Architecture.Mediator.HighPerformance;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMediatorHighPerformance(typeof(Program).Assembly);

var app = builder.Build();
```

### 🛠️ Guia de Implementação Completo

Abaixo, um exemplo completo de um caso de uso para **Criar Cliente**.

#### 1. Definindo o Request (Comando)

O Request representa a intenção de executar uma ação.

**Arquivo: `Application/Customers/CreateCustomerCommand.cs`**

*Modo Standard:*
```csharp
using Prime.Architecture.Mediator.Standard.Requests;

public record CreateCustomerCommand(string Name, string Email) : IRequest<int>;
```

*Modo HighPerformance:*
```csharp
using Prime.Architecture.Mediator.HighPerformance;
using Prime.Architecture.Mediator.HighPerformance.Requests;

public record CreateCustomerCommand(string Name, string Email) : IRequest<int>
{
    // No modo HighPerformance, o próprio request sabe como se enviar
    public Task<int> SendTo(Mediator mediator, CancellationToken ct) 
        => mediator.HandleInternal<CreateCustomerCommand, int>(this, ct);
}
```

#### 2. Implementando o Handler

O Handler contém a lógica de negócio.

**Arquivo: `Application/Customers/CreateCustomerHandler.cs`**

```csharp
using Prime.Architecture.Mediator.Standard.Requests; // ou HighPerformance.Requests

public class CreateCustomerHandler : IRequestHandler<CreateCustomerCommand, int>
{
    private readonly ICustomerRepository _repository;

    public CreateCustomerHandler(ICustomerRepository repository)
    {
        _repository = repository;
    }

    public async Task<int> Handle(CreateCustomerCommand request, CancellationToken ct)
    {
        var customer = new Customer(request.Name, request.Email);
        await _repository.AddAsync(customer, ct);
        
        return customer.Id;
    }
}
```

#### 3. Utilizando no Controller

Injete o `Mediator` e envie o comando.

**Arquivo: `API/Controllers/CustomersController.cs`**

```csharp
using Microsoft.AspNetCore.Mvc;
using Prime.Architecture.Mediator.Standard; // ou HighPerformance

[ApiController]
[Route("api/[controller]")]
public class CustomersController : ControllerBase
{
    private readonly Mediator _mediator;

    public CustomersController(Mediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCustomerCommand command)
    {
        var id = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetById), new { id }, id);
    }
    
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        // Exemplo de Query
        var result = await _mediator.Send(new GetCustomerQuery(id));
        return Ok(result);
    }
}
#### 4. Notificações (Eventos)

Use notificações para efeitos colaterais (ex: enviar email, logs), onde múltiplos handlers podem escutar o mesmo evento.

**Arquivo: `Application/Customers/CustomerCreatedEvent.cs`**
```csharp
using Prime.Architecture.Mediator.Standard.Notifications;

public record CustomerCreatedEvent(int CustomerId, string Email) : INotification;
```

**Arquivo: `Application/Notifications/EmailHandler.cs`**
```csharp
using Prime.Architecture.Mediator.Standard.Notifications;

public class EmailHandler : INotificationHandler<CustomerCreatedEvent>
{
    public Task Handle(CustomerCreatedEvent notification, CancellationToken ct)
    {
        Console.WriteLine($"Enviando e-mail de boas-vindas para o cliente {notification.CustomerId} no email {notification.Email}");
        return Task.CompletedTask;
    }
}
```

---

## 🗺️ Mapper

Uma biblioteca de mapeamento de objetos eficiente que suporta configurações via atributos ou profiles fluentes.

### 📦 Configuração e Injeção de Dependência

Recomendamos configurar o Mapper como um Singleton no container de DI.

**Arquivo: `Program.cs`**

```csharp
using Prime.Architecture.Mapper.Configuration;
using Prime.Architecture.Mapper;

var builder = WebApplication.CreateBuilder(args);

// Configuração do Mapper
var mapperConfig = new MapperConfiguration();
mapperConfig.AddProfile<UserProfile>(); // Registre seus profiles aqui
// mapperConfig.AddProfile<ProductProfile>();

// Registra a instância do IMapper
IMapper mapper = mapperConfig.CreateMapper();
builder.Services.AddSingleton(mapper);

var app = builder.Build();
```

### 🛠️ Guia de Implementação Completo

Exemplo de mapeamento de uma Entidade para um DTO (Data Transfer Object).

#### 1. Classes de Origem e Destino

**Arquivo: `Domain/Entities/User.cs`**
```csharp
public class User
{
    public int Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public DateTime BirthDate { get; set; }
    public string EmailAddress { get; set; }
}
```

**Arquivo: `Application/DTOs/UserDto.cs`**
```csharp
public class UserDto
{
    public int Id { get; set; }
    public string FullName { get; set; } // Propriedade calculada
    public int Age { get; set; }         // Propriedade calculada
    public string Email { get; set; }    // Nome diferente da origem
}
```

#### 2. Criando o Profile de Mapeamento

Profiles organizam as regras de transformação.

**Arquivo: `Application/Mappings/UserProfile.cs`**
```csharp
using Prime.Architecture.Mapper.Configuration;
using Prime.Architecture.Mapper.Attributes;

public class UserProfile : MappingProfile
{
    public UserProfile()
    {
        CreateMap<User, UserDto>()
            // Mapeamento customizado: Concatenação de strings
            .ForMember(dest => dest.FullName, opt => opt.MapFrom(src => $"{src.FirstName} {src.LastName}"))
            
            // Mapeamento customizado: Cálculo de idade
            // Nota: O método MapFrom aceita uma função lambda para cálculo
            .ForMember(dest => dest.Age, opt => opt.MapFrom(src => DateTime.Now.Year - src.BirthDate.Year))
            
            // Mapeamento de propriedade com nome diferente
            // Nota: Para mapear de uma propriedade específica, use MapFrom com lambda
            .ForMember(dest => dest.Email, opt => opt.MapFrom(src => src.EmailAddress));
    }
}
```

#### 3. Utilizando no Serviço ou Controller

**Arquivo: `API/Controllers/UsersController.cs`**
```csharp
using Microsoft.AspNetCore.Mvc;
using Prime.Architecture.Mapper;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IMapper _mapper;
    private readonly IUserRepository _repository;

    public UsersController(IMapper mapper, IUserRepository repository)
    {
        _mapper = mapper;
        _repository = repository;
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(int id)
    {
        var user = await _repository.GetByIdAsync(id);
        
        // Converte a entidade User para UserDto
        var userDto = _mapper.Map<User, UserDto>(user);
        
        return Ok(userDto);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var users = await _repository.GetAllAsync();
        
        // Mapeamento de listas
        // Nota: MapList é um método de extensão
        var userDtos = users.MapList<User, UserDto>(_mapper);
        
        return Ok(userDtos);
    }
}
```

### ✨ Funcionalidades Extras

#### Mapeamento via Atributos (Sem Profile)

Para casos simples, você pode anotar diretamente o DTO ou a Entidade sem precisar criar um Profile.

> **Importante**: Mesmo utilizando apenas atributos, você **deve** registrar o `IMapper` no seu container de injeção de dependência (conforme a seção [Configuração](#-configuração-e-injeção-de-dependência)). O `MapperConfiguration` pode ser instanciado sem adicionar nenhum profile.

**1. Atributo `[MapFrom]` (No Destino)**
Use quando você quer mapear uma propriedade do DTO a partir de uma propriedade com nome diferente na origem.

**Arquivo: `Application/DTOs/SimpleUserDto.cs`**
```csharp
using Prime.Architecture.Mapper.Attributes;

public class SimpleUserDto
{
    public string Name { get; set; }
    
    // Busca o valor de "EmailAddress" na classe de origem (ex: User)
    [MapFrom("EmailAddress")] 
    public string Email { get; set; }
    
    // Esta propriedade não será preenchida pelo mapper
    [IgnoreMap] 
    public string InternalCode { get; set; }
}
```

**2. Atributo `[MapTo]` (Na Origem)**
Use quando você quer indicar na classe de domínio para qual propriedade ela deve ser mapeada no destino.

**Arquivo: `Domain/Entities/Product.cs`**
```csharp
using Prime.Architecture.Mapper.Attributes;

public class Product
{
    public int Id { get; set; }
    
    // Quando mapeado para um DTO que tenha a propriedade "ProductDescription",
    // o valor desta propriedade será usado automaticamente.
    [MapTo("ProductDescription")]
    public string Description { get; set; }
}
```

**3. Exemplo de Uso (Atributos)**

**Arquivo: `API/Controllers/ProductsController.cs`**
```csharp
[HttpGet("{id}")]
public async Task<IActionResult> GetSimple(int id)
{
    // Supondo que 'user' venha do repositório
    var user = await _userRepository.GetByIdAsync(id);
    
    // O mapeamento funciona automaticamente lendo os atributos [MapFrom]
    var simpleDto = _mapper.Map<User, SimpleUserDto>(user);
    
    return Ok(simpleDto);
}
```

---

## 🔐 Authorization

Autorização por **recurso e permissão** (`recurso:acao`, ex.: `produto:read`) para ASP.NET Core, baseada em policies.
**Não depende do nome do controller**: funciona em MVC, Minimal APIs e em serviços (`IAuthorizationService`).

> Documentação detalhada: [Authorization](Prime.Architecture.Authorization/README.md) ·
> [Authorization.EntityFramework](Prime.Architecture.Authorization.EntityFramework/README.md) ·
> [Checklist de produção](PRODUCAO.md) · [CHANGELOG](CHANGELOG.md)

### 📦 Instalação

```bash
dotnet add package Prime.Architecture.Authorization
dotnet add package Prime.Architecture.Authorization.EntityFramework   # opcional: permissões no banco
```

Requisitos: `net10.0` (ASP.NET Core).

### 🚀 Exemplo completo, do zero

Um projeto web mínimo que funciona de ponta a ponta (JWT, login que emite o token **com as permissões**, controller,
Minimal API e verificação de endpoints). O código abaixo está em
[`Prime.Architecture.Authorization.Examples`](Prime.Architecture.Authorization.Examples) e é compilado e testado junto com a solution.

**Arquivo: `Program.cs`**
```csharp
// EXEMPLO COMPLETO (modo padrão: permissões nas claims do token). É o mesmo código do README.
// Rodar:  Jwt__Key="uma-chave-com-pelo-menos-32-caracteres!!" dotnet run
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Prime.Architecture.Authorization.Discovery;
using Prime.Architecture.Authorization.Extensions;
using Prime.Architecture.Authorization.Security;

var builder = WebApplication.CreateBuilder(args);

var chave = builder.Configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Configure Jwt:Key (mínimo 32 caracteres), fora do código-fonte.");
const string issuer = "exemplo-api";
const string audience = "exemplo-clientes";
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(chave));

// 1) Autenticação JWT: o pacote confia no usuário que o JwtBearer entrega, então valide TUDO
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = issuer,
        ValidateAudience = true, ValidAudience = audience,
        ValidateLifetime = true, ClockSkew = TimeSpan.Zero,
        ValidateIssuerSigningKey = true, IssuerSigningKey = signingKey,
        ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 }
    });

// 2) Autorização por permissão
builder.Services.AddAuthorization();
builder.Services.AddResourceAuthorization();

builder.Services.AddControllers();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// 3) Login de exemplo: o token carrega as permissões do usuário na claim "permissao"
var perfis = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
{
    ["admin"] = new[] { "produto:read", "produto:create", "produto:delete", "relatorio:read" },
    ["leitor"] = new[] { "produto:read" }
};

app.MapPost("/auth/login", ([FromBody] LoginRequest req) =>
{
    // ATENÇÃO: exemplo sem senha. Em produção valide credenciais de verdade.
    if (!perfis.TryGetValue(req.Usuario, out var permissoes))
        return Results.Unauthorized();

    var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, req.Usuario) };
    claims.AddRange(permissoes.Select(p => new Claim("permissao", p)));   // <- uma claim por permissão

    var jwt = new JwtSecurityToken(issuer, audience, claims,
        expires: DateTime.UtcNow.AddMinutes(30),                           // vida curta
        signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));

    return Results.Ok(new { token = new JwtSecurityTokenHandler().WriteToken(jwt) });
}).AllowAnonymous();

// 4) Minimal API protegida pela mesma convenção
app.MapGet("/relatorio", () => "relatório").RequerPermissao("relatorio", "read");

// 5) Lista as permissões descobertas (base para popular a tabela, no modo com banco)
app.MapGet("/permissoes", () => PermissaoCatalogo.Descobrir(app)).AllowAnonymous();

// 6) Falha o startup se algum endpoint ficou sem autorização declarada
app.ExigirEndpointsProtegidos();

app.Run();

record LoginRequest(string Usuario);
```

**Arquivo: `Controllers/ProdutosController.cs`**
```csharp
using Microsoft.AspNetCore.Mvc;
using Prime.Architecture.Authorization.Attributes;

namespace Prime.Architecture.Authorization.Examples.Controllers;

[ApiController, Route("api/produtos")]
public class ProdutosController : ControllerBase
{
    [HttpGet, RequerPermissao("produto", "read")]
    public IActionResult Listar() => Ok(new[] { "caneta", "caderno" });

    [HttpPost, RequerPermissao("produto", "create")]
    public IActionResult Criar() => Ok("criado");

    [HttpDelete("{id}"), RequerPermissao("produto", "delete")]
    public IActionResult Excluir(int id) => Ok($"excluído {id}");
}
```

**Rodando e testando**
```bash
export Jwt__Key="uma-chave-de-exemplo-com-mais-de-32-caracteres!!"
dotnet run --urls http://localhost:5000

TOKEN=$(curl -s -X POST localhost:5000/auth/login -H 'Content-Type: application/json' -d '{"usuario":"leitor"}' | jq -r .token)
curl -i localhost:5000/api/produtos                                  # 401 (sem token)
curl -i -H "Authorization: Bearer $TOKEN" localhost:5000/api/produtos       # 200 (tem produto:read)
curl -i -X DELETE -H "Authorization: Bearer $TOKEN" localhost:5000/api/produtos/1   # 403 (não tem produto:delete)
```

### 🎫 Como o token carrega as permissões

No modo padrão, cada permissão é **uma claim `permissao`** com o valor `recurso:acao`, adicionada ao gerar o JWT:

```csharp
var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, usuarioId) };
claims.AddRange(permissoes.Select(p => new Claim("permissao", p)));   // "produto:read", "produto:delete", ...
```

- O tipo da claim é configurável: `AddResourceAuthorization(o => o.ClaimType = "minha_claim")`.
- Quanto mais permissões, maior o token (vai em toda requisição) e as mudanças só valem quando ele expira. Para
  evitar isso, use o modo com banco (seção abaixo), em que o token só identifica o usuário.

### 🛠️ Uso

```csharp
using Prime.Architecture.Authorization.Attributes;

[ApiController, Route("api/produtos")]
public class ProdutosController : ControllerBase
{
    [HttpGet, RequerPermissao("produto", "read")]       // exige a permissão "produto:read"
    public IActionResult Listar() => Ok();

    [HttpDelete("{id}"), RequerPermissao("produto", "delete")]
    public IActionResult Excluir(int id) => Ok();

    [HttpPost("{id}/aprovar"), RequerPermissao("produto", "approve")]   // ação customizada
    public IActionResult Aprovar(int id) => Ok();
}

// Minimal API
app.MapGet("/relatorio", () => "ok").RequerPermissao("relatorio", "read");
app.MapGroup("/admin").RequerPermissao("admin", "manage");              // vale para o grupo todo

// Dentro de um serviço
await authorizationService.AuthorizeAsync(User, "perm:produto:read");
```

- Sem a permissão: **403**. Sem autenticação: **401**. Erro ao consultar permissões: **nunca autoriza**.
- Atributos na classe e no método se **somam** (o usuário precisa de todos).
- Recurso e ação não podem ser vazios nem conter `:`; as siglas são normalizadas em minúsculas.

### 🛡️ Seguro por padrão

- Endpoints sem nenhum atributo exigem usuário autenticado (`ExigirAutenticacaoPorPadrao = true`). `[AllowAnonymous]` continua público.
- `app.ExigirEndpointsProtegidos()` lista os endpoints sem `[RequerPermissao]`, `[Authorize]` ou `[AllowAnonymous]`.
- Negações são registradas em log (sem token e sem claims, valores sanitizados contra *log forging*) e em métricas.
- A validade do token (assinatura, expiração, issuer, audience) é responsabilidade do seu `AddJwtBearer`.

```csharp
builder.Services.AddResourceAuthorization(o =>
{
    o.ExigirAutenticacaoPorPadrao = true;     // padrão
    o.EscreverResposta = (ctx, status) => ctx.Response.WriteAsJsonAsync(new { status });   // corpo do 401/403
});
```

### 🗄️ Permissões no banco (EntityFramework)

O token passa a só identificar o usuário; as permissões vêm do banco, com cache.

```csharp
// DbContext
protected override void OnModelCreating(ModelBuilder modelBuilder)
    => modelBuilder.AplicarResourceAuthorization();      // tabelas Permissoes, PerfisPermissoes, UsuariosPermissoes

// Perfil VIGENTE do usuário, lido do banco (não do token)
public class UsuarioPerfilDoBanco(AppDbContext db) : IUsuarioPerfilProvider
{
    public async Task<UsuarioPerfilInfo?> ObterAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (!int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return null;
        return await db.Usuarios.AsNoTracking()
            .Where(u => u.Id == id && u.Ativo)                       // inativo/inexistente = sem permissões
            .Select(u => new UsuarioPerfilInfo(u.Id, u.PerfilId))
            .FirstOrDefaultAsync(ct);
    }
}

builder.Services.AddResourceAuthorization();
builder.Services.AddResourceAuthorizationEntityFramework<AppDbContext, UsuarioPerfilDoBanco>();
```

Regra de resolução: `(permissões do perfil + concedidas ao usuário) − negadas ao usuário`. Somente permissões ativas valem,
e a **negação explícita por usuário vence** a concessão do perfil.

**Sincronizar a tabela de permissões com o código** (cria, reativa e desativa órfãs, com trava de segurança):
```csharp
var siglas = app.Descobrir(typeof(Program).Assembly);   // controllers, Minimal API, policies e catálogos de constantes
await db.SincronizarAsync(siglas, opcoes: new SincronizacaoOpcoes { Logger = logger });
```

**Cache:** a consulta de usuário/perfil e as permissões ficam em cache (com proteção contra *stampede*).
Ao bloquear um usuário ou trocar o perfil, invalide: `await invalidator.InvalidarUsuarioAsync(id);`.
Com várias instâncias, registre um `IPermissaoInvalidationBus` (ex.: Redis) com `AddPermissaoInvalidationBus<TBus>()`.

### 🗃️ Migrations e sincronização das permissões (EntityFramework)

**1. Gere a migration** (o `AplicarResourceAuthorization()` acrescenta as tabelas ao seu modelo):

```bash
dotnet ef migrations add AddResourceAuthorization --context AppDbContext
dotnet ef database update --context AppDbContext
```

Cria `Permissoes` (`Sigla` única), `PerfisPermissoes` e `UsuariosPermissoes` (índices únicos por par). Se o seu app não
registra o `DbContext` no host, crie um `IDesignTimeDbContextFactory<AppDbContext>` (veja o exemplo abaixo).

**2. Sincronize as permissões com o código no startup**, depois de mapear as rotas. Em CI/homologação use `Simular = true`
para só ver o que mudaria. A sincronização **aborta sem gravar nada** se desativaria mais de 50% das permissões
(assembly errado, lista vazia):

```csharp
app.MapControllers();
await ExemploComBanco.SincronizarPermissoesAsync(app, somenteSimular: false);   // veja o código completo abaixo
```

**3. Associe permissões aos perfis** (a biblioteca não impõe perfis; você decide). Exemplo de seed:

```csharp
var ids = db.Set<Permissao>().ToDictionary(p => p.Sigla, p => p.Id);
db.Set<PerfilPermissao>().Add(new PerfilPermissao { PerfilId = 1, PermissaoId = ids["produto:read"] });
await db.SaveChangesAsync();
```

**Código completo do modo com banco** (DbContext, provider de perfil, auditoria em tabela, registro, sincronização e bloqueio de usuário):

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

### 🧩 Extensões

Tudo é substituível via DI. Os exemplos abaixo compilam em [`Prime.Architecture.Authorization.Examples`](Prime.Architecture.Authorization.Examples).

**Origem própria de permissões** (`IPermissaoResolver`): serviço de identidade, cache distribuído, arquivo etc.
Retorne um conjunto vazio quando não houver identidade (falha fechada):

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

**Auditoria** (`IPermissaoAuditor`): o padrão grava em log; o exemplo do modo com banco (acima) grava em tabela. Chame-o em
qualquer ponto que conceda, negue ou altere permissões:

```csharp
await auditor.RegistrarAsync(new PermissaoAuditoriaEvento(
    PermissaoAuditoriaTipo.Negada, Ator: User.Identity?.Name, Alvo: "usuario 7", Permissao: "produto:delete"));
```

**Invalidação de cache entre instâncias** (`IPermissaoInvalidationBus`), por exemplo com Redis:

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

**Catálogo central de constantes** (opcional), em vez de strings soltas:

```csharp
[CatalogoDePermissoes]
public static class Permissoes
{
    public const string RelatorioExportar = "relatorio:exportar";
    public static class Pedido { public const string Ler = "pedido:read"; public const string Cancelar = "pedido:cancel"; }
}
```

### 📚 Recursos adicionais

| Recurso | Para quê |
|---|---|
| `[CatalogoDePermissoes]` | Catálogo central de permissões em constantes `const string "recurso:acao"` |
| `IPermissaoResolver` | Trocar a origem das permissões (claims, banco, cache, serviço externo) |
| `IPermissaoAuditor` | Registrar alterações administrativas (concedeu, negou, trocou perfil) |
| `AuthorizationMetrics` | Métricas `prime.authorization.permissao_negada` e `requisicao_negada` (OpenTelemetry: `.AddMeter(...)`) |
| `IPermissaoInvalidationBus` | Invalidação de cache entre instâncias |

### 🔧 Resolução de problemas

| Sintoma | Causa provável | O que fazer |
|---|---|---|
| **401** mesmo com token | Token inválido para o `JwtBearer` (expirado, issuer/audience/chave diferentes) ou `UseAuthentication()` faltando / depois de `UseAuthorization()` | Confira a validação do JWT e a ordem do pipeline; envie `Authorization: Bearer <token>` |
| **403** com usuário logado | Falta a permissão exata `recurso:acao`; claim com tipo diferente de `ClaimType`; no modo banco, usuário inativo/inexistente (provider devolveu `null`) | Veja o log `Acesso negado: usuário X sem a permissão Y`; confira a claim `permissao` ou as tabelas |
| Mudei o perfil / bloqueei o usuário e ele ainda acessa | O perfil e as permissões ficam em cache | Chame `InvalidarUsuarioAsync(id)` (ou `InvalidarPerfilAsync`); com várias instâncias, registre um `IPermissaoInvalidationBus` |
| Endpoint que era público agora dá **401** | Padrão seguro: endpoint sem atributo exige autenticação | Marque `[AllowAnonymous]` se for público, ou `[RequerPermissao]`; só desligue `ExigirAutenticacaoPorPadrao` conscientemente |
| App não sobe: `Endpoints sem autorização declarada` | `ExigirEndpointsProtegidos()` encontrou rotas sem declaração | Adicione `[RequerPermissao]`, `[Authorize]` ou `[AllowAnonymous]`; ignore rotas técnicas: `ExigirEndpointsProtegidos(e => e.RoutePattern.RawText == "/health")` |
| Permissão não aparece na tabela | Sincronização antes de `MapControllers`; assembly errado; policy fora do formato `perm:recurso:acao` | Sincronize depois de mapear as rotas, use `app.Descobrir(...)` e confira o formato |
| `Sincronização abortada: desativaria N de M...` | Trava de segurança: assembly errado ou lista vazia | Verifique os assemblies; só aumente `LimiteDesativacao` se a remoção for intencional |
| `InvalidOperationException` ao registrar o EF | Falta informar de onde vem o perfil do usuário | Use `AddResourceAuthorizationEntityFramework<TContext, TProvider>()` (recomendado) ou `ConfiarNoPerfilDoToken = true` |
| `ArgumentException` no `[RequerPermissao]` | Recurso ou ação vazio, ou contendo `:` | Use valores como `("produto", "read")` |
| 401/403 sem corpo | Padrão do ASP.NET Core | Defina `EscreverResposta` para devolver o envelope da sua API |

Para diagnosticar, ative logs `Warning` de `Prime.Architecture.Authorization` e acompanhe as métricas
`prime.authorization.permissao_negada` e `prime.authorization.requisicao_negada`.

### 🧪 Projetos de exemplo e testes

| Projeto | O que é |
|---|---|
| `Prime.Architecture.Authorization.Examples` | Exemplos deste README (compilados e executados) |
| `Prime.Architecture.Authorization.Tests` | Testes do núcleo (unitários e de pipeline HTTP) |
| `Prime.Architecture.Authorization.EntityFramework.Tests` | Testes do pacote EF (SQLite, cache, bus, sincronização) |
| `Prime.Architecture.Authorization.SecurityTests` | Testes ofensivos (JWT forjado, contorno de rota, claims hostis, SQLi, log forging) e fuzzing com sementes reproduzíveis (`FUZZ_SEED`) |
| `Prime.Architecture.Authorization.LoadTests` | Teste de carga (Kestrel real): vazão, latência p50/p95/p99, stampede e memória. Resultados no [PRODUCAO.md](PRODUCAO.md) |

```bash
dotnet test      # roda tudo (unitários, integração, segurança e fuzzing)

# Teste de carga: 64 conexões simultâneas, 8 s por endpoint
dotnet run -c Release --project Prime.Architecture.Authorization.LoadTests -- 64 8

# Fuzzing com outra semente
FUZZ_SEED=12345 dotnet test Prime.Architecture.Authorization.SecurityTests --filter "FullyQualifiedName~Fuzz"
```

### ⚠️ Antes de usar em produção

Leia o [PRODUCAO.md](PRODUCAO.md): validação correta do JWT, tokens de vida curta, invalidar cache ao alterar
perfis, revisão de segurança independente e piloto em homologação.

---
*Desenvolvido com foco em escalabilidade e baixo consumo de recursos.*
