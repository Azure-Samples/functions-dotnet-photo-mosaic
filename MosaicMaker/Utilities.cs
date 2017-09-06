using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MosaicMaker
{
    public class Utilities
    {
        /// <summary>
        /// Computes a stable non-cryptographic hash
        /// </summary>
        /// <param name="value">The string to use for computation</param>
        /// <returns>A stable, non-cryptographic, hash</returns>
        internal static int GetStableHash(string value)
        {
            if (value == null) {
                throw new ArgumentNullException(nameof(value));
            }

            unchecked {
                int hash = 23;
                foreach (char c in value) {
                    hash = (hash * 31) + c;
                }
                return hash;
            }
        }
    }
}
