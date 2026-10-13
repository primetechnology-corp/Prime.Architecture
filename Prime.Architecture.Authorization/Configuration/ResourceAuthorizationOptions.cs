using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Prime.Architecture.Authorization.Configuration;

/// <summary>
/// Opções do ResourceAuthorize.
/// </summary>
public class ResourceAuthorizationOptions
{
    /// <summary>
    /// Tipo da claim que carrega as permissões (padrão: "permissao").
    /// </summary>
    public string ClaimType { get; set; } = "permissao";


    /// <summary>
    /// Quando definido, escreve a resposta de 401 (não autenticado) e 403 (sem permissão),
    /// permitindo usar o envelope de resposta da API. Recebe o contexto e o status code.
    /// </summary>
    public Func<HttpContext, int, Task>? EscreverResposta { get; set; }

    /// <summary>
    /// Quando verdadeiro, endpoints sem nenhum atributo de autorização exigem usuário autenticado
    /// (FallbackPolicy). Endpoints com [AllowAnonymous] continuam públicos.
    /// Padrão: verdadeiro (seguro por padrão). Desligue apenas se souber o que está fazendo.
    /// </summary>
    public bool ExigirAutenticacaoPorPadrao { get; set; } = true;
}
