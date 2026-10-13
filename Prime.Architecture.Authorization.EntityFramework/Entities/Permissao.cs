using System.Collections.Generic;

namespace Prime.Architecture.Authorization.EntityFramework.Entities;

/// <summary>Permissão no formato "recurso:acao".</summary>
public class Permissao
{
    public int Id { get; set; }
    public string Sigla { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;

    /// <summary>Permissões que deixaram de existir no código ficam inativas (e deixam de valer).</summary>
    public bool Ativo { get; set; } = true;

    public ICollection<PerfilPermissao> Perfis { get; set; } = new List<PerfilPermissao>();
    public ICollection<UsuarioPermissao> Usuarios { get; set; } = new List<UsuarioPermissao>();
}
