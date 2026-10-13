// Teste de carga do Prime.Architecture.Authorization (servidor Kestrel real + gerador de carga no mesmo processo).
// Uso:  dotnet run -c Release --project Prime.Architecture.Authorization.LoadTests -- [concorrencia] [segundos]
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Prime.Architecture.Authorization.EntityFramework;
using Prime.Architecture.Authorization.EntityFramework.Configuration;
using Prime.Architecture.Authorization.EntityFramework.Entities;
using Prime.Architecture.Authorization.EntityFramework.Extensions;
using Prime.Architecture.Authorization.EntityFramework.Implementation;
using Prime.Architecture.Authorization.Extensions;

var concorrencia = args.Length > 0 ? int.Parse(args[0]) : 64;
var segundos = args.Length > 1 ? int.Parse(args[1]) : 8;
const string chave = "ChaveDeCargaComMaisDe32Caracteres-Apenas-Para-Teste!!";
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(chave));
string NovoDb() => Path.Combine(Path.GetTempPath(), $"prime-load-{Guid.NewGuid():N}.db");
var dbFile = NovoDb();

Console.WriteLine($"Carga: {concorrencia} conexões simultâneas, {segundos}s por endpoint, {Environment.ProcessorCount} núcleos, GC servidor.");
Console.WriteLine();

var resultados = new List<(string Cenario, string Endpoint, Resultado R)>();

// ---------- Cenários ----------
await Cenario("claims (3 permissões no token)", Modo.Claims, 3);
await Cenario("claims (100 permissões no token)", Modo.Claims, 100);
await Cenario("EF com cache", Modo.EfComCache, 0);
await Cenario("EF sem cache (consulta a cada requisição)", Modo.EfSemCache, 0);

await Stampede();
await Memoria();

Console.WriteLine();
Console.WriteLine("## Resumo (req/s | p50 / p95 / p99 em ms | erros)");
Console.WriteLine();
Console.WriteLine("| Cenário | Endpoint | req/s | p50 | p95 | p99 | max | respostas inesperadas |");
Console.WriteLine("|---|---|---:|---:|---:|---:|---:|---:|");
foreach (var (c, e, r) in resultados)
    Console.WriteLine($"| {c} | {e} | {r.Rps:N0} | {r.P50:F2} | {r.P95:F2} | {r.P99:F2} | {r.Max:F1} | {r.Inesperadas} |");

return resultados.Any(x => x.R.Inesperadas > 0) ? 1 : 0;

// ===================================================================================
async Task Cenario(string nome, Modo modo, int permissoesNoToken)
{
    Console.WriteLine($"### {nome}");
    var porta = 5200 + resultados.Count % 50 + Random.Shared.Next(0, 40);
    await using var app = await Subir(modo, porta);
    var baseUrl = $"http://127.0.0.1:{porta}";

    var token = Token(modo == Modo.Claims ? Enumerable.Range(0, permissoesNoToken).Select(i => i == 0 ? "produto:read" : $"filler{i}:read") : null);

    // endpoint, status esperado, com token?
    var alvos = new (string Caminho, int Esperado, bool ComToken)[]
    {
        ("/livre", 200, false),            // sem autenticação (baseline do servidor)
        ("/autenticado", 200, true),       // só JWT (custo da autenticação)
        ("/protegido", 200, true),         // JWT + permissão concedida
        ("/negado", 403, true),            // JWT + permissão negada (caminho do 403)
        ("/protegido", 401, false),        // sem token (caminho do 401)
    };

    foreach (var (caminho, esperado, comToken) in alvos)
    {
        var rotulo = comToken || esperado == 200 ? caminho : caminho + " (sem token)";
        var r = await Carga(baseUrl + caminho, comToken ? token : null, esperado, concorrencia, segundos);
        resultados.Add((nome, rotulo, r));
        Console.WriteLine($"  {rotulo,-24} {r.Rps,9:N0} req/s   p50 {r.P50,6:F2}  p95 {r.P95,6:F2}  p99 {r.P99,7:F2}  max {r.Max,7:F1} ms   inesperadas: {r.Inesperadas}");
    }

    Console.WriteLine($"  memória (working set): {Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024} MB");
    Console.WriteLine();
}

