using System;
using MALPlus.Models.AdapterModels;

namespace MALPlus.Adapters.Credentials
{
    public interface IPasswordVault
    {
        void Add(VaultCredential credential);

        /// <summary>
        /// <exception cref="Exception">
        ///     Should throw exception in case of failure.
        /// </exception>
        /// </summary>
        /// <returns></returns>
        VaultCredential Get(string domain);

        void Reset();
    }
}
