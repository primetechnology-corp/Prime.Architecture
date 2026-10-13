# Changelog

## Prime.Architecture.Authorization 1.0.0 / Prime.Architecture.Authorization.EntityFramework 1.0.0
Primeira versão.

**Núcleo**
- `[RequerPermissao("recurso", "acao")]` para controllers/actions e `.RequerPermissao(...)` para Minimal API, baseado em policies.
- `IPermissaoResolver` substituível; resolver padrão lê as claims `permissao`.
- Seguro por padrão: endpoints sem atributo exigem autenticação (`ExigirAutenticacaoPorPadrao = true`); falha fechada.
- Falha do `IPermissaoResolver` (ex.: banco fora do ar) nega o acesso com 403, registra erro no log (sem vazar token nem claims) e conta na métrica `prime.authorization.falha_no_resolver`; a exceção não sobe como 500.
- `ExigirEndpointsProtegidos()`: falha o startup se algum endpoint não declarar autorização.
- Descoberta de permissões em controllers, Minimal API, policies escritas à mão e catálogos de constantes (`[CatalogoDePermissoes]`).
- Respostas 401/403 padronizáveis (`EscreverResposta`).
- Auditoria de negações (log sanitizado contra log forging), `IPermissaoAuditor` para alterações administrativas e métricas (`System.Diagnostics.Metrics`).

**EntityFramework**
- Tabelas `Permissoes`, `PerfisPermissoes`, `UsuariosPermissoes`; negação explícita por usuário vence o perfil.
- Resolver com cache; perfil vigente lido do banco por `IUsuarioPerfilProvider` (usuário inativo = sem permissões).
- Cache da consulta de usuário/perfil (`DuracaoCachePerfilUsuario`) com proteção contra *stampede*; invalidação por `InvalidarUsuarioAsync`.
- Sincronização de permissões com trava de desativação, simulação e log.
- Sincronização tolerante a concorrência: conflito de gravação (índice único, deadlock) relê o banco e repete (`SincronizacaoOpcoes.MaximoTentativas`, padrão 3); várias instâncias podem sincronizar no startup sem lock externo.
- `SincronizacaoOpcoes.Invalidador`: após reativar ou desativar permissões, renova o cache de todas as instâncias; uma falha do bus é registrada e não desfaz a sincronização já gravada.
- Invalidação de cache entre instâncias via `IPermissaoInvalidationBus`.
- `IPermissaoCacheInvalidator.InvalidarTudo()`/`InvalidarTudoAsync()` e `PermissaoInvalidacaoTipo.Todos`: invalidam só o cache do pacote (contador de geração no `SingleFlightCache`), sem afetar o restante do `IMemoryCache` da aplicação.
- O assinante do bus ignora tipos de invalidação desconhecidos em vez de tratá-los como invalidação de usuário.

**Pacotes**
- Dependências do pacote EF fixadas no mínimo `10.0.0` (EF Core Relational e Hosting.Abstractions), sem versão flutuante.
- Licença MIT (arquivo `LICENSE`), README e símbolos (`.snupkg`).

**Qualidade**
- Testes unitários e de integração HTTP; suíte ofensiva (JWT adulterado, contorno de rota e verbo, claims hostis, log forging, injeção de SQL) e fuzzing com sementes reproduzíveis.
- Testes contra MySQL 8.4 (`...EntityFramework.MySqlTests`): rodam apenas com a variável `PRIME_MYSQL` e somente no banco `prime_auth_test`.
- Teste de carga (`...LoadTests`): sem respostas inesperadas; custo da autorização com cache de ~2 a 7%.

**Limitações conhecidas**
- Sem `Invalidador`, uma permissão reativada ou desativada pela sincronização só vale quando o cache expira (`DuracaoCache`, padrão 5 min); mudanças de perfil ou bloqueio de usuário valem em até `DuracaoCachePerfilUsuario` (padrão 1 min), ou na hora com `InvalidarUsuarioAsync`.
- Com várias instâncias, a invalidação só as alcança se um `IPermissaoInvalidationBus` (ex.: Redis) estiver registrado.
- Em bancos com collation insensível a acento (padrão do MySQL 8, `utf8mb4_0900_ai_ci`), siglas que só diferem por acento (`acao:ler` e `ação:ler`) colidem no índice único e a sincronização falha. Siglas acima de 150 caracteres também são rejeitadas.
- Os ids de usuário e de perfil são `int`.
- Validados em SQLite e MySQL; outros bancos (SQL Server, Postgres) ainda não foram testados.
