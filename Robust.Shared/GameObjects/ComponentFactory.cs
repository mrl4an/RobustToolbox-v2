using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using Robust.Shared.GameStates;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Prototypes;
using Robust.Shared.Reflection;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Robust.Shared.GameObjects
{
    /// <summary>
    ///     Default implementation of <see cref="IComponentFactory"/>. It owns every component registration:
    ///     name, type, <see cref="CompIdx"/> and (once generated) network id lookups, and it creates new
    ///     component instances.
    /// </summary>
    /// <remarks>
    ///     All lookup tables are immutable frozen collections that get replaced as a whole when registrations
    ///     change, so readers never observe a half-updated state.
    /// </remarks>
    [Virtual]
    internal class ComponentFactory(
        IDynamicTypeFactoryInternal _typeFactory,
        IReflectionManager _reflectionManager,
        ISerializationManager _serManager,
        ILogManager logManager) : IComponentFactory
    {
        private readonly ISawmill _sawmill = logManager.GetSawmill("ent.componentFactory");

        /// <summary>Component name to registration.</summary>
        private FrozenDictionary<string, ComponentRegistration> _names =
            FrozenDictionary<string, ComponentRegistration>.Empty;

        /// <summary>Lower-cased component name to the correctly cased registered name.</summary>
        private FrozenDictionary<string, string> _lowerCaseNames = FrozenDictionary<string, string>.Empty;

        /// <summary>
        ///     Registrations ordered by network id (the index is the id). A non-null value also means that
        ///     registration is locked, because it is only set by <see cref="GenerateNetIds"/>.
        /// </summary>
        private List<ComponentRegistration>? _networkedComponents;

        /// <summary>Concrete component type to registration.</summary>
        private FrozenDictionary<Type, ComponentRegistration> _types =
            FrozenDictionary<Type, ComponentRegistration>.Empty;

        /// <summary>Registrations indexed by <see cref="CompIdx"/> value.</summary>
        private ComponentRegistration[] _array = Array.Empty<ComponentRegistration>();

        private string? _ignoreMissingComponentPostfix = null;

        private FrozenSet<string> _ignored = FrozenSet<string>.Empty;

        private FrozenDictionary<CompIdx, Type> _idxToType = FrozenDictionary<CompIdx, Type>.Empty;

        /// <summary>Slow-path lookup from a runtime type to its <see cref="CompIdx"/>.</summary>
        private FrozenDictionary<Type, CompIdx> _typeToIdx = FrozenDictionary<Type, CompIdx>.Empty;

        /// <inheritdoc />
        public event Action<ComponentRegistration[]>? ComponentsAdded;

        /// <inheritdoc />
        public event Action<string>? ComponentIgnoreAdded;

        /// <inheritdoc />
        public IEnumerable<Type> AllRegisteredTypes => _types.Keys;

        /// <inheritdoc />
        public IReadOnlyList<ComponentRegistration>? NetworkedComponents => _networkedComponents;

        private IEnumerable<ComponentRegistration> AllRegistrations => _types.Values;

        /// <summary>
        ///     Validates a type and records its registration in the supplied working collections.
        ///     The live <see cref="_array"/> is written directly.
        /// </summary>
        private ComponentRegistration Register(
            Type type,
            CompIdx idx,
            Dictionary<string, ComponentRegistration> names,
            Dictionary<string, string> lowerCaseNames,
            Dictionary<Type, ComponentRegistration> types,
            Dictionary<CompIdx, Type> idxToType,
            HashSet<string> ignored,
            bool overwrite = false)
        {
            if (_networkedComponents != null)
                throw new ComponentRegistrationLockException();

            if (types.ContainsKey(type))
                throw new InvalidOperationException($"Type is already registered: {type}");

            if (!type.IsSubclassOf(typeof(Component)))
                throw new InvalidOperationException($"Type is not derived from component: {type}");

            if (!typeof(IComponent).IsAssignableFrom(type))
            {
                throw new InvalidOperationException(
                    $"Type {type} has RegisterComponentAttribute but does not implement IComponent.");
            }

            var name = CalculateComponentName(type);
            var lowerCaseName = name.ToLowerInvariant();

            if (ignored.Contains(name))
            {
                if (!overwrite)
                    throw new InvalidOperationException($"{name} is already marked as ignored component");

                ignored.Remove(name);
            }

            if (names.TryGetValue(name, out var prev))
            {
                if (!overwrite)
                    throw new InvalidOperationException($"{name} is already registered, previous: {prev}");

                // Replacing: drop everything the previous registration contributed.
                types.Remove(prev.Type);
                names.Remove(prev.Name);
                lowerCaseNames.Remove(prev.Name);
            }

            if (!overwrite && lowerCaseNames.TryGetValue(lowerCaseName, out var prevName))
            {
                // Names that differ only by casing are not allowed either.
                throw new InvalidOperationException(
                    $"{lowerCaseName} is already registered, previous: {prevName}");
            }

            var unsaved = type.HasCustomAttribute<UnsavedComponentAttribute>();
            var registration = new ComponentRegistration(name, type, idx, unsaved);

            idxToType[idx] = type;
            names[name] = registration;
            lowerCaseNames[lowerCaseName] = name;
            types[type] = registration;
            CompIdx.AssignArray(ref _array, idx, registration);

            return registration;
        }

        /// <summary>
        ///     Works out the prototype-facing name of a component type: either the explicit
        ///     <see cref="ComponentProtoNameAttribute"/> value or the type name without the "Component" suffix
        ///     and without a leading Client/Server/Shared prefix.
        /// </summary>
        private static string CalculateComponentName(Type type)
        {
            if (Attribute.GetCustomAttribute(type, typeof(ComponentProtoNameAttribute))
                is ComponentProtoNameAttribute attribute)
            {
                return attribute.PrototypeName;
            }

            const string suffix = "Component";
            var typeName = type.Name;

            if (!typeName.EndsWith(suffix))
                throw new InvalidComponentNameException($"Component {type} must end with the word Component");

            var name = typeName.Substring(0, typeName.Length - suffix.Length);

            foreach (var prefix in new[] { "Client", "Server", "Shared" })
            {
                if (typeName.StartsWith(prefix, StringComparison.Ordinal))
                {
                    name = typeName.Substring(prefix.Length, typeName.Length - prefix.Length - suffix.Length);
                    break;
                }
            }

            DebugTools.Assert(name != string.Empty, $"Component {type} has invalid name {type.Name}");
            return name;
        }

        /// <inheritdoc />
        public void RegisterNetworkedFields<T>(params string[] fields) where T : IComponent
        {
            var registration = GetRegistration(CompIdx.Index<T>());
            RegisterNetworkedFields(registration, fields);
        }

        /// <inheritdoc />
        public void RegisterNetworkedFields(ComponentRegistration compReg, params string[] fields)
        {
            // Already configured, or nothing to configure.
            if (compReg.NetworkedFields.Length > 0 || fields.Length == 0)
                return;

            DebugTools.Assert(fields.Length <= 32);

            if (fields.Length > 32)
            {
                throw new NotSupportedException(
                    "Components with more than 32 networked fields unsupported! Consider splitting it up or making a pr for 64-bit flags");
            }

            compReg.NetworkedFields = fields;

            var lookup = new Dictionary<string, int>();
            for (var i = 0; i < fields.Length; i++)
            {
                lookup[fields[i]] = i;
            }

            compReg.NetworkedFieldLookup = lookup.ToFrozenDictionary();
        }

        /// <inheritdoc />
        public void IgnoreMissingComponents(string postfix = "")
        {
            if (_ignoreMissingComponentPostfix != null && _ignoreMissingComponentPostfix != postfix)
                throw new InvalidOperationException("Ignoring multiple prefixes is not supported");

            _ignoreMissingComponentPostfix = postfix ?? throw new ArgumentNullException(nameof(postfix));
        }

        /// <inheritdoc />
        public IComponent GetComponent(EntityPrototype.ComponentRegistryEntry entry)
        {
            var copy = GetComponent(entry.Component.GetType());
            _serManager.CopyTo(entry.Component, ref copy, notNullableOverride: true);
            return copy;
        }

        /// <inheritdoc />
        public void RegisterIgnore(params string[] names)
        {
            // Validate everything first so a failure leaves the factory untouched.
            foreach (var name in names)
            {
                if (_names.ContainsKey(name))
                {
                    throw new InvalidOperationException(
                        $"Cannot add {name} to ignored components: It is already registered as a component");
                }
            }

            var ignored = _ignored.ToHashSet();

            foreach (var name in names)
            {
                if (!ignored.Add(name))
                    _sawmill.Warning($"Duplicate ignored component: {name}");
            }

            _ignored = ignored.ToFrozenSet();

            foreach (var name in names)
            {
                ComponentIgnoreAdded?.Invoke(name);
            }
        }

        /// <inheritdoc />
        public ComponentAvailability GetComponentAvailability(string componentName, bool ignoreCase = false)
        {
            // The caller is expected to pass an already lower-cased name when ignoreCase is set.
            if (ignoreCase && _lowerCaseNames.TryGetValue(componentName, out var lowerCaseName))
                componentName = lowerCaseName;

            if (_names.ContainsKey(componentName))
                return ComponentAvailability.Available;

            if (_ignored.Contains(componentName) ||
                (_ignoreMissingComponentPostfix != null && componentName.EndsWith(_ignoreMissingComponentPostfix)))
            {
                return ComponentAvailability.Ignore;
            }

            return ComponentAvailability.Unknown;
        }

        /// <inheritdoc />
        public IComponent GetComponent(Type componentType)
        {
            if (!_types.TryGetValue(componentType, out var registration))
                throw new InvalidOperationException($"{componentType} is not a registered component.");

            return _typeFactory.CreateInstanceUnchecked<IComponent>(registration.Type);
        }

        /// <inheritdoc />
        public IComponent GetComponent(CompIdx componentType)
        {
            return _typeFactory.CreateInstanceUnchecked<IComponent>(_array[componentType.Value].Type);
        }

        /// <inheritdoc />
        public T GetComponent<T>() where T : IComponent, new()
        {
            if (!_types.TryGetValue(typeof(T), out var registration))
                throw new InvalidOperationException($"{typeof(T)} is not a registered component.");

            return _typeFactory.CreateInstanceUnchecked<T>(registration.Type);
        }

        /// <inheritdoc />
        public IComponent GetComponent(ComponentRegistration reg)
        {
            return (IComponent) _typeFactory.CreateInstanceUnchecked(reg.Type);
        }

        /// <inheritdoc />
        public IComponent GetComponent(string componentName, bool ignoreCase = false)
        {
            if (ignoreCase && _lowerCaseNames.TryGetValue(componentName, out var lowerCaseName))
                componentName = lowerCaseName;

            return _typeFactory.CreateInstanceUnchecked<IComponent>(GetRegistration(componentName).Type);
        }

        /// <inheritdoc />
        public IComponent GetComponent(ushort netId)
        {
            return _typeFactory.CreateInstanceUnchecked<IComponent>(GetRegistration(netId).Type);
        }

        /// <inheritdoc />
        public ComponentRegistration GetRegistration(string componentName, bool ignoreCase = false)
        {
            if (ignoreCase && _lowerCaseNames.TryGetValue(componentName, out var lowerCaseName))
                componentName = lowerCaseName;

            try
            {
                return _names[componentName];
            }
            catch (KeyNotFoundException)
            {
                throw new UnknownComponentException($"Unknown name: {componentName}");
            }
        }

        /// <inheritdoc />
        public ComponentRegistration GetRegistration(ushort netID)
        {
            if (_networkedComponents == null)
                throw new ComponentRegistrationLockException();

            try
            {
                return _networkedComponents[netID];
            }
            catch (KeyNotFoundException)
            {
                throw new UnknownComponentException($"Unknown net ID: {netID}");
            }
        }

        /// <inheritdoc />
        public ComponentRegistration GetRegistration(Type reference)
        {
            try
            {
                return _types[reference];
            }
            catch (KeyNotFoundException)
            {
                throw new UnknownComponentException($"Unknown type: {reference}");
            }
        }

        /// <inheritdoc />
        public ComponentRegistration GetRegistration<T>() where T : IComponent, new()
        {
            return GetRegistration(CompIdx.Index<T>());
        }

        /// <inheritdoc />
        public ComponentRegistration GetRegistration(IComponent component)
        {
            return GetRegistration(component.GetType());
        }

        /// <inheritdoc />
        public ComponentRegistration GetRegistration(CompIdx idx)
        {
            return _array[idx.Value];
        }

        /// <inheritdoc />
        public bool IsIgnored(string componentName)
        {
            return _ignored.Contains(componentName);
        }

        /// <inheritdoc />
        [Pure]
        public string GetComponentName(Type type)
        {
            return GetRegistration(type).Name;
        }

        /// <inheritdoc />
        [Pure]
        public string GetComponentName<T>() where T : IComponent, new()
        {
            return GetRegistration<T>().Name;
        }

        /// <inheritdoc />
        [Pure]
        public string GetComponentName(ushort netID)
        {
            return GetRegistration(netID).Name;
        }

        /// <inheritdoc />
        public bool TryGetRegistration(
            string componentName,
            [NotNullWhen(true)] out ComponentRegistration? registration,
            bool ignoreCase = false)
        {
            if (ignoreCase && _lowerCaseNames.TryGetValue(componentName, out var lowerCaseName))
                componentName = lowerCaseName;

            return _names.TryGetValue(componentName, out registration);
        }

        /// <inheritdoc />
        public bool TryGetRegistration(Type reference, [NotNullWhen(true)] out ComponentRegistration? registration)
        {
            return _types.TryGetValue(reference, out registration);
        }

        /// <inheritdoc />
        public bool TryGetRegistration<T>([NotNullWhen(true)] out ComponentRegistration? registration)
            where T : IComponent, new()
        {
            return TryGetRegistration(typeof(T), out registration);
        }

        /// <inheritdoc />
        public bool TryGetRegistration(ushort netID, [NotNullWhen(true)] out ComponentRegistration? registration)
        {
            if (_networkedComponents != null && _networkedComponents.TryGetValue(netID, out var found))
            {
                registration = found;
                return true;
            }

            registration = null;
            return false;
        }

        /// <inheritdoc />
        public bool TryGetRegistration(IComponent component, [NotNullWhen(true)] out ComponentRegistration? registration)
        {
            return TryGetRegistration(component.GetType(), out registration);
        }

        /// <inheritdoc />
        public void DoAutoRegistrations()
        {
            var types = _reflectionManager.FindTypesWithAttribute<RegisterComponentAttribute>().ToArray();
            RegisterTypesInternal(types, false);
        }

        /// <inheritdoc />
        [Pure]
        public CompIdx GetIndex(Type type)
        {
            return _typeToIdx[type];
        }

        /// <inheritdoc />
        [Pure]
        public int GetArrayIndex(Type type)
        {
            return _typeToIdx[type].Value;
        }

        /// <inheritdoc />
        public Type IdxToType(CompIdx idx)
        {
            return _idxToType[idx];
        }

        /// <summary>
        ///     Registers a batch of types by editing mutable copies of the lookup tables and only publishing
        ///     them (as frozen collections) once every type registered successfully.
        /// </summary>
        private void RegisterTypesInternal(Type[] types, bool overwrite)
        {
            var names = _names.ToDictionary();
            var lowerCaseNames = _lowerCaseNames.ToDictionary();
            var typesDict = _types.ToDictionary();
            var idxToType = _idxToType.ToDictionary();
            var typeToidx = _typeToIdx.ToDictionary();
            var ignored = _ignored.ToHashSet();
            var added = new ComponentRegistration[types.Length];

            for (var i = 0; i < types.Length; i++)
            {
                var type = types[i];
                var idx = CompIdx.GetIndex(type);
                typeToidx[type] = idx;
                added[i] = Register(type, idx, names, lowerCaseNames, typesDict, idxToType, ignored, overwrite);
            }

            var st = RStopwatch.StartNew();

            _typeToIdx = typeToidx.ToFrozenDictionary();
            _names = names.ToFrozenDictionary();
            _lowerCaseNames = lowerCaseNames.ToFrozenDictionary();
            _types = typesDict.ToFrozenDictionary();
            _idxToType = idxToType.ToFrozenDictionary();
            _ignored = ignored.ToFrozenSet();

            _sawmill.Verbose($"Freezing component factory took {st.Elapsed.TotalMilliseconds:f2}ms");

            ComponentsAdded?.Invoke(added);
        }

        /// <inheritdoc />
        public void RegisterClass<T>(bool overwrite = false) where T : IComponent, new()
        {
            RegisterTypesInternal(new[] { typeof(T) }, overwrite);
        }

        /// <inheritdoc />
        public void RegisterTypes(params Type[] types)
        {
            foreach (var type in types)
            {
                if (!typeof(IComponent).IsAssignableFrom(type) || !type.HasParameterlessConstructor())
                    throw new InvalidOperationException($"Invalid type: {type}");
            }

            RegisterTypesInternal(types, false);
        }

        /// <inheritdoc />
        public IEnumerable<CompIdx> GetAllRefTypes()
        {
            return AllRegistrations.Select(x => x.Idx).Distinct();
        }

        /// <inheritdoc />
        public IEnumerable<ComponentRegistration> GetAllRegistrations()
        {
            return _types.Values;
        }

        /// <inheritdoc />
        public void GenerateNetIds()
        {
            var networked = new List<ComponentRegistration>(_names.Count);

            foreach (var registration in _names.Values)
            {
                if (Attribute.GetCustomAttribute(registration.Type, typeof(NetworkedComponentAttribute)) != null)
                    networked.Add(registration);
            }

            // Ordinal comparison keeps the order (and therefore the ids) identical on every machine.
            networked.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

            for (var i = 0; i < networked.Count; i++)
            {
                networked[i].NetID = (ushort) i;
            }

            _networkedComponents = networked;
        }

        /// <inheritdoc />
        public byte[] GetHash(bool networkedOnly)
        {
            if (_networkedComponents == null)
                throw new ComponentRegistrationLockException();

            return networkedOnly ? GetHash(_networkedComponents) : GetHash(_array);
        }

        /// <summary>
        ///     Hashes the names and network ids of the given registrations (ordered by name) with SHA-256.
        /// </summary>
        public byte[] GetHash(IEnumerable<ComponentRegistration> comps)
        {
            var ordered = comps.OrderBy(x => x.Name, StringComparer.InvariantCulture);

            using var stream = new MemoryStream();
            using (var writer = new StreamWriter(stream, leaveOpen: true))
            {
                foreach (var item in ordered)
                {
                    writer.Write(item.Name);
                    writer.Write(item.NetID);
                }
            }

            stream.Position = 0;
            using var sha = System.Security.Cryptography.SHA256.Create();
            return sha.ComputeHash(stream);
        }
    }

    /// <summary>
    ///     Exception fired whenever a component not recognized by the engine is encountered.
    ///     This is usually caused by forgetting <see cref="RegisterComponentAttribute"/>.
    /// </summary>
    [Serializable]
    public sealed class UnknownComponentException : Exception
    {
        public UnknownComponentException()
        {
        }
        public UnknownComponentException(string message) : base(message)
        {
        }
        public UnknownComponentException(string message, Exception inner) : base(message, inner)
        {
        }
    }

    /// <summary>
    ///     Exception fired if you try to register a new component after all registrations have been locked.
    ///     This is, typically, after network IDs have been assigned.
    /// </summary>
    public sealed class ComponentRegistrationLockException : Exception
    {
    }

    /// <summary>
    ///     Exception fired when a component's name is entirely invalid. All component type names must end with Component.
    /// </summary>
    public sealed class InvalidComponentNameException : Exception
    {
        public InvalidComponentNameException(string message) : base(message)
        {
        }
    }
}
