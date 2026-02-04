namespace Prime.Architecture.Mapper.Interfaces
{
    /// <summary>
    /// Interface principal para mapeamento de objetos entre tipos diferentes.
    /// </summary>
    public interface IMapper
    {
        /// <summary>
        /// Mapeia um objeto de origem para um novo objeto de destino.
        /// </summary>
        /// <typeparam name="TSource">Tipo do objeto de origem</typeparam>
        /// <typeparam name="TDestination">Tipo do objeto de destino</typeparam>
        /// <param name="source">Objeto de origem</param>
        /// <returns>Nova instância do tipo de destino com propriedades mapeadas</returns>
        TDestination Map<TSource, TDestination>(TSource source);

        /// <summary>
        /// Mapeia um objeto de origem para um objeto de destino existente.
        /// </summary>
        /// <typeparam name="TSource">Tipo do objeto de origem</typeparam>
        /// <typeparam name="TDestination">Tipo do objeto de destino</typeparam>
        /// <param name="source">Objeto de origem</param>
        /// <param name="destination">Objeto de destino existente</param>
        /// <returns>O objeto de destino com propriedades atualizadas</returns>
        TDestination Map<TSource, TDestination>(TSource source, TDestination destination);
    }
}
