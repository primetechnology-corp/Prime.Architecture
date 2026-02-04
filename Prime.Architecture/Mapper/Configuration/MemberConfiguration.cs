using System;
using System.Linq.Expressions;

namespace Prime.Architecture.Mapper.Configuration
{
    /// <summary>
    /// Configuração de um membro individual.
    /// </summary>
    internal class MemberConfiguration
    {
        public LambdaExpression? SourceExpression { get; set; }
        public Delegate? ValueResolver { get; set; }
    }
}
