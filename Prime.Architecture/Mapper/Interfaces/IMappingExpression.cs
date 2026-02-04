using System;
using System.Linq.Expressions;

namespace Prime.Architecture.Mapper.Interfaces
{
    /// <summary>
    /// Interface para configuração fluente de mapeamento.
    /// </summary>
    public interface IMappingExpression
    {
        Type SourceType { get; }
        Type DestinationType { get; }
    }

    /// <summary>
    /// Interface genérica para configuração fluente de mapeamento.
    /// </summary>
    /// <typeparam name="TSource">Tipo de origem</typeparam>
    /// <typeparam name="TDestination">Tipo de destino</typeparam>
    public interface IMappingExpression<TSource, TDestination> : IMappingExpression
    {
        /// <summary>
        /// Configura o mapeamento de um membro específico.
        /// </summary>
        /// <typeparam name="TMember">Tipo do membro</typeparam>
        /// <param name="destinationMember">Expressão que seleciona o membro de destino</param>
        /// <param name="options">Opções de configuração do membro</param>
        /// <returns>Esta expressão de mapeamento para encadeamento fluente</returns>
        IMappingExpression<TSource, TDestination> ForMember<TMember>(
            Expression<Func<TDestination, TMember>> destinationMember,
            Action<IMemberConfigurationExpression<TSource, TDestination, TMember>> options);

        /// <summary>
        /// Ignora um membro específico no mapeamento.
        /// </summary>
        /// <typeparam name="TMember">Tipo do membro</typeparam>
        /// <param name="destinationMember">Expressão que seleciona o membro a ser ignorado</param>
        /// <returns>Esta expressão de mapeamento para encadeamento fluente</returns>
        IMappingExpression<TSource, TDestination> Ignore<TMember>(
            Expression<Func<TDestination, TMember>> destinationMember);

        /// <summary>
        /// Usa um conversor customizado para este mapeamento.
        /// </summary>
        /// <param name="converter">Função de conversão</param>
        /// <returns>Esta expressão de mapeamento para encadeamento fluente</returns>
        IMappingExpression<TSource, TDestination> ConvertUsing(Func<TSource, TDestination> converter);
    }

    /// <summary>
    /// Interface para configuração de membro individual.
    /// </summary>
    /// <typeparam name="TSource">Tipo de origem</typeparam>
    /// <typeparam name="TDestination">Tipo de destino</typeparam>
    /// <typeparam name="TMember">Tipo do membro</typeparam>
    public interface IMemberConfigurationExpression<TSource, TDestination, TMember>
    {
        /// <summary>
        /// Especifica de onde mapear o valor do membro.
        /// </summary>
        /// <param name="sourceMember">Expressão que seleciona o membro de origem</param>
        void MapFromExpression(Expression<Func<TSource, TMember>> sourceMember);

        /// <summary>
        /// Especifica de onde mapear o valor do membro usando uma função customizada.
        /// </summary>
        /// <param name="valueResolver">Função que resolve o valor</param>
        void MapFrom(Func<TSource, TMember> valueResolver);
    }
}
