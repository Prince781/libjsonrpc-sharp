using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UdpJson
{
    public interface RpcObject
    {
        /// <summary>
        /// Determines if this object is valid.
        /// </summary>
        /// <exception cref="RpcException">Thrown if invalid</exception>
        void CheckValidity();
    }
}
