using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MALPlus.Adapters
{
    public interface IDispatcherAdapter
    {
        void Run(Action action);
    }
}
