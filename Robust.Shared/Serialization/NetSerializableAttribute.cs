using System;

namespace Robust.Shared.Serialization;

/// <summary>
///     Marks a class, struct or enum as part of the network protocol so that the engine's network serializer
///     (<see cref="IRobustSerializer"/>) knows about it.
/// </summary>
/// <remarks>
/// <para>
///     This is a pure marker with no parameters. The serializer scans all loaded assemblies for types carrying
///     the attribute, sorts them by full type name and assigns type ids from that order, so the set of annotated
///     types and their full names must be identical on client and server; otherwise the serializer handshake
///     fails. Annotated types therefore belong in shared assemblies, not in client-only or server-only ones.
/// </para>
/// <para>
///     By convention annotated types are also marked with <see cref="SerializableAttribute"/>, and members that
///     must not be sent are marked with <see cref="NonSerializedAttribute"/>. Static analyzers look this
///     attribute up by its full metadata name, so neither the name nor the namespace may change.
/// </para>
/// <para>
///     The usage sets <c>Inherited = false</c>, so reflection lookups with inheritance do not see the attribute
///     on derived types; annotate each concrete type that is sent over the network.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum, AllowMultiple = false, Inherited = false)]
public sealed class NetSerializableAttribute : Attribute
{
}
