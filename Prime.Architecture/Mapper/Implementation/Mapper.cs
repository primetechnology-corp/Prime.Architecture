using Prime.Architecture.Mapper.Attributes;
using Prime.Architecture.Mapper.Interfaces;
using Prime.Architecture.Mapper.Configuration;
using Prime.Architecture.Mapper.Internal;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Prime.Architecture.Mapper.Implementation
{
    /// <summary>
    /// Implementação principal do mapeador de objetos.
    /// </summary>
    public class Mapper : IMapper
    {
        private readonly MapperConfiguration _configuration;
        private readonly ConcurrentDictionary<TypePair, Delegate> _compiledMappers = new ConcurrentDictionary<TypePair, Delegate>();
        private readonly ConcurrentDictionary<Type, Delegate> _propertyCopiers = new ConcurrentDictionary<Type, Delegate>();
        private readonly CircularReferenceTracker _circularReferenceTracker = new CircularReferenceTracker();

        internal Mapper(MapperConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public TDestination Map<TSource, TDestination>(TSource source)
        {
            if (source == null)
                return default!;

            var destination = Activator.CreateInstance<TDestination>();
            return Map(source, destination);
        }

        public TDestination Map<TSource, TDestination>(TSource source, TDestination destination)
        {
            if (source == null)
                return destination;

            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            var sourceType = typeof(TSource);
            var destinationType = typeof(TDestination);

            // Verificar se há um conversor customizado configurado
            var mappingExpression = _configuration.GetMapping(sourceType, destinationType) as MappingExpression<TSource, TDestination>;
            if (mappingExpression?.CustomConverter != null)
            {
                var result = mappingExpression.CustomConverter(source);
                CopyProperties(result, destination);
                return destination;
            }

            // Obter ou criar o mapeador compilado
            var key = new TypePair(sourceType, destinationType);
            var mapper = _compiledMappers.GetOrAdd(key, _ => CreateMapper<TSource, TDestination>());

            var typedMapper = (Action<TSource, TDestination, Mapper>)mapper;
            typedMapper(source, destination, this);

            return destination;
        }

        private Delegate CreateMapper<TSource, TDestination>()
        {
            var sourceParam = Expression.Parameter(typeof(TSource), "source");
            var destParam = Expression.Parameter(typeof(TDestination), "dest");
            var mapperParam = Expression.Parameter(typeof(Mapper), "mapper");

            var assignments = new List<Expression>();
            var mappingExpression = _configuration.GetMapping(typeof(TSource), typeof(TDestination)) as MappingExpression<TSource, TDestination>;

            var destProperties = typeof(TDestination).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite);

            foreach (var destProp in destProperties)
            {
                // Verificar se a propriedade deve ser ignorada
                if (destProp.GetCustomAttribute<IgnoreMapAttribute>() != null)
                    continue;

                if (mappingExpression?.IgnoredMembers.Contains(destProp.Name) == true)
                    continue;

                Expression? valueExpression = null;

                // Verificar se há configuração customizada para este membro
                if (mappingExpression?.MemberConfigurations.TryGetValue(destProp.Name, out var memberConfig) == true)
                {
                    if (memberConfig.SourceExpression != null)
                    {
                        // Substituir o parâmetro da expressão pelo nosso parâmetro source
                        var visitor = new ParameterReplacementVisitor(memberConfig.SourceExpression.Parameters[0], sourceParam);
                        valueExpression = visitor.Visit(memberConfig.SourceExpression.Body);
                    }
                    else if (memberConfig.ValueResolver != null)
                    {
                        // Usar o value resolver
                        var resolverCall = Expression.Invoke(Expression.Constant(memberConfig.ValueResolver), sourceParam);
                        valueExpression = resolverCall;
                    }
                }

                // Se não há configuração customizada, usar mapeamento por convenção
                if (valueExpression == null)
                {
                    valueExpression = GetConventionBasedMapping(sourceParam, destProp);
                }

                if (valueExpression != null)
                {
                    // Converter tipo se necessário
                    if (valueExpression.Type != destProp.PropertyType)
                    {
                        valueExpression = ConvertExpression(valueExpression, destProp.PropertyType, mapperParam);
                    }

                    var destProperty = Expression.Property(destParam, destProp);
                    var assignment = Expression.Assign(destProperty, valueExpression);
                    assignments.Add(assignment);
                }
            }

            if (assignments.Count == 0)
            {
                // Se não há propriedades para mapear, retornar uma ação vazia
                return (Action<TSource, TDestination, Mapper>)((s, d, m) => { });
            }

            var body = Expression.Block(assignments);
            var lambda = Expression.Lambda<Action<TSource, TDestination, Mapper>>(body, sourceParam, destParam, mapperParam);

            return lambda.Compile();
        }

        private Expression? GetConventionBasedMapping(ParameterExpression sourceParam, PropertyInfo destProp)
        {
            var sourceType = sourceParam.Type;

            // Verificar atributo MapFrom na propriedade de destino
            var mapFromAttr = destProp.GetCustomAttribute<MapFromAttribute>();
            if (mapFromAttr != null)
            {
                var sourceProp = sourceType.GetProperty(mapFromAttr.PropertyName, BindingFlags.Public | BindingFlags.Instance);
                if (sourceProp != null && sourceProp.CanRead)
                {
                    return Expression.Property(sourceParam, sourceProp);
                }
            }

            // Procurar propriedade com o mesmo nome
            var sourceProperty = sourceType.GetProperty(destProp.Name, BindingFlags.Public | BindingFlags.Instance);
            if (sourceProperty != null && sourceProperty.CanRead)
            {
                return Expression.Property(sourceParam, sourceProperty);
            }

            // Verificar atributo MapTo nas propriedades de origem
            var sourceProperties = sourceType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead);

            foreach (var sourceProp in sourceProperties)
            {
                var mapToAttr = sourceProp.GetCustomAttribute<MapToAttribute>();
                if (mapToAttr?.PropertyName == destProp.Name)
                {
                    return Expression.Property(sourceParam, sourceProp);
                }
            }

            return null;
        }

        private Expression ConvertExpression(Expression source, Type targetType, ParameterExpression mapperParam)
        {
            // Se os tipos são compatíveis, fazer conversão direta
            if (targetType.IsAssignableFrom(source.Type))
            {
                return source;
            }

            // Tratar tipos nullable
            var underlyingTargetType = Nullable.GetUnderlyingType(targetType);
            var underlyingSourceType = Nullable.GetUnderlyingType(source.Type);

            if (underlyingTargetType != null)
            {
                // Destino é nullable
                if (underlyingSourceType != null)
                {
                    // Ambos são nullable
                    var convertedValue = ConvertExpression(
                        Expression.Property(source, "Value"),
                        underlyingTargetType,
                        mapperParam);
                    return Expression.Condition(
                        Expression.Property(source, "HasValue"),
                        Expression.Convert(convertedValue, targetType),
                        Expression.Constant(null, targetType));
                }
                else
                {
                    // Apenas destino é nullable
                    var convertedValue = ConvertExpression(source, underlyingTargetType, mapperParam);
                    return Expression.Convert(convertedValue, targetType);
                }
            }

            // Se a origem é nullable mas o destino não é
            if (underlyingSourceType != null)
            {
                var valueExpression = Expression.Property(source, "Value");
                return ConvertExpression(valueExpression, targetType, mapperParam);
            }

            // Verificar se é uma coleção
            if (IsEnumerableType(source.Type) && IsEnumerableType(targetType))
            {
                return CreateCollectionMapping(source, targetType, mapperParam);
            }

            // Verificar se é um objeto complexo que precisa ser mapeado
            if (!source.Type.IsPrimitive && !targetType.IsPrimitive &&
                source.Type != typeof(string) && targetType != typeof(string) &&
                !source.Type.IsEnum && !targetType.IsEnum)
            {
                return CreateNestedObjectMapping(source, targetType, mapperParam);
            }

            // Conversão padrão
            try
            {
                return Expression.Convert(source, targetType);
            }
            catch
            {
                // Se a conversão falhar, retornar valor padrão
                return Expression.Default(targetType);
            }
        }

        private Expression CreateCollectionMapping(Expression source, Type targetType, ParameterExpression mapperParam)
        {
            var sourceElementType = GetEnumerableElementType(source.Type);
            var targetElementType = GetEnumerableElementType(targetType);

            if (sourceElementType == null || targetElementType == null)
            {
                return Expression.Default(targetType);
            }

            // Criar expressão para mapear cada elemento
            var mapMethod = typeof(Mapper).GetMethod(nameof(MapInternal), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(sourceElementType, targetElementType);

            var selectMethod = typeof(Enumerable).GetMethods()
                .First(m => m.Name == "Select" && m.GetParameters().Length == 2)
                .MakeGenericMethod(sourceElementType, targetElementType);

            var sourceParam = Expression.Parameter(sourceElementType, "item");
            var mapCall = Expression.Call(mapperParam, mapMethod, sourceParam);
            var selector = Expression.Lambda(mapCall, sourceParam);

            var selectCall = Expression.Call(selectMethod, source, selector);

            // Converter para o tipo de coleção apropriado
            if (targetType.IsArray)
            {
                var toArrayMethod = typeof(Enumerable).GetMethod("ToArray")!.MakeGenericMethod(targetElementType);
                return Expression.Call(toArrayMethod, selectCall);
            }
            else if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(List<>))
            {
                var toListMethod = typeof(Enumerable).GetMethod("ToList")!.MakeGenericMethod(targetElementType);
                return Expression.Call(toListMethod, selectCall);
            }
            else
            {
                return selectCall;
            }
        }

        private Expression CreateNestedObjectMapping(Expression source, Type targetType, ParameterExpression mapperParam)
        {
            var mapMethod = typeof(Mapper).GetMethod(nameof(MapInternal), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(source.Type, targetType);

            return Expression.Call(mapperParam, mapMethod, source);
        }

        private TDestination MapInternal<TSource, TDestination>(TSource source)
        {
            if (source == null)
                return default!;

            // Verificar referência circular
            if (_circularReferenceTracker.IsCircularReference(source))
            {
                return default!;
            }

            try
            {
                _circularReferenceTracker.Push(source);
                return Map<TSource, TDestination>(source);
            }
            finally
            {
                _circularReferenceTracker.Pop(source);
            }
        }

        private bool IsEnumerableType(Type type)
        {
            if (type == typeof(string))
                return false;

            return typeof(IEnumerable).IsAssignableFrom(type);
        }

        private Type? GetEnumerableElementType(Type type)
        {
            if (type.IsArray)
                return type.GetElementType();

            if (type.IsGenericType)
            {
                var genericArgs = type.GetGenericArguments();
                if (genericArgs.Length == 1)
                    return genericArgs[0];
            }

            var enumerableInterface = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

            return enumerableInterface?.GetGenericArguments()[0];
        }

        private void CopyProperties<T>(T source, T destination)
        {
            if (source == null || destination == null)
                return;

            var copier = (Action<T, T>)_propertyCopiers.GetOrAdd(typeof(T), _ => CreatePropertyCopier<T>());
            copier(source, destination);
        }

        private Action<T, T> CreatePropertyCopier<T>()
        {
            var sourceParam = Expression.Parameter(typeof(T), "source");
            var destParam = Expression.Parameter(typeof(T), "dest");

            var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite);

            var assignments = new List<Expression>();
            foreach (var prop in properties)
            {
                var destProperty = Expression.Property(destParam, prop);
                var sourceProperty = Expression.Property(sourceParam, prop);
                var assignment = Expression.Assign(destProperty, sourceProperty);
                assignments.Add(assignment);
            }

            if (assignments.Count == 0)
            {
                return (s, d) => { };
            }

            var body = Expression.Block(assignments);
            var lambda = Expression.Lambda<Action<T, T>>(body, sourceParam, destParam);

            return lambda.Compile();
        }

        private struct TypePair : IEquatable<TypePair>
        {
            public Type SourceType { get; }
            public Type DestinationType { get; }

            public TypePair(Type sourceType, Type destinationType)
            {
                SourceType = sourceType;
                DestinationType = destinationType;
            }

            public bool Equals(TypePair other)
            {
                return SourceType == other.SourceType && DestinationType == other.DestinationType;
            }

            public override bool Equals(object? obj)
            {
                return obj is TypePair other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((SourceType?.GetHashCode() ?? 0) * 397) ^ (DestinationType?.GetHashCode() ?? 0);
                }
            }
        }
    }
}
