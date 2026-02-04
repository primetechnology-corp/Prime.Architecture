using Prime.Architecture.Mapper.Configuration;
using Prime.Architecture.Mapper.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Prime.Architecture.Mapper.Implementation
{
    /// <summary>
    /// Implementação da expressão de mapeamento fluente.
    /// </summary>
    /// <typeparam name="TSource">Tipo de origem</typeparam>
    /// <typeparam name="TDestination">Tipo de destino</typeparam>
    public class MappingExpression<TSource, TDestination> : IMappingExpression<TSource, TDestination>
    {
        public Type SourceType => typeof(TSource);
        public Type DestinationType => typeof(TDestination);

        internal Dictionary<string, MemberConfiguration> MemberConfigurations { get; } = new Dictionary<string, MemberConfiguration>();
        internal HashSet<string> IgnoredMembers { get; } = new HashSet<string>();
        internal Func<TSource, TDestination>? CustomConverter { get; private set; }

        public IMappingExpression<TSource, TDestination> ForMember<TMember>(
            Expression<Func<TDestination, TMember>> destinationMember,
            Action<IMemberConfigurationExpression<TSource, TDestination, TMember>> options)
        {
            var memberName = GetMemberName(destinationMember);
            var config = new MemberConfigurationExpression<TSource, TDestination, TMember>();
            options(config);

            MemberConfigurations[memberName] = new MemberConfiguration
            {
                SourceExpression = config.SourceExpression,
                ValueResolver = config.ValueResolver
            };

            return this;
        }

        public IMappingExpression<TSource, TDestination> Ignore<TMember>(
            Expression<Func<TDestination, TMember>> destinationMember)
        {
            var memberName = GetMemberName(destinationMember);
            IgnoredMembers.Add(memberName);
            return this;
        }

        public IMappingExpression<TSource, TDestination> ConvertUsing(Func<TSource, TDestination> converter)
        {
            CustomConverter = converter ?? throw new ArgumentNullException(nameof(converter));
            return this;
        }

        private string GetMemberName<TMember>(Expression<Func<TDestination, TMember>> expression)
        {
            if (expression.Body is MemberExpression memberExpression)
            {
                return memberExpression.Member.Name;
            }

            throw new ArgumentException("Expression must be a member expression", nameof(expression));
        }
    }

    /// <summary>
    /// Implementação da configuração de membro.
    /// </summary>
    internal class MemberConfigurationExpression<TSource, TDestination, TMember> : IMemberConfigurationExpression<TSource, TDestination, TMember>
    {
        internal LambdaExpression? SourceExpression { get; private set; }
        internal Delegate? ValueResolver { get; private set; }

        public void MapFromExpression(Expression<Func<TSource, TMember>> sourceMember)
        {
            SourceExpression = sourceMember ?? throw new ArgumentNullException(nameof(sourceMember));
        }

        public void MapFrom(Func<TSource, TMember> valueResolver)
        {
            ValueResolver = valueResolver ?? throw new ArgumentNullException(nameof(valueResolver));
        }
    }
}
