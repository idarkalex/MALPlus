using System;
using System.Collections.Generic;
using System.Text;

namespace MALPlus.Adapters
{
    public interface IShareProvider
    {
        void Share(string message);
        void Share(string message, string imagePath);
    }
}
