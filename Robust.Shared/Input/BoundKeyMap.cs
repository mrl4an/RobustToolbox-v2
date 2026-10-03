using System;
using System.Collections.Generic;
using System.Reflection;
using Robust.Shared.Reflection;
using Robust.Shared.Serialization;

namespace Robust.Shared.Input
{
    /// <summary>
    ///     The id that stands in for a <see cref="BoundKeyFunction"/> on the network.
    /// </summary>
    [Serializable, NetSerializable]
    public readonly struct KeyFunctionId : IEquatable<KeyFunctionId>
    {
        private readonly int _value;

        /// <summary>
        ///     Wraps a raw integer as a key function id. The value is stored as-is; no range check is performed,
        ///     so whether the id refers to a real function is only decided when it is looked up in a <see cref="BoundKeyMap"/>.
        /// </summary>
        /// <param name="id">The raw numeric id (the function's index in the sorted function table).</param>
        public KeyFunctionId(int id)
        {
            _value = id;
        }

        /// <summary>
        ///     Extracts the raw integer from a <see cref="KeyFunctionId"/>. Explicit only, to avoid accidental
        ///     mixing of ids with ordinary numbers. A default-constructed id yields 0.
        /// </summary>
        /// <param name="funcId">The id to unwrap.</param>
        public static explicit operator int(KeyFunctionId funcId)
        {
            return funcId._value;
        }

        /// <summary>
        ///     Returns the id as plain decimal text, exactly as <see cref="int.ToString()"/> formats it.
        /// </summary>
        public override string ToString()
        {
            return _value.ToString();
        }

        public bool Equals(KeyFunctionId other)
        {
            return _value == other._value;
        }

        public override bool Equals(object? obj)
        {
            return obj is KeyFunctionId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return _value;
        }

        public static bool operator ==(KeyFunctionId left, KeyFunctionId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(KeyFunctionId left, KeyFunctionId right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    ///     Translates between <see cref="BoundKeyFunction"/> and <see cref="KeyFunctionId"/> so key functions can be sent over the network.
    /// </summary>
    public sealed class BoundKeyMap
    {
        private readonly IReflectionManager reflectionManager;

        private readonly Dictionary<BoundKeyFunction, KeyFunctionId> KeyFunctionsMap = new();
        private readonly List<BoundKeyFunction> KeyFunctionsList = new();

        public BoundKeyMap(IReflectionManager reflectionManager)
        {
            this.reflectionManager = reflectionManager;
        }

        internal IEnumerable<BoundKeyFunction> AllKeyFunctions => KeyFunctionsList;

        public void PopulateKeyFunctionsMap()
        {
            if (KeyFunctionsMap.Count != 0)
            {
                throw new InvalidOperationException("Cannot run this method twice.");
            }

            foreach (var type in reflectionManager.FindTypesWithAttribute<KeyFunctionsAttribute>())
            {
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
                {
                    // This check makes sure we ONLY get readonly fields with the type we want.
                    if (field.IsLiteral || !field.IsInitOnly || field.FieldType != typeof(BoundKeyFunction))
                    {
                        continue;
                    }

                    KeyFunctionsList.Add((BoundKeyFunction)field.GetValue(null)!);
                }
            }

            KeyFunctionsList.Sort();

            for (var i = 0; i < KeyFunctionsList.Count; i++)
            {
                KeyFunctionsMap.Add(KeyFunctionsList[i], new KeyFunctionId(i));
            }
        }

        public bool FunctionExists(string name)
        {
            return KeyFunctionsMap.ContainsKey(new BoundKeyFunction(name));
        }

        public KeyFunctionId KeyFunctionID(BoundKeyFunction function)
        {
            return KeyFunctionsMap[function];
        }

        /// <summary>
        ///     Resolves an id to its <see cref="BoundKeyFunction"/>.
        /// </summary>
        /// <param name="function">The id to resolve.</param>
        /// <returns>The function stored at the id's position in the function table.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        ///     The id is negative or past the end of the table (this includes every id before
        ///     <see cref="PopulateKeyFunctionsMap"/> has run). Use <see cref="TryGetKeyFunction"/> for untrusted ids.
        /// </exception>
        public BoundKeyFunction KeyFunctionName(KeyFunctionId function)
        {
            return KeyFunctionsList[(int) function];
        }

        /// <summary>
        ///     Non-throwing variant of <see cref="KeyFunctionName"/>, safe for ids received from the network.
        /// </summary>
        /// <param name="funcId">The id to resolve.</param>
        /// <param name="func">
        ///     The matching function, or <c>default</c> when the id does not refer to a known function.
        /// </param>
        /// <returns>True if the id was valid and <paramref name="func"/> was filled in; otherwise false.</returns>
        public bool TryGetKeyFunction(KeyFunctionId funcId, out BoundKeyFunction func)
        {
            var index = (int) funcId;

            if (index < 0 || index >= KeyFunctionsList.Count)
            {
                func = default;
                return false;
            }

            func = KeyFunctionsList[index];
            return true;
        }
    }
}