async Task<WebApplication> Subir(Modo modo, int porta)
{
    var builder = WebApplication.CreateBuilder();
    builder.Logging.ClearProviders();   // mede a autorização, não a escrita de logs em console
    builder.WebHost.UseUrls($"http://127.0.0.1:{porta}");

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = "carga",
            ValidateAudience = true, ValidAudience = "carga",
            ValidateLifetime = true, ValidateIssuerSigningKey = true, IssuerSigningKey = signingKey,
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 }
        });
    builder.Services.AddAuthorization();
    builder.Services.AddResourceAuthorization();

    if (modo != Modo.Claims)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        dbFile = NovoDb();   // um arquivo por cenário
        var arquivo = dbFile;
        builder.Services.AddDbContext<CargaDbContext>(o => o.UseSqlite($"Data Source={arquivo}"));
        builder.Services.AddResourceAuthorizationEntityFramework<CargaDbContext, UsuarioPerfilDoBanco>(o =>
        {
            if (modo == Modo.EfSemCache)
            {
                o.DuracaoCache = TimeSpan.Zero;
                o.DuracaoCachePerfilUsuario = TimeSpan.Zero;
            }
        });
    }

    var app = builder.Build();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapGet("/livre", () => "ok").AllowAnonymous();
    app.MapGet("/autenticado", () => "ok").RequireAuthorization();
    app.MapGet("/protegido", () => "ok").RequerPermissao("produto", "read");
    app.MapGet("/negado", () => "ok").RequerPermissao("produto", "delete");

    if (modo != Modo.Claims)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CargaDbContext>();
        await db.Database.EnsureCreatedAsync();
        await db.SincronizarAsync(new[] { "produto:read", "produto:delete" });
        var ids = db.Set<Permissao>().ToDictionary(p => p.Sigla, p => p.Id);
        db.Set<PerfilPermissao>().Add(new PerfilPermissao { PerfilId = 1, PermissaoId = ids["produto:read"] });   // só leitura
        db.Usuarios.Add(new Usuario { Id = 1, PerfilId = 1, Ativo = true });
        await db.SaveChangesAsync();
    }

    await app.StartAsync();
    return app;
}

string Token(IEnumerable<string>? permissoes)
{
    var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "1") };
    if (permissoes != null) claims.AddRange(permissoes.Select(p => new Claim("permissao", p)));
    var jwt = new JwtSecurityToken("carga", "carga", claims, expires: DateTime.UtcNow.AddHours(1),
        signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));
    return new JwtSecurityTokenHandler().WriteToken(jwt);
}

async Task<Resultado> Carga(string url, string? token, int esperado, int conc, int segs)
{
    using var http = new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer = conc, PooledConnectionLifetime = TimeSpan.FromMinutes(5) });
    if (token != null) http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    async Task<(List<double> Lat, long Ok, long Inesperadas)> Fase(TimeSpan duracao, bool medir)
    {
        var fim = Stopwatch.GetTimestamp() + (long)(duracao.TotalSeconds * Stopwatch.Frequency);
        var tarefas = Enumerable.Range(0, conc).Select(_ => Task.Run(async () =>
        {
            var lat = new List<double>(medir ? 50_000 : 0);
            long ok = 0, ruim = 0;
            while (Stopwatch.GetTimestamp() < fim)
            {
                var t0 = Stopwatch.GetTimestamp();
                try
                {
                    using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                    await resp.Content.CopyToAsync(Stream.Null);
                    if ((int)resp.StatusCode == esperado) ok++; else ruim++;
                }
                catch { ruim++; }
                if (medir) lat.Add((Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency);
            }
            return (lat, ok, ruim);
        })).ToList();

        var partes = await Task.WhenAll(tarefas);
        return (partes.SelectMany(p => p.lat).ToList(), partes.Sum(p => p.ok), partes.Sum(p => p.ruim));
    }

    await Fase(TimeSpan.FromSeconds(2), medir: false);   // aquecimento (JIT, pool de conexões, cache)

    var sw = Stopwatch.StartNew();
    var (lats, okTotal, ruimTotal) = await Fase(TimeSpan.FromSeconds(segs), medir: true);
    sw.Stop();

    lats.Sort();
    double P(double q) => lats.Count == 0 ? 0 : lats[Math.Min(lats.Count - 1, (int)(lats.Count * q))];
    return new Resultado((okTotal + ruimTotal) / sw.Elapsed.TotalSeconds, P(0.50), P(0.95), P(0.99), lats.Count == 0 ? 0 : lats[^1], ruimTotal);
}

