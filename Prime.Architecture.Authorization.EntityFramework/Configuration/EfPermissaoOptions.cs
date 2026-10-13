using System;
using System.Security.Claims;

namespace Prime.Architecture.Authorization.EntityFramework.Configuration;

public class EfPermissaoOptions
{
    /// <summary>
    /// Usa o perfil que vem no token em vez de consultar o banco. Mudanças de perfil e bloqueio de usuário só valem
    /// quando o token expira. Prefira registrar um <see cref="IUsuarioPerfilProvider"/>. Padrão: falso.
    /// </summary>
    public bool ConfiarNoPerfilDoToken { get; set; }

    /// <summary>Claim com o id do perfil (usado só com <see cref="ConfiarNoPerfilDoToken"/>; padrão: "perfil_id").</summary>
    public string ClaimPerfil { get; set; } = "perfil_id";

    /// <summary>Claim com o id do usuário (padrão: NameIdentifier).</summary>
    public string ClaimUsuario { get; set; } = ClaimTypes.NameIdentifier;

    /// <summary>
    /// Tempo de cache da consulta de usuário/perfil (<see cref="IUsuarioPerfilProvider"/>), padrão 1 minuto.
    /// Bloqueios e trocas de perfil valem no máximo após este tempo, ou na hora com <c>InvalidarUsuarioAsync</c>.
    /// Use <see cref="TimeSpan.Zero"/> para consultar a cada requisição.
    /// </summary>
    public TimeSpan DuracaoCachePerfilUsuario { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Tempo de cache das permissões. Use <see cref="TimeSpan.Zero"/> para desativar.</summary>
    public TimeSpan DuracaoCache { get; set; } = TimeSpan.FromMinutes(5);
}
