using System;
using System.Collections.Generic;
using System.Reflection;

namespace Robust.Shared.Configuration
{
    internal interface IConfigurationManagerInternal : IConfigurationManager
    {
        void OverrideConVars(IEnumerable<(string key, string value)> cVars);
        void LoadCVarsFromAssembly(Assembly assembly);
        void LoadCVarsFromType(Type containingType);

        /// <summary>
        /// Indicate that config should be stored in-memory.
        /// </summary>
        /// <remarks>
        /// This suppresses warnings from <see cref="IConfigurationManager.SaveToFile"/>
        /// if no config is otherwise loaded.
        /// </remarks>
        void SetVirtualConfig();

        void Initialize(bool isServer);

        void Shutdown();

        /// <summary>
        ///     Prepares the configuration manager for use with a configuration file on disk and loads that file.
        /// </summary>
        /// <param name="configFile">Full path of the TOML-format configuration file.</param>
        /// <returns>The set of CVar names.</returns>
        HashSet<string> LoadFromFile(string configFile);

        /// <summary>
        ///     Specifies the location where the config file should be saved, without trying to load from it.
        /// </summary>
        void SetSaveFile(string configFile);

        /// <summary>
        /// Check the list of CVars to make sure there's no unused CVars set, which might indicate a typo or such.
        /// </summary>
        void CheckUnusedCVars();
    }
}
