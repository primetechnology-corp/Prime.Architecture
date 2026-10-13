namespace Prime.Architecture.Authorization.EntityFramework.Entities;

/// <summary>
/// Exceção por usuário. <see cref="Concedida"/> verdadeiro acrescenta a permissão ao que o perfil dá;
/// falso a nega explicitamente e sempre vence a concessão do perfil.
/// </summary>
public class UsuarioPermissao
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }
    public int PermissaoId { get; set; }
    public bool Concedida { get; set; } = true;

    public Permissao Permissao { get; set; } = null!;
}
