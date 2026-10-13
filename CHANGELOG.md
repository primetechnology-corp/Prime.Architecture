# Changelog

## Prime.Architecture.Authorization 1.0.0 / Prime.Architecture.Authorization.EntityFramework 1.0.0
Primeira versão.

**Núcleo**
- `[RequerPermissao("recurso", "acao")]` para controllers/actions e `.RequerPermissao(...)` para Minimal API, baseado em policies.
- `IPermissaoResolver` substituível; resolver padrão lê as claims `permissao`.
- Seguro por padrão: endpoints sem atributo exigem autenticação (`ExigirAutenticacaoPorPadrao = true`); falha fechada.
- `ExigirEndpointsProtegidos()`: falha o startup se algum endpoint não declarar autorização.
- Descoberta de permissões em controllers, Minimal API, policies escritas à mão e catálogos de constantes (`[CatalogoDePermissoes]`).
- Respostas 401/403 padronizáveis (`EscreverResposta`).
- Auditoria de negações (log sanitizado contra log forging), `IPermissaoAuditor` para alterações administrativas e métricas (`System.Diagnostics.Metrics`).

**EntityFramework**
- Tabelas `Permissoes`, `PerfisPermissoes`, `UsuariosPermissoes`; negação explícita por usuário vence o perfil.
- Resolver com cache; perfil vigente lido do banco por `IUsuarioPerfilProvider` (usuário inativo = sem permissões).
- Sincronização de permissões com trava de desativação, simulação e log.
- Invalidação de cache entre instâncias via `IPermissaoInvalidationBus`.
- Cache da consulta de usuário/perfil (`DuracaoCachePerfilUsuario`) com proteção contra *stampede*; invalidação por `InvalidarUsuarioAsync`.
- Sincronização tolerante a concorrência: conflito de gravação (índice único, deadlock) relê o banco e repete (`SincronizacaoOpcoes.MaximoTentativas`, padrão 3).
- `SincronizacaoOpcoes.Invalidador`: após reativar/desativar permissões, renova o cache de todas as instâncias; falha do bus é registrada, não desfaz a sincronização.
- `IPermissaoCacheInvalidator.InvalidarTudo()`/`InvalidarTudoAsync()` e `PermissaoInvalidacaoTipo.Todos`: invalidam só o cache do pacote (contador de geração no `SingleFlightCache`), sem afetar o restante do `IMemoryCache`.
- O assinante do bus ignora tipos de invalidação desconhecidos em vez de tratá-los como invalidação de usuário.
- Testes contra MySQL (`...EntityFramework.MySqlTests`, usa a variável `PRIME_MYSQL` e só roda no banco `prime_auth_test`).
- Falha do `IPermissaoResolver` (ex.: banco fora do ar) agora nega o acesso com 403, registra erro no log (sem vazar token nem claims) e conta na métrica `prime.authorization.falha_no_resolver`; antes a exceção subia como 500.
- Dependências do pacote EF fixadas no mínimo `10.0.0` (EF Core Relational e Hosting.Abstractions), em vez de `10.*`.

