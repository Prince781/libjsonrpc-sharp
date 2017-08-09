using System;
using System.Collections.Generic;

namespace UdpJson
{
    public abstract class Method
    {
        public virtual IList<Tuple<string, Method>> Methods { get { return null; } }

        /// <summary>
        /// Invokes the method.
        /// </summary>
        /// <returns>A response object</returns>
        public abstract object Invoke();
    }
}