// Cache frio + muitas requisições simultâneas: o banco deve ser consultado uma vez, não uma por requisição
async Task Stampede()
{
    Console.WriteLine("### Stampede (cache frio, requisições simultâneas)");
    await using var app = await Subir(Modo.EfComCache, 5190);
    var token = Token(null);

    foreach (var rajada in new[] { 200, 1000 })
    {
        UsuarioPerfilDoBanco.Consultas = 0;
        using (var scope = app.Services.CreateScope())
        {
            var inv = app.Services.GetRequiredService<IPermissaoCacheInvalidator>();
            await inv.InvalidarUsuarioAsync(1);
            await inv.InvalidarPerfilAsync(1);
        }

        using var http = new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer = rajada });
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var respostas = await Task.WhenAll(Enumerable.Range(0, rajada).Select(_ => http.GetAsync("http://127.0.0.1:5190/protegido")));
        var ok = respostas.Count(r => r.StatusCode == HttpStatusCode.OK);

        Console.WriteLine($"  rajada de {rajada}: {ok} responderam 200; consultas ao banco do provider de usuário/perfil = {UsuarioPerfilDoBanco.Consultas}");
        if (ok != rajada || UsuarioPerfilDoBanco.Consultas != 1)
            resultados.Add(("stampede", $"rajada {rajada}", new Resultado(0, 0, 0, 0, 0, rajada - ok + Math.Max(0, UsuarioPerfilDoBanco.Consultas - 1))));
    }
    Console.WriteLine();
}

// Crescimento de memória do cache com muitos usuários distintos
async Task Memoria()
{
    Console.WriteLine("### Memória do cache (usuários distintos)");
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    var arquivoMem = NovoDb();
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddAuthorization();
    services.AddResourceAuthorization();
    services.AddDbContext<CargaDbContext>(o => o.UseSqlite($"Data Source={arquivoMem}"));
    services.AddResourceAuthorizationEntityFramework<CargaDbContext, UsuariosFicticios>();
    await using var sp = services.BuildServiceProvider();

    using (var scope = sp.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<CargaDbContext>();
        await db.Database.EnsureCreatedAsync();
        await db.SincronizarAsync(new[] { "produto:read" });
        db.Set<PerfilPermissao>().Add(new PerfilPermissao { PerfilId = 1, PermissaoId = db.Set<Permissao>().Single().Id });
        await db.SaveChangesAsync();
    }

    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    var antes = GC.GetTotalMemory(true);

    const int usuarios = 100_000;
    var sw = Stopwatch.StartNew();
    for (var lote = 0; lote < usuarios; lote += 1000)
    {
        await Task.WhenAll(Enumerable.Range(lote + 1, 1000).Select(async id =>
        {
            using var scope = sp.CreateScope();
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, id.ToString()) }, "t"));
            await scope.ServiceProvider.GetRequiredService<Prime.Architecture.Authorization.Interfaces.IPermissaoResolver>().ObterAsync(user);
        }));
    }
    sw.Stop();

    var depois = GC.GetTotalMemory(true);
    Console.WriteLine($"  {usuarios:N0} usuários distintos resolvidos em {sw.Elapsed.TotalSeconds:F1}s; heap +{(depois - antes) / 1024 / 1024} MB (~{(depois - antes) / usuarios} bytes por usuário em cache)");
    Console.WriteLine();
}

// ---------- tipos ----------
record Resultado(double Rps, double P50, double P95, double P99, double Max, long Inesperadas);
enum Modo { Claims, EfComCache, EfSemCache }

public class Usuario { public int Id { get; set; } public int PerfilId { get; set; } public bool Ativo { get; set; } }

public class CargaDbContext(DbContextOptions<CargaDbContext> options) : DbContext(options)
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    protected override void OnModelCreating(ModelBuilder b) { base.OnModelCreating(b); b.AplicarResourceAuthorization(); }
}

public class UsuarioPerfilDoBanco(CargaDbContext db) : IUsuarioPerfilProvider
{
    public static long Consultas;

    public async Task<UsuarioPerfilInfo?> ObterAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        Interlocked.Increment(ref Consultas);
        if (!int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return null;
        return await db.Usuarios.AsNoTracking().Where(u => u.Id == id && u.Ativo)
            .Select(u => new UsuarioPerfilInfo(u.Id, u.PerfilId)).FirstOrDefaultAsync(ct);
    }
}

public class UsuariosFicticios : IUsuarioPerfilProvider
{
    public Task<UsuarioPerfilInfo?> ObterAsync(ClaimsPrincipal user, CancellationToken ct = default)
        => Task.FromResult<UsuarioPerfilInfo?>(new UsuarioPerfilInfo(int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!), 1));
}
