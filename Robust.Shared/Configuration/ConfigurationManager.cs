using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Nett;
using Robust.Shared.Collections;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Robust.Shared.Configuration
{
    /// <summary>
    ///     The engine's store of CVars (configuration variables): their registrations and current values.
    /// </summary>
    [Virtual]
    internal partial class ConfigurationManager : IConfigurationManagerInternal
    {
        [Dependency] private readonly IGameTiming _gameTiming = default!;
        [Dependency] private readonly ILogManager _logManager = default!;

        private const char TABLE_DELIMITER = '.';
        protected readonly Dictionary<string, ConfigVar> _configVars = new();
        private ConfigFileStorage? _configFile;
        protected bool _isServer;

        protected readonly ReaderWriterLockSlim Lock = new();

        private ISawmill _sawmill = default!;

        public event Action<CVarChangeInfo>? OnCVarValueChanged;

        /// <summary>
        ///     Constructs a new configuration manager.
        /// </summary>
        public ConfigurationManager()
        {
        }

        public void Initialize(bool isServer)
        {
            _isServer = isServer;

            _sawmill = _logManager.GetSawmill("cfg");
        }

        public virtual void Shutdown()
        {
            using var _ = Lock.WriteGuard();

            _configVars.Clear();
            _configFile = null;
        }

        /// <inheritdoc />
        public HashSet<string> LoadFromTomlStream(Stream file)
        {
            TomlTable root;
            try
            {
                // Only the parsing step is guarded: a malformed document is reported and treated as empty,
                // while semantic problems found while applying the values must reach the caller.
                root = Toml.ReadStream(file);
            }
            catch (Exception e)
            {
                _sawmill.Error($"Unable to load configuration from the TOML stream:\n{e}");
                return new HashSet<string>();
            }

            return LoadFromTomlTable(root);
        }

        private HashSet<string> LoadFromTomlTable(TomlTable table)
        {
            var loaded = new HashSet<string>();
            var callbackEvents = new ValueList<ValueChangedInvoke>();
            try
            {
                // Ensure callbacks are raised OUTSIDE the write lock.
                using (Lock.WriteGuard())
                {
                    foreach (var (cvar, value) in ParseCVarValuesFromToml(table))
                    {
                        loaded.Add(cvar);
                        LoadParsedVar(cvar, value, ref callbackEvents);
                    }
                }
            }
            finally
            {
                RunDeferredInvokeCallbacks(in callbackEvents);
            }

            return loaded;
        }

        private void RunDeferredInvokeCallbacks(in ValueList<ValueChangedInvoke> callbackEvents)
        {
            foreach (var callback in callbackEvents)
            {
                InvokeValueChanged(callback);
            }
        }

        private void LoadParsedVar(
            string cvar,
            object value,
            ref ValueList<ValueChangedInvoke> changedInvokes)
        {
            if (_configVars.TryGetValue(cvar, out var existing))
            {
                var oldValue = GetConfigVarValue(existing);

                object converted;
                try
                {
                    // Entries that are not registered yet have no Type, so they end up in the catch block below.
                    converted = existing.Type == value.GetType()
                        ? value
                        : ConvertToCVarType(value, existing.Type!);
                }
                catch
                {
                    _sawmill.Error(
                        $"TOML parsed cvar does not match registered cvar type. Name: {cvar}. " +
                        $"Code Type: {existing.Type}. Toml type: {value.GetType()}");
                    return;
                }

                // The snapshot has to be taken before the stored value is replaced.
                changedInvokes.Add(SetupInvokeValueChanged(existing, converted, oldValue));
                existing.Value = converted;
                existing.ConfigModified = true;
            }
            else
            {
                // Unknown CVar: keep the raw value so that it can be picked up on registration and survives saving.
                var added = AddUnregisteredCVar(cvar, value);
                added.ConfigModified = true;
            }
        }

        private ConfigVar AddUnregisteredCVar(string name, object value)
        {
            //Note: the initial defaultValue is null, but it will get overwritten when the cvar is registered.
            var cfgVar = new ConfigVar(name, null!, CVar.NONE) { Value = value };
            _configVars.Add(name, cfgVar);
            return cfgVar;
        }

        public HashSet<string> LoadDefaultsFromTomlStream(Stream stream)
        {
            var tblRoot = Toml.ReadStream(stream);

            var loaded = new HashSet<string>();

            var callbackEvents = new ValueList<ValueChangedInvoke>();

            // Ensure callbacks are raised OUTSIDE the write lock.
            using (Lock.WriteGuard())
            {
                foreach (var (cVarName, value) in ParseCVarValuesFromToml(tblRoot))
                {
                    if (!_configVars.TryGetValue(cVarName, out var cVar) || !cVar.Registered)
                    {
                        _sawmill.Error($"Trying to set unregistered variable '{cVarName}'");
                        continue;
                    }

                    var convertedValue = value;
                    if (cVar.Type != value.GetType())
                    {
                        try
                        {
                            convertedValue = ConvertToCVarType(value, cVar.Type!);
                        }
                        catch
                        {
                            _sawmill.Error($"Override TOML parsed cvar does not match registered cvar type. Name: {cVarName}. Code Type: {cVar.Type}. Toml type: {value.GetType()}");
                            continue;
                        }
                    }

                    if (cVar.OverrideValue == null && cVar.Value == null)
                    {
                        var oldValue = GetConfigVarValue(cVar);
                        callbackEvents.Add(SetupInvokeValueChanged(cVar, convertedValue, oldValue));
                    }

                    cVar.DefaultValue = convertedValue;
                }
            }

            foreach (var callback in callbackEvents)
            {
                InvokeValueChanged(callback);
            }

            return loaded;
        }

        /// <inheritdoc />
        public HashSet<string> LoadFromFile(string configFile)
        {
            try
            {
                HashSet<string> loaded;
                using (var stream = File.OpenRead(configFile))
                {
                    loaded = LoadFromTomlStream(stream);
                }

                // The save target must be known before applying rollbacks, because that saves the file.
                SetSaveFile(configFile);
                ApplyRollback();

                _sawmill.Info("Configuration loaded from file.");
                return loaded;
            }
            catch (Exception e)
            {
                _sawmill.Error($"Unable to load configuration file:\n{e}");
                return new HashSet<string>(0);
            }
        }

        public void SetSaveFile(string configFile)
        {
            _configFile = new ConfigFileStorageDisk { Path = configFile };
        }

        public void SetVirtualConfig()
        {
            _configFile = new ConfigFileStorageVirtual();
        }

        public void CheckUnusedCVars()
        {
            if (!GetCVar(CVars.CfgCheckUnused))
                return;

            using (Lock.ReadGuard())
            {
                foreach (var cVar in _configVars.Values)
                {
                    if (cVar.Registered)
                        continue;

                    _sawmill.Warning("Unknown CVar found (typo in config?): {CVar}", cVar.Name);
                }
            }
        }

        /// <inheritdoc />
        public void SaveToTomlStream(Stream stream, IEnumerable<string> cvars)
        {
            var table = SaveToTomlTable(cvars);

            Toml.WriteStream(table, stream);
        }

        private TomlTable SaveToTomlTable(IEnumerable<string> cvars, Func<string, object>? overrideValue = null)
        {
            var root = Toml.Create();

            // The read lock covers the enumeration too, since callers may hand in a lazy query over _configVars.
            using var _ = Lock.ReadGuard();

            foreach (var name in cvars)
            {
                if (!_configVars.TryGetValue(name, out var entry))
                    continue;

                object? value;
                if (overrideValue != null)
                {
                    value = overrideValue(name);
                }
                else
                {
                    // Command line / environment overrides are intentionally never persisted.
                    value = entry.Value;
                    if (value == null && entry.Registered)
                        value = entry.DefaultValue;
                }

                if (value == null)
                {
                    _sawmill.Error(
                        $"CVar {name} has no value and no default value. " +
                        "Was its default value registered as null?");
                    continue;
                }

                // Everything before the last dot selects the (nested) table, the remainder is the key.
                var lastDot = name.LastIndexOf(TABLE_DELIMITER);
                if (lastDot < 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(cvars),
                        name,
                        "CVar names must contain at least one '.' to be saved to TOML.");
                }

                var tablePath = name.Substring(0, lastDot);
                var key = name.Substring(lastDot + 1);

                var table = root;
                foreach (var segment in tablePath.Split(TABLE_DELIMITER))
                {
                    if (table.TryGetValue(segment, out var child))
                    {
                        if (child is not TomlTable childTable)
                        {
                            throw new InvalidConfigurationException(
                                $"The object '{segment}' is being used as a table, but it is something else. " +
                                "Are the CVar names well formed?");
                        }

                        table = childTable;
                    }
                    else
                    {
                        table = (TomlTable) table.Add(segment, new Dictionary<string, TomlObject>()).Added;
                    }
                }

                var type = value.GetType();
                if (type.IsEnum)
                {
                    table.Add(key, (int) value);
                }
                else if (value is int intValue)
                {
                    table.Add(key, intValue);
                }
                else if (value is long longValue)
                {
                    table.Add(key, longValue);
                }
                else if (value is bool boolValue)
                {
                    table.Add(key, boolValue);
                }
                else if (value is string stringValue)
                {
                    table.Add(key, stringValue);
                }
                else if (value is float floatValue)
                {
                    table.Add(key, floatValue);
                }
                else if (value is double doubleValue)
                {
                    table.Add(key, doubleValue);
                }
                else
                {
                    _sawmill.Warning($"Cannot serialize CVar '{name}': unsupported type {type}.");
                }
            }

            return root;
        }

        /// <inheritdoc />
        public void SaveToFile()
        {
            var target = _configFile;
            if (target == null)
            {
                _sawmill.Warning("Cannot save the configuration: no config file was ever loaded or set.");
                return;
            }

            try
            {
                // Deliberately lazy: it is enumerated inside the read lock taken by SaveToTomlTable.
                var names = _configVars
                    .Where(pair =>
                    {
                        var cVar = pair.Value;
                        if (cVar.ConfigModified)
                            return true;

                        return (cVar.Flags & CVar.ARCHIVE) != 0
                               && cVar.Value != null
                               && !cVar.Value.Equals(cVar.DefaultValue);
                    })
                    .Select(pair => pair.Key);

                // Serialize to memory first so that a failure cannot damage the existing file.
                using var buffer = new MemoryStream();
                SaveToTomlStream(buffer, names);
                buffer.Seek(0, SeekOrigin.Begin);

                switch (target)
                {
                    case ConfigFileStorageDisk disk:
                        using (var fileStream = File.Create(disk.Path))
                        {
                            buffer.CopyTo(fileStream);
                        }

                        break;

                    case ConfigFileStorageVirtual virtualStorage:
                        virtualStorage.Stream.SetLength(0);
                        buffer.CopyTo(virtualStorage.Stream);
                        break;

                    default:
                        throw new UnreachableException();
                }

                _sawmill.Info($"Config saved to '{target}'");
            }
            catch (Exception e)
            {
                _sawmill.Warning($"Cannot save the config file '{target}':\n{e}");
            }
        }

        public void RegisterCVar<T>(string name, T defaultValue, CVar flags = CVar.NONE,
            Action<T>? onValueChanged = null)
            where T : notnull
        {
            RegisterCVar(name, typeof(T), defaultValue, flags);

            if (onValueChanged != null)
                OnValueChanged(name, onValueChanged);
        }

        private void RegisterCVar(string name, Type type, object defaultValue, CVar flags)
        {
            DebugTools.Assert(
                defaultValue.GetType() == type,
                $"The default value of CVar '{name}' is not of the declared type {type}.");
            DebugTools.Assert(
                !type.IsEnum || type.GetEnumUnderlyingType() == typeof(int),
                $"CVar '{name}' is an enum, but enum CVars must use int as the underlying type.");

            // CVars meant for the other side are never created here.
            var wrongSide = _isServer ? CVar.CLIENTONLY : CVar.SERVERONLY;
            if ((flags & wrongSide) != 0)
                return;

            using var _ = Lock.WriteGuard();

            if (_configVars.TryGetValue(name, out var existing))
            {
                // Upgrade the existing entry (e.g. one created from the config file) in place.
                if (existing.Registered)
                    _sawmill.Error($"The variable '{name}' has already been registered.");

                if (existing.Value != null && existing.Value.GetType() != type)
                {
                    try
                    {
                        existing.Value = ConvertToCVarType(existing.Value, type);
                    }
                    catch
                    {
                        _sawmill.Error(
                            $"TOML parsed cvar does not match registered cvar type. Name: {name}. " +
                            $"Code Type: {type.Name}. Toml type: {existing.Value.GetType().Name}");
                        return;
                    }
                }

                existing.DefaultValue = defaultValue;
                existing.Flags = flags;
                existing.Register();

                if (existing.OverrideValue != null)
                    existing.OverrideValueParsed = ParseOverrideValue(existing.OverrideValue, type);

                return;
            }

            var created = new ConfigVar(name, defaultValue, flags);
            created.Register();
            _configVars.Add(name, created);
        }

        public void OnValueChanged<T>(CVarDef<T> cVar, Action<T> onValueChanged, bool invokeImmediately = false)
            where T : notnull
        {
            OnValueChanged(cVar.Name, onValueChanged, invokeImmediately);
        }

        public void OnValueChanged<T>(string name, Action<T> onValueChanged, bool invokeImmediately = false)
            where T : notnull
        {
            using (Lock.WriteGuard())
            {
                var reg = _configVars[name];

                reg.ValueChanged.AddInPlace(
                    (object value, in CVarChangeInfo _) => onValueChanged((T)value),
                    onValueChanged);
            }

            if (invokeImmediately)
            {
                onValueChanged(GetCVar<T>(name));
            }
        }

        public void UnsubValueChanged<T>(CVarDef<T> cVar, Action<T> onValueChanged) where T : notnull
        {
            UnsubValueChanged(cVar.Name, onValueChanged);
        }

        public void UnsubValueChanged<T>(string name, Action<T> onValueChanged) where T : notnull
        {
            using var _ = Lock.WriteGuard();

            var reg = _configVars[name];
            reg.ValueChanged.RemoveInPlace(onValueChanged);
        }

        public void OnValueChanged<T>(CVarDef<T> cVar, CVarChanged<T> onValueChanged, bool invokeImmediately = false)
            where T : notnull
        {
            OnValueChanged(cVar.Name, onValueChanged, invokeImmediately);
        }

        public void OnValueChanged<T>(string name, CVarChanged<T> onValueChanged, bool invokeImmediately = false)
            where T : notnull
        {
            object value;
            using (Lock.WriteGuard())
            {
                var reg = _configVars[name];
                value = GetConfigVarValue(reg);
                reg.ValueChanged.AddInPlace(
                    (object value, in CVarChangeInfo info) => onValueChanged((T)value, info),
                    onValueChanged);
            }

            if (invokeImmediately)
            {
                onValueChanged(GetCVar<T>(name), new CVarChangeInfo(name, _gameTiming.CurTick, value, value));
            }
        }

        public void UnsubValueChanged<T>(CVarDef<T> cVar, CVarChanged<T> onValueChanged) where T : notnull
        {
            UnsubValueChanged(cVar.Name, onValueChanged);
        }

        public void UnsubValueChanged<T>(string name, CVarChanged<T> onValueChanged) where T : notnull
        {
            using var _ = Lock.WriteGuard();

            var reg = _configVars[name];
            reg.ValueChanged.RemoveInPlace(onValueChanged);
        }

        public void LoadCVarsFromAssembly(Assembly assembly)
        {
            foreach (var type in assembly
                         .GetTypes()
                         .Where(p => Attribute.IsDefined(p, typeof(CVarDefsAttribute))))
            {
                LoadCVarsFromType(type);
            }
        }

        public void LoadCVarsFromType(Type containingType)
        {
            foreach (var defField in containingType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                var fieldType = defField.FieldType;
                if (!fieldType.IsGenericType || fieldType.GetGenericTypeDefinition() != typeof(CVarDef<>))
                {
                    continue;
                }

                var type = fieldType.GetGenericArguments()[0];

                if (!defField.IsInitOnly)
                {
                    throw new InvalidOperationException(
                        $"Found CVarDef '{defField.Name}' on '{defField.DeclaringType?.FullName}' that is not readonly. Please mark it as readonly.");
                }

                var def = (CVarDef?)defField.GetValue(null);

                if (def == null)
                {
                    throw new InvalidOperationException(
                        $"CVarDef '{defField.Name}' on '{defField.DeclaringType?.FullName}' is null.");
                }

                RegisterCVar(def.Name, type, def.DefaultValue, def.Flags);
            }
        }

        /// <inheritdoc />
        public bool IsCVarRegistered(string name)
        {
            using var _ = Lock.ReadGuard();
            return _configVars.TryGetValue(name, out var cVar) && cVar.Registered;
        }

        public CVar GetCVarFlags(string name)
        {
            using var _ = Lock.ReadGuard();
            return _configVars[name].Flags;
        }

        /// <inheritdoc />
        public IEnumerable<string> GetRegisteredCVars()
        {
            using var _ = Lock.ReadGuard();
            // Have to .ToArray() so the lock is held for the whole iteration operation.
            // This function is only currently used for the cvar ? command so I'm not too worried.
            return _configVars.Where(c => c.Value.Registered).Select(p => p.Key).ToArray();
        }

        /// <inheritdoc />
        public virtual void SetCVar(string name, object value, bool force = false)
        {
            SetCVarInternal(name, value, _gameTiming.CurTick);
        }

        protected void SetCVarInternal(string name, object value, GameTick intendedTick)
        {
            ValueChangedInvoke? pending = null;

            using (Lock.WriteGuard())
            {
                if (!_configVars.TryGetValue(name, out var cVar) || !cVar.Registered)
                    throw new InvalidConfigurationException($"Trying to set unregistered variable '{name}'");

                // The default is intentionally not part of the comparison.
                var current = cVar.OverrideValueParsed ?? cVar.Value;
                if (!Equals(current, value))
                {
                    // Snapshot the old value before it is overwritten.
                    var oldValue = GetConfigVarValue(cVar);
                    pending = SetupInvokeValueChanged(cVar, value, oldValue, intendedTick);

                    // Setting a CVar explicitly cancels any command line / environment override.
                    cVar.OverrideValue = null;
                    cVar.OverrideValueParsed = null;
                    cVar.Value = value;
                }
            }

            // Handlers run outside the lock so they may call back into the manager.
            if (pending != null)
                InvokeValueChanged(pending.Value);
        }

        public void SetCVar<T>(CVarDef<T> def, T value, bool force = false) where T : notnull
        {
            SetCVar(def.Name, value, force);
        }

        public void OverrideDefault(string name, object value)
        {
            ValueChangedInvoke? pending = null;

            using (Lock.WriteGuard())
            {
                if (!_configVars.TryGetValue(name, out var cVar) || !cVar.Registered)
                    throw new InvalidConfigurationException($"Trying to set unregistered variable '{name}'");

                // The effective value only changes when neither an override nor an explicit value shadows the default.
                if (cVar.OverrideValue == null && cVar.Value == null)
                {
                    var oldValue = GetConfigVarValue(cVar);
                    pending = SetupInvokeValueChanged(cVar, value, oldValue);
                }

                cVar.DefaultValue = value;
            }

            if (pending != null)
                InvokeValueChanged(pending.Value);
        }

        public void OverrideDefault<T>(CVarDef<T> def, T value) where T : notnull
        {
            OverrideDefault(def.Name, value);
        }

        public object GetCVar(string name)
        {
            using var _ = Lock.ReadGuard();
            if (_configVars.TryGetValue(name, out var cVar) && cVar.Registered)
                return GetConfigVarValue(cVar);

            throw new InvalidConfigurationException($"Trying to get unregistered variable '{name}'");
        }

        /// <inheritdoc />
        public T GetCVar<T>(string name)
        {
            return (T)GetCVar(name);
        }

        public T GetCVar<T>(CVarDef<T> def) where T : notnull
        {
            return GetCVar<T>(def.Name);
        }

        public Type GetCVarType(string name)
        {
            using var _ = Lock.ReadGuard();
            if (!_configVars.TryGetValue(name, out var cVar) || !cVar.Registered)
            {
                throw new InvalidConfigurationException($"Trying to get type of unregistered variable '{name}'");
            }

            // If it's null it's a string, since the rest is primitives which aren't null.
            return cVar.Type!;
        }

        protected static object GetConfigVarValue(ConfigVar cVar)
        {
            return cVar.OverrideValueParsed ?? cVar.Value ?? cVar.DefaultValue;
        }

        public void OverrideConVars(IEnumerable<(string key, string value)> cVars)
        {
            var invokes = new ValueList<ValueChangedInvoke>();

            using (Lock.WriteGuard())
            {
                foreach (var (key, value) in cVars)
                {
                    if (_configVars.TryGetValue(key, out var cfgVar))
                    {
                        cfgVar.OverrideValue = value;
                        if (!cfgVar.Registered)
                            continue;

                        var newValue = ParseOverrideValue(value, cfgVar.Type!);
                        var oldValue = GetConfigVarValue(cfgVar);
                        invokes.Add(SetupInvokeValueChanged(cfgVar, newValue, oldValue));
                        cfgVar.OverrideValueParsed = newValue;
                    }
                    else
                    {
                        // Otherwise record the value as an unregistered CVar; it takes effect once the CVar gets registered.
                        //Note: the initial defaultValue is null, but it will get overwritten when the cvar is registered.
                        var cVar = new ConfigVar(key, null!, CVar.NONE) { OverrideValue = value };
                        _configVars.Add(key, cVar);
                    }
                }
            }

            foreach (var invoke in invokes)
            {
                InvokeValueChanged(invoke);
            }
        }

        private static object ParseOverrideValue(string value, Type? type)
        {
            if (type == typeof(int))
            {
                return int.Parse(value);
            }

            if (type == typeof(bool))
            {
                return bool.Parse(value);
            }

            if (type == typeof(float))
            {
                return float.Parse(value, CultureInfo.InvariantCulture);
            }

            if (type?.IsEnum ?? false)
            {
                return Enum.Parse(type, value);
            }

            if (type == typeof(long))
            {
                return long.Parse(value);
            }

            if (type == typeof(ushort))
            {
                return ushort.Parse(value);
            }

            // Must be a string.
            return value;
        }

        /// <summary>
        ///     Turns a parsed TOML value into the matching .NET value.
        /// </summary>
        /// <param name="obj">The TOML value to convert.</param>
        /// <returns>The value as a boxed .NET object.</returns>
        private static object TypeConvert(TomlObject obj)
        {
            switch (obj.TomlType)
            {
                case TomlObjectType.Bool:
                    return obj.Get<bool>();

                case TomlObjectType.Float:
                    return obj.Get<float>();

                case TomlObjectType.Int:
                {
                    // TOML integers are 64-bit. Values that fit are narrowed to int; the registered type decides later.
                    var wide = obj.Get<long>();
                    if (wide >= int.MinValue && wide <= int.MaxValue)
                        return (int) wide;

                    return wide;
                }

                case TomlObjectType.String:
                    return obj.Get<string>();

                default:
                    throw new InvalidConfigurationException(
                        $"Cannot convert TOML object of type {obj.TomlType} to a CVar value.");
            }
        }

        private void InvokeValueChanged(in ValueChangedInvoke invoke)
        {
            try
            {
                OnCVarValueChanged?.Invoke(invoke.Info);
            }
            catch (Exception e)
            {
                _sawmill.Error($"Error while running OnCVarValueChanged callback: {e}");
            }

            foreach (var entry in invoke.Invoke.Entries)
            {
                try
                {
                    entry.Value!.Invoke(invoke.Value, in invoke.Info);
                }
                catch (Exception e)
                {
                    _sawmill.Error($"Error while running OnValueChanged callback: {e}");
                }
            }
        }

        private ValueChangedInvoke SetupInvokeValueChanged(ConfigVar var, object newValue, object oldValue, GameTick? tick = null)
        {
            tick ??= _gameTiming.CurTick;
            var info = new CVarChangeInfo(var.Name, tick.Value, newValue, oldValue);
            return new ValueChangedInvoke(info, var.ValueChanged);
        }

        private IEnumerable<(string cvar, object value)> ParseCVarValuesFromToml(TomlTable tblRoot)
        {
            return Flatten(tblRoot, string.Empty);

            // Lazy depth-first walk: values are converted as the enumerator reaches them.
            static IEnumerable<(string cvar, object value)> Flatten(TomlTable table, string prefix)
            {
                foreach (var pair in table)
                {
                    if (pair.Value is TomlTable nested)
                    {
                        foreach (var inner in Flatten(nested, prefix + pair.Key + TABLE_DELIMITER))
                        {
                            yield return inner;
                        }
                    }
                    else
                    {
                        yield return (prefix + pair.Key, TypeConvert(pair.Value));
                    }
                }
            }
        }

        /// <summary>
        /// Try to convert a compatible value to the actual registration type of a CVar.
        /// </summary>
        /// <remarks>
        /// When CVars are parsed from TOML, their in-code type is not known.
        /// This function does the necessary conversions from e.g. int to long.
        /// </remarks>
        /// <param name="value">
        /// The value to convert.
        /// This must be a simple type like strings or integers.
        /// </param>
        /// <param name="cVar">
        /// The registration type of the CVar.
        /// </param>
        /// <returns></returns>
        private static object ConvertToCVarType(object value, Type cVar)
        {
            if (cVar.IsEnum)
                return Enum.Parse(cVar, value.ToString() ?? string.Empty);

            return Convert.ChangeType(value, cVar);
        }

        internal List<Delegate> GetSubs(string name)
        {
            using (Lock.ReadGuard())
            {
                var list = new List<Delegate>();

                if (!_configVars.TryGetValue(name, out var cVar))
                    throw new InvalidConfigurationException($"Trying to get unregistered variable '{name}'");

                foreach (var entry in cVar.ValueChanged.Entries)
                {
                    list.Add((Delegate) entry.Equality!);
                }

                return list;
            }
        }

        /// <summary>
        ///     Bookkeeping for one CVar: its default, its current value and whether it is registered.
        /// </summary>
        protected sealed class ConfigVar
        {
            /// <summary>
            ///     Constructs a new CVar entry.
            /// </summary>
            /// <param name="name">
            ///     The full name of the CVar. It may only contain printable characters. Everything before the
            ///     last '.' selects the TOML table (nested at each '.'), the remainder is the key inside it.
            /// </param>
            /// <param name="defaultValue">The default value of the CVar.</param>
            /// <param name="flags">Optional flags modifying the behavior of the CVar.</param>
            public ConfigVar(string name, object defaultValue, CVar flags)
            {
                Name = name;
                // Assigned to the field directly: unregistered entries pass null and have no Type to validate against.
                _defaultValue = defaultValue;
                Flags = flags;
            }

            /// <summary>
            ///     The type of the cvar's value. This may be null until the cvar is registered.
            /// </summary>
            public Type? Type { get; internal set; }

            /// <summary>The name of the CVar.</summary>
            public string Name { get; }

            /// <summary>
            ///     The value this CVar has unless something overrides it.
            /// </summary>
            public object DefaultValue
            {
                get => _defaultValue;
                set
                {
                    if (Registered)
                        DebugTools.AssertEqual(value.GetType(), Type);
                    _defaultValue = value;
                }
            }

            /// <summary>Optional flags modifying the behavior of this CVar.</summary>
            public CVar Flags { get; set; }

            /// <summary>
            ///     The value that is currently in effect.
            /// </summary>
            public object? Value
            {
                get => _value;
                set
                {
                    if (value != null && Registered)
                        DebugTools.AssertEqual(value.GetType(), Type);
                    _value = value;
                }
            }

            /// <summary>
            ///     Whether code has registered this CVar yet.
            /// </summary>
            public bool Registered { get; private set; }

            public void Register()
            {
                if (Registered)
                {
                    DebugTools.AssertNotNull(DefaultValue);
                    DebugTools.AssertEqual(DefaultValue.GetType(), Type);
                    DebugTools.Assert(Value == null || Value.GetType() == Type);
                    DebugTools.Assert(OverrideValueParsed == null || OverrideValueParsed.GetType() == Type);
                    return;
                }

                if (_defaultValue == null)
                    throw new NullReferenceException("Must specify default value before registering");

                if (Value != null && DefaultValue.GetType() != Value.GetType())
                    throw new Exception($"The cvar value & default value must be of the same type");

                if (OverrideValueParsed != null && DefaultValue.GetType() != OverrideValueParsed.GetType())
                    throw new Exception($"The cvar override value & default value must be of the same type");

                Type = DefaultValue.GetType();
                Registered = true;
            }

            /// <summary>
            ///     Was the CVar present in the config file?
            ///     If so we need to always re-save it even if it's not ARCHIVE.
            /// </summary>
            public bool ConfigModified;

            public InvokeList<ValueChangedDelegate> ValueChanged;
            private object _defaultValue;
            private object? _value;
            private object? _overrideValueParsed;

            // We don't know what the type of the var is until it's registered.
            // So we can't actually parse them until then.
            // So we keep the raw string around.
            public string? OverrideValue { get; set; }

            public object? OverrideValueParsed
            {
                get => _overrideValueParsed;
                set
                {
                    if (value != null && Registered)
                        DebugTools.AssertEqual(value.GetType(), Type);
                    _overrideValueParsed = value;
                }
            }
        }

        /// <summary>
        /// All data we need to invoke a deferred ValueChanged handler outside of a write lock.
        /// </summary>
        private struct ValueChangedInvoke
        {
            public InvokeList<ValueChangedDelegate> Invoke;
            public object Value => Info.NewValue;
            public CVarChangeInfo Info;

            public ValueChangedInvoke(CVarChangeInfo info, InvokeList<ValueChangedDelegate> invoke) : this()
            {
                Info = info;
                Invoke = invoke;
            }
        }

        protected delegate void ValueChangedDelegate(object value, in CVarChangeInfo info);

        private abstract class ConfigFileStorage;

        private sealed class ConfigFileStorageDisk : ConfigFileStorage
        {
            public required string Path;

            public override string ToString()
            {
                return Path;
            }
        }

        private sealed class ConfigFileStorageVirtual : ConfigFileStorage
        {
            // I did not realize when adding this class that there is currently no way to *load* this data again.
            // Oh well, might be useful for a future unit test.
            public readonly MemoryStream Stream = new();

            public override string ToString()
            {
                return "<VIRTUAL>";
            }
        }
    }

    [Serializable]
    [Virtual]
    public class InvalidConfigurationException : Exception
    {
        public InvalidConfigurationException()
        {
        }

        public InvalidConfigurationException(string message) : base(message)
        {
        }

        public InvalidConfigurationException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
