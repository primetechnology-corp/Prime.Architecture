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
