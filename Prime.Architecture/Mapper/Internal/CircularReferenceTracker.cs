using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Prime.Architecture.Mapper.Internal
{
    /// <summary>
    /// Rastreador de referências circulares usando ConditionalWeakTable.
    /// </summary>
    internal class CircularReferenceTracker
    {
        private readonly ConditionalWeakTable<object, object> _references = new ConditionalWeakTable<object, object>();
        private readonly HashSet<object> _currentPath = new HashSet<object>();

        /// <summary>
        /// Verifica se o objeto já está sendo processado (referência circular).
        /// </summary>
        public bool IsCircularReference(object obj)
        {
            if (obj == null)
                return false;

            return _currentPath.Contains(obj);
        }

        /// <summary>
        /// Adiciona o objeto ao caminho atual.
        /// </summary>
        public void Push(object obj)
        {
            if (obj != null)
            {
                _currentPath.Add(obj);
            }
        }

        /// <summary>
        /// Remove o objeto do caminho atual.
        /// </summary>
        public void Pop(object obj)
        {
            if (obj != null)
            {
                _currentPath.Remove(obj);
            }
        }
    }
}
