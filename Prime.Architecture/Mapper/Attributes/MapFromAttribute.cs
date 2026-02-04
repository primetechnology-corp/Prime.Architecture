using System;

namespace Prime.Architecture.Mapper.Attributes
{
    /// <summary>
    /// Atributo para especificar o nome da propriedade de origem no mapeamento.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class MapFromAttribute : Attribute
    {
        /// <summary>
        /// Nome da propriedade de origem.
        /// </summary>
        public string PropertyName { get; }

        /// <summary>
        /// Inicializa uma nova instância do atributo MapFrom.
        /// </summary>
        /// <param name="propertyName">Nome da propriedade de origem</param>
        public MapFromAttribute(string propertyName)
        {
            PropertyName = propertyName ?? throw new ArgumentNullException(nameof(propertyName));
        }
    }
}
