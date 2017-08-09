using System;
using System.Collections.Generic;

namespace UdpJson
{
    public abstract class Method
    {
        /// <summary>
        /// If overridden to return a non-null list of methods, these methods will
        /// become the only available methods from all future requests. It is suggested
        /// that one of these methods has <see cref="PopContext"/> = true, if you want
        /// have a way of exiting the current context.
        /// </summary>
        public virtual IList<Tuple<string, Method>> PushMethods { get { return null; } }

        /// <summary>
        /// If true, this will pop the current execution context after <see cref="Invoke(object)"/>
        /// finishes.
        /// </summary>
        public virtual bool PopContext { get { return false; } }

        /// <summary>
        /// If overridden to return a non-null type, the returned type will be the
        /// type of the 'params' argument passed to <see cref="Invoke(object)"/>
        /// </summary>
        public virtual Type ParamsType { get { return null; } }

        /// <summary>
        /// Invokes the method.
        /// </summary>
        /// <param name="params">By default, the type will be the type of <see cref="Request.Params"/>.</param>
        /// <returns>A response object</returns>
        public abstract object Invoke(object @params);
    }
}