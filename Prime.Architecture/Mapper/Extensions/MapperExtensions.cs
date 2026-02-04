using Prime.Architecture.Mapper.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Prime.Architecture.Mapper.Extensions
{
    /// <summary>
    /// Métodos de extensão para facilitar o uso do mapeador.
    /// </summary>
    public static class MapperExtensions
    {
        /// <summary>
        /// Mapeia uma coleção de objetos para uma lista de outro tipo.
        /// </summary>
        /// <typeparam name="TSource">Tipo de origem</typeparam>
        /// <typeparam name="TDestination">Tipo de destino</typeparam>
        /// <param name="source">Coleção de origem</param>
        /// <param name="mapper">Instância do mapeador</param>
        /// <returns>Lista de objetos mapeados</returns>
        public static List<TDestination> MapList<TSource, TDestination>(
            this IEnumerable<TSource> source,
            IMapper mapper)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            if (mapper == null)
                throw new ArgumentNullException(nameof(mapper));

            return source.Select(item => mapper.Map<TSource, TDestination>(item)).ToList();
        }

        /// <summary>
        /// Mapeia um objeto para outro tipo.
        /// </summary>
        /// <typeparam name="TDestination">Tipo de destino</typeparam>
        /// <param name="source">Objeto de origem</param>
        /// <param name="mapper">Instância do mapeador</param>
        /// <returns>Objeto mapeado</returns>
        public static TDestination MapTo<TDestination>(this object source, IMapper mapper)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            if (mapper == null)
                throw new ArgumentNullException(nameof(mapper));

            var mapMethod = typeof(IMapper).GetMethod(nameof(IMapper.Map), new[] { source.GetType() })!
                .MakeGenericMethod(source.GetType(), typeof(TDestination));

            return (TDestination)mapMethod.Invoke(mapper, new[] { source })!;
        }
    }
}
