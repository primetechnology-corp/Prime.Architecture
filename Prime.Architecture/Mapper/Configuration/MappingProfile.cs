using System;
using System.Collections.Generic;
using Prime.Architecture.Mapper.Interfaces;
using Prime.Architecture.Mapper.Implementation;

namespace Prime.Architecture.Mapper.Configuration
{
    /// <summary>
    /// Classe base para criar perfis de mapeamento.
    /// </summary>
    public abstract class MappingProfile
    {
        internal List<IMappingExpression> Mappings { get; } = new List<IMappingExpression>();

        /// <summary>
        /// Cria uma configuração de mapeamento entre dois tipos.
        /// </summary>
        /// <typeparam name="TSource">Tipo de origem</typeparam>
        /// <typeparam name="TDestination">Tipo de destino</typeparam>
        /// <returns>Expressão de mapeamento para configuração fluente</returns>
        protected IMappingExpression<TSource, TDestination> CreateMap<TSource, TDestination>()
        {
            var expression = new MappingExpression<TSource, TDestination>();
            Mappings.Add(expression);
            return expression;
        }
    }
}
