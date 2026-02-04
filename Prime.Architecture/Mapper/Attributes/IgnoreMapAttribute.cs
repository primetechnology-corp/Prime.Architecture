using System;

namespace Prime.Architecture.Mapper.Attributes
{
    /// <summary>
    /// Atributo para marcar propriedades que devem ser ignoradas no mapeamento.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class IgnoreMapAttribute : Attribute
    {
    }
}
