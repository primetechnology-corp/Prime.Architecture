using System.Linq.Expressions;

namespace Prime.Architecture.Mapper.Internal
{
    /// <summary>
    /// Visitor para substituir parâmetros em expressões.
    /// </summary>
    internal class ParameterReplacementVisitor : ExpressionVisitor
    {
        private readonly ParameterExpression _oldParameter;
        private readonly ParameterExpression _newParameter;

        public ParameterReplacementVisitor(ParameterExpression oldParameter, ParameterExpression newParameter)
        {
            _oldParameter = oldParameter;
            _newParameter = newParameter;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            return node == _oldParameter ? _newParameter : base.VisitParameter(node);
        }
    }
}
