using System;

namespace Prime.Architecture.Mapper.Attributes
{
    /// <summary>
    /// Atributo para especificar o nome da propriedade de destino no mapeamento.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class MapToAttribute : Attribute
    {
        /// <summary>
        /// Nome da propriedade de destino.
        /// </summary>
        public string PropertyName { get; }

        /// <summary>
        /// Inicializa uma nova instância do atributo MapTo.
        /// </summary>
        /// <param name="propertyName">Nome da propriedade de destino</param>
        public MapToAttribute(string propertyName)
        {
            PropertyName = propertyName ?? throw new ArgumentNullException(nameof(propertyName));
        }
    }
}
