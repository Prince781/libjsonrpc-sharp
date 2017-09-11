using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JsonRpc
{
    /// <summary>
    /// Defines a state transition.
    /// </summary>
    public class StateTransition
    {
        /// <summary>
        /// If this is set to a non-null list of methods, these methods will become the 
        /// only available methods from all future requests. It is suggested that one of 
        /// these methods has <see cref="PopContext"/> = true, if you want have a way of 
        /// exiting the current context.
        /// </summary>
        [JsonIgnore]
        public IList<Tuple<string, Type>> PushMethods { get; set; }

        /// <summary>
        /// If true, this will pop the current execution context after <see cref="Method.Invoke(Server, object)"/>
        /// finishes.
        /// </summary>
        [JsonIgnore]
        public bool PopContext { get; set; }
    }
}
