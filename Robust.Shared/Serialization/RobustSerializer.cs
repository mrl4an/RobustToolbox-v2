using NetSerializer;
using Robust.Shared.IoC;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Robust.Shared.Log;
using Robust.Shared.Maths;
using Robust.Shared.Reflection;
using Robust.Shared.Utility;

namespace Robust.Shared.Serialization
{
    internal abstract partial class RobustSerializer : IRobustSerializerInternal
    {
        [Dependency] private readonly IReflectionManager _reflectionManager = default!;
        [Dependency] protected readonly IRobustMappedStringSerializer MappedStringSerializer = default!;
        [Dependency] private readonly ILogManager _logManager = default!;

        private readonly Dictionary<Type, Dictionary<string, Type?>> _cachedSerialized = new();

        private ISawmill LogSzr = default!;


        private Serializer _serializer = default!;

        private HashSet<Type> _serializableTypes = default!;
        private bool _initialized;
        private SerializerFloatFlags _floatFlags;

        private static Type[] AlwaysNetSerializable => new[]
        {
            typeof(Vector2i)
        };

        #region Statistics

        private readonly object _statsLock = new();

        // These stats aren't tracked correctly because the tracking code isn't thread safe. Oops!
        public long LargestObjectSerializedBytes { get; private set; }

        public Type? LargestObjectSerializedType { get; private set; }

        public long BytesSerialized { get; private set; }

        public long ObjectsSerialized { get; private set; }

        public long LargestObjectDeserializedBytes { get; private set; }

        public Type? LargestObjectDeserializedType { get; private set; }

        public long BytesDeserialized { get; private set; }

        public long ObjectsDeserialized { get; private set; }

        #endregion

        public SerializerFloatFlags FloatFlags
        {
            get => _floatFlags;
            set
            {
                if (_initialized)
                    throw new InvalidOperationException("Already initialized!");

                _floatFlags = value;
            }
        }

        /// <summary>
        ///     Builds the underlying NetSerializer instance from every <see cref="NetSerializableAttribute"/> type.
        ///     The type list order and custom serializer order are part of the wire format hash and must stay stable.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if called more than once.</exception>
        public void Initialize()
        {
            if (_initialized)
                throw new InvalidOperationException("Already initialized!");

            _initialized = true;

            // Deterministic ordering so client and server derive identical type maps.
            var types = _reflectionManager
                .FindTypesWithAttribute<NetSerializableAttribute>()
                .OrderBy(t => t.FullName, StringComparer.InvariantCulture)
                .ToList();

#if DEBUG
            // Shared network types must not live in side-specific assemblies.
            foreach (var type in types)
            {
                var assemblyName = type.Assembly.FullName;

                if (assemblyName != null && assemblyName.Contains("Server"))
                    throw new InvalidOperationException($"Type {type} is server specific but has a NetSerializableAttribute!");

                if (assemblyName != null && assemblyName.Contains("Client"))
                    throw new InvalidOperationException($"Type {type} is client specific but has a NetSerializableAttribute!");
            }
#endif

            LogSzr = _logManager.GetSawmill("szr");

            // Extra types are appended after the sorted ones, not merged into the sort.
            types.AddRange(AlwaysNetSerializable);
            types.Add(typeof(Vector2));

            MappedStringSerializer.Initialize();

            var settings = new Settings
            {
                CustomTypeSerializers = new ITypeSerializer[]
                {
                    MappedStringSerializer.TypeSerializer,
                    new NetMathSerializer(),
                    new NetBitArraySerializer(),
                    new NetFormattedStringSerializer(),
                    new NetUnsafeFloatSerializer(),
                }
            };

            if ((_floatFlags & SerializerFloatFlags.RemoveReadNan) != 0)
            {
                // The safe float serializer must come last so it overrides the default float handling.
                settings.CustomTypeSerializers = new ITypeSerializer[]
                {
                    MappedStringSerializer.TypeSerializer,
                    new NetMathSerializer(),
                    new NetBitArraySerializer(),
                    new NetFormattedStringSerializer(),
                    new NetUnsafeFloatSerializer(),
                    new NetSafeFloatSerializer(),
                };
            }

            _serializer = new Serializer(types, settings);
            _serializableTypes = new HashSet<Type>(_serializer.GetTypeMap().Keys);

            LogSzr.Info($"Serializer Types Hash: {_serializer.GetSHA256()}");
        }

