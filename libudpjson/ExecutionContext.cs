using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UdpJson
{
    /// <summary>
    /// An execution context defines a set of valid methods for a listener.
    /// </summary>
    public class ExecutionContext
    {
        /// <summary>
        /// The executing method.
        /// </summary>
        public Method method { get; }

        /// <summary>
        /// A storage of all available methods. The key is the command
        /// name, and the value is the method type.
        /// </summary>
        public ReadOnlyDictionary<string, Type> availableMethods { get; }

        /// <summary>
        /// Creates a new command context.
        /// </summary>
        /// <param name="method">The executing method.</param>
        /// <param name="availableMethods">The types of available methods in this context.</param>
        public ExecutionContext(Method method, IList<Tuple<string, Type>> availableMethods)
        {
            this.method = method;
            this.availableMethods = new ReadOnlyDictionary<string, Type>(availableMethods.ToDictionary(x => x.Item1, x => x.Item2));
        }
    }
}
