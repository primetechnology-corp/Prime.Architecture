namespace Prime.Architecture.Authorization.EntityFramework.Entities;

/// <summary>Permissão concedida a um perfil. O perfil em si pertence à aplicação (sem FK).</summary>
public class PerfilPermissao
{
    public int Id { get; set; }
    public int PerfilId { get; set; }
    public int PermissaoId { get; set; }

    /// <summary>Falso é equivalente à ausência do registro: a permissão não é concedida pelo perfil.</summary>
    public bool Concedida { get; set; } = true;

    public Permissao Permissao { get; set; } = null!;
}