        public byte[] GetSerializableTypesHash() => Convert.FromHexString(_serializer.GetSHA256());
        public string GetSerializableTypesHashString() => _serializer.GetSHA256();

        internal void GetHashManifest(Stream stream, bool writeNewline=false)
        {
            _serializer.GetHashManifest(stream, writeNewline);
        }

        public (byte[] Hash, byte[] Package) GetStringSerializerPackage() => MappedStringSerializer.GeneratePackage();

        public Dictionary<Type, uint> GetTypeMap() => _serializer.GetTypeMap();

        /// <summary>
        ///     Writes <paramref name="toSerialize"/> to the stream, prefixed with its type id, and updates statistics.
        /// </summary>
        public void Serialize(Stream stream, object toSerialize)
        {
            var start = StartMeasureStats(stream);
            _serializer.Serialize(stream, toSerialize);
            EndMeasureSerialize(stream, start, toSerialize.GetType());
        }

        public void SerializeDirect<T>(Stream stream, T toSerialize)
        {
            DebugTools.Assert(toSerialize == null || typeof(T) == toSerialize.GetType(),
                "Object must be of exact type specified in the generic parameter.");

            var start = StartMeasureStats(stream);
            _serializer.SerializeDirect(stream, toSerialize);
            EndMeasureSerialize(stream, start, typeof(T));
        }

        public T Deserialize<T>(Stream stream)
            => (T) Deserialize(stream);

        public void DeserializeDirect<T>(Stream stream, out T value)
        {
            var start = StartMeasureStats(stream);
            _serializer.DeserializeDirect(stream, out value);
            EndMeasureDeserialize(stream, start, typeof(T));
        }

        /// <summary>
        ///     Reads a type-prefixed object from the stream and updates statistics.
        /// </summary>
        public object Deserialize(Stream stream)
        {
            var start = StartMeasureStats(stream);
            var result = _serializer.Deserialize(stream);
            EndMeasureDeserialize(stream, start, result.GetType());
            return result;
        }

        public bool CanSerialize(Type type)
            => _serializableTypes.Contains(type);

        /// <inheritdoc />
        public Type? FindSerializedType(Type assignableType, string serializedTypeName)
        {
            if (!_cachedSerialized.TryGetValue(assignableType, out var assigned))
            {
                assigned = new Dictionary<string, Type?>();
                _cachedSerialized[assignableType] = assigned;
            }

            if (assigned.TryGetValue(serializedTypeName, out var resolved))
                return resolved;

            var types = _reflectionManager.GetAllChildren(assignableType);
            foreach (var type in types)
            {
                var serializedAttribute = type.GetCustomAttribute<SerializedTypeAttribute>();

                if(serializedAttribute is null)
                    continue;

                if (serializedAttribute.SerializeName == serializedTypeName)
                {
                    assigned[serializedTypeName] = type;
                    return type;
                }
            }

            assigned[serializedTypeName] = null;
            return null;
        }

        private static long StartMeasureStats(Stream stream)
        {
            return stream.CanSeek ? stream.Position : 0;
        }

        /// <summary>
        ///     Records one deserialized object. Byte counts are only tracked for seekable streams.
        /// </summary>
        private void EndMeasureDeserialize(Stream stream, long start, Type type)
        {
            lock (_statsLock)
            {
                ObjectsDeserialized += 1;

                if (!stream.CanSeek)
                    return;

                var byteCount = stream.Position - start;
                BytesDeserialized += byteCount;

                if (byteCount > LargestObjectDeserializedBytes)
                {
                    LargestObjectDeserializedBytes = byteCount;
                    LargestObjectDeserializedType = type;
                }
            }
        }

        private void EndMeasureSerialize(Stream stream, long start, Type type)
        {
            lock (_statsLock)
            {
                ObjectsSerialized += 1;

                if (stream.CanSeek)
                {
                    var end = stream.Position;
                    var byteCount = end - start;
                    BytesSerialized += byteCount;

                    if (byteCount > LargestObjectSerializedBytes)
                    {
                        LargestObjectSerializedBytes = byteCount;
                        LargestObjectSerializedType = type;
                    }
                }
            }
        }
    }
}
