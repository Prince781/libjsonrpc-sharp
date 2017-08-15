using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace UdpJson
{
    /// <summary>
    /// A method that is invoked.
    /// </summary>
    public abstract class Method
    {
        /// <summary>
        /// Invokes the method.
        /// </summary>
        /// <param name="server">The server that invoked this method.</param>
        /// <param name="params">The object will be <see cref="Server.Data"/>.</param>
        /// <returns>A response object</returns>
        public abstract object Invoke(Server server, object @params);

        /// <summary>
        /// Gets state transition information. Return null for no transition.
        /// </summary>
        /// <returns>A new state to transition to or <code>null</code>.</returns>
        public virtual StateTransition GetTransition()
        {
            return null;
        }
    }
}