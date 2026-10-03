using System;
using System.Linq;
using System.Reflection;
using Robust.Shared.Collections;

namespace Robust.Shared.ContentPack
{
    public static class AppDomainExt
    {
        /// <summary>
        ///     Finds the one loaded assembly whose simple name matches <paramref name="name"/>.
        /// </summary>
        /// <param name="domain">The application domain whose loaded assemblies are searched.</param>
        /// <param name="name">Simple assembly name, compared case-sensitively.</param>
        /// <returns>The single matching assembly.</returns>
        /// <exception cref="InvalidOperationException">No assembly or more than one assembly matched.</exception>
        public static Assembly GetAssemblyByName(this AppDomain domain, string name)
        {
            var matches = new System.Collections.Generic.List<Assembly>();

            foreach (var candidate in domain.GetAssemblies())
            {
                if (string.Equals(candidate.GetName().Name, name, StringComparison.Ordinal))
                    matches.Add(candidate);
            }

            if (matches.Count != 1)
            {
                var names = string.Join(" ", matches.Select(a => a.GetName().Name));
                throw new InvalidOperationException($"Expected 1 assembly for {name}, found {matches.Count}. Found {names}");
            }

            return matches[0];
        }
    }
}
