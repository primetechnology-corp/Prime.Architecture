# Checklist de produção — Prime.Architecture.Authorization

## Já implementado e testado
- [x] Falha fechada: sem permissão, sem autenticação ou erro no resolver (banco fora do ar) → acesso negado (403, com log de erro e métrica `falha_no_resolver`), nunca 500.
- [x] Padrão seguro: endpoints sem atributo exigem autenticação (`ExigirAutenticacaoPorPadrao = true`).
- [x] Siglas validadas (recurso/ação não vazios, sem `:`), normalizadas em minúsculas.
- [x] Auditoria de negações (401/403) via `ILogger`, sem vazar token nem claims.
- [x] EF: perfil e situação do usuário lidos do banco (`IUsuarioPerfilProvider`); usuário inativo/inexistente = sem permissões.
- [x] EF: negação explícita por usuário vence a concessão do perfil; permissão inativa não vale.
- [x] EF: sincronização com trava (limite de desativação) e modo simulação.
- [x] Suíte de testes ofensivos (`Prime.Architecture.Authorization.SecurityTests`, 79 testes): ataques a JWT (alg=none, payload adulterado, chave errada, expirado, issuer/audience), contorno de rota e verbo, claims manipuladas (curingas, unicode, byte nulo), log forging, ids hostis, injeção de SQL, sincronização maliciosa, perfil forjado no token.
- [x] Fuzzing (`FuzzTests`, sementes reproduzíveis via `FUZZ_SEED`): oráculo de autorização com 20 mil casos aleatórios, 50 mil entradas hostis no atributo e no sanitizador de log, 4 mil requisições HTTP com caminhos/cabeçalhos aleatórios, sincronização com siglas aleatórias. Validado por teste de mutação (3 bugs inseridos de propósito foram detectados).
- [x] Teste de carga (`Prime.Architecture.Authorization.LoadTests`): milhões de requisições sem nenhuma resposta com status inesperado (ver resultados abaixo).
- [x] Log sanitizado contra *log forging* (quebras de linha e caracteres de controle em id de usuário e caminho).
- [x] Build com `TreatWarningsAsErrors`, pacotes determinísticos, testes unitários e de integração (HTTP).

- [x] `ExigirEndpointsProtegidos()`, descoberta de Minimal API/policies/constantes, auditoria (`IPermissaoAuditor`), métricas e invalidação de cache entre instâncias (`IPermissaoInvalidationBus`).

- [x] Testes contra MySQL 8.4 real (`...EntityFramework.MySqlTests`): esquema, resolver, collation, sincronização concorrente, stampede e latência (~0,04 ms com cache, ~17–20 ms sem, banco remoto). Rodam com `PRIME_MYSQL` apontando para um banco **descartável** chamado `prime_auth_test`.
- [x] Sincronização tolera várias instâncias subindo juntas (releitura e nova tentativa) e renova o cache após reativar/desativar permissões (`SincronizacaoOpcoes.Invalidador`).

- [x] Cache da consulta de perfil com proteção contra stampede (uma consulta por usuário, mesmo com cache frio e requisições simultâneas).

## Responsabilidade da aplicação (não do pacote)
- [ ] Validar o JWT corretamente: `ValidateIssuer`, `ValidateAudience`, `ValidateLifetime`, chave forte fora do código.
- [ ] Tokens de vida curta (ex.: 15–60 min) e refresh seguro.
- [ ] Implementar `IUsuarioPerfilProvider` consultando o banco (usuário ativo, perfil vigente).
- [ ] Ao bloquear usuário ou trocar perfil, chamar `InvalidarUsuarioAsync` (o perfil do usuário fica em cache por `DuracaoCachePerfilUsuario`, padrão 1 min). Invalidar o cache com `InvalidarPerfilAsync`/`InvalidarUsuarioAsync` ao alterar perfis. Em várias instâncias, registre um `IPermissaoInvalidationBus` (ex.: Redis) ou mantenha `DuracaoCache` curto.
- [ ] Registrar quem concede/nega permissões com `IPermissaoAuditor` (ou sua implementação em tabela) e restringir `admin:manage`.
- [ ] Rodar `SincronizarAsync` com `Simular = true` em CI/homologação antes do deploy; em produção, passe `Invalidador` para a revogação valer na hora.
- [ ] Banco com collation insensível a acento (MySQL 8 padrão): não use siglas que só diferem por acento.
- [ ] Chamar `app.ExigirEndpointsProtegidos()` após mapear as rotas (falha o startup se algum endpoint ficou sem declaração).
- [ ] Alertar em picos de 403/401 e em qualquer valor da métrica `prime.authorization.falha_no_resolver` (indica banco/provider fora do ar) no meter `Prime.Architecture.Authorization`.

## Antes do primeiro uso em produção
- [ ] Revisão de segurança independente e teste de invasão (não realizados).
- [ ] Piloto em homologação com dados reais de perfis/permissões e tokens reais.
- [ ] Publicar os pacotes em um feed e fixar as versões (nada publicado até agora).
- [ ] Plano de rollback (manter o mecanismo anterior até validar).

## Resultados do teste de carga

Executado com `dotnet run -c Release --project Prime.Architecture.Authorization.LoadTests -- 64 8` (64 conexões simultâneas,
8 s por endpoint, 14 núcleos, Kestrel real, SQLite local, logs desligados). Servidor e gerador de carga na mesma máquina:
use os números como **comparação relativa**, não como capacidade de produção.

| Cenário | /autenticado (só JWT) | /protegido (JWT + permissão) | /negado (403) | Custo da autorização |
|---|---:|---:|---:|---:|
| Claims, 3 permissões no token | 334 mil req/s | 321 mil req/s | 313 mil req/s | ~4% |
| Claims, 100 permissões no token | 164 mil req/s | 172 mil req/s | 170 mil req/s | desprezível (o custo é o token maior) |
| EF **com** cache | 241 mil req/s | 230 mil req/s | 230 mil req/s | ~5% |
| EF **sem** cache | 237 mil req/s | 25 mil req/s | 25 mil req/s | ~10x mais lento |

- Latência p99 com cache: ~1 ms. Sem cache (SQLite local): ~6 ms; com banco remoto será maior. **Não desligue o cache** sem necessidade.
- **Stampede:** com cache frio, rajadas de 200 e de 1.000 requisições simultâneas resultaram em **1 única consulta** ao banco.
- **Memória:** ~600 bytes por usuário em cache (100 mil usuários distintos ≈ 56 MB); as entradas expiram (`DuracaoCache`).
- **Correção sob carga:** 0 respostas com status inesperado (nenhum 200 onde devia ser 403/401), também conferido com o `ab` (ApacheBench).
- Tokens grandes (muitas claims) custam muito mais do que a verificação de permissão: prefira o modo com banco se o usuário tem dezenas de permissões.
