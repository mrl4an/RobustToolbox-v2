using System;

namespace Robust.Shared.Configuration
{
    /// <summary>
    ///     Flags that adjust the behavior of a configuration variable (cvar). A cvar's flag set is the bitwise OR of
    ///     the members below.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Flags are supplied when a cvar is defined (<c>CVarDef.Create</c>) or registered
    ///         (<c>IConfigurationManager.RegisterCVar</c>), and can be read back with
    ///         <c>IConfigurationManager.GetCVarFlags</c>. They are consumed by the configuration managers (shared,
    ///         server and client), the network replication layer, the <c>cvar</c> console command and the
    ///         config file saver.
    ///     </para>
    ///     <para>
    ///         Flags are an in-process contract: config files and the replication messages carry names and values,
    ///         not flags. The numeric values still matter, because compiled assemblies embed them as constants.
    ///         Never renumber or reorder existing members. Bits 10 to 14 are free; bit 15 is the sign bit of the
    ///         underlying type and must not be used.
    ///     </para>
    ///     <para>
    ///         Engine code tests flags with bit masks (<c>(flags &amp; CVar.X) != 0</c>) or <c>HasFlag</c>, so flags
    ///         combine freely, for example <c>ARCHIVE | REPLICATED | SERVER</c>. Register-time flags
    ///         (<see cref="SERVERONLY"/> and <see cref="CLIENTONLY"/>) are evaluated when the cvar is registered; all
    ///         others are evaluated when a value is set, synchronized or saved. Flags supplied at registration
    ///         replace those a cvar had before, including the empty set of a variable that was only known from a
    ///         loaded config file.
    ///     </para>
    /// </remarks>
    [Flags]
    public enum CVar : short
    {
        /// <summary>
        ///     No special behavior. This is the default for the optional flags parameters. To test for it, compare
        ///     for equality; <c>HasFlag(NONE)</c> is always true.
        /// </summary>
        NONE = 0,

        /// <summary>
        ///     Marks a debug cvar whose modification counts as cheating, such as simulated network loss or latency.
        /// </summary>
        /// <remarks>
        ///     Currently the engine does not branch on this flag. It is a declared marker that content or tooling may
        ///     inspect.
        /// </remarks>
        CHEAT = 1,

        /// <summary>
        ///     Only the server may change the value.
        /// </summary>
        /// <remarks>
        ///     On a client, a non-forced <c>SetCVar</c> is refused with a warning, unless the client is in
        ///     single-player (a local game without a remote connection). The server has no restriction. Combined with
        ///     <see cref="REPLICATED"/>, the client does not upload its own value to the server, and the server's value
        ///     is applied on the client. Test harness code that resets cvars skips those that are both
        ///     <see cref="REPLICATED"/> and <see cref="SERVER"/> on the client.
        ///     Not to be confused with <see cref="SERVERONLY"/>, which controls registration instead.
        /// </remarks>
        SERVER = 2,

        /// <summary>
        ///     The value cannot be changed while the client is connected to a server.
        /// </summary>
        /// <remarks>
        ///     Enforced on the client only. A non-forced <c>SetCVar</c> while connected logs a warning and
        ///     does nothing.
        /// </remarks>
        NOT_CONNECTED = 4,

        /// <summary>
        ///     The value is synchronized between server and clients. Only meant for cvars defined in an assembly that
        ///     is present on both sides.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Server: when the value changes, it is broadcast to all clients, and every replicated cvar is sent
        ///         to a client when it connects. The server leaves out cvars that also carry <see cref="CLIENT"/>.
        ///     </para>
        ///     <para>
        ///         Client: when the value is changed locally while connected, the new value is sent to the server,
        ///         which stores it per connection, so that the server can query what a given client uses. The
        ///         server only accepts this if its own cvar is also <see cref="REPLICATED"/>; otherwise it ignores
        ///         it with a warning. The client leaves out cvars that also carry <see cref="SERVER"/>, and ignores
        ///         incoming server values for cvars that carry <see cref="CLIENT"/>.
        ///     </para>
        ///     Replication (this flag) and persistence (<see cref="ARCHIVE"/>) are independent.
        /// </remarks>
        REPLICATED = 8,

        /// <summary>
        ///     The value is persisted to the config file.
        /// </summary>
        /// <remarks>
        ///     When saving, a cvar is written if it was present in the originally loaded config file, or if it has
        ///     this flag and its current value is non-null and differs from its default. Archived cvars still at
        ///     their default are not written. The file is TOML, and everything before the last period of a cvar
        ///     name selects its table.
        /// </remarks>
        ARCHIVE = 16,

        /// <summary>
        ///     Intended meaning: a change made on the server notifies all clients, with no effect on the client side.
        ///     Like <see cref="REPLICATED"/>, only meant for cvars defined in shared assemblies.
        /// </summary>
        /// <remarks>
        ///     Currently the engine does not branch on this flag; it is reserved. The value is kept for
        ///     compatibility.
        /// </remarks>
        NOTIFY = 32,

        /// <summary>
        ///     The cvar only exists on the server. Registering it on a client is silently ignored.
        /// </summary>
        /// <remarks>
        ///     This lets shared code define a cvar that only the server owns. On the client it is unregistered, so
        ///     accessing it there fails. Evaluated at registration time. Not to be confused with
        ///     <see cref="SERVER"/>, which controls who may change an existing cvar.
        /// </remarks>
        SERVERONLY = 64,

        /// <summary>
        ///     The cvar only exists on the client. Registering it on a server is silently ignored.
        /// </summary>
        /// <remarks>
        ///     Mirror of <see cref="SERVERONLY"/>. Not to be confused with <see cref="CLIENT"/>.
        /// </remarks>
        CLIENTONLY = 128,

        /// <summary>
        ///     The value contains sensitive data.
        /// </summary>
        /// <remarks>
        ///     The <c>cvar</c> console command's completion hint then shows a localized "hidden" placeholder instead
        ///     of the current value. Without this flag the hint shows the value, truncated to 50 characters followed by
        ///     an ellipsis. The flag does not encrypt the value or exclude it from config files or replication.
        /// </remarks>
        CONFIDENTIAL = 256,

        /// <summary>
        ///     Only the client may change the value.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         On the server a non-forced <c>SetCVar</c> is refused with a warning. The server leaves such cvars
        ///         out of its replicated list, and a client ignores any server-sent value for them. A typical use is
        ///         a <see cref="REPLICATED"/> cvar owned by each client that the server only reads per connection.
        ///     </para>
        ///     <para>
        ///         Setting both <see cref="SERVER"/> and <see cref="CLIENT"/> blocks non-forced changes on both sides in
        ///         a networked game, because each side refuses on behalf of the other's flag. That is intentional; each
        ///         side then only changes its own copy through forced or local means.
        ///     </para>
        ///     Not to be confused with <see cref="CLIENTONLY"/>.
        /// </remarks>
        CLIENT = 512,
    }
}
