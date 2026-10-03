using System.Globalization;
using JetBrains.Annotations;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;

namespace Robust.Shared.Serialization.TypeSerializers.Implementations
{
    [TypeSerializer]
    public sealed class UIBox2Serializer : ITypeSerializer<UIBox2, ValueDataNode>, ITypeCopyCreator<UIBox2>
    {
        /// <summary>
        ///     Parses a box from text of the form <c>Top,Left,Bottom,Right</c> (invariant culture, no spaces).
        ///     Note that this order differs from the <see cref="UIBox2"/> constructor order.
        /// </summary>
        public UIBox2 Read(ISerializationManager serializationManager, ValueDataNode node,
            IDependencyCollection dependencies,
            SerializationHookContext hookCtx,
            ISerializationContext? context = null,
            ISerializationManager.InstantiationDelegate<UIBox2>? instanceProvider = null)
        {
            var parts = node.Value.Split(',');

            if (parts.Length != 4)
            {
                throw new InvalidMappingException($"Could not parse {nameof(UIBox2)}: '{node.Value}'");
            }

            var top = float.Parse(parts[0], CultureInfo.InvariantCulture);
            var left = float.Parse(parts[1], CultureInfo.InvariantCulture);
            var bottom = float.Parse(parts[2], CultureInfo.InvariantCulture);
            var right = float.Parse(parts[3], CultureInfo.InvariantCulture);

            return new UIBox2(left, top, right, bottom);
        }

        public ValidationNode Validate(ISerializationManager serializationManager, ValueDataNode node,
            IDependencyCollection dependencies,
            ISerializationContext? context = null)
        {
            string raw = node.Value;
            string[] args = raw.Split(',');

            if (args.Length != 4)
            {
                return new ErrorNode(node, "Invalid amount of arguments for UIBox2.");
            }

            return float.TryParse(args[0], NumberStyles.Any, CultureInfo.InvariantCulture, out _) &&
                   float.TryParse(args[1], NumberStyles.Any, CultureInfo.InvariantCulture, out _) &&
                   float.TryParse(args[2], NumberStyles.Any, CultureInfo.InvariantCulture, out _) &&
                   float.TryParse(args[3], NumberStyles.Any, CultureInfo.InvariantCulture, out _)
                ? new ValidatedValueNode(node)
                : new ErrorNode(node, "Failed parsing values for UIBox2.");
        }

        public DataNode Write(ISerializationManager serializationManager, UIBox2 value,
            IDependencyCollection dependencies, bool alwaysWrite = false,
            ISerializationContext? context = null)
        {
            return new ValueDataNode($"{value.Top.ToString(CultureInfo.InvariantCulture)}," +
                                     $"{value.Left.ToString(CultureInfo.InvariantCulture)}," +
                                     $"{value.Bottom.ToString(CultureInfo.InvariantCulture)}," +
                                     $"{value.Right.ToString(CultureInfo.InvariantCulture)}");
        }

        [MustUseReturnValue]
        public UIBox2 CreateCopy(ISerializationManager serializationManager, UIBox2 source,
            IDependencyCollection dependencies, SerializationHookContext hookCtx, ISerializationContext? context = null)
        {
            return new(source.Left, source.Top, source.Right, source.Bottom);
        }
    }
}
