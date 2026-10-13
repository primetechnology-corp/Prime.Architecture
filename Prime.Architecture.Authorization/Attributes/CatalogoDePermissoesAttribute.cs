using System;

namespace Prime.Architecture.Authorization.Attributes;

/// <summary>
/// Marca uma classe estática cujas constantes <c>const string</c> ("recurso:acao") formam o catálogo central de permissões.
/// Classes aninhadas também são lidas. Exemplo: <c>public const string ProdutoRead = "produto:read";</c>
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class CatalogoDePermissoesAttribute : Attribute
{
}
