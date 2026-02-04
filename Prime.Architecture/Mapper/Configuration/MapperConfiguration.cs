using Prime.Architecture.Mapper.Interfaces;
using System;
using System.Collections.Generic;

namespace Prime.Architecture.Mapper.Configuration
{
    /// <summary>
    /// Configuração global do mapeador.
    /// </summary>
    public class MapperConfiguration
    {
        private readonly List<MappingProfile> _profiles = new List<MappingProfile>();
        private readonly Dictionary<TypePair, IMappingExpression> _mappings = new Dictionary<TypePair, IMappingExpression>();

        /// <summary>
        /// Adiciona um perfil de mapeamento.
        /// </summary>
        /// <typeparam name="TProfile">Tipo do perfil</typeparam>
        public void AddProfile<TProfile>() where TProfile : MappingProfile, new()
        {
            var profile = new TProfile();
            AddProfile(profile);
        }

        /// <summary>
        /// Adiciona um perfil de mapeamento.
        /// </summary>
        /// <param name="profile">Instância do perfil</param>
        public void AddProfile(MappingProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            _profiles.Add(profile);

            foreach (var mapping in profile.Mappings)
            {
                var key = new TypePair(mapping.SourceType, mapping.DestinationType);
                _mappings[key] = mapping;
            }
        }

        /// <summary>
        /// Cria uma instância do mapeador com esta configuração.
        /// </summary>
        /// <returns>Nova instância do mapeador</returns>
        public IMapper CreateMapper()
        {
            return new Implementation.Mapper(this);
        }

        /// <summary>
        /// Obtém a configuração de mapeamento para um par de tipos.
        /// </summary>
        internal IMappingExpression? GetMapping(Type sourceType, Type destinationType)
        {
            var key = new TypePair(sourceType, destinationType);
            return _mappings.TryGetValue(key, out var mapping) ? mapping : null;
        }

        /// <summary>
        /// Valida a configuração.
        /// </summary>
        public void AssertConfigurationIsValid()
        {
            // Implementação futura: validar que todos os mapeamentos estão corretos
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
